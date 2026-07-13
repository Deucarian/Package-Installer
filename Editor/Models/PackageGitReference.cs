using System;

namespace Deucarian.PackageInstaller.Editor
{
    internal readonly struct PackageGitReference
    {
        private const string GitPrefix = "git+";

        private PackageGitReference(
            string repositoryIdentity,
            string packagePath,
            string referenceName)
        {
            RepositoryIdentity = repositoryIdentity;
            PackagePath = packagePath;
            ReferenceName = referenceName;
        }

        public string RepositoryIdentity { get; }

        public string PackagePath { get; }

        public string ReferenceName { get; }

        public static bool TryParse(string value, out PackageGitReference packageReference)
        {
            packageReference = default(PackageGitReference);

            if (string.IsNullOrWhiteSpace(value))
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

            if (queryIndex >= 0)
            {
                packagePath = ExtractPackagePath(remoteAndQuery.Substring(queryIndex + 1));
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
                referenceName);
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
