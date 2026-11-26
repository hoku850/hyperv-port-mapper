using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using HyperVPortMapper.Core;
using Microsoft.Win32;

namespace HyperVPortMapper;

// Windows 适配层负责转发规则读取、权限检查和系统命令执行。
internal sealed class WindowsMappingService
{
    private readonly MappingManager _manager;

    public WindowsMappingService()
    {
        // 核心管理器使用当前系统快照进行写入前检查和失败恢复。
        _manager = new MappingManager(NetshRunner.ExecuteAsync, Read, ReadFirewallNames);
    }

    public static bool IsAdministrator
    {
        get
        {
            // 检查进程令牌是否已通过管理员授权。
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public IReadOnlyList<MappingEntry> Read()
    {
        // 显式使用 64 位视图读取 Windows 系统规则。
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var rules = machine.OpenSubKey(AppSettings.PortProxyPath);
        // 防火墙规则名称在同一视图内读取，以标记本工具持有的项。
        var firewallNames = ReadFirewallNames(machine);
        if (rules is null) return [];
        var result = new List<MappingEntry>();
        // 格式错误时显示读取失败，不把已有规则隐藏成空列表。
        foreach (var name in rules.GetValueNames())
        {
            // 系统命令维护注册表值，读取时不写回归一化结果。
            var value = rules.GetValue(name) as string ?? throw new FormatException($"系统规则格式异常：{name}");
            // 通过核心解析器检查端点结构与端口边界。
            var mapping = MappingValidator.Parse(name, value);
            result.Add(new(mapping, firewallNames.Contains(mapping.FirewallName)));
        }
        // 按宿主机监听端口排序以便扫描列表。
        return result.OrderBy(entry => entry.Mapping.ListenPort).ThenBy(entry => entry.Mapping.ListenAddress).ToArray();
    }

    private static HashSet<string> ReadFirewallNames()
    {
        // 孤立规则也参与端点冲突检查，并及时释放注册表句柄。
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return ReadFirewallNames(machine);
    }

    private static HashSet<string> ReadFirewallNames(RegistryKey machine)
    {
        using var rules = machine.OpenSubKey(AppSettings.FirewallPath);
        var result = new HashSet<string>(StringComparer.Ordinal);
        // 只读取名称字段；防火墙属性的显示顺序与系统语言无关。
        foreach (var key in rules?.GetValueNames() ?? [])
        {
            var value = rules?.GetValue(key) as string;
            if (value is null) continue;
            var name = value.Split('|').FirstOrDefault(part => part.StartsWith("Name=", StringComparison.Ordinal));
            // 仅匹配本工具前缀，不识别或删除其他软件规则。
            if (name is not null && name[5..].StartsWith(MappingDefaults.FirewallPrefix, StringComparison.Ordinal))
                result.Add(name[5..]);
        }
        return result;
    }

    public async Task SaveAsync(MappingEntry? original, PortMapping mapping, bool openFirewall)
    {
        // 非管理员进程只能查看与检测，不能更改系统配置。
        EnsureAdministrator();
        var problem = MappingValidator.Validate(mapping);
        // Windows 边界也校验地址、端口，不能只信任表单输入。
        if (problem is not null) throw new InvalidOperationException(problem);
        EnsureHelperRunning();
        // 本机网卡、自环和监听占用均在执行命令前检查。
        CheckLocalEndpoint(original, mapping);
        var current = await Task.Run(Read);
        // 管理器重新读取防火墙快照并在锁内检查外部变化。
        await _manager.SaveAsync(original, mapping, openFirewall, current);
        // 命令返回成功后再读取一次，确认映射实际存在。
        var saved = (await Task.Run(Read)).FirstOrDefault(entry => entry.Mapping == mapping);
        if (saved is null || saved.HasManagedFirewall != openFirewall)
            throw new InvalidOperationException("系统返回成功，但复查规则不一致。请刷新并用 netsh 核对。 ");
    }

    public async Task DeleteAsync(MappingEntry original)
    {
        // 删除同样需要管理员权限，并验证规则读取快照没有过期。
        EnsureAdministrator();
        var current = await Task.Run(Read);
        await _manager.DeleteAsync(original, current);
        // 删除后确认系统中已不存在相同监听端点。
        if ((await Task.Run(Read)).Any(entry => entry.Mapping.Key == original.Mapping.Key))
            throw new InvalidOperationException("删除后仍发现此规则，请刷新并核对系统配置。 ");
    }

    private static void CheckLocalEndpoint(MappingEntry? original, PortMapping mapping)
    {
        // 当前启用网卡地址用于检查监听地址和目标自环。
        var local = LocalAddresses();
        if (mapping.ListenAddress != MappingDefaults.AnyAddress && !local.Contains(mapping.ListenAddress))
            throw new InvalidOperationException("监听地址不属于当前宿主机，请选择本机网卡地址或 0.0.0.0。 ");
        // 127.0.0.0/8 全部属于宿主机回环网段。
        if (MappingValidator.IsLoopbackSelfProxy(mapping))
            throw new InvalidOperationException("目标指向宿主机回环网卡的同一端口，会形成转发自环。 ");
        // 指向宿主机其他网卡的同端口也不能形成循环。
        if (mapping.ListenPort == mapping.TargetPort && local.Contains(mapping.TargetAddress)
            && mapping.ListenAddress == mapping.TargetAddress)
            throw new InvalidOperationException("目标指向宿主机的同一监听端口，会形成转发自环。 ");
        CheckExistingListeners(original, mapping);
    }

    private static void CheckExistingListeners(MappingEntry? original, PortMapping mapping)
    {
        // 编辑原监听键时不把自己的端口计为冲突。
        if (original?.Mapping.Key == mapping.Key) return;
        var endpoints = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        // 本机监听器列表单独读取一次后再逐项比较端口。
        foreach (var endpoint in endpoints)
        {
            if (endpoint.AddressFamily != AddressFamily.InterNetwork) continue;
            var listener = new PortMapping(endpoint.Address.ToString(), endpoint.Port, mapping.TargetAddress, mapping.TargetPort);
            // 更换监听键时排除原映射占用的旧端点。
            if (original is not null && MappingManager.Overlaps(listener, original.Mapping)) continue;
            if (MappingManager.Overlaps(listener, mapping))
                throw new InvalidOperationException($"监听端口已被占用：{listener.ListenEndpoint}。请选择其他端口。 ");
        }
    }

    // 地址列表只提供配置建议，不会修改宿主机网络配置。
    public static string[] LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
        // 地址可能来自外网卡、回环卡或 Hyper-V 虚拟交换机。
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(address => address.Address.ToString()).Append(IPAddress.Loopback.ToString()).Distinct().Order().ToArray();

    public static string HelperStatus()
    {
        // 服务停止或不可读都会显示具体原因，避免误当成正常运行。
        try
        {
            return WindowsServiceStatus.IsRunning(AppSettings.HelperService)
                ? "IP Helper 正在运行" : "IP Helper 未运行，映射暂不可用";
        }
        catch (Exception error) { return $"无法读取 IP Helper 状态：{error.Message}"; }
    }

    private static void EnsureHelperRunning()
    {
        // 客户端不启动、停止或修改服务的自动启动设置。
        if (!WindowsServiceStatus.IsRunning(AppSettings.HelperService))
            throw new InvalidOperationException("IP Helper 未运行。请在 Windows 服务中启动 IP Helper 后重试。 ");
    }

    public static void EnsureAdministrator()
    {
        // 普通权限允许查看规则和检测目标，只有系统写入需要管理员身份。
        if (!IsAdministrator) throw new InvalidOperationException("保存和删除需要管理员权限。请关闭此窗口，点击主界面的“以管理员身份重启”。 ");
    }

    public static void RestartElevated()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前程序。 ");
        // 提权只由用户点击触发，取消授权不会关闭原窗口。
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        // dotnet 开发运行时需要 DLL 路径；自包含 EXE 不追加脚本参数。
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HyperVPortMapper.dll"));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("管理员启动失败。 ");
    }
}
