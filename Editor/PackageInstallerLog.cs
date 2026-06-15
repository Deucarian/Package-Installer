using Deucarian.Logging;

namespace Deucarian.PackageInstaller.Editor
{
    /// <summary>
    /// Package-level log categories for Package Installer.
    /// </summary>
    internal static class PackageInstallerLog
    {
        public static readonly DLog General = DLog.For("PackageInstaller");
        public static readonly DLog Registry = DLog.For("PackageInstaller.Registry");
        public static readonly DLog Install = DLog.For("PackageInstaller.Install");
        public static readonly DLog Samples = DLog.For("PackageInstaller.Samples");
        public static readonly DLog UpdateChecks = DLog.For("PackageInstaller.UpdateChecks");
    }
}
