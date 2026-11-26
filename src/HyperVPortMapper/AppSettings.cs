namespace HyperVPortMapper;

// Windows 路径、默认值与等待限制集中管理。
internal static class AppSettings
{
    public const string Title = "Hyper-V 端口映射";
    public const string PortProxyPath = @"SYSTEM\CurrentControlSet\Services\PortProxy\v4tov4\tcp";
    // 只读规则源，不直接写注册表以免绕过 Windows 的配置流程。
    public const string FirewallPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
    public const string HelperService = "iphlpsvc";
    public const int CommandTimeoutSeconds = 20;
    // 新建规则默认端口只是起点，保存前仍需校验冲突。
    public const int DefaultListenPort = 8080;
    public const int DefaultTargetPort = 80;
    // 示例地址只供说明和占位，不作为默认保存目标。
    public const string ExampleTargetAddress = "192.168.137.2";
    public const int UacCancelledError = 1223;
}
