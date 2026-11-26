using HyperVPortMapper.Core;

namespace HyperVPortMapper;

// 状态与数据刷新集中在主窗口，布局和用户动作分别放在同名文件。
internal sealed partial class MainForm : Form
{
    // 服务只在保存和删除入口申请写入，表格始终只读。
    private readonly WindowsMappingService _service = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    // 规则为空、搜索无结果和加载失败使用不同文字。
    private readonly Label _empty = Theme.Label("");
    private readonly Label _permission = Theme.Label("");
    private readonly Label _status = Theme.Label("正在读取系统规则…", true);
    // 数量随搜索同步更新，不把过滤后的数量当成系统总数。
    private readonly Label _count = Theme.Label("规则列表");
    // 所有按钮在忙碌期间禁用，避免重复执行系统操作。
    private readonly Button _add = Theme.Button("新增映射 (&N)", primary: true);
    private readonly Button _refresh = Theme.Button("刷新 (&R)");
    private readonly Button _elevate = Theme.Button("以管理员身份重启");
    // 单条规则的动作共用当前行选择。
    private readonly Button _edit = Theme.Button("编辑 (&E)");
    private readonly Button _probe = Theme.Button("检测目标 (&T)");
    private readonly Button _delete = Theme.Button("删除 (&D)", danger: true);
    // 检测结果仅在当前快照有效，刷新时清除。
    private readonly Dictionary<string, string> _probeResults = [];
    private IReadOnlyList<MappingEntry> _entries = [];
    private bool _loaded;
    // 忙碌标记防止刷新、检测和删除互相交叉。
    private bool _busy;

    public MainForm()
    {
        // 统一窗口尺寸和最小宽度，确保表格各列可见。
        Text = AppSettings.Title;
        ClientSize = Theme.WindowSize;
        MinimumSize = Theme.MinimumWindowSize;
        // 居中启动，高分屏由布局管理器负责缩放。
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        Font = Theme.BodyFont;
        // 事件在布局完成后绑定，刷新时控件已经就绪。
        BuildLayout();
        ConnectActions();
        Shown += async (_, _) => await RefreshAsync();
        // 操作中关闭会中断变更恢复，所以等待当前操作完成。
        FormClosing += (_, args) => { if (_busy) args.Cancel = true; };
    }

    // 行标签保存原始快照，编辑时可以检测外部修改。
    private MappingEntry? Selected => _grid.CurrentRow?.Tag as MappingEntry;

    private async Task RefreshAsync()
    {
        // 重复刷新直接忽略，不并发读取和替换当前选择。
        if (_busy) return;
        SetBusy(true, "正在读取系统规则…");
        try
        {
            // 注册表与服务读取放到后台线程，界面保留响应。
            var result = await Task.Run(() => (_service.Read(), WindowsMappingService.HelperStatus()));
            _entries = result.Item1;
            _probeResults.Clear();
            // 只有完整读取成功后才开放规则操作。
            _loaded = true;
            var permission = WindowsMappingService.IsAdministrator ? "管理员模式 · 可以保存和删除" : "只读权限 · 保存和删除前请以管理员身份重启";
            _permission.Text = $"{permission}\n{result.Item2}";
            // 搜索条件保留，但检测结果要重新确认。
            ApplyFilter();
            SetStatus($"已刷新 · 共 {_entries.Count} 条 TCP 映射 · {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception error)
        {
            // 读取失败必须清空旧数据显示错误，禁止基于旧快照写入。
            _loaded = false;
            _entries = [];
            ApplyFilter();
            // 加载失败直接显示原因，避免误认为系统没有配置。
            _empty.Text = $"无法读取系统规则\n\n{error.Message}\n\n请检查权限后点击刷新。";
            SetStatus("读取失败，请刷新重试", true);
        }
        finally { SetBusy(false); }
    }

    private void ApplyFilter()
    {
        // 重新筛选时尽量恢复用户原先选中的端口。
        var selectedKey = Selected?.Mapping.Key;
        var query = _search.Text.Trim();
        // 对 IP 与端口的完整端点搜索，不引入额外索引。
        var visible = _entries.Where(entry => $"{entry.Mapping.ListenEndpoint} {entry.Mapping.TargetEndpoint}"
            .Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _grid.Rows.Clear();
        // 每一行绑定完整对象，显示文本不作为写入依据。
        foreach (var entry in visible)
        {
            var mapping = entry.Mapping;
            // 列表不声称防火墙最终有效，只显示本工具规则是否存在。
            var index = _grid.Rows.Add(mapping.ListenAddress, mapping.ListenPort, "TCP →", mapping.TargetAddress,
                mapping.TargetPort, entry.HasManagedFirewall ? "有本工具规则" : "未由本工具管理",
                _probeResults.GetValueOrDefault(mapping.Key, "未检测"));
            // 当前单元格恢复同时更新下面的单条规则动作。
            _grid.Rows[index].Tag = entry;
            if (mapping.Key == selectedKey) _grid.CurrentCell = _grid.Rows[index].Cells[0];
        }
        // 空列表覆盖层同时提供下一步操作说明。
        _count.Text = $"规则列表    {visible.Length} / {_entries.Count}";
        _empty.Visible = visible.Length == 0;
        if (_loaded) _empty.Text = _entries.Count == 0 ? "还没有端口映射\n\n点击“新增映射”，连接你的第一项虚拟机服务。"
            : "没有匹配的规则\n\n试试其他 IP 或端口，或清空搜索。";
        UpdateButtons();
    }

    private void SetBusy(bool busy, string? message = null)
    {
        // 操作执行期间禁用变更按钮，并显示等待光标。
        _busy = busy;
        UseWaitCursor = busy;
        // 状态文字与按钮禁用共同反馈正在进行的操作。
        if (message is not null) SetStatus(message);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        // 读取失败禁用新增，刷新和授权入口仍可使用。
        _add.Enabled = !_busy && _loaded;
        _refresh.Enabled = !_busy;
        _elevate.Enabled = !_busy && !WindowsMappingService.IsAdministrator;
        // 只读权限可以打开编辑表单检查参数，保存入口会明确拦截。
        _edit.Enabled = _probe.Enabled = _delete.Enabled = !_busy && _loaded && Selected is not null;
        // 搜索或切换行不能改变正在执行的操作对象。
        _search.Enabled = !_busy;
        _grid.Enabled = !_busy;
    }

    private void SetStatus(string text, bool error = false)
    {
        // 错误也通过文字区分，不仅依靠颜色。
        _status.Text = text;
        _status.ForeColor = error ? Theme.Danger : Theme.Muted;
    }
}
