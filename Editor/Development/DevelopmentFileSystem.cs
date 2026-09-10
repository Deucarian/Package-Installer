using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal interface IDevelopmentFileSystem
    {
        string Canonicalize(string existingPath);
        bool FileExists(string path);
        bool DirectoryExists(string path);
        string ReadText(string path);
        bool IsBinary(string path);
        string Fingerprint(string path);
        IDisposable Lock(string existingDirectory, string name);
    }

    /// <summary>Resolves physical filesystem identity, including Windows junctions and linked Git worktrees.</summary>
    internal sealed class DevelopmentFileSystem : IDevelopmentFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public string ReadText(string path)
        {
            if (new FileInfo(path).Length > 1048576) throw new DevelopmentGitException("Package metadata is too large.");
            return File.ReadAllText(path);
        }
        public bool IsBinary(string path)
        {
            if (!File.Exists(path)) return false;
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] bytes = new byte[8192];
                int count = stream.Read(bytes, 0, bytes.Length);
                for (int i = 0; i < count; i++) if (bytes[i] == 0) return true;
                return false;
            }
        }
        public string Fingerprint(string path)
        {
            if (!File.Exists(path)) return "missing";
            if (new FileInfo(path).Length > 67108864)
                throw new DevelopmentGitException("Changed files larger than 64 MiB require your external Git client.");
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream));
        }
        public IDisposable Lock(string existingDirectory, string name)
        {
            if (!DevelopmentGitPolicy.SamePath(existingDirectory, Canonicalize(existingDirectory)))
                throw new DevelopmentGitException("Repository metadata moved. Inspect the checkout again before acting.");
            string lockPath = Path.Combine(existingDirectory, name);
            DevelopmentSourceStoragePaths.RequireRegular(existingDirectory, lockPath);
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { throw new DevelopmentGitException("Another package operation owns this repository. Wait and refresh."); }
        }
        public string Canonicalize(string existingPath)
        {
            string path = Path.GetFullPath(existingPath);
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new DevelopmentGitException("The selected repository or package path no longer exists.");
            return Path.DirectorySeparatorChar == '\\' ? WindowsPath(path) : UnixPath(path);
        }

        private static string WindowsPath(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new DevelopmentGitException("Cannot establish the physical repository path.");
                StringBuilder result = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, result, (uint)result.Capacity, 0);
                if (length == 0 || length >= result.Capacity)
                    throw new DevelopmentGitException("Cannot establish the physical repository path.");
                string canonical = result.ToString();
                if (canonical.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
                    canonical = "\\\\" + canonical.Substring(8);
                else if (canonical.StartsWith("\\\\?\\", StringComparison.Ordinal)) canonical = canonical.Substring(4);
                canonical = Path.GetFullPath(canonical);
                return canonical.Length > Path.GetPathRoot(canonical).Length ? canonical.TrimEnd(Path.DirectorySeparatorChar) : canonical;
            }
        }
        private static string UnixPath(string path)
        {
            IntPtr resolved = RealPath(path, IntPtr.Zero);
            if (resolved == IntPtr.Zero) throw new DevelopmentGitException("Cannot establish the physical repository path.");
            try { string result = Marshal.PtrToStringAnsi(resolved); return result.Length > 1 ? result.TrimEnd('/') : result; }
            finally { Free(resolved); }
        }
        // Exact native path APIs are confined to this Editor adapter; no assembly scanning or reflection.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
        [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPath(string path, IntPtr buffer);
        [DllImport("libc", EntryPoint = "free")]
        private static extern void Free(IntPtr pointer);
    }
}
