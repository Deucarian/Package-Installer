using System;
using UnityEditor;

namespace JorisHoef.PackageInstaller.Editor
{
    internal static class PackageUpdateCheckPreferences
    {
        private const string CheckOnWindowOpenKey = "JorisHoef.PackageInstaller.CheckUpdatesOnWindowOpen";
        private const string CheckOnStartupKey = "JorisHoef.PackageInstaller.CheckUpdatesOnStartup";
        private const string LastCheckedUtcTicksKey = "JorisHoef.PackageInstaller.LastUpdateCheckUtcTicks";

        public static readonly TimeSpan WindowOpenThrottle = TimeSpan.FromMinutes(30);

        public static bool CheckOnWindowOpen
        {
            get => EditorPrefs.GetBool(CheckOnWindowOpenKey, true);
            set => EditorPrefs.SetBool(CheckOnWindowOpenKey, value);
        }

        public static bool CheckOnStartup
        {
            get => EditorPrefs.GetBool(CheckOnStartupKey, true);
            set => EditorPrefs.SetBool(CheckOnStartupKey, value);
        }

        public static DateTime? LastCheckedUtc
        {
            get
            {
                string ticksText = EditorPrefs.GetString(LastCheckedUtcTicksKey, string.Empty);

                if (!long.TryParse(ticksText, out long ticks) || ticks <= 0L)
                {
                    return null;
                }

                try
                {
                    return new DateTime(ticks, DateTimeKind.Utc);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
            set
            {
                if (!value.HasValue)
                {
                    EditorPrefs.DeleteKey(LastCheckedUtcTicksKey);
                    return;
                }

                EditorPrefs.SetString(
                    LastCheckedUtcTicksKey,
                    value.Value.ToUniversalTime().Ticks.ToString());
            }
        }

        public static bool ShouldCheckOnWindowOpen(DateTime utcNow)
        {
            return CheckOnWindowOpen &&
                   ShouldRunThrottledCheck(utcNow, LastCheckedUtc, WindowOpenThrottle);
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
