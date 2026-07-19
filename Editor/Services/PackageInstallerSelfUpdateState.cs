using System;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Deucarian.PackageInstaller.Editor
{
    internal readonly struct PackageInstallerAssemblyIdentity
    {
        public PackageInstallerAssemblyIdentity(string version, string moduleVersionId)
        {
            Version = version ?? string.Empty;
            ModuleVersionId = moduleVersionId ?? string.Empty;
        }

        public string Version { get; }

        public string ModuleVersionId { get; }
    }

    internal static class PackageInstallerRuntimeIdentity
    {
        public const string PackageId = "com.deucarian.package-installer";
        public const string Version = "1.1.78";

        public static PackageInstallerAssemblyIdentity Current =>
            new PackageInstallerAssemblyIdentity(
                Version,
                typeof(PackageInstallerRuntimeIdentity).Assembly.ManifestModule.ModuleVersionId.ToString("N"));

        public static bool IsSelf(string packageId)
        {
            return string.Equals(packageId, PackageId, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal enum PackageInstallerSelfUpdatePhase
    {
        None,
        Installing,
        AwaitingReload,
        Applied
    }

    internal enum PackageInstallerSelfUpdateReconcileResult
    {
        None,
        Pending,
        AppliedOnReload
    }

    internal readonly struct PackageInstallerSelfUpdateSnapshot
    {
        public PackageInstallerSelfUpdateSnapshot(
            bool isAwaitingReload,
            string sourceVersion,
            string sourceModuleVersionId,
            string resolvedVersion,
            string selectedUrl)
            : this(
                isAwaitingReload,
                sourceVersion,
                sourceModuleVersionId,
                sourceVersion,
                resolvedVersion,
                selectedUrl)
        {
        }

        public PackageInstallerSelfUpdateSnapshot(
            bool isAwaitingReload,
            string sourceVersion,
            string sourceModuleVersionId,
            string resolvedVersionBeforeAdd,
            string resolvedVersionAfterAdd,
            string selectedUrl)
        {
            IsAwaitingReload = isAwaitingReload;
            SourceVersion = sourceVersion ?? string.Empty;
            SourceModuleVersionId = sourceModuleVersionId ?? string.Empty;
            ResolvedVersionBeforeAdd = resolvedVersionBeforeAdd ?? string.Empty;
            ResolvedVersionAfterAdd = resolvedVersionAfterAdd ?? string.Empty;
            SelectedUrl = selectedUrl ?? string.Empty;
        }

        public bool IsAwaitingReload { get; }

        public string SourceVersion { get; }

        public string SourceModuleVersionId { get; }

        public string ResolvedVersionBeforeAdd { get; }

        public string ResolvedVersionAfterAdd { get; }

        public string ResolvedVersion => !string.IsNullOrWhiteSpace(ResolvedVersionAfterAdd)
            ? ResolvedVersionAfterAdd
            : ResolvedVersionBeforeAdd;

        public string SelectedUrl { get; }

        public static PackageInstallerSelfUpdateSnapshot None =>
            new PackageInstallerSelfUpdateSnapshot(
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
    }

    internal static class PackageInstallerSelfUpdateState
    {
        private const int CurrentSchemaVersion = 2;
        private const string StateKey = "Deucarian.PackageInstaller.SelfUpdateState";

        public static void Begin(string selectedUrl)
        {
            PackageInstallerAssemblyIdentity currentIdentity = PackageInstallerRuntimeIdentity.Current;
            Begin(selectedUrl, currentIdentity, ResolveCurrentPackageVersion(currentIdentity));
        }

        public static void MarkResolved(string resolvedVersionAfterAdd)
        {
            if (!TryLoad(out PersistedSelfUpdateState state))
            {
                return;
            }

            state.phase = (int)PackageInstallerSelfUpdatePhase.AwaitingReload;
            state.resolvedVersionAfterAdd = resolvedVersionAfterAdd ?? string.Empty;
            Save(state);
        }

        public static void MarkInstallFailed()
        {
            Clear();
        }

        public static PackageInstallerSelfUpdateReconcileResult ReconcileCurrentRuntime()
        {
            PackageInstallerAssemblyIdentity currentIdentity = PackageInstallerRuntimeIdentity.Current;
            return Reconcile(
                currentIdentity,
                ResolveCurrentPackageVersion(currentIdentity));
        }

        public static PackageInstallerSelfUpdateSnapshot CaptureSnapshot()
        {
            return CaptureSnapshot(PackageInstallerRuntimeIdentity.Current);
        }

        public static bool AcknowledgeApplied()
        {
            if (!TryLoad(out PersistedSelfUpdateState state) ||
                state.phase != (int)PackageInstallerSelfUpdatePhase.Applied)
            {
                return false;
            }

            Clear();
            return true;
        }

        public static void Clear()
        {
            SessionState.EraseString(StateKey);
        }

        internal static void BeginForTests(
            string selectedUrl,
            PackageInstallerAssemblyIdentity sourceIdentity)
        {
            Begin(selectedUrl, sourceIdentity, sourceIdentity.Version);
        }

        internal static void BeginForTests(
            string selectedUrl,
            PackageInstallerAssemblyIdentity sourceIdentity,
            string resolvedVersionBeforeAdd)
        {
            Begin(selectedUrl, sourceIdentity, resolvedVersionBeforeAdd);
        }

        internal static PackageInstallerSelfUpdateReconcileResult ReconcileForTests(
            PackageInstallerAssemblyIdentity currentIdentity)
        {
            return Reconcile(currentIdentity, currentIdentity.Version);
        }

        internal static PackageInstallerSelfUpdateSnapshot CaptureSnapshotForTests(
            PackageInstallerAssemblyIdentity currentIdentity)
        {
            return CaptureSnapshot(currentIdentity);
        }

        internal static PackageInstallerSelfUpdateSnapshot CapturePersistedSnapshotForTests()
        {
            if (!TryLoad(out PersistedSelfUpdateState state))
            {
                return PackageInstallerSelfUpdateSnapshot.None;
            }

            return new PackageInstallerSelfUpdateSnapshot(
                state.phase == (int)PackageInstallerSelfUpdatePhase.AwaitingReload,
                state.sourceVersion,
                state.sourceModuleVersionId,
                state.resolvedVersionBeforeAdd,
                state.resolvedVersionAfterAdd,
                state.selectedUrl);
        }

        private static PackageInstallerSelfUpdateSnapshot CaptureSnapshot(
            PackageInstallerAssemblyIdentity currentIdentity)
        {
            if (!TryLoad(out PersistedSelfUpdateState state) ||
                state.phase != (int)PackageInstallerSelfUpdatePhase.AwaitingReload ||
                !ModuleVersionIdsMatch(
                    new PackageInstallerAssemblyIdentity(
                        state.sourceVersion,
                        state.sourceModuleVersionId),
                    currentIdentity))
            {
                return PackageInstallerSelfUpdateSnapshot.None;
            }

            return new PackageInstallerSelfUpdateSnapshot(
                true,
                state.sourceVersion,
                state.sourceModuleVersionId,
                state.resolvedVersionBeforeAdd,
                state.resolvedVersionAfterAdd,
                state.selectedUrl);
        }

        private static void Begin(
            string selectedUrl,
            PackageInstallerAssemblyIdentity sourceIdentity,
            string resolvedVersionBeforeAdd)
        {
            Save(new PersistedSelfUpdateState
            {
                schemaVersion = CurrentSchemaVersion,
                phase = (int)PackageInstallerSelfUpdatePhase.Installing,
                sourceVersion = sourceIdentity.Version,
                sourceModuleVersionId = sourceIdentity.ModuleVersionId,
                selectedUrl = selectedUrl ?? string.Empty,
                resolvedVersionBeforeAdd = resolvedVersionBeforeAdd ?? string.Empty,
                resolvedVersionAfterAdd = string.Empty
            });
        }

        private static PackageInstallerSelfUpdateReconcileResult Reconcile(
            PackageInstallerAssemblyIdentity currentIdentity,
            string currentResolvedVersion)
        {
            if (!TryLoad(out PersistedSelfUpdateState state))
            {
                return PackageInstallerSelfUpdateReconcileResult.None;
            }

            PackageInstallerAssemblyIdentity sourceIdentity = new PackageInstallerAssemblyIdentity(
                state.sourceVersion,
                state.sourceModuleVersionId);

            if (state.phase == (int)PackageInstallerSelfUpdatePhase.Applied)
            {
                return PackageInstallerSelfUpdateReconcileResult.AppliedOnReload;
            }

            if (!ModuleVersionIdsMatch(sourceIdentity, currentIdentity))
            {
                if (state.phase == (int)PackageInstallerSelfUpdatePhase.Installing)
                {
                    state.phase = (int)PackageInstallerSelfUpdatePhase.AwaitingReload;
                    state.resolvedVersionAfterAdd = currentResolvedVersion ?? string.Empty;
                    Save(state);
                }

                if (state.phase == (int)PackageInstallerSelfUpdatePhase.AwaitingReload)
                {
                    state.phase = (int)PackageInstallerSelfUpdatePhase.Applied;
                    Save(state);
                    return PackageInstallerSelfUpdateReconcileResult.AppliedOnReload;
                }

                return PackageInstallerSelfUpdateReconcileResult.None;
            }

            return state.phase == (int)PackageInstallerSelfUpdatePhase.AwaitingReload
                ? PackageInstallerSelfUpdateReconcileResult.Pending
                : PackageInstallerSelfUpdateReconcileResult.None;
        }

        private static bool ModuleVersionIdsMatch(
            PackageInstallerAssemblyIdentity first,
            PackageInstallerAssemblyIdentity second)
        {
            return !string.IsNullOrWhiteSpace(first.ModuleVersionId) &&
                   string.Equals(
                       first.ModuleVersionId,
                       second.ModuleVersionId,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveCurrentPackageVersion(
            PackageInstallerAssemblyIdentity currentIdentity)
        {
            PackageManagerPackageInfo packageInfo = PackageManagerPackageInfo.FindForAssembly(
                typeof(PackageInstallerRuntimeIdentity).Assembly);

            return packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.version)
                ? packageInfo.version
                : currentIdentity.Version;
        }

        private static bool TryLoad(out PersistedSelfUpdateState state)
        {
            state = null;
            string json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                state = JsonUtility.FromJson<PersistedSelfUpdateState>(json);
            }
            catch (ArgumentException)
            {
                Clear();
                return false;
            }

            if (state == null ||
                state.schemaVersion != CurrentSchemaVersion ||
                state.phase == (int)PackageInstallerSelfUpdatePhase.None ||
                state.phase > (int)PackageInstallerSelfUpdatePhase.Applied ||
                string.IsNullOrWhiteSpace(state.sourceModuleVersionId))
            {
                Clear();
                state = null;
                return false;
            }

            return true;
        }

        private static void Save(PersistedSelfUpdateState state)
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        [Serializable]
        private sealed class PersistedSelfUpdateState
        {
            public int schemaVersion;
            public int phase;
            public string sourceVersion;
            public string sourceModuleVersionId;
            public string selectedUrl;
            public string resolvedVersionBeforeAdd;
            public string resolvedVersionAfterAdd;
        }
    }
}
