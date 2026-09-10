using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentGitStatusReader
    {
        private readonly IDevelopmentGitRunner _git;
        private readonly IDevelopmentFileSystem _files;
        public DevelopmentGitStatusReader(IDevelopmentGitRunner git, IDevelopmentFileSystem files)
        { _git = git; _files = files; }

        public async Task<DevelopmentGitSnapshot> ReadAsync(DevelopmentRepository repository, CancellationToken token)
        {
            string root = repository.Root;
            string status = await Read(root, token, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none").ConfigureAwait(false);
            List<DevelopmentChangedFile> files = ParseStatus(status);
            HashSet<string> binary = ParseBinary(await Read(root, token, "diff", "--numstat", "-z", "--no-renames", "--no-ext-diff", "--no-textconv").ConfigureAwait(false));
            binary.UnionWith(ParseBinary(await Read(root, token, "diff", "--cached", "--numstat", "-z", "--no-renames", "--no-ext-diff", "--no-textconv").ConfigureAwait(false)));
            foreach (DevelopmentChangedFile file in files)
            {
                token.ThrowIfCancellationRequested();
                ValidateFile(root, file.Path);
                if (!string.IsNullOrEmpty(file.OriginalPath)) ValidateFile(root, file.OriginalPath);
                file.IsBinary = binary.Contains(file.Path) || (file.IsUntracked && _files.IsBinary(Path.Combine(root, file.Path)));
                file.WorktreeFingerprint = _files.Fingerprint(Path.Combine(root, file.Path));
            }
            DevelopmentGitResult branch = await _git.RunAsync(root, new[] { "symbolic-ref", "--quiet", "--short", "HEAD" }, token).ConfigureAwait(false);
            DevelopmentGitResult upstream = await _git.RunAsync(root, new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" }, token).ConfigureAwait(false);
            int ahead = 0, behind = 0;
            if (upstream.Success)
            {
                string[] counts = (await Read(root, token, "rev-list", "--left-right", "--count", "HEAD...@{upstream}").ConfigureAwait(false)).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (counts.Length != 2 || !int.TryParse(counts[0], out ahead) || !int.TryParse(counts[1], out behind))
                    throw new DevelopmentGitException("Git returned an invalid ahead/behind count.");
            }
            string indexRecords = await Read(root, token, "ls-files", "--stage", "-z").ConfigureAwait(false);
            return new DevelopmentGitSnapshot
            {
                Head = (await Read(root, token, "rev-parse", "--verify", "HEAD").ConfigureAwait(false)).Trim(),
                Branch = branch.Success ? branch.Output.Trim() : "", Upstream = upstream.Success ? upstream.Output.Trim() : "",
                Ahead = ahead, Behind = behind, Files = files,
                IndexRecords = indexRecords, IndexFingerprint = DevelopmentGitPolicy.Hash(indexRecords)
            };
        }

        public void ValidateFile(string root, string path)
        {
            DevelopmentGitPolicy.RequireRelativePath(path);
            string candidate = Path.Combine(root, path);
            string existing = candidate;
            while (!_files.FileExists(existing) && !_files.DirectoryExists(existing)) existing = Path.GetDirectoryName(existing);
            string physical = _files.Canonicalize(existing);
            if (!DevelopmentGitPolicy.ContainsPath(root, physical) ||
                DevelopmentGitPolicy.ContainsPath(Path.Combine(root, ".git"), physical))
                throw new DevelopmentGitException("A changed file resolves outside the package repository. Use your Git client to review the link.");
            if (_files.DirectoryExists(candidate))
                throw new DevelopmentGitException("Submodules and nested repository changes require your external Git client.");
        }

        internal static List<DevelopmentChangedFile> ParseStatus(string status)
        {
            string[] records = status.Split('\0');
            List<DevelopmentChangedFile> files = new List<DevelopmentChangedFile>();
            for (int i = 0; i < records.Length; i++)
            {
                string record = records[i];
                if (record.Length == 0) continue;
                if (record.Length < 4 || record[2] != ' ' || files.Count >= 2000)
                    throw new DevelopmentGitException("Git changes exceed the supported display limit or contain invalid records.");
                DevelopmentChangedFile file = new DevelopmentChangedFile
                { IndexStatus = record[0], WorktreeStatus = record[1], Path = DevelopmentGitPolicy.RequireRelativePath(record.Substring(3)) };
                if (file.IndexStatus == 'R' || file.IndexStatus == 'C' || file.WorktreeStatus == 'R' || file.WorktreeStatus == 'C')
                {
                    if (++i >= records.Length) throw new DevelopmentGitException("Git returned an incomplete rename record.");
                    file.OriginalPath = DevelopmentGitPolicy.RequireRelativePath(records[i]);
                }
                files.Add(file);
            }
            return files;
        }
        private static HashSet<string> ParseBinary(string numstat)
        {
            HashSet<string> binary = new HashSet<string>(StringComparer.Ordinal);
            foreach (string record in numstat.Split('\0'))
                if (record.StartsWith("-\t-\t", StringComparison.Ordinal)) binary.Add(record.Substring(4));
            return binary;
        }
        private async Task<string> Read(string root, CancellationToken token, params string[] args) =>
            (await _git.RunAsync(root, args, token).ConfigureAwait(false)).RequireSuccess();
    }
}
