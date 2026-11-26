# Hyper-V 端口映射 | Hyper-V Port Mapper

[English](#english) | [简体中文](#简体中文)

<a id="简体中文"></a>

## 简介

Windows 桌面工具，将宿主机的 IPv4 TCP 端口转发到 Hyper-V 虚拟机服务。使用 Windows 自带的 `netsh interface portproxy`，无需额外运行时依赖。

## 获取与启动

- 获取源码后，按[开发与测试](#开发与测试)安装 .NET 8 SDK 并运行，或生成 Windows x64 自包含单文件 `HyperVPortMapper.exe`。
- 生成的自包含 EXE 可直接启动，无需安装 .NET 或启动网页服务。
- 当前界面为简体中文。普通权限可以查看规则、搜索及检测目标；新增、编辑、删除需要管理员权限。点击“以管理员身份重启”，或右键 EXE 选择“以管理员身份运行”。

## 新建映射

例如，虚拟机 `192.168.137.2` 上的 Web 服务监听 `80`，希望通过宿主机 `8080` 访问：

1. 点击“新增映射”。
2. 选择宿主机监听地址：网卡 IP 用于指定网卡；`0.0.0.0` 表示所有网卡；`127.0.0.1` 仅供宿主机本地访问。
3. 设置宿主机监听端口 `8080`。
4. 输入虚拟机 IPv4 `192.168.137.2` 和服务端口 `80`。
5. 点击“检测目标”确认宿主机可以访问虚拟机服务。
6. 如确实需要公网或局域网入站访问，可以勾选 Windows 防火墙放行。此选项默认关闭；勾选后允许所有来源、所有网络配置文件访问监听端口。
7. 点击“保存映射”，然后使用 `http://宿主机IP:8080` 访问。

关闭客户端后，系统映射仍会生效。删除前需要确认。编辑时取消勾选防火墙放行，会移除本工具创建的对应规则，但不会更改其他应用或手动添加的规则。

如果原映射已有本工具的防火墙规则，要更改监听地址或端口并继续放行，请先取消放行并保存，再修改监听端点，以免把用户收紧过的防火墙范围恢复为默认值。

## 功能

- 列出、搜索和刷新系统 IPv4/TCP 端口映射。
- 新增、编辑、删除宿主机到虚拟机的 TCP 映射。
- 可选创建和管理本工具的 Windows 防火墙规则。
- 检测目标 TCP 端口连通性。
- 校验地址和端口，检查监听冲突、外部修改及明显的转发自环。
- 多步系统操作失败时尝试安全恢复；恢复异常会明确显示并要求刷新核对。
- 使用快捷键：`F5` 刷新，`Ctrl+F` 搜索，`Alt+N` 新增；双击规则进入编辑。

## 使用要求与限制

- Windows 10/11 x64。
- 虚拟机的网络已配置，宿主机可以访问虚拟机 IP 和目标服务端口。
- Windows **IP Helper** 服务正在运行。客户端显示其状态，但不会自动修改服务设置。
- `portproxy` 仅支持 **IPv4 TCP**，不支持 UDP。
- 本工具不会自动设置 Hyper-V 虚拟交换机、网络共享、路由器或虚拟机内部防火墙。
- 虚拟机 IP 变更后，需更新映射；建议为虚拟机配置稳定 IP。

“检测目标”仅验证宿主机到虚拟机目标 TCP 端口的连接，不验证 HTTP 等应用响应，也不保证外网可访问。“防火墙”状态仅表示本工具对应的规则是否存在，不代表组策略或其他防火墙规则的最终效果。

## 故障排查

- **提示管理员权限不足**：返回主窗口，点击“以管理员身份重启”。取消 UAC 会保留当前窗口。
- **提示端口已占用或规则冲突**：选择其他端口。`0.0.0.0:8080` 会与任意本地地址的 `8080` 监听冲突。
- **目标连接被拒绝**：检查虚拟机服务是否启动，且是否监听虚拟机网卡地址，而非仅监听 `127.0.0.1`。
- **目标连接超时**：检查 Hyper-V 交换机、虚拟机地址、虚拟机防火墙及宿主机网络。
- **目标可连接，但外部访问失败**：检查宿主机防火墙，并用实际宿主机 IP 访问；公网访问还需配置上游路由器或云平台的转发规则。
- **提示规则被其他程序修改**：刷新后重新选择规则，避免覆盖外部变更。
- **提示操作或恢复失败**：先刷新，然后使用管理员终端查看 `netsh interface portproxy show v4tov4`。命令超时时可能已有部分变更，勿直接清空其他系统规则。

## 开发与测试

需要 .NET 8 SDK：

```powershell
dotnet build .\HyperVPortMapper.sln -c Release
dotnet run --project .\tests\HyperVPortMapper.Checks -c Release
dotnet run --project .\src\HyperVPortMapper -c Release
```

检查程序使用内存执行器覆盖验证、冲突、操作失败和回滚，并用本地 TCP 监听器测试目标连通性；不会修改当前 Windows 端口映射或防火墙规则。

已在开发电脑完成验证：15 项核心检查通过、Release 构建零警告零错误、发布 EXE 的普通权限界面操作检查通过。自动检查没有执行管理员写入或真实虚拟机的端到端转发。

发布 Windows x64 自包含单文件：

```powershell
dotnet publish .\src\HyperVPortMapper -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\release\win-x64
```

---

<a id="english"></a>

## Overview

A native Windows desktop utility for forwarding an IPv4 TCP port on the host to a service in a Hyper-V virtual machine. It uses the Windows built-in `netsh interface portproxy` command and requires no additional runtime dependencies.

## Get started

- Get the source and follow [Build and test](#build-and-test) to run the application with the .NET 8 SDK or publish a self-contained Windows x64 `HyperVPortMapper.exe`.
- The published self-contained executable runs directly; no .NET installation or web server is required.
- The application UI is currently in Simplified Chinese. Standard users can view and search rules and test target connectivity. Creating, editing, or deleting a mapping requires elevation. Click **以管理员身份重启** (Restart as administrator) or right-click the executable and choose **Run as administrator**.

## Create a mapping

For example, to expose a web service on `192.168.137.2:80` through host port `8080`:

1. Select **Add mapping**.
2. Choose a host listening address. A host NIC address binds to that interface; `0.0.0.0` listens on all IPv4 interfaces; `127.0.0.1` allows host-local access only.
3. Enter host port `8080`.
4. Enter the VM IPv4 address `192.168.137.2` and service port `80`.
5. Use **Test target** to confirm the host can reach the VM service.
6. If inbound access is required, optionally enable the Windows Firewall rule. This is unchecked by default. When enabled, the rule permits all sources on all network profiles for the listening port.
7. Select **Save mapping**, then connect to `http://HOST-IP:8080`.

Mappings remain active after the client closes. Deletion requires confirmation. When editing a rule, unchecking the firewall option removes the corresponding rule managed by this tool only; unrelated firewall rules are left untouched.

If an existing mapping has a firewall rule managed by this tool, changing the listening address or port while keeping firewall access requires first unchecking the firewall option and saving, then editing the listening endpoint. This preserves user intent if the original rule had narrower access restrictions.

## Features

- List, search, and refresh system IPv4/TCP port proxy rules.
- Create, edit, and delete host-to-VM TCP mappings.
- Optionally create and manage this tool's Windows Firewall rules.
- Test TCP connectivity to the VM target.
- Validate addresses and ports; detect listener conflicts, external changes, and obvious forwarding loops.
- Attempt to roll back completed steps when a multi-step operation fails. Recovery errors are reported and require a refresh.
- Shortcuts: `F5` refreshes, `Ctrl+F` focuses search, and `Alt+N` adds a mapping. Double-click a rule to edit it.

## Requirements and limitations

- Windows 10/11 x64.
- VM networking must be configured, and the host must be able to reach the VM IP and service port.
- The Windows **IP Helper** service must be running. The client reports its state but does not change service settings.
- Windows `portproxy` supports **IPv4 TCP only**; UDP is not supported.
- Hyper-V switches, Internet Connection Sharing, routers, and the VM's internal firewall are not configured automatically.
- Update mappings if the VM IP changes; a stable VM address is recommended.

**Test target** checks only the host-to-VM TCP connection. It does not verify HTTP or another application protocol and does not guarantee public Internet reachability. The firewall column reports only whether this tool's corresponding rule exists; it cannot determine the effect of group policy or other firewall rules.

## Troubleshooting

- **Administrator permission required:** Return to the main window and select **Restart as administrator**. Cancelling UAC leaves the current window open.
- **Port is in use or a rule conflicts:** Choose another port. `0.0.0.0:8080` conflicts with port `8080` on any specific local address.
- **Connection refused:** Check that the VM service is running and listening on its VM interface, not only on `127.0.0.1`.
- **Connection timed out:** Check the Hyper-V switch, VM IP address, VM firewall, and host networking.
- **Target is reachable but external access fails:** Check the host firewall and use the real host IP. Public access also requires appropriate forwarding on the upstream router or cloud platform.
- **A rule changed elsewhere:** Refresh and select the rule again before editing to avoid overwriting external changes.
- **An operation or recovery fails:** Refresh first, then inspect `netsh interface portproxy show v4tov4` from an administrator terminal. A timeout may leave partial system changes; do not clear unrelated system rules.

## Build and test

Install the .NET 8 SDK, then run:

```powershell
dotnet build .\HyperVPortMapper.sln -c Release
dotnet run --project .\tests\HyperVPortMapper.Checks -c Release
dotnet run --project .\src\HyperVPortMapper -c Release
```

The checks use an in-memory command executor to cover validation, conflicts, operation failures, and recovery. A local TCP listener verifies connectivity tests. The checks do not modify the host's real port proxy or firewall configuration.

Validation completed on the development machine: 15 core checks pass, the Release build has no warnings or errors, and the published EXE has passed UI checks under a standard user account. Administrator writes and end-to-end forwarding to a real VM were not exercised by the automated checks.

Publish a self-contained Windows x64 single-file executable with:

```powershell
dotnet publish .\src\HyperVPortMapper -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\release\win-x64
```
