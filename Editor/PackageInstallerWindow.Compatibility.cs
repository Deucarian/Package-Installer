namespace Deucarian.PackageInstaller.Editor
{
    internal sealed partial class PackageInstallerWindow
    {
        internal static PackageChannel ResolveSelectedChannel(PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection, PackageChannelSelection packageSelection,
            bool hasInstalledChannel, PackageChannel installedChannel)
            => PackageChannelPolicy.ResolveSelectedChannel(packageDefinition, projectSelection,
                packageSelection, hasInstalledChannel, installedChannel);

        internal static string GetContextualChannelProvenance(PackageDefinition packageDefinition,
            PackageChannelSelection projectSelection, PackageChannelSelection packageSelection,
            bool hasInstalledChannel, PackageChannel installedChannel, string installedSourceReason)
            => PackageChannelPolicy.GetContextualChannelProvenance(packageDefinition, projectSelection,
                packageSelection, hasInstalledChannel, installedChannel, installedSourceReason);
    }
}
