using System;
using System.Reflection;

namespace Deucarian.PackageInstaller.Editor
{
    /// <summary>
    /// Optional editor-only bridge to Bootstrap's public API. The exact type lookup
    /// avoids both a package dependency and broad assembly discovery.
    /// </summary>
    internal static class BootstrapEditorOpenBridge
    {
        private const string BootstrapApiTypeName =
            "Deucarian.Bootstrap.Editor.DeucarianBootstrap, Deucarian.Bootstrap.Editor";
        private const string OpenMethodName = "Open";

        internal static string ApiTypeNameForTests => BootstrapApiTypeName;

        internal static bool TryOpen()
        {
            return TryOpen(Type.GetType(BootstrapApiTypeName, false));
        }

        internal static bool TryOpen(Type bootstrapApiType)
        {
            if (bootstrapApiType == null)
            {
                return false;
            }

            MethodInfo open = bootstrapApiType.GetMethod(
                OpenMethodName,
                BindingFlags.Public | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);
            if (open == null || open.ReturnType != typeof(void))
            {
                return false;
            }

            try
            {
                open.Invoke(null, null);
                return true;
            }
            catch (TargetInvocationException)
            {
                return false;
            }
            catch (MethodAccessException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
