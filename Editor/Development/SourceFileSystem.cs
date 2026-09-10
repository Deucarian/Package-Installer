using System;
using System.IO;
using System.Linq;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentSourceFileSystem : IDevelopmentSourceFileSystem
    {
        private readonly string _manifest;
        private readonly string _projectRoot;
        private readonly string _stateDirectory;
        private readonly PackageInstallerAtomicFileCommitter _committer;

        internal DevelopmentSourceFileSystem(string projectRoot, PackageInstallerAtomicFileCommitter committer)
        {
            _projectRoot = Path.GetFullPath(projectRoot);
            _manifest = Path.Combine(_projectRoot, "Packages", "manifest.json");
            _stateDirectory = Path.Combine(Path.GetFullPath(projectRoot), "Library", "Deucarian",
                "PackageInstaller", "Development");
            _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        }

        public byte[] ReadManifest()
        {
            DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, _manifest);
            return File.ReadAllBytes(_manifest);
        }

        public IDisposable AcquireProjectLock()
        {
            string lockPath = Path.Combine(_stateDirectory, "operation.lock");
            DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, lockPath);
            Directory.CreateDirectory(_stateDirectory);
            DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, lockPath);
            try
            {
                return new FileStream(lockPath,
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                throw new InvalidOperationException("Another package development operation is using this project.");
            }
        }

        public void CompareExchangeManifest(byte[] expected, byte[] replacement)
        {
            string temporary = _manifest + ".deucarian-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, temporary);
                DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, _manifest);
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None))
                {
                    stream.Write(replacement, 0, replacement.Length);
                    stream.Flush(true);
                }
                // Disallow direct external writers while checking and replacing. Delete sharing
                // is required for atomic replacement; uncooperative external renames still need
                // user review, because the filesystem has no portable compare-and-swap rename.
                using (new FileStream(_manifest, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    _committer.Commit(temporary, _manifest, () =>
                    {
                        DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, _manifest);
                        DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, temporary);
                        if (!ReadManifest().SequenceEqual(expected))
                            throw new InvalidOperationException("The manifest changed during this operation. No reference was overwritten; review and retry.");
                    });
                }
            }
            finally
            {
                DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, temporary);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
