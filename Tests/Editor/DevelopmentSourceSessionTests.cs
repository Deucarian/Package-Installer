using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentSourceSessionTests
    {
        private const string Package = "com.deucarian.source-fixture";
        private const string Root = "D:/Codex-storage/validation/package-development-20260910/virtual-consumer";
        private const string Checkout = "D:/Codex-storage/validation/package-development-20260910/virtual-checkout";
        private const string Original = "https://github.com/Deucarian/Source-Fixture.git#develop";
        private MemoryFiles _files;
        private MemoryStore _store;
        private Resolver _resolver;
        private Claims _claims;
        private PackageDevelopmentSourceService _service;

        [SetUp]
        public void SetUp()
        {
            _files = new MemoryFiles("{\"dependencies\":{\"" + Package + "\":\"" + Original + "\"}}");
            _store = new MemoryStore();
            _resolver = new Resolver();
            _claims = new Claims();
            _service = Service();
        }

        [Test]
        public void EmbeddedSourceCannotBeConnectedThroughManifestOverride()
        {
            Assert.Throws<InvalidOperationException>(() => _service.Prepare(Package, Checkout, Checkout + "/.git",
                Original, Root + "/Packages/" + Package, "Embedded"));
            Assert.IsEmpty(_store.LoadAll());
            Assert.AreEqual(0, _files.Writes);
        }

        [Test]
        public void LocalSourceRequiresOriginalResolvedPathForTruthfulRestoration()
        {
            Assert.Throws<InvalidOperationException>(() => _service.Prepare(Package, Checkout, Checkout + "/.git", "file:../original"));
            PackageDevelopmentSession session = _service.Prepare(Package, Checkout, Checkout + "/.git",
                "file:../original", Root + "/original", "Local");
            Assert.AreEqual(Root + "/original", session.OriginalResolvedPath);
        }

        [Test]
        public void PreviewHasNoSideEffectsAndShowsOriginalSource()
        {
            PackageDevelopmentSession session = Preview();
            Assert.AreEqual(Original, session.OriginalReference);
            Assert.IsTrue(session.WasDirectDependency);
            Assert.AreEqual(0, _files.Writes);
            Assert.AreEqual(0, _claims.Acquires);
            Assert.IsEmpty(_store.LoadAll());
            Assert.AreEqual(0, _resolver.Calls);
        }

        [UnityTest]
        public IEnumerator SuccessfulConnectionIsJournaledBeforeWriteAndRemainsProtected() => DevelopmentAsyncTest.Run(SuccessfulConnectionIsJournaledBeforeWriteAndRemainsProtectedAsync);

        public async Task SuccessfulConnectionIsJournaledBeforeWriteAndRemainsProtectedAsync()
        {
            _files.BeforeWrite = () => Assert.AreEqual(DevelopmentSourceState.Connecting, _store.Record.State);
            PackageDevelopmentSession session = await _service.ConnectAsync(Preview(), CancellationToken.None);
            Assert.AreEqual(DevelopmentSourceState.Connected, session.State);
            Assert.IsTrue(_service.IsManaged(Package));
            Assert.AreEqual(1, _claims.Acquires);
            Assert.AreEqual(1, _resolver.Calls);
            Assert.IsTrue(_resolver.Last.Connecting);
            StringAssert.Contains("no reported compilation errors", session.Message);
        }

        [UnityTest]
        public IEnumerator ManifestChangedSincePreviewRequiresFreshReview() => DevelopmentAsyncTest.Run(ManifestChangedSincePreviewRequiresFreshReviewAsync);

        public async Task ManifestChangedSincePreviewRequiresFreshReviewAsync()
        {
            PackageDevelopmentSession session = Preview();
            _files.Bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{},\"testables\":[]}");
            await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(async () => await _service.ConnectAsync(session, CancellationToken.None));
            Assert.AreEqual(0, _claims.Acquires);
            Assert.AreEqual(0, _files.Writes);
        }

        [UnityTest]
        public IEnumerator ConcurrentManifestWriteKeepsRecoveryRecordAndDoesNotOverwrite() => DevelopmentAsyncTest.Run(ConcurrentManifestWriteKeepsRecoveryRecordAndDoesNotOverwriteAsync);

        public async Task ConcurrentManifestWriteKeepsRecoveryRecordAndDoesNotOverwriteAsync()
        {
            _files.BeforeWrite = () => _files.Bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"com.example.concurrent\":\"1\"}}");
            await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(async () => await _service.ConnectAsync(Preview(), CancellationToken.None));
            Assert.IsTrue(_service.IsManaged(Package));
            Assert.AreEqual(DevelopmentSourceState.Attention, _store.Record.State);
            Assert.AreEqual(0, _files.Writes);
            Assert.AreEqual("1", new SourceManifestEdit(_files.Bytes).GetReference("com.example.concurrent"));
        }

        [UnityTest]
        public IEnumerator CancelBeforeConnectMakesNoChanges() => DevelopmentAsyncTest.Run(CancelBeforeConnectMakesNoChangesAsync);

        public async Task CancelBeforeConnectMakesNoChangesAsync()
        {
            var token = new CancellationToken(true);
            await DevelopmentAsyncTest.ThrowsAsync<OperationCanceledException>(async () => await _service.ConnectAsync(Preview(), token));
            Assert.AreEqual(0, _claims.Acquires);
            Assert.AreEqual(0, _files.Writes);
            Assert.IsEmpty(_store.LoadAll());
        }

        [UnityTest]
        public IEnumerator CancelAfterManifestWriteLetsResolverSettleAndReportsRealConnectedState() => DevelopmentAsyncTest.Run(CancelAfterManifestWriteLetsResolverSettleAndReportsRealConnectedStateAsync);

        public async Task CancelAfterManifestWriteLetsResolverSettleAndReportsRealConnectedStateAsync()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var pending = new TaskCompletionSource<DevelopmentSourceResolution>();
                _resolver.Pending = pending.Task;
                Task<PackageDevelopmentSession> connection = _service.ConnectAsync(Preview(), cancel.Token);
                Assert.IsFalse(connection.IsCompleted);
                Assert.AreEqual(DevelopmentSourceState.ResolvingLocal, _store.Record.State);
                cancel.Cancel();
                Assert.IsFalse(connection.IsCompleted);
                pending.SetResult(new DevelopmentSourceResolution(DevelopmentResolutionState.Ready, "ready"));
                PackageDevelopmentSession result = await connection;
                Assert.AreEqual(DevelopmentSourceState.Connected, result.State);
                Assert.IsTrue(result.CancellationRequested);
            }
        }

        [UnityTest]
        public IEnumerator ConcurrentOperationIsBlockedUntilResolverSettles() => DevelopmentAsyncTest.Run(ConcurrentOperationIsBlockedUntilResolverSettlesAsync);

        public async Task ConcurrentOperationIsBlockedUntilResolverSettlesAsync()
        {
            var pending = new TaskCompletionSource<DevelopmentSourceResolution>();
            _resolver.Pending = pending.Task;
            Task<PackageDevelopmentSession> connection = _service.ConnectAsync(Preview(), CancellationToken.None);
            await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(async () => await _service.RestoreAsync(Package, CancellationToken.None));
            pending.SetResult(new DevelopmentSourceResolution(DevelopmentResolutionState.Ready, "ready"));
            await connection;
            Assert.IsFalse(_files.Locked);
        }

        [UnityTest]
        public IEnumerator ResolutionFailureDoesNotClaimSuccessOrReleaseProtection() => DevelopmentAsyncTest.Run(ResolutionFailureDoesNotClaimSuccessOrReleaseProtectionAsync);

        public async Task ResolutionFailureDoesNotClaimSuccessOrReleaseProtectionAsync()
        {
            _resolver.Result = new DevelopmentSourceResolution(DevelopmentResolutionState.Failed, "Compilation needs repair.");
            PackageDevelopmentSession session = await _service.ConnectAsync(Preview(), CancellationToken.None);
            Assert.AreEqual(DevelopmentSourceState.Attention, session.State);
            Assert.IsTrue(_service.IsManaged(Package));
            Assert.AreEqual(0, _claims.Releases);
        }

        [UnityTest]
        public IEnumerator ReloadRecoveryRecognizesAlreadyWrittenLocalManifestWithoutRewritingIt() => DevelopmentAsyncTest.Run(ReloadRecoveryRecognizesAlreadyWrittenLocalManifestWithoutRewritingItAsync);

        public async Task ReloadRecoveryRecognizesAlreadyWrittenLocalManifestWithoutRewritingItAsync()
        {
            _resolver.Result = new DevelopmentSourceResolution(DevelopmentResolutionState.Pending, "reload");
            await _service.ConnectAsync(Preview(), CancellationToken.None);
            _resolver.Result = new DevelopmentSourceResolution(DevelopmentResolutionState.Ready, "ready");
            PackageDevelopmentSession recovered = await Service().RecoverAsync(Package, CancellationToken.None);
            Assert.AreEqual(DevelopmentSourceState.Connected, recovered.State);
            Assert.AreEqual(1, _files.Writes);
        }

        [UnityTest]
        public IEnumerator RestoreDirectReferenceRoundTripsExactBytesAndKeepsLocalWork() => DevelopmentAsyncTest.Run(RestoreDirectReferenceRoundTripsExactBytesAndKeepsLocalWorkAsync);

        public async Task RestoreDirectReferenceRoundTripsExactBytesAndKeepsLocalWorkAsync()
        {
            byte[] original = (byte[])_files.Bytes.Clone();
            await _service.ConnectAsync(Preview(), CancellationToken.None);
            _claims.CheckoutMissing = true;
            PackageDevelopmentSession restored = await _service.RestoreAsync(Package, CancellationToken.None);
            CollectionAssert.AreEqual(original, _files.Bytes);
            Assert.AreEqual(DevelopmentSourceState.Restored, restored.State);
            Assert.IsFalse(_service.IsManaged(Package));
            Assert.AreEqual(1, _claims.Releases);
            Assert.IsFalse(_resolver.Last.Connecting);
            Assert.AreEqual(Original, _resolver.Last.OriginalReference);
        }

        [UnityTest]
        public IEnumerator RestoreTransitiveDependencyRemovesOnlyTemporaryDirectReference() => DevelopmentAsyncTest.Run(RestoreTransitiveDependencyRemovesOnlyTemporaryDirectReferenceAsync);

        public async Task RestoreTransitiveDependencyRemovesOnlyTemporaryDirectReferenceAsync()
        {
            _files.Bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"com.example.parent\":\"1\"}}\r\n");
            byte[] original = (byte[])_files.Bytes.Clone();
            PackageDevelopmentSession connected = await _service.ConnectAsync(Preview(), CancellationToken.None);
            Assert.IsFalse(connected.WasDirectDependency);
            await _service.RestoreAsync(Package, CancellationToken.None);
            CollectionAssert.AreEqual(original, _files.Bytes);
            Assert.AreEqual(Original, _resolver.Last.OriginalReference);
        }

        [UnityTest]
        public IEnumerator InterruptedRestoreRecoversOriginalManifestAfterRestart() => DevelopmentAsyncTest.Run(InterruptedRestoreRecoversOriginalManifestAfterRestartAsync);

        public async Task InterruptedRestoreRecoversOriginalManifestAfterRestartAsync()
        {
            await _service.ConnectAsync(Preview(), CancellationToken.None);
            _resolver.Result = new DevelopmentSourceResolution(DevelopmentResolutionState.Pending, "restart");
            await _service.RestoreAsync(Package, CancellationToken.None);
            Assert.IsTrue(_service.IsManaged(Package));
            _resolver.Result = new DevelopmentSourceResolution(DevelopmentResolutionState.Ready, "ready");
            PackageDevelopmentSession restored = await Service().RecoverAsync(Package, CancellationToken.None);
            Assert.AreEqual(DevelopmentSourceState.Restored, restored.State);
            Assert.AreEqual(2, _files.Writes);
        }

        [UnityTest]
        public IEnumerator RecoveryDoesNotOverwriteAnExternallySelectedSource() => DevelopmentAsyncTest.Run(RecoveryDoesNotOverwriteAnExternallySelectedSourceAsync);

        public async Task RecoveryDoesNotOverwriteAnExternallySelectedSourceAsync()
        {
            await _service.ConnectAsync(Preview(), CancellationToken.None);
            _files.Bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"" + Package + "\":\"9\"}}");
            PackageDevelopmentSession result = await _service.RecoverAsync(Package, CancellationToken.None);
            Assert.AreEqual(DevelopmentSourceState.Attention, result.State);
            Assert.AreEqual(1, _files.Writes);
            Assert.AreEqual("9", new SourceManifestEdit(_files.Bytes).GetReference(Package));
        }

        [UnityTest]
        public IEnumerator ClaimOwnedByAnotherProjectPreventsConnection() => DevelopmentAsyncTest.Run(ClaimOwnedByAnotherProjectPreventsConnectionAsync);

        public async Task ClaimOwnedByAnotherProjectPreventsConnectionAsync()
        {
            _claims.Reject = true;
            await DevelopmentAsyncTest.ThrowsAsync<InvalidOperationException>(async () => await _service.ConnectAsync(Preview(), CancellationToken.None));
            Assert.AreEqual(0, _files.Writes);
            Assert.IsEmpty(_store.LoadAll());
        }

        [UnityTest]
        public IEnumerator ManifestChangeDuringResolveIsDetectedBeforeSuccess() => DevelopmentAsyncTest.Run(ManifestChangeDuringResolveIsDetectedBeforeSuccessAsync);

        public async Task ManifestChangeDuringResolveIsDetectedBeforeSuccessAsync()
        {
            var pending = new TaskCompletionSource<DevelopmentSourceResolution>();
            _resolver.Pending = pending.Task;
            Task<PackageDevelopmentSession> connection = _service.ConnectAsync(Preview(), CancellationToken.None);
            _files.Bytes = Encoding.UTF8.GetBytes("{\"dependencies\":{\"" + Package + "\":\"9\"}}");
            pending.SetResult(new DevelopmentSourceResolution(DevelopmentResolutionState.Ready, "ready"));
            Assert.AreEqual(DevelopmentSourceState.Attention, (await connection).State);
        }

        private PackageDevelopmentSourceService Service() => new PackageDevelopmentSourceService(Root, _files, _store, _resolver, _claims);
        private PackageDevelopmentSession Preview() => _service.Prepare(Package, Checkout, Checkout + "/.git", Original);

        private sealed class MemoryFiles : IDevelopmentSourceFileSystem
        {
            internal byte[] Bytes;
            internal int Writes;
            internal bool Locked;
            internal Action BeforeWrite;
            internal MemoryFiles(string text) { Bytes = Encoding.UTF8.GetBytes(text); }
            public byte[] ReadManifest() => (byte[])Bytes.Clone();
            public void CompareExchangeManifest(byte[] expected, byte[] replacement)
            {
                BeforeWrite?.Invoke();
                if (!Bytes.SequenceEqual(expected)) throw new InvalidOperationException("Concurrent write.");
                Bytes = replacement;
                ++Writes;
            }
            public IDisposable AcquireProjectLock()
            {
                if (Locked) throw new InvalidOperationException("Locked.");
                Locked = true;
                return new Unlock(() => Locked = false);
            }
        }
        private sealed class Unlock : IDisposable
        {
            private readonly Action _release;
            internal Unlock(Action release) { _release = release; }
            public void Dispose() => _release();
        }
        private sealed class MemoryStore : IDevelopmentSessionStore
        {
            internal PackageDevelopmentSession Record;
            public IReadOnlyList<PackageDevelopmentSession> LoadAll() => Record == null ?
                Array.Empty<PackageDevelopmentSession>() : new[] { Record };
            public void Save(PackageDevelopmentSession session) { Record = session; }
        }
        private sealed class Resolver : IDevelopmentSourceResolver
        {
            internal int Calls;
            internal DevelopmentSourceExpectation Last;
            internal Task<DevelopmentSourceResolution> Pending;
            internal DevelopmentSourceResolution Result = new DevelopmentSourceResolution(DevelopmentResolutionState.Ready, "ready");
            public Task<DevelopmentSourceResolution> ResolveAsync(DevelopmentSourceExpectation expectation,
                bool requestResolution, CancellationToken cancellationToken)
            {
                ++Calls;
                Last = expectation;
                return Pending ?? Task.FromResult(Result);
            }
        }
        private sealed class Claims : IDevelopmentCheckoutClaims
        {
            internal int Acquires, Releases;
            internal bool Reject, CheckoutMissing;
            public void Acquire(PackageDevelopmentSession session)
            {
                if (Reject) throw new InvalidOperationException("Owned elsewhere.");
                ++Acquires;
            }
            public void AssertOwned(PackageDevelopmentSession session)
            {
                if (CheckoutMissing) throw new InvalidOperationException("Missing checkout.");
            }
            public void Release(PackageDevelopmentSession session) { ++Releases; }
        }
    }
}
