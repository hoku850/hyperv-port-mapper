namespace HyperVPortMapper;

// 颜色、尺寸和字体统一定义，便于维护与高分屏调整。
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(245, 247, 251);
    public static readonly Color Surface = Color.White;
    // 正文与辅助文字均用于浅色背景，保持可读对比。
    public static readonly Color Text = Color.FromArgb(26, 39, 60);
    // 蓝色用于主要动作，错误和成功同时配合文字说明。
    public static readonly Color Muted = Color.FromArgb(86, 101, 122);
    public static readonly Color Primary = Color.FromArgb(37, 89, 190);
    public static readonly Color Danger = Color.FromArgb(175, 38, 38);
    public static readonly Color Success = Color.FromArgb(15, 112, 77);
    // 表格选中状态保持深色文字，避免白底浅色文字。
    public static readonly Color Border = Color.FromArgb(216, 225, 238);
    public static readonly Color Selection = Color.FromArgb(225, 237, 255);
    // 中文优先使用系统字体，不依赖联网加载字体文件。
    public static readonly Color Header = Color.FromArgb(237, 242, 249);
    public static readonly Font BodyFont = new("Microsoft YaHei UI", 10F);
    // 字体只创建一套，应用存活期间复用。
    public static readonly Font TitleFont = new("Microsoft YaHei UI", 23F, FontStyle.Bold);
    public static readonly Font SectionFont = new("Microsoft YaHei UI", 11F, FontStyle.Bold);
    public static readonly Size WindowSize = new(1120, 760);
    public static readonly Size MinimumWindowSize = new(980, 650);
    // 表单使用足够高度展示多行错误和网络范围说明。
    public static readonly Size DialogSize = new(660, 660);
    public const int Gap = 24;
    public const int SmallGap = 8;
    public const int ButtonHeight = 40;
    // 各个区域使用相同的节奏，不在控件中散布尺寸。
    public const int TitleHeight = 108;
    public const int PermissionHeight = 76;
    public const int ToolbarHeight = 78;
    // 主窗口最低尺寸也必须容纳这些固定区域。
    public const int ActionHeight = 70;
    public const int NoteHeight = 58;
    public const int StatusHeight = 32;
    public const int GridRowHeight = 44;
    // 表格列宽和表单区域高度集中调整。
    public const int AddressColumnWidth = 145;
    public const int PortColumnWidth = 70;
    public const int ProtocolColumnWidth = 75;
    // 地址、端口、连接状态的最小宽度反映各自内容。
    public const int TargetPortColumnWidth = 85;
    public const int FirewallColumnWidth = 155;
    public const int ConnectionColumnWidth = 120;
    // 各高度依次对应说明、主机、提示、虚拟机与防火墙区域。
    public static readonly int[] DialogRowHeights = [62, 30, 64, 34, 30, 64, 34, 38, 48];
    public const int GridHeaderHeight = 48;
    // 原生控件保留焦点框和键盘快捷键。
    public static Button Button(string text, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, Height = ButtonHeight, MinimumSize = new(112, ButtonHeight),
            // 自动宽度使中文或系统字体变大时不裁切文字。
            Padding = new(SmallGap, 0, SmallGap, 0), Margin = new(0, 0, SmallGap, 0),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
            BackColor = primary ? Primary : Surface, ForeColor = primary ? Surface : danger ? Danger : Text
        };
        // 主按钮使用实心底色，危险次要动作保留浅底。
        // 次要按钮边框与表格保持一致。
        button.FlatAppearance.BorderColor = primary ? Primary : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Primary : Header;
        return button;
    }
    // 标签保留原生文本语义，不使用图片代替中文文字。

    public static Label Label(string text, bool muted = false) => new()
    {
        // 所有文字允许跟随容器重排，并保留中文可访问名称。
        Text = text, ForeColor = muted ? Muted : Text, Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, AutoEllipsis = true
    };
    // 布局容器只管理排列，业务状态由窗口负责。

    public static TableLayoutPanel Table(int columns = 1) => new()
    {
        // 自适应布局用于所有窗口，避免绝对定位在高分屏上错位。
        ColumnCount = columns, Dock = DockStyle.Fill, Margin = Padding.Empty,
        BackColor = Color.Transparent, Padding = Padding.Empty
    };
}
