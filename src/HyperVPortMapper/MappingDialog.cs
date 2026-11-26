using HyperVPortMapper.Core;

namespace HyperVPortMapper;

// 新增和编辑共用一张表单，保存失败时保留全部输入。
internal sealed class MappingDialog : Form
{
    // 服务负责真实系统写入，原始快照用于检查外部修改。
    private readonly WindowsMappingService _service;
    private readonly MappingEntry? _original;
    private readonly ComboBox _listen = new();
    // 端口用数值控件限制范围，地址仍做严格入口校验。
    private readonly NumericUpDown _listenPort = PortInput();
    private readonly TextBox _target = new();
    private readonly NumericUpDown _targetPort = PortInput();
    private readonly CheckBox _firewall = new();
    // 多行反馈可滚动，恢复失败等长错误也可以完整查看。
    private readonly TextBox _feedback = new();
    // 保存、取消和检测共用忙碌状态，防止重复提交。
    private readonly Button _save = Theme.Button("保存映射 (&S)", primary: true);
    private readonly Button _cancel = Theme.Button("取消");
    private readonly Button _test = Theme.Button("检测目标");
    private bool _busy;

    public MappingDialog(WindowsMappingService service, MappingEntry? original)
    {
        // 新建没有原记录，编辑则保留完整旧值。
        _service = service;
        _original = original;
        Text = original is null ? "新增端口映射" : "编辑端口映射";
        ClientSize = Theme.DialogSize;
        // 对话框不允许缩至字段无法阅读，支持系统字体缩放。
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        // 表单沿用主界面的颜色和字体。
        BackColor = Theme.Surface;
        Font = Theme.BodyFont;
        // 先建立布局，再填充原规则，避免事件引用未建控件。
        BuildLayout();
        LoadValues();
        _save.Click += async (_, _) => await SaveAsync();
        _test.Click += async (_, _) => await TestAsync();
        // 保存中不能退出，其他时刻 Esc 与取消保持原生行为。
        _cancel.Click += (_, _) => Close();
        AcceptButton = _save;
        CancelButton = _cancel;
        // 用户关闭窗口只取消输入，不撤销已经完成的系统变更。
        _cancel.DialogResult = DialogResult.Cancel;
        FormClosing += (_, args) => { if (_busy) args.Cancel = true; };
    }

    private void BuildLayout()
    {
        // 各区高度集中在主题中，反馈区使用剩余空间。
        var root = Theme.Table();
        root.Padding = new Padding(Theme.Gap);
        foreach (var height in Theme.DialogRowHeights)
            root.RowStyles.Add(new(SizeType.Absolute, height));
        // 操作按钮固定在下方，长错误只滚动反馈区。
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.Absolute, Theme.ActionHeight));
        root.RowCount = root.RowStyles.Count;
        // 主机和虚拟机分区沿着转发方向排列。
        BuildEndpointSections(root);
        BuildFirewallSection(root);
        root.Controls.Add(BuildFeedback(), 0, 9);
        // 操作区最后加入，键盘可按视觉顺序进入。
        root.Controls.Add(BuildButtons(), 0, 10);
        Controls.Add(root);
    }

    private void BuildEndpointSections(TableLayoutPanel root)
    {
        // 在输入前说明系统规则会立即生效。
        root.Controls.Add(Theme.Label("将宿主机端口转发到虚拟机服务。\n保存后立即写入系统，关闭客户端仍会生效。", true), 0, 0);
        var source = Theme.Label("01  宿主机监听端点");
        source.Font = Theme.SectionFont;
        // 宿主机监听允许所有网卡，也允许仅回环访问。
        root.Controls.Add(source, 0, 1);
        root.Controls.Add(EndpointFields("监听地址", _listen, "监听端口", _listenPort), 0, 2);
        root.Controls.Add(Theme.Label("0.0.0.0 表示所有网卡；127.0.0.1 仅供宿主机本地访问。", true), 0, 3);
        // 虚拟机端点只接受完整 IPv4 和有效 TCP 端口。
        var target = Theme.Label("02  虚拟机目标端点");
        target.Font = Theme.SectionFont;
        root.Controls.Add(target, 0, 4);
        root.Controls.Add(EndpointFields("虚拟机 IPv4", _target, "服务端口", _targetPort), 0, 5);
        // 示例只作说明，不自动作为要保存的真实目标。
        root.Controls.Add(Theme.Label($"请填写虚拟机当前的 IPv4 地址，例如 {AppSettings.ExampleTargetAddress}。", true), 0, 6);
    }

    private void BuildFirewallSection(TableLayoutPanel root)
    {
        // 放行范围直接说明，用户主动选择才会创建规则。
        _firewall.Text = "由本工具管理此端口的 Windows 防火墙入站放行规则";
        _firewall.Dock = DockStyle.Fill;
        root.Controls.Add(_firewall, 0, 7);
        // 取消勾选只移除工具拥有的规则，其他规则保留。
        root.Controls.Add(Theme.Label("勾选后：允许所有来源、所有网络配置文件访问此监听端口。\n不勾选：移除本工具已有的对应规则，其他防火墙规则不变。", true), 0, 8);
    }

    private Control BuildFeedback()
    {
        // 反馈保留完整文本，错误可以复制和滚动查看。
        _feedback.Multiline = _feedback.ReadOnly = true;
        _feedback.ScrollBars = ScrollBars.Vertical;
        _feedback.BorderStyle = BorderStyle.None;
        // 使用与表单一致的底色，避免被误认为可编辑输入。
        _feedback.BackColor = Theme.Surface;
        _feedback.ForeColor = Theme.Muted;
        _feedback.Dock = DockStyle.Fill;
        // 辅助功能使用稳定名称，不随错误内容变化。
        _feedback.AccessibleName = "参数验证与操作结果";
        return _feedback;
    }

    private static Control EndpointFields(string addressLabel, Control address, string portLabel, Control port)
    {
        var fields = Theme.Table(2);
        // 地址字段占大部分空间，端口字段仍留足五位数字。
        fields.ColumnStyles.Add(new(SizeType.Percent, 70));
        fields.ColumnStyles.Add(new(SizeType.Percent, 30));
        // 标签和输入各占一行，标签不会被占位文字替代。
        fields.RowStyles.Add(new(SizeType.Percent, 45));
        fields.RowStyles.Add(new(SizeType.Percent, 55));
        fields.Controls.Add(Theme.Label(addressLabel), 0, 0);
        fields.Controls.Add(Theme.Label(portLabel), 1, 0);
        // 可访问名称与视觉标签一致，键盘顺序从地址到端口。
        address.AccessibleName = addressLabel;
        port.AccessibleName = portLabel;
        address.Dock = port.Dock = DockStyle.Fill;
        // 两字段之间留固定间距，地址可随表单宽度增长。
        address.Margin = new(0, 0, Theme.SmallGap, 0);
        port.Margin = Padding.Empty;
        // 地址在前、端口在后，对应填写顺序。
        fields.Controls.Add(address, 0, 1);
        fields.Controls.Add(port, 1, 1);
        return fields;
    }

    private Control BuildButtons()
    {
        // 操作区域固定，长错误不会挤走保存按钮。
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            Padding = new(0, Theme.SmallGap, 0, 0), WrapContents = false };
        // 从右往左依次是取消、主要保存、次要检测。
        panel.Controls.AddRange([_cancel, _save, _test]);
        return panel;
    }

    private static NumericUpDown PortInput() => new()
    {
        // 范围与核心校验共享常量，键盘输入也受约束。
        Minimum = MappingDefaults.MinPort, Maximum = MappingDefaults.MaxPort,
        Value = MappingDefaults.MinPort, ThousandsSeparator = false
    };

    private void LoadValues()
    {
        // 下拉建议仍允许输入，最终由系统网卡检查兜底。
        _listen.DropDownStyle = ComboBoxStyle.DropDown;
        _listen.Items.Add(MappingDefaults.AnyAddress);
        // 网卡地址只是建议，最终保存仍检查是否属于本机。
        try { _listen.Items.AddRange(WindowsMappingService.LocalAddresses()); }
        catch (Exception error) { _feedback.Text = $"无法枚举网卡：{error.Message}"; }
        // 原记录精确回填，避免编辑时重置已有端口。
        var mapping = _original?.Mapping;
        _listen.Text = mapping?.ListenAddress ?? MappingDefaults.AnyAddress;
        _listenPort.Value = mapping?.ListenPort ?? AppSettings.DefaultListenPort;
        // 不预填虚拟机地址，避免把示例误当成真实环境。
        _target.Text = mapping?.TargetAddress ?? "";
        _target.PlaceholderText = AppSettings.ExampleTargetAddress;
        _targetPort.Value = mapping?.TargetPort ?? AppSettings.DefaultTargetPort;
        // 只有工具已拥有对应防火墙规则时才回填勾选。
        _firewall.Checked = _original?.HasManagedFirewall ?? false;
        // 明确普通权限限制，同时仍允许验证输入和检测目标。
        if (!WindowsMappingService.IsAdministrator)
            _feedback.Text = "当前为普通权限：可填写与检测目标。保存前请返回主窗口，以管理员身份重启。";
    }

    private PortMapping ReadInput() => new(_listen.Text.Trim(), (int)_listenPort.Value,
        // 只去除用户误输的首尾空格，不修改目标地址本身。
        _target.Text.Trim(), (int)_targetPort.Value);

    private async Task SaveAsync()
    {
        // 表单忙碌时不能用 Enter 发起第二次保存。
        if (_busy) return;
        var mapping = ReadInput();
        var problem = MappingValidator.Validate(mapping);
        // 参数错误就地反馈，避免无意义的管理员请求。
        if (problem is not null) { Feedback(problem, true); return; }
        // 输入通过后先锁住控件，再等待系统写入和复查。
        SetBusy(true);
        Feedback("正在保存并复查系统规则…");
        try
        {
            await _service.SaveAsync(_original, mapping, _firewall.Checked);
            // 只有系统复查成功才关闭，失败时保留输入。
            DialogResult = DialogResult.OK;
            _busy = false;
            Close();
        }
        // 系统拒绝或恢复失败时保持窗口和全部输入。
        catch (Exception error) { Feedback(error.Message, true); }
        finally { if (!IsDisposed) SetBusy(false); }
    }

    private async Task TestAsync()
    {
        // 检测端点在等待前复制，不读取过程中变化的字段。
        if (_busy) return;
        var mapping = ReadInput();
        // 检测只依赖目标端点，无需监听地址或管理员权限。
        if (!MappingValidator.IsIPv4(mapping.TargetAddress, false)) { Feedback("请先填写有效的虚拟机 IPv4 地址。", true); return; }
        // 连接检测与系统保存共用忙碌反馈。
        SetBusy(true);
        Feedback($"正在检测 {mapping.TargetEndpoint}…");
        try
        {
            var result = await ConnectivityProbe.CheckAsync(mapping.TargetAddress, mapping.TargetPort);
            // 反馈文字包含目标与耗时，颜色只作辅助。
            Feedback($"{mapping.TargetEndpoint}：{result.Message}（{result.ElapsedMilliseconds} ms）。\r\n此结果只说明目标 TCP 连接，不代表外网访问结果。", !result.Success);
        }
        // 检测异常只影响结果，完成后恢复全部输入。
        catch (Exception error) { Feedback(error.Message, true); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        // 同时锁住输入，防止在异步保存期间修改正在提交的内容。
        foreach (var control in new Control[] { _save, _cancel, _test, _listen, _listenPort, _target, _targetPort, _firewall })
            control.Enabled = !busy;
    }

    private void Feedback(string message, bool error = false)
    {
        // 保留完整错误，可通过 Ctrl+A、Ctrl+C 复制排查。
        _feedback.ForeColor = error ? Theme.Danger : Theme.Muted;
        _feedback.Text = message;
    }
}
