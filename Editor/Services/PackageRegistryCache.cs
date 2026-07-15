using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryCacheCommitGuard
    {
        private readonly object _gate = new object();
        private bool _canCommit = true;

        public void Revoke()
        {
            lock (_gate)
            {
                _canCommit = false;
            }
        }

        public bool TryCommit(Action commit)
        {
            if (commit == null)
            {
                return false;
            }

            lock (_gate)
            {
                if (!_canCommit)
                {
                    return false;
                }

                commit();
                return true;
            }
        }
    }

    internal sealed class PackageRegistryCacheEntry
    {
        public PackageRegistryCacheEntry(
            string registryJson,
            string sourceUrl,
            string entityTag,
            string contentHash,
            DateTimeOffset fetchedAtUtc,
            string registryUpdatedAt)
        {
            RegistryJson = registryJson ?? string.Empty;
            SourceUrl = sourceUrl ?? string.Empty;
            EntityTag = entityTag ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            FetchedAtUtc = fetchedAtUtc;
            RegistryUpdatedAt = registryUpdatedAt ?? string.Empty;
        }

        public string RegistryJson { get; }

        public string SourceUrl { get; }

        public string EntityTag { get; }

        public string ContentHash { get; }

        public DateTimeOffset FetchedAtUtc { get; }

        public string RegistryUpdatedAt { get; }
    }

    internal sealed class PackageRegistryCache
    {
        public const int CacheSchemaVersion = 1;
        public const string CacheFileName = "registry-cache-v1.json";

        private readonly string _cachePath;
        private readonly PackageInstallerAtomicFileCommitter _atomicCommitter;

        public PackageRegistryCache(string cachePath = null)
            : this(cachePath, null)
        {
        }

        internal PackageRegistryCache(
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
            string expectedSourceUrl,
            out PackageRegistryCacheEntry entry,
            out string errorMessage)
        {
            entry = null;
            errorMessage = string.Empty;

            if (!File.Exists(_cachePath))
            {
                return false;
            }

            try
            {
                string cacheJson = File.ReadAllText(_cachePath);
                CacheEnvelope envelope = JsonUtility.FromJson<CacheEnvelope>(cacheJson);

                if (envelope == null)
                {
                    errorMessage = "Cached registry metadata is empty.";
                    return false;
                }

                if (envelope.schemaVersion != CacheSchemaVersion)
                {
                    errorMessage = "Unsupported cached registry schemaVersion: " +
                                   envelope.schemaVersion + ".";
                    return false;
                }

                if (!string.Equals(
                        (expectedSourceUrl ?? string.Empty).Trim(),
                        (envelope.sourceUrl ?? string.Empty).Trim(),
                        StringComparison.Ordinal))
                {
                    errorMessage = "Cached registry source URL does not match the configured remote registry.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(envelope.registryJson))
                {
                    errorMessage = "Cached registry JSON is empty.";
                    return false;
                }

                string contentHash = ComputeContentHash(envelope.registryJson);

                if (!string.Equals(
                        contentHash,
                        envelope.contentHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Cached registry content hash does not match its metadata.";
                    return false;
                }

                if (!DateTimeOffset.TryParse(
                        envelope.fetchedAtUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset fetchedAtUtc))
                {
                    errorMessage = "Cached registry fetch timestamp is invalid.";
                    return false;
                }

                entry = new PackageRegistryCacheEntry(
                    envelope.registryJson,
                    envelope.sourceUrl,
                    envelope.entityTag,
                    contentHash,
                    fetchedAtUtc.ToUniversalTime(),
                    envelope.registryUpdatedAt);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not read cached registry: " + exception.Message;
                return false;
            }
        }

        public bool TryWrite(
            string registryJson,
            string sourceUrl,
            string entityTag,
            DateTimeOffset fetchedAtUtc,
            string registryUpdatedAt,
            out string errorMessage,
            CancellationToken cancellationToken = default(CancellationToken),
            PackageRegistryCacheCommitGuard commitGuard = null)
        {
            errorMessage = string.Empty;
            string tempPath = string.Empty;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = Path.GetDirectoryName(_cachePath);

                if (string.IsNullOrWhiteSpace(directory))
                {
                    errorMessage = "Cached registry path has no parent directory.";
                    return false;
                }

                Directory.CreateDirectory(directory);

                CacheEnvelope envelope = new CacheEnvelope
                {
                    schemaVersion = CacheSchemaVersion,
                    sourceUrl = (sourceUrl ?? string.Empty).Trim(),
                    entityTag = entityTag ?? string.Empty,
                    contentHash = ComputeContentHash(registryJson),
                    fetchedAtUtc = fetchedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    registryUpdatedAt = registryUpdatedAt ?? string.Empty,
                    registryJson = registryJson ?? string.Empty
                };
                string cacheJson = JsonUtility.ToJson(envelope, true);
                tempPath = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(tempPath, cacheJson, new UTF8Encoding(false));
                cancellationToken.ThrowIfCancellationRequested();

                Action commit = () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _atomicCommitter.Commit(
                        tempPath,
                        _cachePath,
                        () => cancellationToken.ThrowIfCancellationRequested());
                };

                if (commitGuard != null && !commitGuard.TryCommit(commit))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    errorMessage = "Cached registry write was superseded by a newer refresh.";
                    return false;
                }

                if (commitGuard == null)
                {
                    commit();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not write cached registry atomically: " + exception.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        internal static string ComputeContentHash(string content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
                byte[] hash = sha256.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);

                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
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

        [Serializable]
        private sealed class CacheEnvelope
        {
            public int schemaVersion;
            public string sourceUrl;
            public string entityTag;
            public string contentHash;
            public string fetchedAtUtc;
            public string registryUpdatedAt;
            public string registryJson;
        }
    }
}
