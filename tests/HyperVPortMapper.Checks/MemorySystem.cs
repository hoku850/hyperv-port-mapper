using HyperVPortMapper.Core;

namespace HyperVPortMapper.Checks;

// 仅替代真正会修改 Windows 的进程边界，核心逻辑仍执行生产代码。
internal sealed class MemorySystem
{
    // 规则和防火墙分开存储，与 Windows 两项独立配置一致。
    public Dictionary<string, PortMapping> Mappings { get; } = [];
    public HashSet<string> Firewalls { get; } = [];
    // 故障在命令生效前抛出，可分别检查正向和恢复路径。
    public Func<NetshCommand, bool> ShouldFail { get; set; } = _ => false;
    public List<NetshCommand> Commands { get; } = [];
    // 传入实时快照供失败恢复保护外部修改。
    // 核心通过同一份实时状态检查规则名称和失败恢复。
    public MappingManager Manager => new(ExecuteAsync, Snapshot, () => new HashSet<string>(Firewalls));

    // 核心拿到真实逻辑需要的完整条目，而非固定模拟返回值。
    // 快照按照外部系统的当前状态生成。
    public IReadOnlyList<MappingEntry> Snapshot() => Mappings.Values
        .Select(mapping => new MappingEntry(mapping, Firewalls.Contains(mapping.FirewallName))).ToArray();

    // 故障仅在进程边界注入，不替代输入检查或恢复计划。
    public Task ExecuteAsync(NetshCommand command)
    {
        // 记录失败操作，便于确认未越过权限与输入检查。
        Commands.Add(command);
        if (ShouldFail(command)) throw new InvalidOperationException("模拟系统命令失败");
        var mapping = command.Mapping;
        // 只模拟系统边界的数据变化，不重复实现操作计划。
        switch (command.Operation)
        {
            case NetshOperation.AddMapping:
                Mappings.Add(mapping.Key, mapping);
                break;
            // 修改要求旧规则存在，与 netsh 的语义一致。
            case NetshOperation.SetMapping:
                if (!Mappings.ContainsKey(mapping.Key)) throw new InvalidOperationException("规则不存在");
                Mappings[mapping.Key] = mapping;
                break;
            case NetshOperation.DeleteMapping:
                Mappings.Remove(mapping.Key);
                break;
            // 防火墙集合只包含工具自建规则。
            case NetshOperation.AddFirewall:
                // 名称作为防火墙身份，删除必须精确匹配。
                Firewalls.Add(mapping.FirewallName);
                break;
            case NetshOperation.DeleteFirewall:
                Firewalls.Remove(mapping.FirewallName);
                break;
        }
        return Task.CompletedTask;
    }
}
