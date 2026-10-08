using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VoiceLocalCli.Infrastructure;

/// <summary>
/// A Win32 Job Object with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: every process
/// assigned to this job is torn down by the kernel the instant the job's last handle
/// closes, which happens automatically when this (the owning) process dies for any
/// reason -- a crash, Task Manager "End Task", <c>taskkill /F</c>. No code needs to run at
/// the moment of death for this to work, unlike <see cref="ConsoleCtrlHandler"/>'s
/// graceful path, which only covers the "the user closed the window normally" case. This
/// is the structural fix for the bug that motivated this whole orchestrator: an earlier
/// PowerShell-based version left ffmpeg recording in the background for 35+ minutes after
/// its console window was closed via the X button instead of the documented stop key.
///
/// One static job object for the whole process lifetime; every child process gets
/// assigned to it as soon as it starts.
/// </summary>
internal static class ProcessJobObject
{
    private static readonly Lazy<SafeJobObjectHandle> JobHandle = new(CreateJob, isThreadSafe: true);

    internal static void EnsureProcessIsTracked(Process process)
    {
        SafeJobObjectHandle job = JobHandle.Value;
        if (!AssignProcessToJobObject(job, process.Handle))
        {
            // Historically fails if the process is already in another job that doesn't
            // allow breakaway (rare outside nested containers/CI runners) -- not fatal:
            // the graceful ConsoleCtrlHandler path still covers the common case, this is
            // only the hard-kill guarantee layer.
            int error = Marshal.GetLastWin32Error();
            Trace.TraceWarning($"Could not assign process {process.Id} to the orphan-prevention job object (Win32 error {error}). The graceful stop path is unaffected; only the hard-kill guarantee is weaker for this process.");
        }
    }

    private static SafeJobObjectHandle CreateJob()
    {
        SafeJobObjectHandle job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");
        }

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        };
        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = info,
        };

        int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }

        return job;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobObjectHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeJobObjectHandle hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobObjectHandle hJob, IntPtr hProcess);

    private sealed class SafeJobObjectHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobObjectHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
