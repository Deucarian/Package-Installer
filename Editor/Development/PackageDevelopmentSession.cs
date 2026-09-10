using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal enum DevelopmentSourceState
    {
        Prepared, Connecting, ResolvingLocal, Connected, Restoring, ResolvingOriginal, Restored, Attention
    }

    // This is a local recovery record, never a project setting or diagnostic snapshot.
    // It contains only the selected reference and reversible edit, not a manifest backup.
    [Serializable]
    internal sealed class PackageDevelopmentSession
    {
        public int Schema = 1;
        public string Id;
        public string PackageId;
        public string ProjectRoot;
        public string RepositoryRoot;
        public string CommonDirectory;
        public string InstalledReference;
        public string OriginalResolvedPath;
        public bool WasDirectDependency;
        public string OriginalReference;
        public string OriginalValueJson;
        public string LocalReference;
        public string OriginalManifestHash;
        public string ConnectedManifestHash;
        public int InsertionOffset;
        public string InsertionText;
        public DevelopmentSourceState State;
        public bool Restoring;
        public bool CancellationRequested;
        public string Message;
        public long UpdatedUtcTicks;

        public bool IsManaged => State != DevelopmentSourceState.Restored;
    }

    internal sealed class DevelopmentSourceExpectation
    {
        public string PackageId { get; }
        public string LocalPath { get; }
        public string OriginalReference { get; }
        public string OriginalResolvedPath { get; }
        public bool WasDirectDependency { get; }
        public bool Connecting { get; }

        public DevelopmentSourceExpectation(PackageDevelopmentSession session)
        {
            PackageId = session.PackageId;
            LocalPath = session.RepositoryRoot;
            OriginalResolvedPath = session.OriginalResolvedPath;
            WasDirectDependency = session.WasDirectDependency;
            OriginalReference = session.WasDirectDependency
                ? session.OriginalReference : session.InstalledReference;
            Connecting = !session.Restoring;
        }
    }

    internal enum DevelopmentResolutionState { Ready, Pending, Failed }

    internal sealed class DevelopmentSourceResolution
    {
        public DevelopmentResolutionState State { get; }
        public string Message { get; }

        public DevelopmentSourceResolution(DevelopmentResolutionState state, string message)
        {
            State = state;
            Message = message;
        }
    }

    internal interface IDevelopmentSourceFileSystem
    {
        byte[] ReadManifest();
        void CompareExchangeManifest(byte[] expected, byte[] replacement);
        IDisposable AcquireProjectLock();
    }

    internal interface IDevelopmentSessionStore
    {
        IReadOnlyList<PackageDevelopmentSession> LoadAll();
        void Save(PackageDevelopmentSession session);
    }

    internal interface IDevelopmentSourceResolver
    {
        // Cancellation never implies that Unity stopped a submitted package operation.
        Task<DevelopmentSourceResolution> ResolveAsync(DevelopmentSourceExpectation expectation,
            bool requestResolution, CancellationToken cancellationToken);
    }

    internal interface IDevelopmentCheckoutClaims
    {
        void Acquire(PackageDevelopmentSession session);
        void AssertOwned(PackageDevelopmentSession session);
        void Release(PackageDevelopmentSession session);
    }
}
