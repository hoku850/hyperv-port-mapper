using System.Diagnostics;
using System.Net.Sockets;

namespace HyperVPortMapper.Core;

// 只报告 TCP 是否可达，不把连接成功当成应用服务正常。
public sealed record ProbeResult(bool Success, string Message, long ElapsedMilliseconds);

public static class ConnectivityProbe
    // 调用方可等待检测完成，也可在不再需要结果时取消。
{
    public static async Task<ProbeResult> CheckAsync(string address, int port,
        CancellationToken cancellationToken = default)
    {
        // 拒绝非法端口，保留调用方取消，不将其误报为超时。
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!MappingValidator.IsPort(port)) throw new ArgumentOutOfRangeException(nameof(port));
        cancellationToken.ThrowIfCancellationRequested();
        // 用单独的超时源限制 DNS 和连接等待总时长。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MappingDefaults.ProbeTimeoutMs);
        // 客户端离开作用域即释放套接字，不长期保留检测连接。
        using var client = new TcpClient(AddressFamily.InterNetwork);
        var clock = Stopwatch.StartNew();
        try
        {
            // 异步连接避免界面在虚拟机无响应时卡住。
            await client.ConnectAsync(address, port, timeout.Token);
            return new(true, "目标 TCP 端口可连接", clock.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 内部超时是检测结果，用户取消则向上传递。
            return new(false, "连接超时，请检查虚拟机地址、服务和防火墙", clock.ElapsedMilliseconds);
        }
        catch (SocketException error)
        {
            // 常见错误转换为可操作的中文提示。
            // 拒绝与不可达对应不同排查方向，保留其他系统错误。
            var message = error.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "连接被拒绝，请检查虚拟机服务是否监听该端口",
                SocketError.HostNotFound => "目标名称无法解析，请检查虚拟机地址",
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => "目标网络不可达，请检查虚拟交换机与 IP",
                _ => $"无法连接目标：{error.Message}"
            };
            return new(false, message, clock.ElapsedMilliseconds);
        }
    }
}
