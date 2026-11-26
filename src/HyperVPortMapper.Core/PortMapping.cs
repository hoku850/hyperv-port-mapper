namespace HyperVPortMapper.Core;

// 系统限制与工具拥有的规则名称集中在此处。
public static class MappingDefaults
{
    public const string AnyAddress = "0.0.0.0";
    public const string FirewallPrefix = "HyperVPortMapper TCP ";
    // TCP 端口和连接等待时间使用统一边界。
    public const int MinPort = 1;
    public const int MaxPort = 65535;
    public const int ProbeTimeoutMs = 3000;
}

// 规则对象只承载数据，合法性由入口校验。
public sealed record PortMapping(string ListenAddress, int ListenPort, string TargetAddress, int TargetPort)
{
    public string Key => $"{ListenAddress}/{ListenPort}";
    public string ListenEndpoint => $"{ListenAddress}:{ListenPort}";
    // 显示与规则命名使用同一个监听端点。
    public string TargetEndpoint => $"{TargetAddress}:{TargetPort}";
    public string FirewallName => MappingDefaults.FirewallPrefix + ListenEndpoint;
}

// 防火墙标记只代表本工具拥有的规则。
public sealed record MappingEntry(PortMapping Mapping, bool HasManagedFirewall);
