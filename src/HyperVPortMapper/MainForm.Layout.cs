namespace HyperVPortMapper;

// 主窗口只组织原生控件，不通过自绘替代键盘和辅助功能。
internal sealed partial class MainForm
{
    private void BuildLayout()
    {
        // 主表格控制整体高度，中间列表自动扩展。
        var root = Theme.Table();
        root.RowCount = 7;
        // 上下固定区域，中间表格占据剩余空间。
        foreach (var height in new[] { Theme.TitleHeight, Theme.PermissionHeight, Theme.ToolbarHeight })
            root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        // 底部操作、说明与结果始终保持可见。
        foreach (var height in new[] { Theme.ActionHeight, Theme.NoteHeight, Theme.StatusHeight })
            root.RowStyles.Add(new(SizeType.Absolute, height));
        // 按照视觉顺序添加，默认键盘顺序与界面一致。
        root.Controls.Add(BuildTitle(), 0, 0);
        root.Controls.Add(BuildPermission(), 0, 1);
        root.Controls.Add(BuildToolbar(), 0, 2);
        // 规则表格和空状态共享中间区域。
        root.Controls.Add(BuildGrid(), 0, 3);
        root.Controls.Add(BuildActions(), 0, 4);
        var note = Theme.Label("仅支持 IPv4 / TCP。请先确保宿主机可以访问虚拟机服务。\n关闭客户端后，系统映射仍然保留。", true);
        // 底部说明预留两行，避免缩小窗口后遮挡。
        note.Padding = new(Theme.Gap, 0, Theme.Gap, 0);
        root.Controls.Add(note, 0, 5);
        // 状态栏保持浅色底，操作结果易于定位。
        _status.Padding = new(Theme.Gap, 0, Theme.Gap, 0);
        root.Controls.Add(_status, 0, 6);
        Controls.Add(root);
    }

    private static Control BuildTitle()
    {
        // 标题使用独立白色区域，与操作区分开。
        var panel = Theme.Table();
        panel.BackColor = Theme.Surface;
        panel.Padding = new(Theme.Gap, Theme.SmallGap, Theme.Gap, Theme.SmallGap);
        // 标题与用途说明分开，避免界面出现实现命令细节。
        panel.RowCount = 2;
        panel.RowStyles.Add(new(SizeType.Percent, 65));
        panel.RowStyles.Add(new(SizeType.Percent, 35));
        // 放大的中文标题不改变其他控件的默认字号。
        var title = Theme.Label(AppSettings.Title);
        title.Font = Theme.TitleFont;
        // 标题下用一句话说明流向。
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(Theme.Label("宿主机端口  →  虚拟机服务        IPv4 / TCP", true), 0, 1);
        return panel;
    }

    private Control BuildPermission()
    {
        // 提权入口与状态放在同一区域，用户可以直接解决权限问题。
        var panel = Theme.Table(2);
        panel.Margin = new(Theme.Gap, Theme.SmallGap, Theme.Gap, 0);
        panel.BackColor = Theme.Header;
        // 左侧状态自适应，右侧按钮按内容决定宽度。
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new(SizeType.AutoSize));
        _permission.Padding = new(Theme.SmallGap, 0, 0, 0);
        // 首次读取完成之前显示明确的等待状态。
        _permission.Text = "正在检查权限与 IP Helper…";
        panel.Controls.Add(_permission, 0, 0);
        // 管理员模式保留按钮位置并禁用，避免布局跳动。
        _elevate.Anchor = AnchorStyles.None;
        panel.Controls.Add(_elevate, 1, 0);
        return panel;
    }

    private Control BuildToolbar()
    {
        // 工具栏两侧固定动作，中间空间随窗口改变。
        var panel = Theme.Table(4);
        panel.Padding = new(Theme.Gap, Theme.SmallGap, Theme.Gap, Theme.SmallGap);
        // 主操作靠左，搜索靠右，剩余空间用于规则数量。
        panel.ColumnStyles.Add(new(SizeType.AutoSize));
        panel.ColumnStyles.Add(new(SizeType.AutoSize));
        panel.ColumnStyles.Add(new(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new(SizeType.Percent, 65));
        // 按钮垂直居中，避免随搜索框默认高度拉伸。
        _add.Anchor = _refresh.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_add, 0, 0);
        panel.Controls.Add(_refresh, 1, 0);
        // 显式名称供屏幕阅读器与键盘识别。
        _search.PlaceholderText = "搜索 IP 地址或端口";
        _search.AccessibleName = "搜索 IP 地址或端口";
        _search.Dock = DockStyle.Fill;
        // 搜索框保持横向扩展，输入长 IPv4 也可阅读。
        _search.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_search, 3, 0);
        // 数量放在搜索旁，搜索后的过滤结果易于识别。
        _count.Padding = new(Theme.SmallGap, 0, Theme.SmallGap, 0);
        panel.Controls.Add(_count, 2, 0);
        return panel;
    }

    private Control BuildGrid()
    {
        // 列表与空状态共用同一个白色容器。
        var panel = new Panel { Dock = DockStyle.Fill, Margin = new(Theme.Gap, 0, Theme.Gap, 0), BackColor = Theme.Surface };
        ConfigureGrid();
        AddGridColumns();
        // 空数据时给出下一步，有规则时显示完整表头。
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.ForeColor = Theme.Muted;
        panel.Controls.Add(_grid);
        // 覆盖层放在最上方，过滤更新时只需改变可见状态。
        panel.Controls.Add(_empty);
        _empty.BringToFront();
        return panel;
    }

    private void ConfigureGrid()
    {
        // 原生表格提供完整行选择和辅助功能语义。
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        // 直接改表格不会写系统，所有修改都经过表单校验。
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = _grid.MultiSelect = _grid.RowHeadersVisible = false;
        // 保持单条规则操作，避免批量删除造成意外影响。
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EnableHeadersVisualStyles = false;
        // 列头保持固定高度，数据区随窗口增大。
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = Theme.GridHeaderHeight;
        _grid.ColumnHeadersDefaultCellStyle = new() { BackColor = Theme.Header, ForeColor = Theme.Text, Font = Theme.BodyFont };
        // 淡色选中背景搭配深色字，焦点和选中不依赖颜色推断。
        _grid.RowTemplate.Height = Theme.GridRowHeight;
        _grid.DefaultCellStyle = new() { BackColor = Theme.Surface, ForeColor = Theme.Text,
            SelectionBackColor = Theme.Selection, SelectionForeColor = Theme.Text, Padding = new(Theme.SmallGap, 0, 0, 0) };
        // 横向分隔线提高同一行端点的辨识度。
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Theme.Border;
        _grid.AccessibleName = "端口映射规则列表";
    }

    private void AddGridColumns()
    {
        // 宿主机端点在前，虚拟机端点在后，与实际流向一致。
        AddColumn("宿主机地址", Theme.AddressColumnWidth);
        AddColumn("端口", Theme.PortColumnWidth);
        AddColumn("协议", Theme.ProtocolColumnWidth);
        // 两端地址使用相同宽度，减少视觉偏向。
        AddColumn("虚拟机地址", Theme.AddressColumnWidth);
        AddColumn("目标端口", Theme.TargetPortColumnWidth);
        AddColumn("防火墙", Theme.FirewallColumnWidth);
        // 防火墙只代表工具规则存在，目标连接是单独的主动检测。
        AddColumn("目标连接", Theme.ConnectionColumnWidth);
    }

    private void AddColumn(string title, float weight)
    {
        // 所有列使用一致的只读设置与最小宽度。
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight,
            MinimumWidth = (int)weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    }

    private Control BuildActions()
    {
        // 操作区固定在列表下方，不随数据行数滚动。
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill,
            Padding = new(Theme.Gap, Theme.SmallGap, Theme.Gap, Theme.SmallGap), WrapContents = false };
        // 删除放在次要动作之后，使用明确文字并二次确认。
        panel.Controls.AddRange([_edit, _probe, _delete]);
        return panel;
    }
}
