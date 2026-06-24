using Deucarian.Logging;
using Object = UnityEngine.Object;

namespace Deucarian.PackageInstaller.Editor
{
    /// <summary>
    /// Package-level log categories for Package Installer.
    /// </summary>
    internal static class PackageInstallerLog
    {
        public static readonly PackageInstallerLogCategory General = new PackageInstallerLogCategory("PackageInstaller");
        public static readonly PackageInstallerLogCategory Registry = new PackageInstallerLogCategory("PackageInstaller.Registry");
        public static readonly PackageInstallerLogCategory Install = new PackageInstallerLogCategory("PackageInstaller.Install");
        public static readonly PackageInstallerLogCategory Samples = new PackageInstallerLogCategory("PackageInstaller.Samples");
        public static readonly PackageInstallerLogCategory UpdateChecks = new PackageInstallerLogCategory("PackageInstaller.UpdateChecks");
        public static readonly PackageInstallerLogCategory Graph = new PackageInstallerLogCategory("PackageInstaller.Graph");
    }

    internal sealed class PackageInstallerLogCategory
    {
        private readonly DLog _log;

        public PackageInstallerLogCategory(string category)
        {
            _log = DLog.For(category);
        }

        public string Category => _log.Category;

        public void Info(string message, Object context = null)
        {
            if (PackageInstallerLoggingPreferences.VerboseConsoleLogging)
            {
                _log.Info(message, context);
            }
        }

        public void DiagnosticInfo(string message, Object context = null)
        {
            _log.Info(message, context);
        }

        public void Warning(string message, Object context = null)
        {
            _log.Warning(message, context);
        }

        public void Error(string message, Object context = null)
        {
            _log.Error(message, context);
        }
    }
}
