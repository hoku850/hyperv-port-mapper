using System.Diagnostics;
using System.Globalization;
using System.Text;
using HyperVPortMapper.Core;

namespace HyperVPortMapper;

// 限定系统 netsh 路径，避免从工作目录执行同名程序。
internal static class NetshRunner
{
    // 调用点只能传入已定义的网络操作，不能执行任意程序。
    public static async Task ExecuteAsync(NetshCommand command)
    {
        // 注册 Windows 代码页，以便显示本地化系统错误。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        // 不启用 shell，所有用户输入均为独立参数。
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            // 中文 Windows 返回 OEM 编码错误文字。
            StandardOutputEncoding = encoding, StandardErrorEncoding = encoding
        };
        // 参数逐一传递，地址或名称中的字符不会被 shell 执行。
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动系统网络配置程序。 ");
        // 同时读取两个流，防止错误输出填满缓冲区造成等待。
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        // 限制命令等待时间，防止网络配置程序长期挂住窗口。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(AppSettings.CommandTimeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // 超时后停止命令，调用层会恢复已经完成的步骤。
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
            throw new InvalidOperationException("系统命令等待超时，部分配置可能已执行。 ");
        }
        var detail = ((await output) + "\n" + (await errors)).Trim();
        // 退出失败必须反馈，不能仅凭界面未抛错宣称成功。
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"netsh 返回 {process.ExitCode}：{detail}");
    }
}
