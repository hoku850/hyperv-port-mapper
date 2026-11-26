namespace HyperVPortMapper.Core;

// 每个系统步骤记录正向命令、逆向命令和允许恢复的状态。
internal sealed record MappingStep(
    NetshCommand Forward,
    NetshCommand Undo,
    PortMapping? ExpectedMapping = null,
    bool ExpectMappingAbsent = false);

// 系统变更只依赖受控命令执行器与当前规则读取器。
public sealed class MappingManager(
    Func<NetshCommand, Task> execute,
    Func<IReadOnlyList<MappingEntry>> readCurrent,
    Func<IReadOnlySet<string>>? readFirewallNames = null)
{
    private const string MutationLockName = "Local\\HyperVPortMapper.MappingChanges";
    private static readonly Semaphore MutationGate = new(1, 1, MutationLockName);

    public async Task SaveAsync(MappingEntry? original, PortMapping mapping, bool openFirewall,
        IReadOnlyList<MappingEntry> current)
    {
        // 系统写入入口重复校验，不依赖窗口是否经过验证。
        var problem = MappingValidator.Validate(mapping);
        if (problem is not null) throw new InvalidOperationException(problem);
        CheckOriginal(original, current);
        // 单独读取工具防火墙名称，以发现没有转发的残留放行。
        var firewallNames = readFirewallNames?.Invoke() ?? new HashSet<string>();
        CheckManagedFirewall(original, mapping, openFirewall, firewallNames);
        CheckConflicts(original, mapping, current);
        // 每项配置变更均包含对应的逆操作。
        var steps = new List<MappingStep>();
        AddMappingSteps(steps, original, mapping);
        // 创建新放行前，先确保新转发已经建立。
        AddNewFirewallStep(steps, original, mapping, openFirewall);
        AddOldFirewallStep(steps, original, mapping, openFirewall);
        await ExecuteAsync(steps, current, firewallNames);
    }

    public async Task DeleteAsync(MappingEntry original, IReadOnlyList<MappingEntry> current)
    {
        // 删除前确认读取的规则仍与用户所选快照一致。
        CheckOriginal(original, current);
        var mapping = original.Mapping;
        // 失败恢复只在原监听端点仍为空闲时重建规则。
        var steps = new List<MappingStep>
        {
            new(new(NetshOperation.DeleteMapping, mapping), new(NetshOperation.AddMapping, mapping), ExpectMappingAbsent: true)
        };
        // 仅处理当前映射对应的工具规则，不触碰其他软件规则。
        if (original.HasManagedFirewall)
            steps.Add(new(new(NetshOperation.DeleteFirewall, mapping), new(NetshOperation.AddFirewall, mapping)));
        await ExecuteAsync(steps, current, readFirewallNames?.Invoke());
    }

    // 监听不同具体地址时可共享端口，但通配地址会与具体地址冲突。
    public static bool Overlaps(PortMapping first, PortMapping second) => first.ListenPort == second.ListenPort
        && (first.ListenAddress == second.ListenAddress || first.ListenAddress == MappingDefaults.AnyAddress
            || second.ListenAddress == MappingDefaults.AnyAddress);

    private static void CheckOriginal(MappingEntry? original, IReadOnlyList<MappingEntry> current)
    {
        if (original is null) return;
        var actual = current.FirstOrDefault(entry => entry.Mapping.Key == original.Mapping.Key);
        // 编辑期间系统规则若被改动，要求刷新以保护外部更新。
        if (actual != original) throw new InvalidOperationException("规则已被其他操作修改或删除，请刷新后重试。 ");
    }

    private static void CheckManagedFirewall(MappingEntry? original, PortMapping mapping,
        bool openFirewall, IReadOnlySet<string> firewallNames)
    {
        // 已有孤立规则可能包含自定义限制，不能静默复用或扩大范围。
        if (firewallNames.Contains(mapping.FirewallName) && original?.Mapping.Key != mapping.Key)
            throw new InvalidOperationException("此监听地址已有本工具的防火墙规则但没有对应映射。请先检查规则再操作。 ");
        // 更换端点并继续放行会创建新规则，要求用户先明确移除旧规则。
        if (original is { HasManagedFirewall: true } && original.Mapping.Key != mapping.Key && openFirewall)
            throw new InvalidOperationException("为避免改变已有防火墙限制，请先取消防火墙放行并保存，再编辑监听端点。 ");
    }

    private static void CheckConflicts(MappingEntry? original, PortMapping mapping,
        IReadOnlyList<MappingEntry> current)
    {
        // 检查精确地址和通配地址之间的监听冲突。
        if (current.Any(entry => entry != original && Overlaps(entry.Mapping, mapping)))
            throw new InvalidOperationException("监听地址和端口与已有规则冲突，请刷新后选择其他端口。 ");
    }

    private static void AddMappingSteps(List<MappingStep> steps, MappingEntry? original, PortMapping mapping)
    {
        // 未修改映射时不调用系统命令。
        if (original?.Mapping == mapping) return;
        if (original is null)
        {
            // 新建失败时只撤销本次添加的目标端点。
            steps.Add(new(new(NetshOperation.AddMapping, mapping), new(NetshOperation.DeleteMapping, mapping), mapping));
            return;
        }
        var previous = original.Mapping;
        if (previous.Key == mapping.Key)
        {
            // 相同监听键使用 set 修改虚拟机目标，并保存旧值用于回滚。
            steps.Add(new(new(NetshOperation.SetMapping, mapping), new(NetshOperation.SetMapping, previous), mapping));
        }
        else
        {
            // 更换监听地址或端口时先删旧规则，再建立新规则。
            steps.Add(new(new(NetshOperation.DeleteMapping, previous), new(NetshOperation.AddMapping, previous), ExpectMappingAbsent: true));
            steps.Add(new(new(NetshOperation.AddMapping, mapping), new(NetshOperation.DeleteMapping, mapping), mapping));
        }
    }

    private static void AddNewFirewallStep(List<MappingStep> steps, MappingEntry? original,
        PortMapping mapping, bool openFirewall)
    {
        // 防火墙只跟踪宿主机监听端点，不跟踪虚拟机目标端口。
        var sameKey = original?.Mapping.Key == mapping.Key;
        if (openFirewall && (original?.HasManagedFirewall != true || !sameKey))
            steps.Add(new(new(NetshOperation.AddFirewall, mapping), new(NetshOperation.DeleteFirewall, mapping)));
    }

    private static void AddOldFirewallStep(List<MappingStep> steps, MappingEntry? original,
        PortMapping mapping, bool openFirewall)
    {
        // 等映射与新放行均成功后才删除旧监听端点规则。
        var sameKey = original?.Mapping.Key == mapping.Key;
        if (original is { HasManagedFirewall: true } && (!sameKey || !openFirewall))
            steps.Add(new(new(NetshOperation.DeleteFirewall, original.Mapping), new(NetshOperation.AddFirewall, original.Mapping)));
    }

    private async Task ExecuteAsync(IEnumerable<MappingStep> steps,
        IReadOnlyList<MappingEntry> expectedMappings, IReadOnlySet<string>? expectedFirewalls)
    {
        // 同一台电脑上的多个客户端共享同一写入锁。
        var completed = new Stack<MappingStep>();
        await Task.Run(() => MutationGate.WaitOne());
        try
        {
            // 等待锁期间若系统快照有变化，则在执行前拒绝旧操作。
            EnsureSnapshotsMatch(expectedMappings, expectedFirewalls);
            await ExecuteStepsAsync(steps, completed);
        }
        catch (Exception error)
        {
            if (completed.Count == 0) throw;
            await RecoverAsync(completed, error);
            throw;
        }
        finally { MutationGate.Release(); }
    }

    private void EnsureSnapshotsMatch(IReadOnlyList<MappingEntry> expectedMappings,
        IReadOnlySet<string>? expectedFirewalls)
    {
        if (!readCurrent().SequenceEqual(expectedMappings))
            throw new InvalidOperationException("操作开始前端口映射已发生变化，请刷新后重试。 ");
        if (expectedFirewalls is not null && readFirewallNames is not null
            && !expectedFirewalls.SetEquals(readFirewallNames()))
            throw new InvalidOperationException("操作开始前防火墙规则已发生变化，请刷新后重试。 ");
    }

    private async Task ExecuteStepsAsync(IEnumerable<MappingStep> steps, Stack<MappingStep> completed)
    {
        // 正向命令成功后才将对应逆操作加入回滚栈。
        foreach (var step in steps)
        {
            await execute(step.Forward);
            completed.Push(step);
        }
    }

    private async Task RecoverAsync(Stack<MappingStep> completed, Exception error)
    {
        var failures = new List<string>();
        while (completed.TryPop(out var step))
        {
            // 外部已更新转发时保留现状，不以旧值覆盖新规则。
            if (!await MatchesExpectedAsync(step, failures)) continue;
            try { await execute(step.Undo); }
            catch (Exception recovery) { failures.Add(recovery.Message); }
        }
        var status = failures.Count == 0 ? "已恢复已完成的变更。" : $"恢复失败：{string.Join("；", failures)}";
        throw new InvalidOperationException($"操作失败：{error.Message}\n{status}\n请刷新并核对系统实际状态。", error);
    }

    private async Task<bool> MatchesExpectedAsync(MappingStep step, List<string> failures)
    {
        // 防火墙步骤不单独改变映射，转发回滚需读取当前目标状态。
        if (step.ExpectedMapping is null && !step.ExpectMappingAbsent) return true;
        var mapping = step.ExpectedMapping ?? step.Undo.Mapping;
        try
        {
            var current = (await Task.Run(readCurrent)).FirstOrDefault(entry => entry.Mapping.Key == mapping.Key)?.Mapping;
            var matches = step.ExpectMappingAbsent ? current is null : current == step.ExpectedMapping;
            if (!matches) failures.Add("规则已被其他操作修改，已保留现状。");
            return matches;
        }
        catch (Exception readError)
        {
            failures.Add($"无法核对转发规则：{readError.Message}");
            return false;
        }
    }
}
