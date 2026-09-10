using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Browser handoff only. The hosting provider owns PR creation, authentication, review and merge state.
    internal static class DevelopmentPullRequest
    {
        internal static string UnavailableReason(DevelopmentRepository repository, DevelopmentGitSnapshot snapshot, string target)
        {
            if (repository == null || snapshot == null) return "Inspect a package repository first.";
            try
            {
                DevelopmentGitPolicy.RemoteIdentity(repository.ExpectedRemote);
                DevelopmentGitPolicy.RequireFeatureBranch(snapshot.Branch);
                DevelopmentGitPolicy.RequireSourceBranch(target);
            }
            catch (DevelopmentGitException exception) { return exception.Message; }
            if (snapshot.Branch == target) return "Choose a feature branch different from the development channel.";
            if (snapshot.Upstream != "origin/" + snapshot.Branch || snapshot.Ahead != 0 || snapshot.Behind != 0)
                return "Push this feature branch, then refresh changes. Its upstream must be the same branch on origin.";
            return "";
        }

        internal static string CreateUrl(DevelopmentRepository repository, DevelopmentGitSnapshot snapshot, string target)
        {
            string unavailable = UnavailableReason(repository, snapshot, target);
            if (unavailable.Length != 0) throw new DevelopmentGitException(unavailable);
            string identity = DevelopmentGitPolicy.RemoteIdentity(repository.ExpectedRemote);
            if (identity.StartsWith("bitbucket.org/", StringComparison.Ordinal))
            {
                // Bitbucket's documented push-banner URL prefills only the source. Verify the destination in the browser.
                // https://support.atlassian.com/bitbucket-cloud/kb/terminal-not-showing-create-pull-request-for-branch-http-link-to-bitbucket-repo/
                return "https://" + identity + "/pull-requests/new?source=" + Uri.EscapeDataString(snapshot.Branch) + "&t=1";
            }
            // https://docs.github.com/en/pull-requests/reference/using-query-parameters-to-create-a-pull-request
            // Only repository/branch identifiers enter the URL; never a diff, local path or draft message.
            return "https://" + identity + "/compare/" + Uri.EscapeDataString(target) + "..." +
                Uri.EscapeDataString(snapshot.Branch) + "?quick_pull=1";
        }

        internal static void RequireRemoteBranches(string output, string branch, string target, string head)
        {
            string[] rows = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string sourceRef = "refs/heads/" + branch;
            string targetRef = "refs/heads/" + target;
            string[][] fields = rows.Select(row => row.Split('\t')).ToArray();
            if (fields.Length != 2 || fields.Any(row => row.Length != 2 || !Regex.IsMatch(row[0], @"^(?:[0-9a-f]{40}|[0-9a-f]{64})$")) ||
                fields.Count(row => row[1] == sourceRef && row[0] == head) != 1 ||
                fields.Count(row => row[1] == targetRef && row[0].Length > 0) != 1)
                throw new DevelopmentGitException("The pushed branch changed or a pull request branch is missing on origin. Review your Git client and refresh before continuing.");
        }

        internal static void RequireReviewedHead(DevelopmentRepository current, DevelopmentGitSnapshot reviewed)
        {
            if (current.Branch != reviewed.Branch || current.Head != reviewed.Head)
                throw new DevelopmentGitException("The checked-out branch or commit changed. Refresh before opening the pull request.");
        }
    }
}
