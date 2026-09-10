using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    internal sealed class DevelopmentGitInspection
    {
        private readonly DevelopmentRepository _repository;
        private readonly DevelopmentRepositoryService _repositories;
        private readonly IDevelopmentGitRunner _git;
        private readonly IDevelopmentFileSystem _files;
        private readonly DevelopmentGitStatusReader _reader;
        public DevelopmentGitInspection(DevelopmentRepository repository, DevelopmentRepositoryService repositories,
            IDevelopmentGitRunner git, IDevelopmentFileSystem files)
        { _repository = repository; _repositories = repositories; _git = git; _files = files; _reader = new DevelopmentGitStatusReader(git, files); }

        public async Task<IReadOnlyList<string>> GetBranchesAsync(CancellationToken token)
        {
            await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false);
            string output = (await _git.RunAsync(_repository.Root, new[] { "for-each-ref", "--format=%(refname:short)", "refs/heads/" }, token).ConfigureAwait(false)).RequireSuccess();
            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(branch => branch, StringComparer.Ordinal).Take(256).ToArray();
        }
        public async Task<string> GetHistoryAsync(CancellationToken token)
        {
            await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false);
            return SafeDisplay((await _git.RunAsync(_repository.Root,
                new[] { "log", "--no-color", "--format=%h %s", "-12", "HEAD", "--" }, token).ConfigureAwait(false)).RequireSuccess());
        }
        public async Task<string> GetPullRequestUrlAsync(DevelopmentGitSnapshot reviewed, string target, CancellationToken token)
        {
            string url = DevelopmentPullRequest.CreateUrl(_repository, reviewed, target);
            DevelopmentPullRequest.RequireReviewedHead(await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false), reviewed);
            // Query exact branch refs without fetching or changing local refs, source state or credentials.
            string remote = (await _git.RunAsync(_repository.Root, new[] { "ls-remote", "--heads", "origin",
                "refs/heads/" + reviewed.Branch, "refs/heads/" + target }, token).ConfigureAwait(false)).RequireSuccess();
            DevelopmentPullRequest.RequireRemoteBranches(remote, reviewed.Branch, target, reviewed.Head);
            DevelopmentPullRequest.RequireReviewedHead(await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false), reviewed);
            string upstream = (await _git.RunAsync(_repository.Root,
                new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" }, token).ConfigureAwait(false)).RequireSuccess().Trim();
            if (upstream != reviewed.Upstream)
                throw new DevelopmentGitException("The upstream changed. Refresh before opening the pull request.");
            token.ThrowIfCancellationRequested();
            return url;
        }
        public async Task<string> GetDiffAsync(string path, bool staged, CancellationToken token)
        {
            await _repositories.RevalidateAsync(_repository, token).ConfigureAwait(false);
            _reader.ValidateFile(_repository.Root, path);
            DevelopmentGitSnapshot state = await _reader.ReadAsync(_repository, token).ConfigureAwait(false);
            DevelopmentChangedFile file = state.Files.FirstOrDefault(change => change.Path == path);
            if (file == null) throw new DevelopmentGitException("The file is no longer changed. Refresh the list.");
            if (file.IsBinary) return "Binary file: textual diff is unavailable. Open the file in its associated editor to review it.";
            if (file.IsUntracked)
                return SafeDisplay("New file (not yet staged):\n" + _files.ReadText(Path.Combine(_repository.Root, path)));
            List<string> args = new List<string> { "--literal-pathspecs", "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3" };
            if (staged) args.Add("--cached");
            args.Add("--");
            args.Add(path);
            if (!string.IsNullOrEmpty(file.OriginalPath)) args.Add(file.OriginalPath);
            return SafeDisplay((await _git.RunAsync(_repository.Root, args, token).ConfigureAwait(false)).RequireSuccess());
        }

        internal static string SafeDisplay(string value)
        {
            string[] lines = value.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                // Diffs stay local and are never logged. Hide common credential-bearing lines defensively.
                if (Regex.IsMatch(lines[i], @"(?i)(authorization\s*:|bearer\s+|(?:password|secret|token|api[_-]?key)\s*[=:])") ||
                    Regex.IsMatch(lines[i], @"(?i)https?://[^/\s]+@")) lines[i] = "[credential-bearing line hidden]";
                else lines[i] = new string(lines[i].Where(c => c == '\t' || !char.IsControl(c)).ToArray());
            }
            string result = string.Join("\n", lines);
            return result.Length <= 131072 ? result : result.Substring(0, 131072) + "\n[Preview truncated. Open your external Git client for the complete diff.]";
        }
    }
}
