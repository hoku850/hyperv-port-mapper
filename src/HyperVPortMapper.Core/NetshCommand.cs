namespace HyperVPortMapper.Core;

// 操作集合封闭，调用方不能传入任意 shell 文本。
public enum NetshOperation
{
    AddMapping, SetMapping, DeleteMapping, AddFirewall, DeleteFirewall
}

// 这里只生成参数，Windows 侧使用 ArgumentList 原样传入。
public sealed record NetshCommand(NetshOperation Operation, PortMapping Mapping)
{
    public IReadOnlyList<string> Arguments => Operation switch
    {
        // 新增和修改共享相同的端点参数。
        NetshOperation.AddMapping => MappingArguments("add"),
        NetshOperation.SetMapping => MappingArguments("set"),
        NetshOperation.DeleteMapping => ["interface", "portproxy", "delete", "v4tov4",
            $"listenaddress={Mapping.ListenAddress}", $"listenport={Mapping.ListenPort}", "protocol=tcp"],
        // 删除名称完全匹配，绝不使用清空全部防火墙的命令。
        NetshOperation.AddFirewall => FirewallArguments(),
        NetshOperation.DeleteFirewall => ["advfirewall", "firewall", "delete", "rule", $"name={Mapping.FirewallName}"],
        _ => throw new ArgumentOutOfRangeException(nameof(Operation))
    };

    private string[] MappingArguments(string action) => ["interface", "portproxy", action, "v4tov4",
        $"listenaddress={Mapping.ListenAddress}", $"listenport={Mapping.ListenPort}",
        // connect 为虚拟机端点，listen 为宿主机端点。
        $"connectaddress={Mapping.TargetAddress}", $"connectport={Mapping.TargetPort}", "protocol=tcp"];

    private string[] FirewallArguments()
    {
        // 通配 IPv4 在防火墙语法中必须写为 any。
        var address = Mapping.ListenAddress == MappingDefaults.AnyAddress ? "any" : Mapping.ListenAddress;
        return ["advfirewall", "firewall", "add", "rule", $"name={Mapping.FirewallName}",
            "dir=in", "action=allow", "protocol=TCP", $"localport={Mapping.ListenPort}",
            // 用户勾选时才执行；对话框明确说明包含所有网络配置文件。
            $"localip={address}", "profile=any", "enable=yes"];
    }
}
