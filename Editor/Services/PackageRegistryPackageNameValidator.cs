using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageRegistryPackageNameValidator
    {
        public static bool ValidatePackageNames(
            PackageRegistry registry,
            Func<PackageRegistryEntry, string> packageJsonProvider,
            out string message)
        {
            if (!PackageRegistryValidator.Validate(registry, out message))
            {
                return false;
            }

            if (packageJsonProvider == null)
            {
                message = "Package JSON provider is unavailable.";
                return false;
            }

            foreach (PackageRegistryEntry package in registry.packages)
            {
                string packageJson;

                try
                {
                    packageJson = packageJsonProvider(package);
                }
                catch (Exception exception)
                {
                    message = "Could not read target package.json for " + package.id + ": " +
                              exception.GetBaseException().Message;
                    return false;
                }

                if (!ValidatePackageName(package, packageJson, out message))
                {
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        public static async Task<string> ValidateRemotePackageNamesAsync(
            PackageRegistry registry,
            Func<string, Task<string>> packageJsonFetcher)
        {
            PackageRegistryRemoteFetchDelegate fetcher = packageJsonFetcher != null
                ? PackageRegistryRemoteFetch.WrapLegacy(packageJsonFetcher)
                : null;
            return await ValidateRemotePackageNamesAsync(
                registry,
                fetcher,
                CancellationToken.None,
                PackageRegistryRemoteFetch.DefaultTimeout,
                4).ConfigureAwait(false);
        }

        internal static async Task<string> ValidateRemotePackageNamesAsync(
            PackageRegistry registry,
            PackageRegistryRemoteFetchDelegate packageJsonFetcher,
            CancellationToken cancellationToken,
            TimeSpan timeout,
            int maxConcurrency = 4,
            IPackageManifestReader manifestReader = null)
        {
            if (!PackageRegistryValidator.Validate(registry, out string message))
            {
                return message;
            }

            if (packageJsonFetcher == null)
            {
                return "Package JSON fetcher is unavailable.";
            }

            int concurrency = Math.Max(1, Math.Min(4, maxConcurrency));

            using (SemaphoreSlim semaphore = new SemaphoreSlim(concurrency, concurrency))
            {
                Task<string>[] validationTasks = registry.packages
                    .Select(package => ValidateRemotePackageNameAsync(
                        package,
                        packageJsonFetcher,
                        semaphore,
                        cancellationToken,
                        timeout,
                        manifestReader))
                    .ToArray();
                string[] validationMessages = await Task.WhenAll(validationTasks).ConfigureAwait(false);

                foreach (string validationMessage in validationMessages)
                {
                    if (!string.IsNullOrWhiteSpace(validationMessage))
                    {
                        return validationMessage;
                    }
                }
            }

            return string.Empty;
        }

        private static async Task<string> ValidateRemotePackageNameAsync(
            PackageRegistryEntry package,
            PackageRegistryRemoteFetchDelegate packageJsonFetcher,
            SemaphoreSlim semaphore,
            CancellationToken cancellationToken,
            TimeSpan timeout,
            IPackageManifestReader manifestReader)
        {
            bool enteredSemaphore = false;
            string activePackageJsonUrl = string.Empty;

            try
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                enteredSemaphore = true;
                string[] channelUrls = new[] { package.stableUrl, package.developmentUrl }
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                foreach (string channelUrl in channelUrls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryCreatePackageJsonUrl(channelUrl, string.Empty, out string packageJsonUrl))
                    {
                        return "Could not resolve target package.json URL for " + package.id +
                               ". Use a credential-free GitHub or Bitbucket Cloud package reference.";
                    }

                    activePackageJsonUrl = packageJsonUrl;

                    PackageRegistryRemoteFetchResponse response =
                        manifestReader != null
                        ? await manifestReader.ReadAsync(channelUrl, string.Empty, cancellationToken, timeout).ConfigureAwait(false)
                        : await PackageRegistryRemoteFetch.ExecuteAsync(
                            packageJsonFetcher,
                            packageJsonUrl,
                            cancellationToken,
                            timeout).ConfigureAwait(false);

                    if (!ValidatePackageName(
                            package,
                            response.Content,
                            packageJsonUrl,
                            out string message))
                    {
                        return message;
                    }
                }

                return string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return "Could not fetch target package.json for " + package.id +
                       (string.IsNullOrWhiteSpace(activePackageJsonUrl)
                           ? string.Empty
                           : " at " + activePackageJsonUrl) + ": " +
                       exception.GetBaseException().Message;
            }
            finally
            {
                if (enteredSemaphore)
                {
                    semaphore.Release();
                }
            }
        }

        internal static bool TryCreateGitHubPackageJsonUrl(string packageUrl, out string packageJsonUrl)
        {
            return TryCreateGitHubPackageJsonUrl(packageUrl, string.Empty, out packageJsonUrl);
        }

        internal static bool TryCreateGitHubPackageJsonUrl(
            string packageUrl,
            string referenceNameOverride,
            out string packageJsonUrl)
        {
            packageJsonUrl = string.Empty;
            return PackageGitReference.TryParse(packageUrl, out PackageGitReference reference) &&
                reference.TryCreateGitHubPackageJsonUrl(referenceNameOverride, out packageJsonUrl);
        }

        internal static bool TryCreatePackageJsonUrl(string packageUrl, string referenceOverride, out string packageJsonUrl)
        {
            packageJsonUrl = string.Empty;
            return PackageGitReference.TryParse(packageUrl, out PackageGitReference reference) &&
                reference.TryCreatePackageJsonUrl(referenceOverride, out packageJsonUrl);
        }

        internal static bool TryReadPackageName(string packageJson, out string packageName)
        {
            packageName = string.Empty;

            if (string.IsNullOrWhiteSpace(packageJson))
            {
                return false;
            }

            try
            {
                PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(packageJson);

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.name))
                {
                    return false;
                }

                packageName = manifest.name.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryReadPackageVersion(string packageJson, out string packageVersion)
        {
            packageVersion = string.Empty;

            if (string.IsNullOrWhiteSpace(packageJson))
            {
                return false;
            }

            try
            {
                PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(packageJson);

                if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
                {
                    return false;
                }

                packageVersion = manifest.version.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ValidatePackageName(
            PackageRegistryEntry package,
            string packageJson,
            out string message)
        {
            return ValidatePackageName(package, packageJson, string.Empty, out message);
        }

        private static bool ValidatePackageName(
            PackageRegistryEntry package,
            string packageJson,
            string packageJsonUrl,
            out string message)
        {
            if (!TryReadPackageName(packageJson, out string packageName))
            {
                message = "Could not read package.json name for " + package.id +
                          FormatPackageJsonUrlSuffix(packageJsonUrl) + ".";
                return false;
            }

            string registryPackageId = package.id != null ? package.id.Trim() : string.Empty;

            if (!string.Equals(registryPackageId, packageName, StringComparison.Ordinal))
            {
                message = "Registry package id " + registryPackageId +
                          " does not match target package.json name " + packageName +
                          FormatPackageJsonUrlSuffix(packageJsonUrl) + ".";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string FormatPackageJsonUrlSuffix(string packageJsonUrl)
        {
            return string.IsNullOrWhiteSpace(packageJsonUrl)
                ? string.Empty
                : " at " + packageJsonUrl;
        }

        [Serializable]
        private sealed class PackageManifest
        {
            public string name;
            public string version;
        }
    }
}
