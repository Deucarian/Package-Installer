using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Deucarian.PackageInstaller.Editor.Development
{
    /// <summary>Starts Git suspended, assigns its entire descendant tree to a job, then permits execution.</summary>
    internal sealed class DevelopmentGitWindowsProcess : IDevelopmentGitChildProcess
    {
        private SafeFileHandle _job, _process;
        private StreamReader _output, _error;
        public StreamReader StandardOutput => _output;
        public StreamReader StandardError => _error;
        public int ExitCode
        {
            get { if (!GetExitCodeProcess(_process, out uint code)) throw new Win32Exception(); return unchecked((int)code); }
        }

        public DevelopmentGitWindowsProcess(ProcessStartInfo start)
        {
            try { StartContained(start); }
            catch { Dispose(); throw; }
        }

        private void StartContained(ProcessStartInfo start)
        {
            _job = CreateJobObject(IntPtr.Zero, null);
            if (_job.IsInvalid) throw new Win32Exception();
            ExtendedLimits limits = new ExtendedLimits();
            limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE; no child breakaway permitted.
            if (!SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) throw new Win32Exception();
            SecurityAttributes security = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), InheritHandle = true };
            if (!CreatePipe(out SafeFileHandle outputRead, out SafeFileHandle outputWrite, ref security, 0)) throw new Win32Exception();
            using (outputRead)
            using (outputWrite)
            {
                if (!CreatePipe(out SafeFileHandle errorRead, out SafeFileHandle errorWrite, ref security, 0)) throw new Win32Exception();
                using (errorRead)
                using (errorWrite)
                {
                    if (!CreatePipe(out SafeFileHandle inputRead, out SafeFileHandle inputWrite, ref security, 0)) throw new Win32Exception();
                    using (inputRead)
                    using (inputWrite)
                    {
                        if (!SetHandleInformation(outputRead, 1, 0) || !SetHandleInformation(errorRead, 1, 0) ||
                            !SetHandleInformation(inputWrite, 1, 0)) throw new Win32Exception();
                        using (InheritedHandles handles = new InheritedHandles(inputRead, outputWrite, errorWrite))
                        {
                            StartupInfoEx startup = new StartupInfoEx();
                            startup.Startup.Size = Marshal.SizeOf(typeof(StartupInfoEx));
                            startup.Startup.Flags = 0x100; // STARTF_USESTDHANDLES.
                            startup.Startup.Input = inputRead.DangerousGetHandle();
                            startup.Startup.Output = outputWrite.DangerousGetHandle();
                            startup.Startup.Error = errorWrite.DangerousGetHandle();
                            startup.Attributes = handles.Pointer;
                            string environment = string.Join("\0", start.EnvironmentVariables.Cast<DictionaryEntry>()
                                .OrderBy(entry => (string)entry.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(entry => entry.Key + "=" + entry.Value)) + "\0\0";
                            IntPtr environmentPointer = Marshal.StringToHGlobalUni(environment);
                            ProcessInformation created;
                            bool success;
                            try
                            {
                                success = CreateProcess(start.FileName, new StringBuilder(DevelopmentGitProcessRunner.QuoteArgument(start.FileName) + " " + start.Arguments),
                                    IntPtr.Zero, IntPtr.Zero, true, 0x08080404, environmentPointer, start.WorkingDirectory,
                                    ref startup, out created); // NO_WINDOW | EXTENDED_STARTUPINFO | UNICODE_ENV | SUSPENDED.
                            }
                            finally { Marshal.FreeHGlobal(environmentPointer); }
                            if (!success) throw new Win32Exception();
                            _process = new SafeFileHandle(created.Process, true);
                            using (SafeFileHandle thread = new SafeFileHandle(created.Thread, true))
                            {
                                if (!AssignProcessToJobObject(_job, _process))
                                { TerminateProcess(_process, 1); throw new Win32Exception(); }
                                // Readers own duplicated non-inheritable handles; local pipe handles close before returning.
                                _output = Reader(outputRead);
                                _error = Reader(errorRead);
                                if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception();
                            }
                        }
                    }
                }
            }
        }
        private static StreamReader Reader(SafeFileHandle source)
        {
            IntPtr current = GetCurrentProcess();
            if (!DuplicateHandle(current, source, current, out SafeFileHandle duplicate, 0, false, 2)) throw new Win32Exception();
            return new StreamReader(new FileStream(duplicate, FileAccess.Read), new UTF8Encoding(false), true, 4096);
        }
        public bool WaitForExit(int milliseconds)
        {
            uint result = WaitForSingleObject(_process, (uint)milliseconds);
            if (result == uint.MaxValue) throw new Win32Exception();
            return result == 0;
        }
        public void Kill()
        {
            if (_job != null && !_job.IsInvalid && !_job.IsClosed)
            {
                TerminateJobObject(_job, 1);
                // A lock is never released while normal job descendants are still terminating.
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 5000 && QueryInformationJobObject(_job, 1,
                    out BasicAccounting accounting, (uint)Marshal.SizeOf(typeof(BasicAccounting)), IntPtr.Zero) && accounting.ActiveProcesses != 0)
                    System.Threading.Thread.Sleep(10);
            }
        }
        public void Dispose()
        {
            Kill();
            _output?.Dispose(); _error?.Dispose(); _process?.Dispose(); _job?.Dispose();
        }

        private sealed class InheritedHandles : IDisposable
        {
            public IntPtr Pointer { get; private set; }
            private IntPtr _handles;
            private bool _initialized;
            public InheritedHandles(params SafeFileHandle[] handles)
            {
                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                Pointer = Marshal.AllocHGlobal(size);
                _handles = Marshal.AllocHGlobal(IntPtr.Size * handles.Length);
                if (!InitializeProcThreadAttributeList(Pointer, 1, 0, ref size)) { Dispose(); throw new Win32Exception(); }
                _initialized = true;
                for (int i = 0; i < handles.Length; i++) Marshal.WriteIntPtr(_handles, i * IntPtr.Size, handles[i].DangerousGetHandle());
                if (!UpdateProcThreadAttribute(Pointer, 0, (IntPtr)0x20002, _handles, (IntPtr)(IntPtr.Size * handles.Length), IntPtr.Zero, IntPtr.Zero))
                { Dispose(); throw new Win32Exception(); }
            }
            public void Dispose()
            {
                if (Pointer != IntPtr.Zero) { if (_initialized) DeleteProcThreadAttributeList(Pointer); Marshal.FreeHGlobal(Pointer); Pointer = IntPtr.Zero; }
                if (_handles != IntPtr.Zero) { Marshal.FreeHGlobal(_handles); _handles = IntPtr.Zero; }
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicAccounting { public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime; public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public int Size; public string Reserved, Desktop, Title; public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags; public ushort Show, ReservedSize; public IntPtr ReservedBytes, Input, Output, Error; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Startup; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits information, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, out BasicAccounting information, uint size, IntPtr returnedSize);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInformation process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(SafeFileHandle process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(SafeFileHandle process, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle target, uint access, bool inherit, uint options);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnedSize);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    }
}
