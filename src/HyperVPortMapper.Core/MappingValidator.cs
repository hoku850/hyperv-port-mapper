using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace HyperVPortMapper.Core;

// 入口只接受规范 IPv4，避免 .NET 自动接受简写地址。
public static class MappingValidator
{
    // 返回明确的字段提示，由桌面表单就地显示。
    public static string? Validate(PortMapping mapping)
    {
        // 监听允许所有网卡，目标必须是可指定的主机地址。
        if (!IsIPv4(mapping.ListenAddress, true)) return "宿主机地址必须是完整的 IPv4，例如 0.0.0.0。";
        if (!IsIPv4(mapping.TargetAddress, false)) return "虚拟机地址必须是完整的 IPv4，不能使用广播或组播地址。";
        if (!IsPort(mapping.ListenPort) || !IsPort(mapping.TargetPort)) return "端口必须在 1～65535 之间。";
        // 精确的同端点转发必然形成自环。
        if (mapping.ListenAddress == mapping.TargetAddress && mapping.ListenPort == mapping.TargetPort)
            return "监听端点和目标端点不能相同。";
        return null;
    }
    // IPAddress 本身接受历史简写，这里额外要求点分十进制。
    public static bool IsIPv4(string address, bool allowAny)
    {
        // 比较规范输出，拒绝前导零、数字地址与简写。
        if (!IPAddress.TryParse(address, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;
        if (parsed.ToString() != address) return false;
        // 0 开头与组播保留段不能作为目标；所有网卡是单独例外。
        if (address == MappingDefaults.AnyAddress) return allowAny;
        var firstByte = parsed.GetAddressBytes()[0];
        return firstByte is > 0 and < 224;
    }

    // 读取与新增分别校验，避免已有名称目标无法显示或删除。
    public static PortMapping Parse(string name, string value)
    {
        // 注册表中的目标可能是已有 DNS 名称，只校验记录结构。
        var listen = ParseEndpoint(name);
        var target = ParseEndpoint(value);
        if (!IsIPv4(listen.Address, true) || Uri.CheckHostName(target.Address) == UriHostNameType.Unknown)
            throw new FormatException($"存在无法读取的端口转发记录：{name}。请用 netsh 检查。 ");
        return new(listen.Address, listen.Port, target.Address, target.Port);
    }

    private static (string Address, int Port) ParseEndpoint(string value)
    {
        // 分隔符数量和端口边界同时校验，异常记录不能被隐藏。
        var parts = value.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || !IsPort(port)) throw new FormatException($"端口转发记录格式无效：{value}");
        return (parts[0], port);
    }

    // 同端口转发到本机任何回环地址都会形成自我连接。
    public static bool IsLoopbackSelfProxy(PortMapping mapping)
    {
        if (mapping.ListenPort != mapping.TargetPort) return false;
        var target = IPAddress.Parse(mapping.TargetAddress);
        var listen = IPAddress.Parse(mapping.ListenAddress);
        // 0.0.0.0 覆盖回环地址，特定回环监听只比较相同地址。
        return IPAddress.IsLoopback(target) && (mapping.ListenAddress == MappingDefaults.AnyAddress
            || IPAddress.IsLoopback(listen) && listen.Equals(target));
    }
    // 端口检查也供连接检测与界面共用。
    public static bool IsPort(int port) => port is >= MappingDefaults.MinPort and <= MappingDefaults.MaxPort;
}
