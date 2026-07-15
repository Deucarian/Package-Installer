using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryLoader
    {
        public const string RemoteRegistryUrl =
            "https://raw.githubusercontent.com/Deucarian/Package-Registry/main/packages.json";
        public const string BundledRegistryFileName = "PackageRegistry.json";

        private readonly PackageRegistryRemoteFetchDelegate _remoteFetcher;
        private readonly PackageRegistryRemoteFetchDelegate _packageManifestFetcher;
        private readonly string _remoteRegistryUrl;
        private readonly PackageRegistryCache _cache;
        private readonly TimeSpan _requestTimeout;

        public PackageRegistryLoader(
            Func<string, Task<string>> remoteFetcher = null,
            string remoteRegistryUrl = RemoteRegistryUrl,
            Func<string, Task<string>> packageManifestFetcher = null)
            : this(
                PackageRegistryRemoteFetch.WrapLegacy(remoteFetcher),
                remoteRegistryUrl,
                PackageRegistryRemoteFetch.WrapLegacy(packageManifestFetcher),
                PackageRegistryCache.GetDefaultCachePath(),
                PackageRegistryRemoteFetch.DefaultTimeout)
        {
        }

        internal PackageRegistryLoader(
            PackageRegistryRemoteFetchDelegate remoteFetcher,
            string remoteRegistryUrl,
            PackageRegistryRemoteFetchDelegate packageManifestFetcher,
            string cachePath,
            TimeSpan requestTimeout)
        {
            _remoteFetcher = remoteFetcher ?? PackageRegistryRemoteFetch.FetchAsync;
            _packageManifestFetcher = packageManifestFetcher ?? PackageRegistryRemoteFetch.FetchAsync;
            _remoteRegistryUrl = string.IsNullOrWhiteSpace(remoteRegistryUrl)
                ? RemoteRegistryUrl
                : remoteRegistryUrl;
            _cache = new PackageRegistryCache(cachePath);
            _requestTimeout = requestTimeout > TimeSpan.Zero
                ? requestTimeout
                : PackageRegistryRemoteFetch.DefaultTimeout;
        }

        public PackageRegistryLoadResult LoadBundled()
        {
            if (!TryReadBundledRegistryJson(out string json, out string errorMessage))
            {
                return PackageRegistryLoadResult.Failure(PackageRegistrySource.Bundled, errorMessage);
            }

            return LoadFromJson(json, PackageRegistrySource.Bundled);
        }

        public async Task<PackageRegistryLoadResult> LoadRemoteAsync(PackageRegistry bundledRegistry)
        {
            PackageRegistryLoadResult fallback = bundledRegistry != null
                ? PackageRegistryLoadResult.Success(bundledRegistry, PackageRegistrySource.Bundled)
                : PackageRegistryLoadResult.Failure(
                    PackageRegistrySource.Bundled,
                    "Bundled registry is unavailable.");
            return await LoadRemoteAsync(fallback, CancellationToken.None).ConfigureAwait(false);
        }

        internal async Task<PackageRegistryLoadResult> LoadRemoteAsync(
            PackageRegistryLoadResult fallback,
            CancellationToken cancellationToken,
            PackageRegistryCacheCommitGuard cacheCommitGuard = null)
        {
            try
            {
                PackageRegistryRemoteFetchResponse response =
                    await PackageRegistryRemoteFetch.ExecuteAsync(
                        _remoteFetcher,
                        _remoteRegistryUrl,
                        cancellationToken,
                        _requestTimeout).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                PackageRegistryLoadResult result = LoadFromJson(
                    response.Content,
                    PackageRegistrySource.Remote);

                if (!result.IsValid)
                {
                    return PackageRegistryLoadResult.RemoteFailureUsingFallback(
                        fallback,
                        result.ErrorMessage);
                }

                string packageNameValidationMessage =
                    await PackageRegistryPackageNameValidator.ValidateRemotePackageNamesAsync(
                        result.Registry,
                        _packageManifestFetcher,
                        cancellationToken,
                        _requestTimeout,
                        4).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(packageNameValidationMessage))
                {
                    return PackageRegistryLoadResult.RemoteFailureUsingFallback(
                        fallback,
                        packageNameValidationMessage);
                }

                cancellationToken.ThrowIfCancellationRequested();
                DateTimeOffset fetchedAtUtc = DateTimeOffset.UtcNow;
                string contentHash = PackageRegistryCache.ComputeContentHash(response.Content);

                if (!_cache.TryWrite(
                        response.Content,
                        _remoteRegistryUrl,
                        response.EntityTag,
                        fetchedAtUtc,
                        result.Registry.updatedAt,
                        out string cacheErrorMessage,
                        cancellationToken,
                        cacheCommitGuard))
                {
                    PackageInstallerLog.Registry.Warning(cacheErrorMessage);
                }

                return PackageRegistryLoadResult.Success(
                    result.Registry,
                    PackageRegistrySource.Remote,
                    _remoteRegistryUrl,
                    response.EntityTag,
                    contentHash,
                    fetchedAtUtc);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return PackageRegistryLoadResult.RemoteFailureUsingFallback(
                    fallback,
                    exception.GetBaseException().Message);
            }
        }

        internal bool TryLoadCached(
            out PackageRegistryLoadResult result,
            out string errorMessage)
        {
            result = null;

            if (!_cache.TryRead(_remoteRegistryUrl, out PackageRegistryCacheEntry entry, out errorMessage))
            {
                return false;
            }

            PackageRegistryLoadResult parsed = LoadFromJson(
                entry.RegistryJson,
                PackageRegistrySource.Cached);

            if (!parsed.IsValid)
            {
                errorMessage = "Cached registry failed validation: " + parsed.ErrorMessage;
                return false;
            }

            if (!string.Equals(
                    parsed.Registry.updatedAt ?? string.Empty,
                    entry.RegistryUpdatedAt ?? string.Empty,
                    StringComparison.Ordinal))
            {
                errorMessage = "Cached registry updatedAt does not match its metadata.";
                return false;
            }

            result = PackageRegistryLoadResult.Success(
                parsed.Registry,
                PackageRegistrySource.Cached,
                entry.SourceUrl,
                entry.EntityTag,
                entry.ContentHash,
                entry.FetchedAtUtc);
            errorMessage = string.Empty;
            return true;
        }

        internal PackageRegistryLoadResult LoadFromJson(string json, PackageRegistrySource source)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return PackageRegistryLoadResult.Failure(source, "Registry JSON is empty.");
            }

            try
            {
                PackageRegistry registry = JsonUtility.FromJson<PackageRegistry>(json);

                if (!PackageRegistryValidator.Validate(registry, out string validationMessage))
                {
                    return PackageRegistryLoadResult.Failure(source, validationMessage);
                }

                return PackageRegistryLoadResult.Success(registry, source);
            }
            catch (Exception exception)
            {
                return PackageRegistryLoadResult.Failure(
                    source,
                    "Registry JSON could not be parsed: " + exception.Message);
            }
        }

        private static bool TryReadBundledRegistryJson(out string json, out string errorMessage)
        {
            json = string.Empty;
            errorMessage = string.Empty;

            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(PackageRegistryLoader).Assembly);

            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                errorMessage = "Could not resolve installer package path.";
                return false;
            }

            string registryPath = Path.Combine(packageInfo.resolvedPath, BundledRegistryFileName);

            if (!File.Exists(registryPath))
            {
                errorMessage = "Bundled registry file was not found: " + registryPath;
                return false;
            }

            try
            {
                json = File.ReadAllText(registryPath);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Could not read bundled registry: " + exception.Message;
                return false;
            }
        }
    }
}
