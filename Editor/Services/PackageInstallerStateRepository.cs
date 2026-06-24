using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    // Project-scoped package-management state shared by Bootstrap and Package Installer.
    // Registry/package metadata and installed UPM data remain owned by their existing services.
    internal sealed class PackageInstallerStateRepository
    {
        internal const string ProjectChannelPreferencePrefix =
            "Deucarian.PackageManagement.SelectedChannel.";
        private const string LegacyBootstrapChannelPreferencePrefix =
            "Deucarian.Bootstrap.Channel.";

        public PackageChannel GetProjectChannel()
        {
            return GetProjectChannel(GetProjectRoot());
        }

        public void SetProjectChannel(PackageChannel channel)
        {
            SetProjectChannel(GetProjectRoot(), channel);
        }

        public string GetManifestStateSignature()
        {
            return GetManifestStateSignature(GetProjectRoot());
        }

        internal static PackageChannel GetProjectChannelForTests(string projectRoot)
        {
            return GetProjectChannel(projectRoot);
        }

        internal static void SetProjectChannelForTests(string projectRoot, PackageChannel channel)
        {
            SetProjectChannel(projectRoot, channel);
        }

        internal static string GetProjectChannelPreferenceKeyForTests(string projectRoot)
        {
            return GetProjectChannelPreferenceKey(projectRoot);
        }

        internal static string GetLegacyBootstrapChannelPreferenceKeyForTests(string projectRoot)
        {
            return GetLegacyBootstrapChannelPreferenceKey(projectRoot);
        }

        internal static void DeleteProjectChannelForTests(string projectRoot)
        {
            EditorPrefs.DeleteKey(GetProjectChannelPreferenceKey(projectRoot));
            EditorPrefs.DeleteKey(GetLegacyBootstrapChannelPreferenceKey(projectRoot));
        }

        internal static string GetManifestStateSignatureForTests(string projectRoot)
        {
            return GetManifestStateSignature(projectRoot);
        }

        private static PackageChannel GetProjectChannel(string projectRoot)
        {
            string key = GetProjectChannelPreferenceKey(projectRoot);

            if (EditorPrefs.HasKey(key))
            {
                return ParseStoredProjectChannel(EditorPrefs.GetInt(key, (int)PackageChannel.Stable));
            }

            string legacyBootstrapKey = GetLegacyBootstrapChannelPreferenceKey(projectRoot);

            if (EditorPrefs.HasKey(legacyBootstrapKey))
            {
                return ParseStoredProjectChannel(EditorPrefs.GetInt(legacyBootstrapKey, (int)PackageChannel.Stable));
            }

            return PackageChannel.Stable;
        }

        private static void SetProjectChannel(string projectRoot, PackageChannel channel)
        {
            PackageChannel safeChannel = channel == PackageChannel.Development
                ? PackageChannel.Development
                : PackageChannel.Stable;
            EditorPrefs.SetInt(GetProjectChannelPreferenceKey(projectRoot), (int)safeChannel);
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

        private static string GetLegacyBootstrapChannelPreferenceKey(string projectRoot)
        {
            return LegacyBootstrapChannelPreferencePrefix + ComputeStableProjectHash(projectRoot);
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

            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;
                uint hash = offsetBasis;

                for (int i = 0; i < normalizedProjectRoot.Length; i++)
                {
                    hash ^= normalizedProjectRoot[i];
                    hash *= prime;
                }

                return hash.ToString("x8");
            }
        }
    }
}
