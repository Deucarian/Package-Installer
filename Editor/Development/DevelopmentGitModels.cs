using System.Collections.Generic;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentRepository
    {
        public string Root { get; internal set; }
        public string CommonDirectory { get; internal set; }
        public string GitDirectory { get; internal set; }
        public string ConsumerRoot { get; internal set; }
        public string PackageId { get; internal set; }
        public string RemoteIdentity { get; internal set; }
        public string RemoteDisplay => DevelopmentGitPolicy.DisplayRemote(RemoteIdentity);
        public string Branch { get; internal set; }
        public string Head { get; internal set; }
        public bool HasLinkedWorktrees { get; internal set; }
        internal string ExpectedRemote { get; set; }
    }

    internal sealed class DevelopmentChangedFile
    {
        public string Path { get; internal set; }
        public string OriginalPath { get; internal set; }
        public char IndexStatus { get; internal set; }
        public char WorktreeStatus { get; internal set; }
        public bool IsBinary { get; internal set; }
        internal string WorktreeFingerprint { get; set; }
        public bool IsStaged => IndexStatus != ' ' && IndexStatus != '?';
        public bool IsUnstaged => WorktreeStatus != ' ';
        public bool IsUntracked => IndexStatus == '?' && WorktreeStatus == '?';
        public bool IsConflict => IndexStatus == 'U' || WorktreeStatus == 'U' ||
            (IndexStatus == 'A' && WorktreeStatus == 'A') || (IndexStatus == 'D' && WorktreeStatus == 'D');
    }

    internal sealed class DevelopmentGitSnapshot
    {
        public string Head { get; internal set; }
        public string Branch { get; internal set; }
        public string Upstream { get; internal set; }
        public int Ahead { get; internal set; }
        public int Behind { get; internal set; }
        public IReadOnlyList<DevelopmentChangedFile> Files { get; internal set; }
        public string IndexFingerprint { get; internal set; }
        internal string IndexRecords { get; set; }
        public string StagingBlockReason { get; internal set; }
        public bool IsClean => Files.Count == 0;
    }
}
