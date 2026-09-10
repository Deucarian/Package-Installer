using System;
using System.Threading;
using PackageVersionResult = Deucarian.PackageInstaller.Editor.PackageUpdateCheckService.PackageVersionResult;

namespace Deucarian.PackageInstaller.Editor
{
    // Maps metadata transport outcomes to the Installer's existing version result; no update-state ownership.
    internal static class PackageManifestVersionReader
    {
        internal static PackageVersionResult Read(string originalReference, string revisionOverride, CancellationToken token,
            IPackageManifestReader reader, PackageRegistryRemoteFetchDelegate http, TimeSpan timeout)
        {
            if (!PackageRegistryPackageNameValidator.TryCreatePackageJsonUrl(originalReference, revisionOverride, out string url))
                return PackageVersionResult.Fail("Could not resolve target package.json URL.");
            try
            {
                token.ThrowIfCancellationRequested();
                var response = (reader != null ? reader.ReadAsync(originalReference, revisionOverride, token, timeout) :
                    PackageRegistryRemoteFetch.ExecuteAsync(http ?? PackageRegistryRemoteFetch.FetchAsync, url, token, timeout))
                    .GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                if (!PackageRegistryPackageNameValidator.TryReadPackageVersion(response?.Content, out string version))
                    return PackageVersionResult.Fail("Target package.json did not include a version.");
                if (!PackageInstallSourceUtility.LooksLikeStableOrPrereleaseVersion(version))
                    return PackageVersionResult.Fail("Target package.json version is not valid SemVer.");
                return PackageVersionResult.Ok(version);
            }
            catch (OperationCanceledException) { throw; }
            catch (TimeoutException) { return PackageVersionResult.Fail("Package metadata HTTP read timed out. Retry the check."); }
            catch (PackageManifestReadException exception) { return PackageVersionResult.Fail(exception.Message); }
            catch (Exception) { return PackageVersionResult.Fail("Target package metadata is unavailable. Check existing Git authentication and repository access."); }
        }
    }
}
