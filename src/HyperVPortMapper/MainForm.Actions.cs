using System.ComponentModel;
using HyperVPortMapper.Core;

namespace HyperVPortMapper;

// 事件只协调界面与服务，不在按钮处理函数中拼接命令。
internal sealed partial class MainForm
{
    private void ConnectActions()
    {
        // 刷新和搜索分别更新系统快照与当前列表。
        _refresh.Click += async (_, _) => await RefreshAsync();
        _search.TextChanged += (_, _) => ApplyFilter();
        // 双击和按钮共用编辑入口，权限与错误反馈保持一致。
        _add.Click += async (_, _) => await OpenEditorAsync(null);
        _edit.Click += async (_, _) => await OpenEditorAsync(Selected);
        _grid.CellDoubleClick += async (_, args) => { if (args.RowIndex >= 0) await OpenEditorAsync(Selected); };
        // 选择变化后立即同步按钮可用状态。
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        // 检测与删除都只作用于当前选择的单条规则。
        _probe.Click += async (_, _) => await ProbeSelectedAsync();
        _delete.Click += async (_, _) => await DeleteSelectedAsync();
        _elevate.Click += (_, _) => Elevate();
        // F5 刷新，Ctrl+F 定位搜索；原生按钮助记键仍有效。
        KeyPreview = true;
        KeyDown += async (_, args) =>
        {
            // 快捷键遵守相同的忙碌状态，不能绕开按钮禁用。
            if (args.KeyCode == Keys.F5 && !_busy) { args.Handled = true; await RefreshAsync(); }
            if (args.Control && args.KeyCode == Keys.F && !_busy) { args.Handled = true; _search.Focus(); }
        };
    }

    private async Task OpenEditorAsync(MappingEntry? entry)
    {
        // 读取失败时没有可靠快照，不能进入编辑流程。
        if (_busy || !_loaded) return;
        // 关闭对话框后重新读取，包含可能的失败恢复或外部修改。
        using var dialog = new MappingDialog(_service, entry);
        dialog.ShowDialog(this);
        await RefreshAsync();
    }

    private async Task ProbeSelectedAsync()
    {
        // 在异步等待前固定选中对象，避免目标端点漂移。
        var selected = Selected;
        if (_busy || selected is null) return;
        var mapping = selected.Mapping;
        // 只检测虚拟机目标，结果不会声称外网已经可用。
        SetBusy(true, $"正在检测 {mapping.TargetEndpoint}…");
        try
        {
            var result = await ConnectivityProbe.CheckAsync(mapping.TargetAddress, mapping.TargetPort);
            // 结果仅缓存在本窗口，下一次系统刷新后清除。
            _probeResults[mapping.Key] = result.Success ? $"可连接 · {result.ElapsedMilliseconds} ms" : "无法连接";
            ApplyFilter();
            // 详细原因显示在状态栏和提示框，便于下一步排查。
            SetStatus($"{mapping.TargetEndpoint} · {result.Message}", !result.Success);
            MessageBox.Show(this, $"目标：{mapping.TargetEndpoint}\n{result.Message}\n耗时：{result.ElapsedMilliseconds} ms\n\n此检测只检查目标 TCP 连接，不代表外网访问结果。",
                "目标连通性", MessageBoxButtons.OK, result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        // 无论连接成功或失败，都要恢复主窗口操作。
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); }
    }

    private async Task DeleteSelectedAsync()
    {
        // 权限检查在确认删除之前，避免无权限时误导用户。
        var selected = Selected;
        if (_busy || selected is null) return;
        try { WindowsMappingService.EnsureAdministrator(); }
        catch (Exception error) { ShowError(error); return; }
        // 确认框明确显示完整方向，避免误删相邻端口。
        var mapping = selected.Mapping;
        var firewall = selected.HasManagedFirewall ? "\n本工具创建的对应防火墙规则也会删除。" : "";
        // 默认焦点放在否，回车不会无意删除规则。
        if (MessageBox.Show(this, $"确定删除这条映射？\n\n{mapping.ListenEndpoint}  →  {mapping.TargetEndpoint}{firewall}",
            "删除端口映射", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        // 删除期间保留窗口，避免退出导致恢复被中断。
        SetBusy(true, "正在删除映射…");
        var succeeded = false;
        try { await _service.DeleteAsync(selected); succeeded = true; }
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); }
        // 失败恢复可能改变系统状态，成功失败都重新读取。
        await RefreshAsync();
        if (succeeded) SetStatus($"已删除 {mapping.ListenEndpoint}");
    }

    private void Elevate()
    {
        // 提权只响应用户主动点击，不在启动时自动触发。
        if (_busy) return;
        try
        {
            // UAC 成功启动后才退出当前只读窗口。
            WindowsMappingService.RestartElevated();
            Close();
        }
        catch (Win32Exception error) when (error.NativeErrorCode == AppSettings.UacCancelledError)
        {
            // 取消授权是正常操作，当前窗口继续可用。
            SetStatus("已取消管理员授权，可继续查看和检测规则。 ");
        }
        catch (Exception error) { ShowError(error); }
    }

    // 错误显示统一保留原始系统信息，便于核对实际状态。
    private void ShowError(Exception error)
    {
        // 完整系统错误保留在对话框中，状态栏仅作醒目提示。
        SetStatus("操作未完成，请查看错误信息并刷新规则。", true);
        MessageBox.Show(this, error.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
