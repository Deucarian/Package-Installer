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

        internal DevelopmentInstalledPackage(string id, string name, string remote, string reference, string revision, string source, string resolvedPath = "")
        { Id = id; Name = name; Remote = remote; InstalledReference = reference; InstalledRevision = revision; Source = source; InstalledResolvedPath = resolvedPath; }

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
                if (fragment >= 0) remote = remote.Substring(0, fragment);
                try { DevelopmentGitPolicy.RemoteIdentity(remote); }
                catch (DevelopmentGitException) { continue; }
                result.Add(new DevelopmentInstalledPackage(info.name, definition.DisplayName, remote,
                    reference, info.git?.hash ?? "No Git revision reported", info.source.ToString(), info.resolvedPath));
            }
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
