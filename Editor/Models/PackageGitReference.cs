using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Deucarian.PackageInstaller.Editor
{
    internal readonly struct PackageGitReference
    {
        private const string GitPrefix = "git+";
        private readonly bool metadataRemoteSupported;
        private readonly string metadataRemote;

        private PackageGitReference(
            string repositoryIdentity,
            string packagePath,
            string referenceName,
            bool metadataRemoteSupported,
            string metadataRemote)
        {
            RepositoryIdentity = repositoryIdentity;
            PackagePath = packagePath;
            ReferenceName = referenceName;
            this.metadataRemoteSupported = metadataRemoteSupported;
            this.metadataRemote = metadataRemote;
        }

        public string RepositoryIdentity { get; }

        public string PackagePath { get; }

        public string ReferenceName { get; }

        public string RepositoryReferenceIdentity =>
            BuildReferenceIdentity(includePackagePath: false);

        public string PackageReferenceIdentity =>
            BuildReferenceIdentity(includePackagePath: true);

        public PackageGitReference WithReferenceName(string referenceName)
        {
            string normalizedReferenceName = NormalizeReferenceName(referenceName);
            return string.IsNullOrWhiteSpace(normalizedReferenceName)
                ? this
                : new PackageGitReference(
                    RepositoryIdentity,
                    PackagePath,
                    normalizedReferenceName,
                    metadataRemoteSupported,
                    metadataRemote);
        }

        public bool TryCreateGitHubPackageJsonUrl(
            string referenceNameOverride,
            out string packageJsonUrl)
        {
            packageJsonUrl = string.Empty;
            return (RepositoryIdentity ?? string.Empty).StartsWith("github.com/", StringComparison.OrdinalIgnoreCase) &&
                   TryCreatePackageJsonUrl(referenceNameOverride, out packageJsonUrl);
        }

        public bool TryCreatePackageJsonUrl(string referenceNameOverride, out string packageJsonUrl)
        {
            packageJsonUrl = string.Empty;
            if (!metadataRemoteSupported) return false;
            PackageGitReference effectiveReference = WithReferenceName(referenceNameOverride);
            string repositoryIdentity = effectiveReference.RepositoryIdentity ?? string.Empty;
            int pathIndex = repositoryIdentity.IndexOf('/');
            if (pathIndex <= 0 || pathIndex == repositoryIdentity.Length - 1) return false;
            string host = repositoryIdentity.Substring(0, pathIndex);
            string repositoryPath = repositoryIdentity.Substring(pathIndex + 1);
            if (!Regex.IsMatch(repositoryPath, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ||
                !SafeMetadataPath(repositoryPath)) return false;
            string manifestPath = string.IsNullOrWhiteSpace(effectiveReference.PackagePath)
                ? "package.json"
                : effectiveReference.PackagePath + "/package.json";
            string referenceName = NormalizeReferenceIdentity(effectiveReference.ReferenceName);
            if (!Regex.IsMatch(referenceName, @"^[A-Za-z0-9][A-Za-z0-9._/-]*$") ||
                !SafeMetadataPath(referenceName) || !SafeMetadataPath(manifestPath)) return false;

            // Bitbucket's src route treats the revision as one URL component, including branch slashes.
            packageJsonUrl = host == "bitbucket.org"
                ? "https://api.bitbucket.org/2.0/repositories/" + repositoryPath + "/src/" +
                  Uri.EscapeDataString(referenceName) + "/" + EscapePath(manifestPath)
                : "https://raw.githubusercontent.com/" + repositoryPath + "/" +
                  EscapePath(referenceName) + "/" + EscapePath(manifestPath);
            return true;
        }

        private static string EscapePath(string value) => string.Join("/", value.Split('/').Select(Uri.EscapeDataString));

        internal bool TryGetMetadataSource(string referenceOverride, out string remote, out string reference, out string manifestPath)
        {
            remote = reference = manifestPath = string.Empty;
            if (!TryCreatePackageJsonUrl(referenceOverride, out _)) return false;
            PackageGitReference effective = WithReferenceName(referenceOverride);
            remote = metadataRemote;
            reference = NormalizeReferenceIdentity(effective.ReferenceName);
            manifestPath = string.IsNullOrEmpty(PackagePath) ? "package.json" : PackagePath + "/package.json";
            return true;
        }

        private static bool SafeMetadataPath(string value) => !string.IsNullOrWhiteSpace(value) &&
            value.Length <= 4096 && !value.Any(char.IsControl) && !value.Contains("\\") &&
            value.Split('/').All(part => part.Length > 0 && part != "." && part != "..");

        private static bool SupportsMetadataRemote(string remote)
        {
            if (remote.StartsWith("git@", StringComparison.Ordinal))
            {
                int colon = remote.IndexOf(':');
                if (colon < 0) return false;
                remote = "ssh://" + remote.Substring(0, colon) + "/" + remote.Substring(colon + 1);
            }
            if (!Uri.TryCreate(remote, UriKind.Absolute, out Uri uri) ||
                !(uri.Host == "github.com" || uri.Host == "bitbucket.org") ||
                !(uri.Scheme == "https" || uri.Scheme == "ssh") || !uri.IsDefaultPort ||
                !(uri.UserInfo.Length == 0 || (uri.Scheme == "ssh" && uri.UserInfo == "git")) ||
                uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
            return Regex.IsMatch(uri.AbsolutePath, @"^/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$") &&
                   SafeMetadataPath(uri.AbsolutePath.Trim('/'));
        }

        public static bool TryParse(string value, out PackageGitReference packageReference)
        {
            packageReference = default(PackageGitReference);

            if (string.IsNullOrWhiteSpace(value) || value.Length > 4096 || value.Any(char.IsControl))
            {
                return false;
            }

            string trimmedValue = value.Trim();
            int hashIndex = trimmedValue.LastIndexOf('#');

            if (hashIndex < 0 || hashIndex == trimmedValue.Length - 1)
            {
                return false;
            }

            string referenceName = NormalizeReferenceName(trimmedValue.Substring(hashIndex + 1));
            string remoteAndQuery = trimmedValue.Substring(0, hashIndex);
            string packagePath = string.Empty;
            int queryIndex = remoteAndQuery.IndexOf('?');
            bool metadataQuerySupported = true;

            if (queryIndex >= 0)
            {
                string query = remoteAndQuery.Substring(queryIndex + 1);
                metadataQuerySupported = query.StartsWith("path=", StringComparison.OrdinalIgnoreCase) &&
                    query.IndexOf('&') < 0 && query.IndexOf(';') < 0;
                packagePath = ExtractPackagePath(query);
                remoteAndQuery = remoteAndQuery.Substring(0, queryIndex);
            }

            if (remoteAndQuery.StartsWith(GitPrefix, StringComparison.OrdinalIgnoreCase))
            {
                remoteAndQuery = remoteAndQuery.Substring(GitPrefix.Length);
            }

            if (string.IsNullOrWhiteSpace(referenceName) ||
                !TryNormalizeRepositoryIdentity(remoteAndQuery, out string repositoryIdentity))
            {
                return false;
            }

            packageReference = new PackageGitReference(
                repositoryIdentity,
                NormalizePackagePath(packagePath),
                referenceName,
                metadataQuerySupported && SupportsMetadataRemote(remoteAndQuery),
                remoteAndQuery);
            return true;
        }

        public static bool MatchesChannel(string installedReference, string channelReference)
        {
            return TryParse(installedReference, out PackageGitReference installed) &&
                   TryParse(channelReference, out PackageGitReference channel) &&
                   installed.Matches(channel);
        }

        public bool Matches(PackageGitReference other)
        {
            return string.Equals(
                       RepositoryIdentity,
                       other.RepositoryIdentity,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(PackagePath, other.PackagePath, StringComparison.Ordinal) &&
                   string.Equals(ReferenceName, other.ReferenceName, StringComparison.Ordinal);
        }

        private string BuildReferenceIdentity(bool includePackagePath)
        {
            string repositoryIdentity = (RepositoryIdentity ?? string.Empty).ToLowerInvariant();
            string referenceIdentity = NormalizeReferenceIdentity(ReferenceName);
            return includePackagePath
                ? repositoryIdentity + "\n" + (PackagePath ?? string.Empty) + "\n" + referenceIdentity
                : repositoryIdentity + "\n" + referenceIdentity;
        }

        private static string NormalizeReferenceIdentity(string referenceName)
        {
            string normalized = NormalizeReferenceName(referenceName);
            return IsHexRevision(normalized)
                ? normalized.ToLowerInvariant()
                : normalized;
        }

        private static bool IsHexRevision(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 7 || value.Length > 40)
            {
                return false;
            }

            foreach (char character in value)
            {
                bool isDigit = character >= '0' && character <= '9';
                bool isLowerHex = character >= 'a' && character <= 'f';
                bool isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isLowerHex && !isUpperHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryNormalizeRepositoryIdentity(
            string remote,
            out string repositoryIdentity)
        {
            repositoryIdentity = string.Empty;
            remote = (remote ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(remote))
            {
                return false;
            }

            if (TryParseScpStyleRemote(remote, out string scpHost, out string scpPath))
            {
                repositoryIdentity = NormalizeRepositoryIdentity(scpHost, scpPath);
                return !string.IsNullOrWhiteSpace(repositoryIdentity);
            }

            if (!Uri.TryCreate(remote, UriKind.Absolute, out Uri remoteUri) ||
                string.IsNullOrWhiteSpace(remoteUri.Host))
            {
                return false;
            }

            repositoryIdentity = NormalizeRepositoryIdentity(
                remoteUri.Host,
                Uri.UnescapeDataString(remoteUri.AbsolutePath));
            return !string.IsNullOrWhiteSpace(repositoryIdentity);
        }

        private static bool TryParseScpStyleRemote(
            string remote,
            out string host,
            out string path)
        {
            host = string.Empty;
            path = string.Empty;

            if (remote.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            int atIndex = remote.LastIndexOf('@');
            int colonIndex = remote.IndexOf(':', atIndex >= 0 ? atIndex + 1 : 0);

            if (colonIndex <= 0 || colonIndex == remote.Length - 1)
            {
                return false;
            }

            host = remote.Substring(atIndex >= 0 ? atIndex + 1 : 0, colonIndex - (atIndex + 1));
            path = remote.Substring(colonIndex + 1);
            return !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(path);
        }

        private static string NormalizeRepositoryIdentity(string host, string path)
        {
            host = (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
            path = NormalizeSlashes(path).Trim('/');

            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - 4);
            }

            return string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : host + "/" + path;
        }

        private static string NormalizeReferenceName(string referenceName)
        {
            referenceName = Uri.UnescapeDataString((referenceName ?? string.Empty).Trim());

            string[] prefixes =
            {
                "refs/heads/",
                "heads/",
                "origin/"
            };

            foreach (string prefix in prefixes)
            {
                if (referenceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return referenceName.Substring(prefix.Length).Trim();
                }
            }

            return referenceName;
        }

        private static string ExtractPackagePath(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return string.Empty;
            }

            foreach (string part in query.Split('&'))
            {
                int equalsIndex = part.IndexOf('=');
                string key = equalsIndex >= 0 ? part.Substring(0, equalsIndex) : part;

                if (!string.Equals(key, "path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = equalsIndex >= 0 ? part.Substring(equalsIndex + 1) : string.Empty;
                return Uri.UnescapeDataString(value).Trim();
            }

            return string.Empty;
        }

        private static string NormalizePackagePath(string path)
        {
            return NormalizeSlashes(path).Trim('/');
        }

        private static string NormalizeSlashes(string value)
        {
            string normalized = (value ?? string.Empty).Trim().Replace('\\', '/');

            while (normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("//", "/");
            }

            return normalized;
        }
    }
}
