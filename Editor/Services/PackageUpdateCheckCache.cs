using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageUpdateCheckCacheSnapshot
    {
        public PackageUpdateCheckCacheSnapshot(
            string manifestSignature,
            DateTime? lastCheckedUtc,
            string lastStatusMessage,
            string lastFailureMessage,
            IEnumerable<PackageUpdateStatus> statuses)
        {
            ManifestSignature = manifestSignature ?? string.Empty;
            LastCheckedUtc = lastCheckedUtc?.ToUniversalTime();
            LastStatusMessage = lastStatusMessage ?? string.Empty;
            LastFailureMessage = lastFailureMessage ?? string.Empty;
            Statuses = (statuses ?? Array.Empty<PackageUpdateStatus>())
                .Where(status => status != null)
                .ToArray();
        }

        public string ManifestSignature { get; }

        public DateTime? LastCheckedUtc { get; }

        public string LastStatusMessage { get; }

        public string LastFailureMessage { get; }

        public IReadOnlyList<PackageUpdateStatus> Statuses { get; }
    }

    internal sealed class PackageUpdateCheckCache
    {
        public const int CacheSchemaVersion = 1;
        public const string CacheFileName = "update-check-cache-v1.json";

        private readonly string _cachePath;
        private readonly PackageInstallerAtomicFileCommitter _atomicCommitter;

        public PackageUpdateCheckCache(string cachePath = null)
            : this(cachePath, null)
        {
        }

        internal PackageUpdateCheckCache(
            string cachePath,
            PackageInstallerAtomicFileCommitter atomicCommitter)
        {
            _cachePath = string.IsNullOrWhiteSpace(cachePath)
                ? GetDefaultCachePath()
                : Path.GetFullPath(cachePath);
            _atomicCommitter = atomicCommitter ?? PackageInstallerAtomicFileCommitter.Shared;
        }

        internal string CachePath => _cachePath;

        public bool TryRead(
            string expectedManifestSignature,
            out PackageUpdateCheckCacheSnapshot snapshot,
            out string errorMessage)
        {
            snapshot = null;
            errorMessage = string.Empty;

            if (!File.Exists(_cachePath))
            {
                return false;
            }

            try
            {
                CacheEnvelope envelope = JsonUtility.FromJson<CacheEnvelope>(File.ReadAllText(_cachePath));

                if (envelope == null)
                {
                    return DiscardInvalidCache("Cached update-check state is empty.", out errorMessage);
                }

                if (envelope.schemaVersion != CacheSchemaVersion)
                {
                    return DiscardInvalidCache(
                        "Unsupported cached update-check schemaVersion: " + envelope.schemaVersion + ".",
                        out errorMessage);
                }

                if (!string.Equals(
                        expectedManifestSignature ?? string.Empty,
                        envelope.manifestSignature ?? string.Empty,
                        StringComparison.Ordinal))
                {
                    TryDelete(out _);
                    return false;
                }

                if (!TryParseOptionalUtc(envelope.lastCheckedUtc, out DateTime? lastCheckedUtc))
                {
                    return DiscardInvalidCache(
                        "Cached update-check timestamp is invalid.",
                        out errorMessage);
                }

                List<PackageUpdateStatus> statuses = new List<PackageUpdateStatus>();

                foreach (CacheStatusEntry entry in envelope.statuses ?? Array.Empty<CacheStatusEntry>())
                {
                    if (!TryRestoreStatus(entry, out PackageUpdateStatus status))
                    {
                        return DiscardInvalidCache(
                            "Cached update-check status data is invalid.",
                            out errorMessage);
                    }

                    if (IsPersistable(status))
                    {
                        statuses.Add(status);
                    }
                }

                snapshot = new PackageUpdateCheckCacheSnapshot(
                    envelope.manifestSignature,
                    lastCheckedUtc,
                    envelope.lastStatusMessage,
                    envelope.lastFailureMessage,
                    statuses);
                return true;
            }
            catch (Exception exception)
            {
                DiscardInvalidCache(
                    "Could not read cached update-check state: " + exception.Message,
                    out errorMessage);
                return false;
            }
        }

        public bool TryWrite(
            string manifestSignature,
            DateTime? lastCheckedUtc,
            string lastStatusMessage,
            string lastFailureMessage,
            IEnumerable<PackageUpdateStatus> statuses,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            string temporaryPath = string.Empty;

            try
            {
                string directory = Path.GetDirectoryName(_cachePath);

                if (string.IsNullOrWhiteSpace(directory))
                {
                    errorMessage = "Cached update-check path has no parent directory.";
                    return false;
                }

                Directory.CreateDirectory(directory);
                CacheEnvelope envelope = new CacheEnvelope
                {
                    schemaVersion = CacheSchemaVersion,
                    manifestSignature = manifestSignature ?? string.Empty,
                    savedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    lastCheckedUtc = FormatOptionalUtc(lastCheckedUtc),
                    lastStatusMessage = lastStatusMessage ?? string.Empty,
                    lastFailureMessage = lastFailureMessage ?? string.Empty,
                    statuses = (statuses ?? Array.Empty<PackageUpdateStatus>())
                        .Where(IsPersistable)
                        .OrderBy(status => status.PackageId, StringComparer.OrdinalIgnoreCase)
                        .Select(CreateEntry)
                        .ToArray()
                };
                string json = JsonUtility.ToJson(envelope, true);
                temporaryPath = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                _atomicCommitter.Commit(temporaryPath, _cachePath);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not write cached update-check state atomically: " + exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public bool TryDelete(out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                _atomicCommitter.Delete(_cachePath);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not discard cached update-check state: " + exception.Message;
                return false;
            }
        }

        internal static bool IsPersistable(PackageUpdateStatus status)
        {
            return status != null &&
                   status.Kind != PackageUpdateStatusKind.Unknown &&
                   status.Kind != PackageUpdateStatusKind.Checking;
        }

        internal static string GetDefaultCachePath()
        {
            DirectoryInfo projectDirectory = Directory.GetParent(Application.dataPath);
            string projectRoot = projectDirectory != null
                ? projectDirectory.FullName
                : Application.dataPath;
            return Path.Combine(
                projectRoot,
                "Library",
                "Deucarian",
                "PackageInstaller",
                CacheFileName);
        }

        private bool DiscardInvalidCache(string message, out string errorMessage)
        {
            errorMessage = message ?? string.Empty;

            if (!TryDelete(out string deleteError) && !string.IsNullOrWhiteSpace(deleteError))
            {
                errorMessage += " " + deleteError;
            }

            return false;
        }

        private static bool TryParseOptionalUtc(string value, out DateTime? utc)
        {
            utc = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (!DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTime parsed))
            {
                return false;
            }

            utc = parsed.ToUniversalTime();
            return true;
        }

        private static string FormatOptionalUtc(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static CacheStatusEntry CreateEntry(PackageUpdateStatus status)
        {
            return new CacheStatusEntry
            {
                kind = (int)status.Kind,
                packageId = status.PackageId,
                displayName = status.DisplayName,
                channel = (int)status.Channel,
                selectedUrl = status.SelectedUrl,
                installedRevision = status.InstalledRevision,
                latestRevision = status.LatestRevision,
                installedVersion = status.InstalledVersion,
                latestVersion = status.LatestVersion,
                runningVersion = status.RunningVersion,
                message = status.Message
            };
        }

        private static bool TryRestoreStatus(
            CacheStatusEntry entry,
            out PackageUpdateStatus status)
        {
            status = null;

            if (entry == null ||
                string.IsNullOrWhiteSpace(entry.packageId) ||
                !Enum.IsDefined(typeof(PackageUpdateStatusKind), entry.kind) ||
                !Enum.IsDefined(typeof(PackageChannel), entry.channel))
            {
                return false;
            }

            status = PackageUpdateStatus.Restore(
                (PackageUpdateStatusKind)entry.kind,
                entry.packageId,
                entry.displayName,
                (PackageChannel)entry.channel,
                entry.selectedUrl,
                entry.installedRevision,
                entry.latestRevision,
                entry.installedVersion,
                entry.latestVersion,
                entry.runningVersion,
                entry.message);
            return true;
        }

        [Serializable]
        private sealed class CacheEnvelope
        {
            public int schemaVersion;
            public string manifestSignature;
            public string savedAtUtc;
            public string lastCheckedUtc;
            public string lastStatusMessage;
            public string lastFailureMessage;
            public CacheStatusEntry[] statuses;
        }

        [Serializable]
        private sealed class CacheStatusEntry
        {
            public int kind;
            public string packageId;
            public string displayName;
            public int channel;
            public string selectedUrl;
            public string installedRevision;
            public string latestRevision;
            public string installedVersion;
            public string latestVersion;
            public string runningVersion;
            public string message;
        }
    }
}
