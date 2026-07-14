using System;
using System.IO;
using System.Threading;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageInstallerAtomicFileCommitter
    {
        private static readonly object CommitGate = new object();
        private static readonly int[] RetryDelaysMilliseconds = { 10, 25, 50 };

        private readonly Func<string, bool> _fileExists;
        private readonly Action<string, string> _replace;
        private readonly Action<string, string> _move;
        private readonly Action<string> _delete;
        private readonly Action<int> _delay;

        public PackageInstallerAtomicFileCommitter()
            : this(
                File.Exists,
                (sourcePath, destinationPath) => File.Replace(sourcePath, destinationPath, null),
                File.Move,
                File.Delete,
                Thread.Sleep)
        {
        }

        internal PackageInstallerAtomicFileCommitter(
            Func<string, bool> fileExists,
            Action<string, string> replace,
            Action<string, string> move,
            Action<int> delay)
            : this(fileExists, replace, move, File.Delete, delay)
        {
        }

        internal PackageInstallerAtomicFileCommitter(
            Func<string, bool> fileExists,
            Action<string, string> replace,
            Action<string, string> move,
            Action<string> delete,
            Action<int> delay)
        {
            _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
            _replace = replace ?? throw new ArgumentNullException(nameof(replace));
            _move = move ?? throw new ArgumentNullException(nameof(move));
            _delete = delete ?? throw new ArgumentNullException(nameof(delete));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        internal static PackageInstallerAtomicFileCommitter Shared { get; } =
            new PackageInstallerAtomicFileCommitter();

        public void Commit(
            string temporaryPath,
            string destinationPath,
            Action beforeAttempt = null)
        {
            if (string.IsNullOrWhiteSpace(temporaryPath))
            {
                throw new ArgumentException("Temporary path is required.", nameof(temporaryPath));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Destination path is required.", nameof(destinationPath));
            }

            lock (CommitGate)
            {
                for (int attempt = 0; ; attempt++)
                {
                    beforeAttempt?.Invoke();

                    try
                    {
                        if (_fileExists(destinationPath))
                        {
                            _replace(temporaryPath, destinationPath);
                        }
                        else
                        {
                            _move(temporaryPath, destinationPath);
                        }

                        return;
                    }
                    catch (IOException) when (attempt < RetryDelaysMilliseconds.Length)
                    {
                        beforeAttempt?.Invoke();
                        _delay(RetryDelaysMilliseconds[attempt]);
                    }
                }
            }
        }

        public void Delete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            lock (CommitGate)
            {
                for (int attempt = 0; ; attempt++)
                {
                    if (!_fileExists(path))
                    {
                        return;
                    }

                    try
                    {
                        _delete(path);
                        return;
                    }
                    catch (IOException) when (attempt < RetryDelaysMilliseconds.Length)
                    {
                        _delay(RetryDelaysMilliseconds[attempt]);
                    }
                }
            }
        }
    }
}
