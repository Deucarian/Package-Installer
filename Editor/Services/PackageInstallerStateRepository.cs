using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal readonly struct PackageChannelSelection
    {
        public PackageChannelSelection(
            PackageChannel channel,
            long changedAtUtcTicks,
            bool hasValue)
        {
            Channel = channel == PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;
            ChangedAtUtcTicks = Math.Max(0L, changedAtUtcTicks);
            HasValue = hasValue;
        }

        public PackageChannel Channel { get; }

        public long ChangedAtUtcTicks { get; }

        public bool HasValue { get; }

        public static PackageChannelSelection None =>
            new PackageChannelSelection(PackageChannel.Stable, 0L, false);

        public static PackageChannelSelection Create(PackageChannel channel, long changedAtUtcTicks)
        {
            return new PackageChannelSelection(channel, changedAtUtcTicks, true);
        }
    }

    // Project-scoped package-management state shared by Bootstrap and Package Installer.
    // Registry/package metadata and installed UPM data remain owned by their existing services.
    internal sealed class PackageInstallerStateRepository
    {
        internal const string ProjectChannelPreferencePrefix =
            "Deucarian.PackageManagement.SelectedChannel.";
        internal const string ProjectChannelChangedAtPreferencePrefix =
            "Deucarian.PackageManagement.SelectedChannelChangedAt.";
        internal const string PackageChannelPreferencePrefix =
            "Deucarian.PackageManagement.PackageSelectedChannel.";
        internal const string PackageChannelChangedAtPreferencePrefix =
            "Deucarian.PackageManagement.PackageSelectedChannelChangedAt.";
        private const string LegacyBootstrapChannelPreferencePrefix =
            "Deucarian.Bootstrap.Channel.";

        public PackageChannel GetProjectChannel()
        {
            return GetProjectChannel(GetProjectRoot());
        }

        public PackageChannelSelection GetProjectChannelSelection()
        {
            return GetProjectChannelSelection(GetProjectRoot());
        }

        public void SetProjectChannel(PackageChannel channel)
        {
            SetProjectChannel(GetProjectRoot(), channel);
        }

        public void ClearProjectChannel()
        {
            ClearProjectChannel(GetProjectRoot());
        }

        public PackageChannelSelection GetPackageChannelSelection(string packageId)
        {
            return GetPackageChannelSelection(GetProjectRoot(), packageId);
        }

        public void SetPackageChannel(string packageId, PackageChannel channel)
        {
            SetPackageChannel(GetProjectRoot(), packageId, channel);
        }

        public void ClearPackageChannel(string packageId)
        {
            ClearPackageChannel(GetProjectRoot(), packageId);
        }

        public string GetManifestStateSignature()
        {
            return GetManifestStateSignature(GetProjectRoot());
        }

        internal static PackageChannel GetProjectChannelForTests(string projectRoot)
        {
            return GetProjectChannel(projectRoot);
        }

        internal static PackageChannelSelection GetProjectChannelSelectionForTests(string projectRoot)
        {
            return GetProjectChannelSelection(projectRoot);
        }

        internal static void SetProjectChannelForTests(string projectRoot, PackageChannel channel)
        {
            SetProjectChannel(projectRoot, channel);
        }

        internal static void SetProjectChannelForTests(
            string projectRoot,
            PackageChannel channel,
            long changedAtUtcTicks)
        {
            SetProjectChannel(projectRoot, channel, changedAtUtcTicks);
        }

        internal static void ClearProjectChannelForTests(string projectRoot)
        {
            ClearProjectChannel(projectRoot);
        }

        internal static PackageChannelSelection GetPackageChannelSelectionForTests(
            string projectRoot,
            string packageId)
        {
            return GetPackageChannelSelection(projectRoot, packageId);
        }

        internal static void SetPackageChannelForTests(
            string projectRoot,
            string packageId,
            PackageChannel channel,
            long changedAtUtcTicks)
        {
            SetPackageChannel(projectRoot, packageId, channel, changedAtUtcTicks);
        }

        internal static void ClearPackageChannelForTests(string projectRoot, string packageId)
        {
            ClearPackageChannel(projectRoot, packageId);
        }

        internal static string GetProjectChannelPreferenceKeyForTests(string projectRoot)
        {
            return GetProjectChannelPreferenceKey(projectRoot);
        }

        internal static string GetProjectChannelChangedAtPreferenceKeyForTests(string projectRoot)
        {
            return GetProjectChannelChangedAtPreferenceKey(projectRoot);
        }

        internal static string GetPackageChannelPreferenceKeyForTests(string projectRoot, string packageId)
        {
            return GetPackageChannelPreferenceKey(projectRoot, packageId);
        }

        internal static string GetPackageChannelChangedAtPreferenceKeyForTests(
            string projectRoot,
            string packageId)
        {
            return GetPackageChannelChangedAtPreferenceKey(projectRoot, packageId);
        }

        internal static string GetLegacyBootstrapChannelPreferenceKeyForTests(string projectRoot)
        {
            return GetLegacyBootstrapChannelPreferenceKey(projectRoot);
        }

        internal static void DeleteProjectChannelForTests(string projectRoot)
        {
            EditorPrefs.DeleteKey(GetProjectChannelPreferenceKey(projectRoot));
            EditorPrefs.DeleteKey(GetProjectChannelChangedAtPreferenceKey(projectRoot));
            EditorPrefs.DeleteKey(GetLegacyBootstrapChannelPreferenceKey(projectRoot));
        }

        internal static void DeletePackageChannelForTests(string projectRoot, string packageId)
        {
            EditorPrefs.DeleteKey(GetPackageChannelPreferenceKey(projectRoot, packageId));
            EditorPrefs.DeleteKey(GetPackageChannelChangedAtPreferenceKey(projectRoot, packageId));
        }

        internal static string GetManifestStateSignatureForTests(string projectRoot)
        {
            return GetManifestStateSignature(projectRoot);
        }

        private static PackageChannel GetProjectChannel(string projectRoot)
        {
            return GetProjectChannelSelection(projectRoot).Channel;
        }

        private static PackageChannelSelection GetProjectChannelSelection(string projectRoot)
        {
            string key = GetProjectChannelPreferenceKey(projectRoot);

            if (EditorPrefs.HasKey(key))
            {
                return PackageChannelSelection.Create(
                    ParseStoredProjectChannel(EditorPrefs.GetInt(key, (int)PackageChannel.Stable)),
                    GetStoredChangedAtUtcTicks(GetProjectChannelChangedAtPreferenceKey(projectRoot)));
            }

            string legacyBootstrapKey = GetLegacyBootstrapChannelPreferenceKey(projectRoot);

            if (EditorPrefs.HasKey(legacyBootstrapKey))
            {
                return PackageChannelSelection.Create(
                    ParseStoredProjectChannel(EditorPrefs.GetInt(legacyBootstrapKey, (int)PackageChannel.Stable)),
                    0L);
            }

            return PackageChannelSelection.None;
        }

        private static void SetProjectChannel(string projectRoot, PackageChannel channel)
        {
            SetProjectChannel(projectRoot, channel, DateTime.UtcNow.Ticks);
        }

        private static void SetProjectChannel(
            string projectRoot,
            PackageChannel channel,
            long changedAtUtcTicks)
        {
            PackageChannel safeChannel = channel == PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;
            EditorPrefs.SetInt(GetProjectChannelPreferenceKey(projectRoot), (int)safeChannel);
            EditorPrefs.SetString(
                GetProjectChannelChangedAtPreferenceKey(projectRoot),
                NormalizeChangedAtUtcTicks(changedAtUtcTicks).ToString());
        }

        private static void ClearProjectChannel(string projectRoot)
        {
            EditorPrefs.DeleteKey(GetProjectChannelPreferenceKey(projectRoot));
            EditorPrefs.DeleteKey(GetProjectChannelChangedAtPreferenceKey(projectRoot));
            EditorPrefs.DeleteKey(GetLegacyBootstrapChannelPreferenceKey(projectRoot));
        }

        private static PackageChannelSelection GetPackageChannelSelection(
            string projectRoot,
            string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return PackageChannelSelection.None;
            }

            string key = GetPackageChannelPreferenceKey(projectRoot, packageId);

            if (!EditorPrefs.HasKey(key))
            {
                return PackageChannelSelection.None;
            }

            return PackageChannelSelection.Create(
                ParseStoredProjectChannel(EditorPrefs.GetInt(key, (int)PackageChannel.Stable)),
                GetStoredChangedAtUtcTicks(GetPackageChannelChangedAtPreferenceKey(projectRoot, packageId)));
        }

        private static void SetPackageChannel(
            string projectRoot,
            string packageId,
            PackageChannel channel)
        {
            SetPackageChannel(projectRoot, packageId, channel, DateTime.UtcNow.Ticks);
        }

        private static void SetPackageChannel(
            string projectRoot,
            string packageId,
            PackageChannel channel,
            long changedAtUtcTicks)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            PackageChannel safeChannel = channel == PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;
            EditorPrefs.SetInt(GetPackageChannelPreferenceKey(projectRoot, packageId), (int)safeChannel);
            EditorPrefs.SetString(
                GetPackageChannelChangedAtPreferenceKey(projectRoot, packageId),
                NormalizeChangedAtUtcTicks(changedAtUtcTicks).ToString());
        }

        private static void ClearPackageChannel(string projectRoot, string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                return;
            }

            EditorPrefs.DeleteKey(GetPackageChannelPreferenceKey(projectRoot, packageId));
            EditorPrefs.DeleteKey(GetPackageChannelChangedAtPreferenceKey(projectRoot, packageId));
        }

        private static PackageChannel ParseStoredProjectChannel(int value)
        {
            return value == (int)PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;
        }

        private static string GetProjectChannelPreferenceKey(string projectRoot)
        {
            return ProjectChannelPreferencePrefix + ComputeStableProjectHash(projectRoot);
        }

        private static string GetProjectChannelChangedAtPreferenceKey(string projectRoot)
        {
            return ProjectChannelChangedAtPreferencePrefix + ComputeStableProjectHash(projectRoot);
        }

        private static string GetPackageChannelPreferenceKey(string projectRoot, string packageId)
        {
            return PackageChannelPreferencePrefix +
                   ComputeStableProjectHash(projectRoot) +
                   "." +
                   ComputeStablePackageHash(packageId);
        }

        private static string GetPackageChannelChangedAtPreferenceKey(string projectRoot, string packageId)
        {
            return PackageChannelChangedAtPreferencePrefix +
                   ComputeStableProjectHash(projectRoot) +
                   "." +
                   ComputeStablePackageHash(packageId);
        }

        private static string GetLegacyBootstrapChannelPreferenceKey(string projectRoot)
        {
            return LegacyBootstrapChannelPreferencePrefix + ComputeStableProjectHash(projectRoot);
        }

        private static long GetStoredChangedAtUtcTicks(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !EditorPrefs.HasKey(key))
            {
                return 0L;
            }

            return long.TryParse(EditorPrefs.GetString(key, "0"), out long changedAtUtcTicks)
                ? Math.Max(0L, changedAtUtcTicks)
                : 0L;
        }

        private static long NormalizeChangedAtUtcTicks(long changedAtUtcTicks)
        {
            return Math.Max(1L, changedAtUtcTicks);
        }

        private static string GetManifestStateSignature(string projectRoot)
        {
            string safeProjectRoot = projectRoot ?? string.Empty;
            return CreateFileStateSignature(
                Path.Combine(safeProjectRoot, "Packages", "manifest.json")) +
                   "|" +
                   CreateFileStateSignature(
                       Path.Combine(safeProjectRoot, "Packages", "packages-lock.json")) +
                   "|" +
                   CreateFileStateSignature(
                       Path.Combine(safeProjectRoot, "Packages", "package-lock.json"));
        }

        private static string CreateFileStateSignature(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "missing";
            }

            try
            {
                FileInfo fileInfo = new FileInfo(path);
                return fileInfo.Length + ":" + fileInfo.LastWriteTimeUtc.Ticks;
            }
            catch (IOException)
            {
                return "unreadable";
            }
            catch (UnauthorizedAccessException)
            {
                return "unreadable";
            }
        }

        private static string GetProjectRoot()
        {
            if (string.IsNullOrWhiteSpace(Application.dataPath))
            {
                return string.Empty;
            }

            DirectoryInfo parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }

        private static string ComputeStableProjectHash(string projectRoot)
        {
            string normalizedProjectRoot = (projectRoot ?? string.Empty)
                .Replace('\\', '/')
                .TrimEnd('/')
                .ToLowerInvariant();

            return ComputeStableHash(normalizedProjectRoot);
        }

        private static string ComputeStablePackageHash(string packageId)
        {
            string normalizedPackageId = (packageId ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            return ComputeStableHash(normalizedPackageId);
        }

        private static string ComputeStableHash(string value)
        {
            string normalizedValue = value ?? string.Empty;

            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;
                uint hash = offsetBasis;

                for (int i = 0; i < normalizedValue.Length; i++)
                {
                    hash ^= normalizedValue[i];
                    hash *= prime;
                }

                return hash.ToString("x8");
            }
        }
    }
}
