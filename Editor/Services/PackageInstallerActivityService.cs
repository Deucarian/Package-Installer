using System;
using System.Collections.Generic;

namespace Deucarian.PackageInstaller.Editor
{
    internal enum PackageInstallerActivitySeverity
    {
        Info,
        Success,
        Warning,
        Error
    }

    internal enum PackageInstallerRetryKind
    {
        None,
        Refresh,
        CheckUpdates,
        ResumeOperation,
        RestartOperation,
        ReplanOperation,
        ImportSample
    }

    internal sealed class PackageInstallerActivityEntry
    {
        public PackageInstallerActivityEntry(
            long sequence,
            DateTime timestampUtc,
            string source,
            PackageInstallerActivitySeverity severity,
            string summary,
            string details,
            string packageId,
            PackageInstallerRetryKind retryKind)
        {
            Sequence = sequence;
            TimestampUtc = timestampUtc;
            Source = source ?? string.Empty;
            Severity = severity;
            Summary = summary ?? string.Empty;
            Details = details ?? string.Empty;
            PackageId = packageId ?? string.Empty;
            RetryKind = retryKind;
        }

        public long Sequence { get; }
        public DateTime TimestampUtc { get; }
        public string Source { get; }
        public PackageInstallerActivitySeverity Severity { get; }
        public string Summary { get; }
        public string Details { get; }
        public string PackageId { get; }
        public PackageInstallerRetryKind RetryKind { get; }
    }

    internal static class PackageInstallerActivityService
    {
        private const int MaximumEntries = 100;
        private static readonly object Gate = new object();
        private static readonly List<PackageInstallerActivityEntry> Entries =
            new List<PackageInstallerActivityEntry>();
        private static long _sequence;

        public static event Action Changed;

        public static PackageInstallerActivityEntry Latest
        {
            get
            {
                lock (Gate)
                {
                    return Entries.Count == 0 ? null : Entries[Entries.Count - 1];
                }
            }
        }

        public static IReadOnlyList<PackageInstallerActivityEntry> Recent
        {
            get
            {
                lock (Gate)
                {
                    return Entries.ToArray();
                }
            }
        }

        public static PackageInstallerActivityEntry Record(
            string source,
            PackageInstallerActivitySeverity severity,
            string summary,
            string details = null,
            string packageId = null,
            PackageInstallerRetryKind retryKind = PackageInstallerRetryKind.None)
        {
            PackageInstallerActivityEntry entry;
            lock (Gate)
            {
                entry = new PackageInstallerActivityEntry(
                    ++_sequence,
                    DateTime.UtcNow,
                    source,
                    severity,
                    summary,
                    details,
                    packageId,
                    retryKind);
                Entries.Add(entry);
                if (Entries.Count > MaximumEntries)
                {
                    Entries.RemoveRange(0, Entries.Count - MaximumEntries);
                }
            }

            Changed?.Invoke();
            return entry;
        }

        internal static void ClearForTests()
        {
            lock (Gate)
            {
                Entries.Clear();
                _sequence = 0;
            }
        }
    }
}
