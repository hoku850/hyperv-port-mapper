# Hyper-V 端口映射客户端 Implementation Plan

> **For agentic workers:** 在当前会话顺序执行本计划并记录验证，不递归委派；用户已要求继续实施。

**Goal:** 交付可直接启动的 Windows 中文端口映射客户端。

**Architecture:** Core 类库负责规则与变更恢复；WinForms 程序负责 Windows 系统与交互。无测试框架的控制台程序验证高影响逻辑，不改动真实网络规则。

**Tech Stack:** C# / .NET 8 / WinForms / Windows netsh / Registry64。

**Spec:** docs/superpowers/specs/2026-11-25-hyperv-port-mapper-design.md

## Global Constraints

- Windows 10/11 x64。
- 中文界面和中文注释；至少每三行代码一行中文注释；函数最多 50 行；行为常量集中管理；不直接删除用户文件；无第三方运行时依赖。
- 防火墙默认不选，只操作本工具前缀的规则。
- 自动验证不修改真实转发和防火墙规则。

## 文件结构

- `src/HyperVPortMapper.Core/`：PortMapping、MappingValidator、NetshCommand、MappingManager、ConnectivityProbe。
- `src/HyperVPortMapper/`：Program、AppSettings、WindowsMappingService、NetshRunner、Theme、MainForm 与 MappingDialog。
- `tests/HyperVPortMapper.Checks/`：可直接运行的控制台行为检查。
- `README.md`：启动、操作、前置条件与故障定位。
- `release/win-x64/HyperVPortMapper.exe`：最终发布产物。

## 任务 1：规则与可恢复操作

**接口：** `PortMapping(string ListenAddress, int ListenPort, string TargetAddress, int TargetPort)`；`MappingEntry(PortMapping Mapping, bool HasManagedFirewall)`；`MappingManager(Func<NetshCommand, Task> execute)`；`SaveAsync(MappingEntry? original, PortMapping mapping, bool openFirewall, IReadOnlyList<MappingEntry> current)`；`DeleteAsync(MappingEntry original, IReadOnlyList<MappingEntry> current)`。

- [ ] 创建类库和检查程序，先用抛出 NotImplementedException 的接口建立可执行失败检查。
- [ ] 测试有效 IPv4 和边界端口、无效地址、通配地址冲突和过期记录。
- [ ] 运行 `dotnet run --project tests/HyperVPortMapper.Checks`，确认行为检查失败。
- [ ] 实现校验及参数化命令，防火墙命名只依赖已校验的监听端点。
- [ ] 保存时先检查 current 的原始规则与监听冲突，再生成正向和逆向命令。编辑监听键变化时先删除旧转发，再建立新转发；失败则恢复。
- [ ] 模拟防火墙失败和编辑失败，断言原规则仍存在、新规则已撤销；模拟逆操作失败，断言错误含有恢复失败提示。
- [ ] 运行检查并记录通过数量。

检查调用示例：
```csharp
var mapping = new PortMapping("0.0.0.0", 8080, "192.168.137.2", 80);
await manager.SaveAsync(null, mapping, false, []);
```

## 任务 2：系统适配与连接检测

**接口：** WindowsMappingService 的 `Read()` 返回 `IReadOnlyList<MappingEntry>`；`SaveAsync`、`DeleteAsync` 读取最新快照再调用核心。`ConnectivityProbe.CheckAsync(string address, int port)` 返回可读结果和耗时。

- [ ] 从 Registry64 读取 PortProxy 和本工具拥有的防火墙记录，异常向上报告。
- [ ] 使用系统目录的 netsh 和 ArgumentList，设置子进程超时并检查退出码。
- [ ] 修改前要求管理员权限，检测 TCP 监听占用和明显自环。
- [ ] 用临时本地 TCP 服务测试连接成功及关闭后的连接失败。
- [ ] 构建适配代码并执行 `netsh interface portproxy show v4tov4` 对照只读列表。

## 任务 3：原生桌面操作

- [ ] 统一颜色、间距和控件尺寸，创建主窗口表格、权限和服务提示。
- [ ] 新增表单使用明确标签、IPv4 提示、端口范围控件、防火墙说明。
- [ ] 连接新增、编辑、删除；删除前显示精确端点并确认；变更期间禁用重复操作。
- [ ] 连接搜索、刷新、双击编辑、目标连接检测和管理员重启。
- [ ] 实际启动窗口并检查空列表、验证错误、取消、尺寸和键盘操作。

## 任务 4：验证和发布

- [ ] `dotnet run --project tests/HyperVPortMapper.Checks -c Release`。
- [ ] `dotnet build HyperVPortMapper.sln -c Release --no-restore`。
- [ ] 只读系统检查；界面检查截图放会话临时目录。
- [ ] `dotnet publish src/HyperVPortMapper -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/win-x64`。
- [ ] 启动发布 EXE 并检查存活和主窗口。
- [ ] 完成 README，记录没有执行真实系统写入测试的限制，更新全部进度。
