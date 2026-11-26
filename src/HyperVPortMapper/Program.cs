namespace HyperVPortMapper;

// 启动入口只配置界面环境，网络规则由用户操作触发。
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 使用原生高分屏配置与中文系统字体。
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(Theme.BodyFont);
        // 意外错误仍通过中文对话框反馈给当前用户。
        Application.ThreadException += (_, args) => MessageBox.Show(args.Exception.Message,
            // 意外界面异常仍需明确显示，不能静默吞掉。
            "发生错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm());
    }
}
