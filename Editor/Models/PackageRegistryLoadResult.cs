using System;

namespace Deucarian.PackageInstaller.Editor
{
    internal sealed class PackageRegistryLoadResult
    {
        private PackageRegistryLoadResult(
            PackageRegistry registry,
            PackageRegistrySource source,
            bool isValid,
            string errorMessage,
            string sourceUrl = "",
            string entityTag = "",
            string contentHash = "",
            DateTimeOffset? fetchedAtUtc = null)
        {
            Registry = registry;
            Source = source;
            IsValid = isValid;
            ErrorMessage = errorMessage ?? string.Empty;
            SourceUrl = sourceUrl ?? string.Empty;
            EntityTag = entityTag ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
            FetchedAtUtc = fetchedAtUtc;
        }

        public PackageRegistry Registry { get; }

        public PackageRegistrySource Source { get; }

        public bool IsValid { get; }

        public string ErrorMessage { get; }

        public string SourceUrl { get; }

        public string EntityTag { get; }

        public string ContentHash { get; }

        public DateTimeOffset? FetchedAtUtc { get; }

        public string StatusMessage
        {
            get
            {
                switch (Source)
                {
                    case PackageRegistrySource.Remote:
                        return "Using remote registry";
                    case PackageRegistrySource.Cached:
                        return "Using cached registry";
                    case PackageRegistrySource.RemoteFailedUsingCache:
                        return "Remote registry failed, using cached registry";
                    case PackageRegistrySource.RemoteFailedUsingBundled:
                        return "Remote registry failed, using bundled registry";
                    default:
                        return "Using bundled registry";
                }
            }
        }

        public static PackageRegistryLoadResult Success(PackageRegistry registry, PackageRegistrySource source)
        {
            return new PackageRegistryLoadResult(registry, source, true, string.Empty);
        }

        public static PackageRegistryLoadResult Success(
            PackageRegistry registry,
            PackageRegistrySource source,
            string sourceUrl,
            string entityTag,
            string contentHash,
            DateTimeOffset? fetchedAtUtc)
        {
            return new PackageRegistryLoadResult(
                registry,
                source,
                true,
                string.Empty,
                sourceUrl,
                entityTag,
                contentHash,
                fetchedAtUtc);
        }

        public static PackageRegistryLoadResult Failure(PackageRegistrySource source, string errorMessage)
        {
            return new PackageRegistryLoadResult(null, source, false, errorMessage);
        }

        public static PackageRegistryLoadResult RemoteFailureUsingBundled(
            PackageRegistry bundledRegistry,
            string errorMessage)
        {
            return new PackageRegistryLoadResult(
                bundledRegistry,
                PackageRegistrySource.RemoteFailedUsingBundled,
                bundledRegistry != null,
                errorMessage);
        }

        public static PackageRegistryLoadResult RemoteFailureUsingFallback(
            PackageRegistryLoadResult fallback,
            string errorMessage)
        {
            if (fallback == null)
            {
                return Failure(PackageRegistrySource.RemoteFailedUsingBundled, errorMessage);
            }

            bool usesCache = fallback.Source == PackageRegistrySource.Cached ||
                             fallback.Source == PackageRegistrySource.Remote ||
                             fallback.Source == PackageRegistrySource.RemoteFailedUsingCache;
            return new PackageRegistryLoadResult(
                fallback.Registry,
                usesCache
                    ? PackageRegistrySource.RemoteFailedUsingCache
                    : PackageRegistrySource.RemoteFailedUsingBundled,
                fallback.IsValid && fallback.Registry != null,
                errorMessage,
                fallback.SourceUrl,
                fallback.EntityTag,
                fallback.ContentHash,
                fallback.FetchedAtUtc);
        }
    }
}
