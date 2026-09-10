using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class PackageDevelopmentSourceService
    {
        private readonly string _projectRoot;
        private readonly IDevelopmentSourceFileSystem _files;
        private readonly IDevelopmentSessionStore _store;
        private readonly IDevelopmentSourceResolver _resolver;
        private readonly IDevelopmentCheckoutClaims _claims;

        internal PackageDevelopmentSourceService(string projectRoot, IDevelopmentSourceFileSystem files,
            IDevelopmentSessionStore store, IDevelopmentSourceResolver resolver, IDevelopmentCheckoutClaims claims)
        {
            _projectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _files = files ?? throw new ArgumentNullException(nameof(files));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _claims = claims ?? throw new ArgumentNullException(nameof(claims));
        }

        internal IReadOnlyList<PackageDevelopmentSession> GetSessions() => _store.LoadAll();
        internal bool IsManaged(string packageId) => GetSessions().Any(session => session.PackageId == packageId && session.IsManaged);
        internal void AssertCheckoutOwned(PackageDevelopmentSession session) => _claims.AssertOwned(session);

        // The caller supplies freshly validated canonical paths from DevelopmentRepositoryService.
        // Preparing is read-only: a preview never claims a checkout or edits a project.
        internal PackageDevelopmentSession Prepare(string packageId, string canonicalRepositoryRoot,
            string canonicalCommonDirectory, string installedReference, string installedResolvedPath = null,
            string installedSource = null)
        {
            if (!PackageDevelopmentSessionStore.ValidPackageId(packageId))
                throw new InvalidOperationException("Select an installed Deucarian package.");
            if (IsManaged(packageId)) throw new InvalidOperationException("This package already has a local development session.");
            SourceManifestEdit.ValidateReference(installedReference);
            if (string.Equals(installedSource, "Embedded", StringComparison.Ordinal))
                throw new InvalidOperationException("Embedded packages must be moved to a separate package checkout explicitly before starting local development.");
            if (installedReference != null && installedReference.StartsWith("file:", StringComparison.Ordinal) &&
                string.IsNullOrEmpty(installedResolvedPath))
                throw new InvalidOperationException("The installed local source path is required to verify exact restoration.");
            if (!Path.IsPathRooted(canonicalRepositoryRoot) || !Path.IsPathRooted(canonicalCommonDirectory))
                throw new InvalidOperationException("A validated absolute package repository is required.");
            byte[] original = _files.ReadManifest();
            PackageDevelopmentSession session = new PackageDevelopmentSession
            {
                Id = Guid.NewGuid().ToString("N"), PackageId = packageId, ProjectRoot = _projectRoot,
                RepositoryRoot = canonicalRepositoryRoot, CommonDirectory = canonicalCommonDirectory,
                InstalledReference = installedReference, OriginalManifestHash = SourceManifestEdit.Hash(original),
                OriginalResolvedPath = installedResolvedPath,
                LocalReference = "file:" + canonicalRepositoryRoot.Replace('\\', '/'),
                State = DevelopmentSourceState.Prepared,
                Message = "Review the repository and branch, then connect this project explicitly."
            };
            byte[] connected = new SourceManifestEdit(original).Prepare(session);
            session.ConnectedManifestHash = SourceManifestEdit.Hash(connected);
            return session;
        }

        internal async Task<PackageDevelopmentSession> ConnectAsync(PackageDevelopmentSession preview,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (_files.AcquireProjectLock())
            {
                if (preview == null || preview.ProjectRoot != _projectRoot || preview.State != DevelopmentSourceState.Prepared)
                    throw new InvalidOperationException("Prepare and review this project's package connection first.");
                if (IsManaged(preview.PackageId)) throw new InvalidOperationException("A development session already exists for this package.");
                byte[] original = _files.ReadManifest();
                if (SourceManifestEdit.Hash(original) != preview.OriginalManifestHash)
                    throw new InvalidOperationException("The manifest changed since preview. Review a fresh connection before continuing.");
                byte[] connected = new SourceManifestEdit(original).Prepare(preview);
                if (SourceManifestEdit.Hash(connected) != preview.ConnectedManifestHash)
                    throw new InvalidOperationException("The development preview changed. Review a fresh connection before continuing.");
                cancellationToken.ThrowIfCancellationRequested();
                _claims.Acquire(preview);
                try { Save(preview, DevelopmentSourceState.Connecting, "Connecting the package source."); }
                catch { _claims.Release(preview); throw; }
                try
                {
                    _files.CompareExchangeManifest(original, connected);
                    Save(preview, DevelopmentSourceState.ResolvingLocal, "Unity is resolving the local source and compiling scripts.");
                }
                catch
                {
                    Save(preview, DevelopmentSourceState.Attention,
                        "Connection was interrupted. Recover the session or restore the original reference.");
                    throw;
                }
                return await SettleAsync(preview, true, cancellationToken);
            }
        }

        internal async Task<PackageDevelopmentSession> RecoverAsync(string packageId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (_files.AcquireProjectLock())
            {
                PackageDevelopmentSession session = Find(packageId);
                if (!session.IsManaged) return session;
                SourceManifestEdit manifest = new SourceManifestEdit(_files.ReadManifest());
                if (manifest.GetReference(packageId) == session.LocalReference)
                {
                    session.Restoring = false;
                    Save(session, DevelopmentSourceState.ResolvingLocal, "Checking the local package after interruption.");
                }
                else if (manifest.IsOriginal(session))
                {
                    session.Restoring = true;
                    Save(session, DevelopmentSourceState.ResolvingOriginal, "Checking the original source after interruption.");
                }
                else
                {
                    Save(session, DevelopmentSourceState.Attention,
                        "The package reference changed outside this session. Review the manifest; no reference was overwritten.");
                    return session;
                }
                return await SettleAsync(session, true, cancellationToken);
            }
        }

        internal async Task<PackageDevelopmentSession> RestoreAsync(string packageId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (_files.AcquireProjectLock())
            {
                PackageDevelopmentSession session = Find(packageId);
                if (!session.IsManaged) return session;
                byte[] current = _files.ReadManifest();
                SourceManifestEdit manifest = new SourceManifestEdit(current);
                byte[] original = manifest.IsOriginal(session) ? current : manifest.Restore(session, current);
                session.Restoring = true;
                Save(session, DevelopmentSourceState.Restoring, "Restoring the original package reference; local files and commits are preserved.");
                try
                {
                    _files.CompareExchangeManifest(current, original);
                    Save(session, DevelopmentSourceState.ResolvingOriginal, "Unity is resolving the original package source and compiling scripts.");
                }
                catch
                {
                    Save(session, DevelopmentSourceState.Attention,
                        "Restoration was interrupted. Recover the session to inspect the current reference.");
                    throw;
                }
                return await SettleAsync(session, true, cancellationToken);
            }
        }

        private async Task<PackageDevelopmentSession> SettleAsync(PackageDevelopmentSession session,
            bool requestResolution, CancellationToken cancellationToken)
        {
            DevelopmentSourceResolution resolution;
            try
            {
                resolution = await _resolver.ResolveAsync(new DevelopmentSourceExpectation(session),
                    requestResolution, cancellationToken);
            }
            catch (Exception)
            {
                resolution = new DevelopmentSourceResolution(DevelopmentResolutionState.Pending,
                    "Unity resolution was interrupted. Recover after compilation or restart; the journal remains protected.");
            }
            session.CancellationRequested |= cancellationToken.IsCancellationRequested;
            SourceManifestEdit current = new SourceManifestEdit(_files.ReadManifest());
            bool expected = session.Restoring ? current.IsOriginal(session) :
                current.GetReference(session.PackageId) == session.LocalReference;
            if (!expected)
            {
                Save(session, DevelopmentSourceState.Attention,
                    "The package reference changed while Unity was resolving. Review the manifest before continuing.");
                return session;
            }
            if (resolution.State == DevelopmentResolutionState.Ready)
            {
                if (session.Restoring)
                {
                    _claims.Release(session);
                    Save(session, DevelopmentSourceState.Restored, "Original source restored. The local repository and all work are preserved.");
                }
                else Save(session, DevelopmentSourceState.Connected,
                    "Local source resolved; the editor is idle with no reported compilation errors. Package tests are a separate validation step.");
            }
            else Save(session, resolution.State == DevelopmentResolutionState.Failed ? DevelopmentSourceState.Attention :
                session.Restoring ? DevelopmentSourceState.ResolvingOriginal : DevelopmentSourceState.ResolvingLocal,
                resolution.Message);
            return session;
        }

        private PackageDevelopmentSession Find(string packageId) =>
            GetSessions().SingleOrDefault(session => session.PackageId == packageId) ??
            throw new InvalidOperationException("No development session exists for this package.");

        private void Save(PackageDevelopmentSession session, DevelopmentSourceState state, string message)
        {
            session.State = state;
            session.Message = message;
            session.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            _store.Save(session);
        }
    }
}
