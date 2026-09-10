using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class PackageDevelopmentSessionStore : IDevelopmentSessionStore
    {
        private readonly string _directory;
        private readonly string _projectRoot;
        private readonly PackageInstallerAtomicFileCommitter _committer;

        internal PackageDevelopmentSessionStore(string projectRoot, PackageInstallerAtomicFileCommitter committer)
        {
            _projectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _directory = Path.Combine(_projectRoot, "Library", "Deucarian", "PackageInstaller", "Development");
            _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        }

        public IReadOnlyList<PackageDevelopmentSession> LoadAll()
        {
            List<PackageDevelopmentSession> sessions = new List<PackageDevelopmentSession>();
            DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, _directory);
            if (!Directory.Exists(_directory)) return sessions;
            string[] files = Directory.GetFiles(_directory, "session-*.json");
            if (files.Length > 256) throw new InvalidOperationException("The local development journal contains too many records. Package changes are blocked.");
            foreach (string file in files)
            {
                try
                {
                    DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, file);
                    if (new FileInfo(file).Length > 128 * 1024) throw new InvalidOperationException();
                    PackageDevelopmentSession session = JsonUtility.FromJson<PackageDevelopmentSession>(File.ReadAllText(file));
                    Validate(session);
                    if (Path.GetFileName(file) != "session-" + session.PackageId + ".json")
                        throw new InvalidOperationException();
                    sessions.Add(session);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is IOException ||
                    exception is InvalidOperationException || exception is UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("The local development journal needs recovery. Package changes are blocked until it is repaired.");
                }
            }
            return sessions;
        }

        public void Save(PackageDevelopmentSession session)
        {
            Validate(session);
            DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, _directory);
            Directory.CreateDirectory(_directory);
            string destination = Path.Combine(_directory, "session-" + session.PackageId + ".json");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, temporary);
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(session, true));
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                _committer.Commit(temporary, destination, () =>
                {
                    DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, destination);
                    DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, temporary);
                });
            }
            finally
            {
                DevelopmentSourceStoragePaths.RequireRegular(_projectRoot, temporary);
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void Validate(PackageDevelopmentSession session)
        {
            if (session == null || session.Schema != 1 || !Guid.TryParseExact(session.Id, "N", out _) ||
                !ValidPackageId(session.PackageId) || session.ProjectRoot != _projectRoot ||
                string.IsNullOrEmpty(session.RepositoryRoot) || string.IsNullOrEmpty(session.CommonDirectory) ||
                string.IsNullOrEmpty(session.LocalReference) || !Enum.IsDefined(typeof(DevelopmentSourceState), session.State))
                throw new InvalidOperationException("The local development journal is invalid.");
            if (!Path.IsPathRooted(session.RepositoryRoot) || !Path.IsPathRooted(session.CommonDirectory) ||
                session.LocalReference != "file:" + session.RepositoryRoot.Replace('\\', '/'))
                throw new InvalidOperationException("The local development journal contains an invalid checkout reference.");
            if (session.WasDirectDependency)
            {
                SourceManifestJson.Member wrapper = new SourceManifestJson("{\"reference\":" + session.OriginalValueJson + "}").Parse();
                SourceManifestJson.Member value = wrapper.Members[0];
                if (wrapper.Members.Count != 1 || value.Value == null || value.Value != session.OriginalReference)
                    throw new InvalidOperationException("The local development journal contains an invalid original reference.");
            }
            SourceManifestEdit.ValidateReference(session.OriginalReference);
            SourceManifestEdit.ValidateReference(session.InstalledReference);
        }

        internal static bool ValidPackageId(string value)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("com.deucarian.", StringComparison.Ordinal) ||
                value.Length > 128 || value.EndsWith(".", StringComparison.Ordinal)) return false;
            foreach (char character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= '0' && character <= '9') &&
                    character != '.' && character != '-') return false;
            return true;
        }
    }
}
