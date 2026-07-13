using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Deucarian.PackageInstaller.Editor
{
    internal interface IPackageFileOperations
    {
        bool DirectoryExists(string path);

        bool FileExists(string path);

        void CreateDirectory(string path);

        string[] GetFiles(string path);

        string[] GetDirectories(string path);

        void CopyFile(string sourcePath, string destinationPath);

        void MoveDirectory(string sourcePath, string destinationPath);

        void DeleteDirectory(string path, bool recursive);

        long GetFileLength(string path);

        byte[] ComputeSha256(string path);
    }

    internal sealed class SystemPackageFileOperations : IPackageFileOperations
    {
        public bool DirectoryExists(string path) => Directory.Exists(path);

        public bool FileExists(string path) => File.Exists(path);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public string[] GetFiles(string path) => Directory.GetFiles(path);

        public string[] GetDirectories(string path) => Directory.GetDirectories(path);

        public void CopyFile(string sourcePath, string destinationPath) =>
            File.Copy(sourcePath, destinationPath, false);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            Directory.Move(sourcePath, destinationPath);

        public void DeleteDirectory(string path, bool recursive) =>
            Directory.Delete(path, recursive);

        public long GetFileLength(string path) => new FileInfo(path).Length;

        public byte[] ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(stream);
            }
        }
    }

    internal enum PackageSampleStageResultState
    {
        Imported,
        AlreadyExists,
        Canceled,
        Failed
    }

    internal sealed class PackageSampleStageResult
    {
        public PackageSampleStageResult(PackageSampleStageResultState state, string message)
        {
            State = state;
            Message = message ?? string.Empty;
        }

        public PackageSampleStageResultState State { get; }

        public string Message { get; }
    }

    internal sealed class PackageSampleStagingImporter
    {
        private readonly IPackageFileOperations _files;

        public PackageSampleStagingImporter(IPackageFileOperations files = null)
        {
            _files = files ?? new SystemPackageFileOperations();
        }

        public PackageSampleStageResult Import(
            string sourcePath,
            string destinationPath,
            string stagingRootPath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !_files.DirectoryExists(sourcePath))
            {
                return Failed("Sample source folder is unavailable.");
            }

            if (string.IsNullOrWhiteSpace(destinationPath) || string.IsNullOrWhiteSpace(stagingRootPath))
            {
                return Failed("Sample destination or staging path is unavailable.");
            }

            if (_files.DirectoryExists(destinationPath) || _files.FileExists(destinationPath))
            {
                return new PackageSampleStageResult(
                    PackageSampleStageResultState.AlreadyExists,
                    "Sample already imported.");
            }

            string operationPath = Path.Combine(stagingRootPath, Guid.NewGuid().ToString("N"));
            string stagedContentPath = Path.Combine(operationPath, "content");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _files.CreateDirectory(stagedContentPath);
                CopyDirectory(sourcePath, stagedContentPath, cancellationToken);
                ValidateCopy(sourcePath, stagedContentPath, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (_files.DirectoryExists(destinationPath) || _files.FileExists(destinationPath))
                {
                    return new PackageSampleStageResult(
                        PackageSampleStageResultState.AlreadyExists,
                        "Sample destination appeared while the import was being staged.");
                }

                string destinationParent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationParent))
                {
                    _files.CreateDirectory(destinationParent);
                }

                _files.MoveDirectory(stagedContentPath, destinationPath);
                return new PackageSampleStageResult(
                    PackageSampleStageResultState.Imported,
                    "Sample import completed.");
            }
            catch (OperationCanceledException)
            {
                return new PackageSampleStageResult(
                    PackageSampleStageResultState.Canceled,
                    "Sample import canceled before commit.");
            }
            catch (Exception exception)
            {
                return Failed("Sample import failed: " + exception.GetBaseException().Message);
            }
            finally
            {
                try
                {
                    if (_files.DirectoryExists(operationPath))
                    {
                        _files.DeleteDirectory(operationPath, true);
                    }
                }
                catch (Exception exception)
                {
                    PackageInstallerLog.Samples.Warning(
                        "Failed to clean sample staging folder: " + exception.GetBaseException().Message);
                }
            }
        }

        private void CopyDirectory(
            string sourceDirectory,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.CreateDirectory(destinationDirectory);

            foreach (string file in _files.GetFiles(sourceDirectory).OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _files.CopyFile(file, Path.Combine(destinationDirectory, Path.GetFileName(file)));
            }

            foreach (string directory in _files.GetDirectories(sourceDirectory)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CopyDirectory(
                    directory,
                    Path.Combine(destinationDirectory, Path.GetFileName(directory)),
                    cancellationToken);
            }
        }

        private void ValidateCopy(
            string sourceDirectory,
            string stagedDirectory,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, string> sourceFiles = CollectFiles(sourceDirectory);
            IReadOnlyDictionary<string, string> stagedFiles = CollectFiles(stagedDirectory);

            if (sourceFiles.Count != stagedFiles.Count ||
                sourceFiles.Keys.Except(stagedFiles.Keys, StringComparer.Ordinal).Any())
            {
                throw new IOException("Staged sample file set does not match the source.");
            }

            foreach (KeyValuePair<string, string> sourceFile in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string stagedFile = stagedFiles[sourceFile.Key];

                if (_files.GetFileLength(sourceFile.Value) != _files.GetFileLength(stagedFile) ||
                    !_files.ComputeSha256(sourceFile.Value).SequenceEqual(_files.ComputeSha256(stagedFile)))
                {
                    throw new IOException("Staged sample verification failed for " + sourceFile.Key + ".");
                }
            }
        }

        private IReadOnlyDictionary<string, string> CollectFiles(string rootPath)
        {
            Dictionary<string, string> files =
                new Dictionary<string, string>(StringComparer.Ordinal);
            CollectFiles(rootPath, string.Empty, files);
            return files;
        }

        private void CollectFiles(
            string directory,
            string relativeDirectory,
            IDictionary<string, string> files)
        {
            foreach (string file in _files.GetFiles(directory))
            {
                string relativePath = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? Path.GetFileName(file)
                    : Path.Combine(relativeDirectory, Path.GetFileName(file));
                files[NormalizeRelativePath(relativePath)] = file;
            }

            foreach (string childDirectory in _files.GetDirectories(directory))
            {
                string childRelativeDirectory = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? Path.GetFileName(childDirectory)
                    : Path.Combine(relativeDirectory, Path.GetFileName(childDirectory));
                CollectFiles(childDirectory, childRelativeDirectory, files);
            }
        }

        private static string NormalizeRelativePath(string path) =>
            (path ?? string.Empty).Replace('\\', '/');

        private static PackageSampleStageResult Failed(string message) =>
            new PackageSampleStageResult(PackageSampleStageResultState.Failed, message);
    }
}
