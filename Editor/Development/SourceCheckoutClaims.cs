using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Claims are per canonical checkout, so separate worktrees may be used independently.
    // They persist across editor restarts and deliberately never expire behind another project.
    internal sealed class DevelopmentCheckoutClaims : IDevelopmentCheckoutClaims
    {
        private readonly PackageInstallerAtomicFileCommitter _committer;

        internal DevelopmentCheckoutClaims(PackageInstallerAtomicFileCommitter committer)
        {
            _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        }

        public void Acquire(PackageDevelopmentSession session)
        {
            string path = ClaimPath(session.CommonDirectory, session.RepositoryRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            DevelopmentSourceStoragePaths.RequireRegular(session.CommonDirectory, path);
            using (AcquireLock(path))
            {
                if (File.Exists(path))
                {
                    AssertRecord(path, session.ProjectRoot, session.Id);
                    return;
                }
                Claim claim = new Claim { Project = session.ProjectRoot, Session = session.Id };
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    DevelopmentSourceStoragePaths.RequireRegular(session.CommonDirectory, temporary);
                    byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(claim));
                    using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    _committer.Commit(temporary, path, () =>
                    {
                        DevelopmentSourceStoragePaths.RequireRegular(session.CommonDirectory, path);
                        DevelopmentSourceStoragePaths.RequireRegular(session.CommonDirectory, temporary);
                    });
                }
                finally
                {
                    DevelopmentSourceStoragePaths.RequireRegular(session.CommonDirectory, temporary);
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        public void AssertOwned(PackageDevelopmentSession session) =>
            AssertOwned(session.CommonDirectory, session.RepositoryRoot, session.ProjectRoot, session.Id);

        internal void AssertOwned(string commonDirectory, string repositoryRoot, string projectRoot, string sessionId)
        {
            string path = ClaimPath(commonDirectory, repositoryRoot);
            if (!File.Exists(path))
                throw new InvalidOperationException("The checkout claim is missing. Reconnect explicitly before changing this repository.");
            AssertRecord(path, projectRoot, sessionId);
        }

        public void Release(PackageDevelopmentSession session)
        {
            string path = ClaimPath(session.CommonDirectory, session.RepositoryRoot);
            if (!File.Exists(path)) return; // The clone may have been moved or removed; restoration still works.
            using (AcquireLock(path))
            {
                AssertRecord(path, session.ProjectRoot, session.Id);
                DevelopmentSourceStoragePaths.RequireRegular(session.CommonDirectory, path);
                File.Delete(path);
            }
        }

        private static void AssertRecord(string path, string project, string session)
        {
            Claim record;
            try
            {
                if (new FileInfo(path).Length > 8192) throw new ArgumentException();
                record = JsonUtility.FromJson<Claim>(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException)
            {
                throw new InvalidOperationException("The checkout claim cannot be read. Use a separate checkout or repair the local claim.");
            }
            if (record == null || record.Project != project || record.Session != session)
                throw new InvalidOperationException("Another project or development session owns this checkout. Use an isolated worktree or finish that session first.");
        }

        private static IDisposable AcquireLock(string claimPath)
        {
            DevelopmentSourceStoragePaths.RequireRegular(Path.GetDirectoryName(claimPath), claimPath + ".lock");
            try { return new FileStream(claimPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("Another project is updating this checkout's development claim."); }
        }

        private static string ClaimPath(string commonDirectory, string repositoryRoot)
        {
            string key = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (Path.DirectorySeparatorChar == '\\') key = key.ToUpperInvariant();
            string path = Path.Combine(commonDirectory, "deucarian-package-development",
                SourceManifestEdit.Hash(Encoding.UTF8.GetBytes(key)) + ".json");
            DevelopmentSourceStoragePaths.RequireRegular(commonDirectory, path);
            return path;
        }

        [Serializable]
        private sealed class Claim
        {
            public string Project;
            public string Session;
        }
    }
}
