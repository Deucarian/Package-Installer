using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal interface IDevelopmentGitChildProcess : IDisposable
    {
        StreamReader StandardOutput { get; }
        StreamReader StandardError { get; }
        bool WaitForExit(int milliseconds);
        int ExitCode { get; }
        void Kill();
    }

    internal static class DevelopmentGitChildProcess
    {
        internal static IDevelopmentGitChildProcess Start(ProcessStartInfo start)
        {
            start.FileName = FindExecutable(Path.DirectorySeparatorChar == '\\' ? "git.exe" : "git");
            return Path.DirectorySeparatorChar == '\\'
                ? (IDevelopmentGitChildProcess)new DevelopmentGitWindowsProcess(start)
                : new DevelopmentGitPosixProcess(start, FindExecutable("setsid"));
        }

        private static string FindExecutable(string name)
        {
            // Search explicit PATH directories only; never the selected repository/current directory.
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string directory = entry.Trim('"');
                if (!Path.IsPathRooted(directory)) continue;
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) return path;
            }
            throw new Win32Exception("The required Git process containment executable is unavailable.");
        }
    }

    /// <summary>Linux uses setsid to exec Git as a process-group leader; the whole group is retired together.</summary>
    internal sealed class DevelopmentGitPosixProcess : IDevelopmentGitChildProcess
    {
        private readonly Process _process;
        public DevelopmentGitPosixProcess(ProcessStartInfo start, string setsid)
        {
            start.Arguments = "-- " + DevelopmentGitProcessRunner.QuoteArgument(start.FileName) + " " + start.Arguments;
            start.FileName = setsid;
            _process = Process.Start(start);
        }
        public StreamReader StandardOutput => _process.StandardOutput;
        public StreamReader StandardError => _process.StandardError;
        public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);
        public int ExitCode => _process.ExitCode;
        public void Kill()
        {
            // setsid execs without --fork, so its PID is the Git process group ID, including after parent exit.
            KillGroup(-_process.Id, 9);
            try { if (!_process.HasExited) _process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
        public void Dispose() { Kill(); _process.Dispose(); }
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int KillGroup(int pid, int signal);
    }
}
