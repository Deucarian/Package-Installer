using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal interface IDevelopmentGitRunner
    {
        Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken);
    }

    internal sealed class DevelopmentGitResult
    {
        public DevelopmentGitResult(int exitCode, string output, string failure = "")
        { ExitCode = exitCode; Output = output ?? ""; Failure = failure ?? ""; }
        public int ExitCode { get; }
        // Raw output is internal parsing data. Never log it or expose stderr from Git.
        public string Output { get; }
        public string Failure { get; }
        public bool Success => ExitCode == 0 && Failure.Length == 0;
        public string RequireSuccess()
        {
            if (!Success) throw new DevelopmentGitException(Failure.Length == 0
                ? "Git could not complete this operation. Review the repository in your Git client." : Failure);
            return Output;
        }
    }

    internal sealed class DevelopmentGitException : Exception
    {
        public DevelopmentGitException(string message) : base(message) { }
    }

    /// <summary>Package-scoped transport. Explicit cwd, no shell, bounded pipes, no argument/output logging.</summary>
    internal sealed class DevelopmentGitProcessRunner : IDevelopmentGitRunner
    {
        internal const string NotRepository = "The selected folder is not a Git repository.";
        private readonly int _timeoutMilliseconds;
        private readonly int _outputLimit;
        public DevelopmentGitProcessRunner(int timeoutMilliseconds = 120000, int outputLimit = 2097152)
        {
            if (timeoutMilliseconds < 1 || outputLimit < 1) throw new ArgumentOutOfRangeException();
            _timeoutMilliseconds = timeoutMilliseconds;
            _outputLimit = outputLimit;
        }

        public Task<DevelopmentGitResult> RunAsync(string directory, IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
                throw new ArgumentException("An absolute repository working directory is required.");
            // Disable the optional fsmonitor hook so even read-only inspection cannot invoke repository scripts.
            // Commit/checkout/push protection hooks remain enabled and use normal Git behavior.
            string commandLine = "-c core.fsmonitor=false " + string.Join(" ", arguments.Select(QuoteArgument));
            return Task.Run(() => Run(directory, commandLine, cancellationToken), CancellationToken.None);
        }

        // ProcessStartInfo.ArgumentList is unavailable on the Unity 2021.3 profile.
        // Quoting every argument with the CRT/Mono escape rules preserves literal pathspecs and messages.
        internal static string QuoteArgument(string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("Invalid Git argument.");
            StringBuilder quoted = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') quoted.Append('\\', slashes * 2 + 1).Append('"');
                else quoted.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        private DevelopmentGitResult Run(string directory, string arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ProcessStartInfo start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // Inherited GIT_DIR/GIT_WORK_TREE/GIT_INDEX_FILE/config overrides must never redirect scope.
            foreach (string key in start.EnvironmentVariables.Keys.Cast<string>().ToArray())
                if (key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)) start.EnvironmentVariables.Remove(key);
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            start.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            IDevelopmentGitChildProcess child;
            try { child = DevelopmentGitChildProcess.Start(start); }
            catch (Win32Exception) { return new DevelopmentGitResult(-1, "", "Git or safe process containment is unavailable. Install Git (and setsid on Linux/macOS), then restart Unity."); }
            using (IDevelopmentGitChildProcess process = child)
            {
                BoundedPipe output = new BoundedPipe(_outputLimit);
                BoundedPipe error = new BoundedPipe(_outputLimit);
                Task stdout = Task.Run(() => output.Read(process.StandardOutput));
                Task stderr = Task.Run(() => error.Read(process.StandardError));
                Stopwatch elapsed = Stopwatch.StartNew();
                while (!process.WaitForExit(50))
                {
                    if (!token.IsCancellationRequested && elapsed.ElapsedMilliseconds < _timeoutMilliseconds &&
                        !output.Exceeded && !error.Exceeded) continue;
                    process.Kill();
                    // Drain tasks are bounded too: a credential helper can outlive its parent process.
                    Task.WaitAll(new[] { stdout, stderr }, 1000);
                    token.ThrowIfCancellationRequested();
                    return new DevelopmentGitResult(-1, "", output.Exceeded || error.Exceeded
                        ? "Git output exceeded the safe display limit. Use your external Git client."
                        : "Git timed out. Refresh repository state before retrying; a remote operation may have completed.");
                }
                if (!Task.WaitAll(new[] { stdout, stderr }, 1000))
                    return new DevelopmentGitResult(-1, "", "Git did not close its output streams. Refresh before retrying.");
                token.ThrowIfCancellationRequested();
                if (output.Exceeded || error.Exceeded)
                    return new DevelopmentGitResult(-1, "", "Git output exceeded the safe display limit. Use your external Git client.");
                return new DevelopmentGitResult(process.ExitCode, output.Value,
                    process.ExitCode == 0 ? "" : ClassifyFailure(error.Value));
            }
        }

        private static string ClassifyFailure(string error)
        {
            if (error.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) >= 0) return NotRepository;
            if (error.IndexOf("non-fast-forward", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("[rejected]", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Push rejected. Fetch, review the divergence, and resolve it in your external Git client.";
            if (error.IndexOf("Authentication", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.IndexOf("could not read Username", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Git authentication failed. Sign in using your existing Git credential manager, then retry.";
            return "Git could not complete this operation. Review repository permissions, hooks, conflicts, or network access in your Git client.";
        }

        private sealed class BoundedPipe
        {
            private readonly int _limit;
            private readonly StringBuilder _value = new StringBuilder();
            private volatile bool _exceeded;
            public BoundedPipe(int limit) { _limit = limit; }
            public bool Exceeded => _exceeded;
            public string Value => _value.ToString();
            public void Read(StreamReader reader)
            {
                char[] buffer = new char[4096];
                try
                {
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        int remaining = _limit - _value.Length;
                        if (count > remaining) _exceeded = true;
                        if (remaining > 0) _value.Append(buffer, 0, Math.Min(count, remaining));
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
