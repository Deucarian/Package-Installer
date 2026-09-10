using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.PackageManager;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentInstalledPackage
    {
        public string Id { get; }
        public string Name { get; }
        public string Remote { get; }
        public string InstalledReference { get; }
        public string InstalledRevision { get; }
        public string Source { get; }
        public string InstalledResolvedPath { get; }
        public string DevelopmentBranch { get; }

        internal DevelopmentInstalledPackage(string id, string name, string remote, string reference, string revision, string source, string resolvedPath = "", string developmentBranch = "develop")
        { Id = id; Name = name; Remote = remote; InstalledReference = reference; InstalledRevision = revision; Source = source; InstalledResolvedPath = resolvedPath; DevelopmentBranch = developmentBranch; }

        internal static IReadOnlyList<DevelopmentInstalledPackage> Capture(IEnumerable<PackageDefinition> catalog,
            IEnumerable<PackageInfo> installed)
        {
            var definitions = catalog.ToDictionary(p => p.PackageId, StringComparer.Ordinal);
            var result = new List<DevelopmentInstalledPackage>();
            foreach (var info in installed)
            {
                if (!definitions.TryGetValue(info.name, out var definition)) continue;
                string reference = info.packageId.StartsWith(info.name + "@", StringComparison.Ordinal)
                    ? info.packageId.Substring(info.name.Length + 1) : info.version;
                try { SourceManifestEdit.ValidateReference(reference); }
                catch (InvalidOperationException) { continue; }
                string remote = definition.GetUrl(PackageChannel.Development);
                int fragment = remote.IndexOf('#');
                if (fragment < 0 || fragment != remote.LastIndexOf('#') ||
                    !PackageGitReference.TryParse(remote, out PackageGitReference channel)) continue;
                string developmentBranch = channel.ReferenceName;
                remote = remote.Substring(0, fragment);
                try { DevelopmentGitPolicy.RemoteIdentity(remote); DevelopmentGitPolicy.RequireSourceBranch(developmentBranch); }
                catch (DevelopmentGitException) { continue; }
                result.Add(new DevelopmentInstalledPackage(info.name, definition.DisplayName, remote,
                    reference, info.git?.hash ?? "No Git revision reported", info.source.ToString(), info.resolvedPath, developmentBranch));
            }
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
