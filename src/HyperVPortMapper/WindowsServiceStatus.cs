using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HyperVPortMapper;

// 只查询系统服务状态，使用 Windows 原生接口避免额外程序集。
internal static class WindowsServiceStatus
{
    private const uint ManagerConnect = 0x0001;
    private const uint QueryStatus = 0x0004;
    // SERVICE_RUNNING 是 Windows 定义的稳定状态值。
    private const uint Running = 4;

    // 查询只申请连接与读状态权限，普通用户也可以执行。
    public static bool IsRunning(string name)
    {
        // 先打开服务管理器，失败时立即抛出 Windows 错误。
        using var manager = OpenSCManager(null, null, ManagerConnect);
        if (manager.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        // 每个句柄在离开作用域时释放，不保留服务修改权限。
        using var service = OpenService(manager, name, QueryStatus);
        if (service.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!QueryServiceStatus(service, out var status)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return status.CurrentState == Running;
    }

    // 不读取或更改启动类型，仅使用当前运行状态。
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        // 字段顺序必须与 Windows SERVICE_STATUS 结构保持一致。
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        // 剩余原生字段即使不使用也需要保留完整布局。
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        // 原生互操作要求存在可调用的无参构造函数。
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }

    // 明确 Unicode 和错误码语义，防止本地服务名被错误转换。
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    // 服务句柄只能查询，不能被此工具用于启动或停止服务。
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);

    // 查询接口不写入任何服务配置。
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(ServiceHandle service, out ServiceStatus status);

    // SafeHandle 在异常路径也会释放原生句柄。
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
