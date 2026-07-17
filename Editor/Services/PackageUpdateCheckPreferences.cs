using System;
using UnityEditor;

namespace Deucarian.PackageInstaller.Editor
{
    internal static class PackageUpdateCheckPreferences
    {
        private const string CheckOnWindowOpenKey = "Deucarian.PackageInstaller.CheckUpdatesOnWindowOpen";
        private const string CheckOnEditorStartKey = "Deucarian.PackageInstaller.CheckUpdatesOnEditorStart";

        public static readonly TimeSpan WindowOpenThrottle = TimeSpan.FromMinutes(30);

        public static bool CheckOnWindowOpen
        {
            get => EditorPrefs.GetBool(CheckOnWindowOpenKey, true);
            set => EditorPrefs.SetBool(CheckOnWindowOpenKey, value);
        }

        public static bool CheckOnEditorStart
        {
            get => EditorPrefs.GetBool(CheckOnEditorStartKey, true);
            set => EditorPrefs.SetBool(CheckOnEditorStartKey, value);
        }

        public static bool ShouldCheckOnWindowOpen(DateTime utcNow, DateTime? lastCheckedUtc)
        {
            return CheckOnWindowOpen &&
                   ShouldRunThrottledCheck(utcNow, lastCheckedUtc, WindowOpenThrottle);
        }

        internal static bool ShouldRunThrottledCheck(
            DateTime utcNow,
            DateTime? lastCheckedUtc,
            TimeSpan throttle)
        {
            if (!lastCheckedUtc.HasValue)
            {
                return true;
            }

            return utcNow.ToUniversalTime() - lastCheckedUtc.Value.ToUniversalTime() >= throttle;
        }
    }
}
