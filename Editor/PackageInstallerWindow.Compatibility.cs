namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        internal static PackageChannel ResolveSelectedChannel(PackageDefinition package,
            PackageChannelSelection project, PackageChannelSelection selection, bool hasInstalled, PackageChannel installed)
            => PackageChannelPolicy.ResolveSelectedChannel(package, project, selection, hasInstalled, installed);

        internal static string GetContextualChannelProvenance(PackageDefinition package,
            PackageChannelSelection project, PackageChannelSelection selection, bool hasInstalled,
            PackageChannel installed, string sourceReason)
            => PackageChannelPolicy.GetContextualChannelProvenance(package, project, selection, hasInstalled, installed, sourceReason);
    }
}
