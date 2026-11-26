// 此程序的任何失败都会返回非零退出码，便于构建后直接验证。
using System.Net;
using System.Net.Sockets;
using HyperVPortMapper.Core;
using HyperVPortMapper.Checks;

// 每项检查针对一类可见错误，期望值均为独立给出的样例。
var checks = new (string Name, Func<Task> Run)[]
{
    // 以下边界检查覆盖配置残留与并发恢复场景。
    ("IPv4 与端口边界校验", CheckValidation),
    ("读取中文系统的原始注册表记录", CheckParsing),
    // 参数数组必须与 netsh 文档的独立样例一致。
    ("系统命令参数不经 shell 拼接", CheckArguments),
    ("新增映射与可选防火墙", CheckCreate),
    ("通配地址与具体地址的监听冲突", CheckConflict),
    // 外部修改和中途失败都不能静默覆盖旧规则。
    ("过期快照阻止写入", CheckStale),
    ("新增防火墙失败后撤销映射", CheckCreateRollback),
    ("修改监听失败后恢复原映射", CheckEditRollback),
    ("删除防火墙失败后恢复原映射", CheckDeleteRollback),
    // 恢复失败需要向用户明确报告，不能声称已经恢复。
    ("恢复失败包含可识别提示", CheckRollbackError),
    ("孤立防火墙规则阻止新映射覆盖", CheckOrphanFirewall),
    ("失败恢复保留外部更新的映射", CheckConcurrentRollback),
    ("移动已放行映射要求显式确认", CheckProtectedFirewallEdit),
    ("真实本地 TCP 连接成功和拒绝", CheckConnectivity),
    ("连接检测遵守调用方取消", CheckCancellation)
};
var failed = 0;
foreach (var (name, run) in checks)
{
    // 所有检查独立运行，避免第一个失败掩盖其他缺失行为。
    try { await run(); Console.WriteLine($"通过：{name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"失败：{name}：{error.Message}"); }
}
Console.WriteLine($"检查结果：{checks.Length - failed}/{checks.Length} 通过");
return failed == 0 ? 0 : 1;
// 固定样例独立于生产默认端口，避免修改默认值让预期自动改变。

static PortMapping Sample(int port = 8080) => new("0.0.0.0", port, "192.168.137.2", 80);

static void Require(bool condition, string message)
{
    // 断言失败直接令控制台进程返回非零。
    if (!condition) throw new InvalidOperationException(message);
}

static async Task<Exception> Reject(Func<Task> action)
    // 未实现异常不能冒充正常的拒绝行为，否则恢复检查会误通过。
{
    // 只捕获被测操作，避免错误地捕获断言自身。
    try { await action(); }
    catch (Exception error) when (error is not NotImplementedException) { return error; }
    throw new InvalidOperationException("操作应该失败，却成功完成");
}

static Task CheckValidation()
{
    // 放宽为简写 IP、允许注入文本或丢失端口边界都会失败。
    Require(MappingValidator.Validate(Sample(1)) is null, "端口 1 应合法");
    Require(MappingValidator.Validate(Sample(65535)) is null, "端口 65535 应合法");
    foreach (var address in new[] { "127.1", "01.2.3.4", "::1", "1.2.3.4 & whoami", "224.0.0.1" })
        Require(MappingValidator.Validate(Sample() with { TargetAddress = address }) is not null, $"不应接受 {address}");
    // 未指定与广播目标不能当成有效的虚拟机地址。
    foreach (var address in new[] { "0.0.0.0", "255.255.255.255" })
        Require(MappingValidator.Validate(Sample() with { TargetAddress = address }) is not null, "目标地址应被拒绝");
    // 两端端口范围一致，越界输入必须在执行命令前拒绝。
    Require(MappingValidator.Validate(Sample(0)) is not null, "端口 0 应被拒绝");
    Require(MappingValidator.Validate(Sample(65536)) is not null, "越界端口应被拒绝");
    // 相同端点会形成明显转发自环。
    Require(MappingValidator.IsLoopbackSelfProxy(new("0.0.0.0", 80, "127.0.0.2", 80)), "其他回环地址也应识别为自环");
    return Task.CompletedTask;
}

static Task CheckParsing()
// 系统原始格式使用手工样例，避免复用被测解析方法生成预期。
{
    // 原始值没有依赖本机语言的表头。
    var parsed = MappingValidator.Parse("0.0.0.0/8080", "192.168.137.2/80");
    Require(parsed == Sample(), "系统记录解析错误");
    var host = MappingValidator.Parse("127.0.0.1/22", "vm.internal/22");
    // 允许展示已有 DNS 目标，新增仍要求 IPv4。
    Require(host.TargetAddress == "vm.internal", "已有 DNS 规则应可读取");
    return Task.CompletedTask;
}

static Task CheckArguments()
{
    var command = new NetshCommand(NetshOperation.AddMapping, Sample());
    // 使用手工给出的预期参数，捕捉方向或端口颠倒。
    string[] expected = ["interface", "portproxy", "add", "v4tov4", "listenaddress=0.0.0.0",
        "listenport=8080", "connectaddress=192.168.137.2", "connectport=80", "protocol=tcp"];
    Require(command.Arguments.SequenceEqual(expected), "转发参数错误");
    var firewall = new NetshCommand(NetshOperation.AddFirewall, Sample());
    // 通配监听必须映射为防火墙 localip=any。
    Require(firewall.Arguments.Contains("localip=any"), "通配防火墙地址错误");
    // 防火墙放行的是宿主机端口，不能误用目标服务端口。
    Require(firewall.Arguments.Contains("localport=8080"), "防火墙应放行宿主机端口");
    return Task.CompletedTask;
}

static async Task CheckCreate()
{
    // 同一保存同时覆盖映射存在和可选防火墙的外部效果。
    var system = new MemorySystem();
    await system.Manager.SaveAsync(null, Sample(), true, system.Snapshot());
    // 忘记建立转发或可选防火墙都会被捕捉。
    Require(system.Mappings["0.0.0.0/8080"] == Sample(), "新增映射不正确");
    Require(system.Firewalls.Contains("HyperVPortMapper TCP 0.0.0.0:8080"), "未创建防火墙规则");
    var before = system.Commands.Count;
    // 无变化的保存不能重复创建系统规则。
    await system.Manager.SaveAsync(system.Snapshot().Single(), Sample(), true, system.Snapshot());
    Require(system.Commands.Count == before, "无变化保存不应执行命令");
}

// 监听重叠检查应拒绝通配重叠，同时允许不同具体地址。
static async Task CheckConflict()
{
    // 用内存规则快照测试通配地址与具体地址的冲突。
    var system = new MemorySystem();
    system.Mappings.Add("0.0.0.0/8080", Sample());
    // 任一方向的通配冲突都要在命令执行前发现。
    await Reject(() => system.Manager.SaveAsync(null, Sample() with { ListenAddress = "127.0.0.1" }, false, system.Snapshot()));
    Require(system.Commands.Count == 0, "冲突时不应执行命令");
    system.Mappings.Clear();
    system.Mappings.Add("127.0.0.1/8080", Sample() with { ListenAddress = "127.0.0.1" });
    await Reject(() => system.Manager.SaveAsync(null, Sample(), false, system.Snapshot()));
    // 具体的不同本地地址可以分别监听同一个端口。
    await system.Manager.SaveAsync(null, Sample() with { ListenAddress = "192.168.1.10" }, false, system.Snapshot());
    Require(system.Mappings.Count == 2, "不应误判不同地址的相同端口");
}

static async Task CheckStale()
{
    var system = new MemorySystem();
    var old = new MappingEntry(Sample(), false);
    system.Mappings.Add(Sample().Key, Sample() with { TargetPort = 81 });
    // 删除和保存均不能用过期记录覆盖外部修改。
    // 外部修改目标端口是实质变化，旧快照不能继续删除或编辑。
    await Reject(() => system.Manager.DeleteAsync(old, system.Snapshot()));
    await Reject(() => system.Manager.SaveAsync(old, Sample(9090), false, system.Snapshot()));
    Require(system.Commands.Count == 0, "过期快照不应执行命令");
}

static async Task CheckCreateRollback()
{
    // 新增映射后模拟防火墙命令失败，检查映射是否回滚。
    var system = new MemorySystem { ShouldFail = command => command.Operation == NetshOperation.AddFirewall };
    // 故障出现在映射新增之后，若缺失逆操作就会留下多余规则。
    await Reject(() => system.Manager.SaveAsync(null, Sample(), true, system.Snapshot()));
    // 防火墙创建失败时，新建转发必须撤销。
    Require(system.Mappings.Count == 0, "失败后留下了映射");
    Require(system.Firewalls.Count == 0, "失败后留下了防火墙规则");
}

static async Task CheckEditRollback()
{
    // 原映射已从最新快照读取，不依赖用户界面缓存。
    var system = new MemorySystem();
    system.Mappings.Add(Sample().Key, Sample());
    // 只让新监听建立失败，旧规则恢复命令应能成功。
    system.ShouldFail = command => command.Operation == NetshOperation.AddMapping && command.Mapping.ListenPort == 9090;
    await Reject(() => system.Manager.SaveAsync(system.Snapshot().Single(), Sample(9090), false, system.Snapshot()));
    Require(system.Mappings.Count == 1 && system.Mappings[Sample().Key] == Sample(), "原映射没有恢复");
}
    // 回滚保护按最新的系统规则读取，不依赖旧表单快照。

static async Task CheckDeleteRollback()
{
    var system = new MemorySystem();
    // 两项系统配置先同时存在，检查失败后是否完整保留。
    system.Mappings.Add(Sample().Key, Sample());
    system.Firewalls.Add(Sample().FirewallName);
    // 删除工具防火墙失败，必须重新建立原转发。
    system.ShouldFail = command => command.Operation == NetshOperation.DeleteFirewall;
    await Reject(() => system.Manager.DeleteAsync(system.Snapshot().Single(), system.Snapshot()));
    Require(system.Mappings[Sample().Key] == Sample(), "删除失败丢失了原映射");
    Require(system.Firewalls.Contains(Sample().FirewallName), "原防火墙不应丢失");
}

static async Task CheckRollbackError()
{
    // 此场景明确检查恢复失败提示，不接受普通命令错误代替。
    var system = new MemorySystem();
    // 同时让正向防火墙和逆向转发删除失败。
    system.ShouldFail = command => command.Operation is NetshOperation.AddFirewall or NetshOperation.DeleteMapping;
    var error = await Reject(() => system.Manager.SaveAsync(null, Sample(), true, system.Snapshot()));
    Require(error.Message.Contains("恢复失败"), "没有明确报告恢复失败");
}

static async Task CheckConnectivity()
{
    // 真实监听器只绑定回环和系统分配的临时端口。
    // 系统分配端口减少与用户已有服务发生冲突的机会。
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    var connected = await ConnectivityProbe.CheckAsync("127.0.0.1", port);
    Require(connected.Success, "本地开放端口应可达");
    // 停止服务后应返回失败，不应误报为连接成功。
    listener.Stop();
    var refused = await ConnectivityProbe.CheckAsync("127.0.0.1", port);
    Require(!refused.Success, "已关闭的端口不应可达");
}

static async Task CheckCancellation()
// 提前取消可以确定性验证取消语义，不依赖外部网络速度。
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    // 忽略取消会导致关闭窗口后仍然持续等待。
    var error = await Reject(() => ConnectivityProbe.CheckAsync("127.0.0.1", 1, cancellation.Token));
    Require(error is OperationCanceledException, "调用方取消应被保留");
}
static async Task CheckOrphanFirewall()
{
    var system = new MemorySystem();
    // 孤立放行可能有用户收紧的范围，新增不能静默覆盖。
    system.Firewalls.Add(Sample().FirewallName);
    var error = await Reject(() => system.Manager.SaveAsync(null, Sample(), false, system.Snapshot()));
    Require(error.Message.Contains("防火墙"), "孤立放行应在创建映射前被拒绝");
    Require(system.Mappings.Count == 0 && system.Commands.Count == 0, "拒绝孤立规则时不应修改系统状态");
}

    // 在失败命令边界修改规则，模拟另一个管理员窗口的更新。
static async Task CheckConcurrentRollback()
{
    var system = new MemorySystem();
    // 防火墙步骤失败前模拟管理员已更新目标端口。
    system.ShouldFail = command =>
    {
        if (command.Operation != NetshOperation.AddFirewall) return false;
        system.Mappings[Sample().Key] = Sample() with { TargetPort = 81 };
        return true;
    };
    var error = await Reject(() => system.Manager.SaveAsync(null, Sample(), true, system.Snapshot()));
    Require(system.Mappings[Sample().Key].TargetPort == 81, "回滚不应删除后来写入的目标端口");
    Require(error.Message.Contains("保留现状"), "跳过回滚时应告知用户实际冲突");
}

static async Task CheckProtectedFirewallEdit()
{
    var system = new MemorySystem();
    var old = new MappingEntry(Sample(), true);
    system.Mappings.Add(old.Mapping.Key, old.Mapping);
    system.Firewalls.Add(old.Mapping.FirewallName);
    // 搬迁并继续勾选会重建规则，必须先要求用户明确移除旧放行。
    var changed = Sample() with { ListenPort = 9090 };
    var error = await Reject(() => system.Manager.SaveAsync(old, changed, true, system.Snapshot()));
    Require(error.Message.Contains("防火墙限制"), "移动防火墙规则前应要求用户确认");
    Require(system.Mappings[old.Mapping.Key] == old.Mapping, "拒绝防火墙规则迁移不应修改原映射");
}
