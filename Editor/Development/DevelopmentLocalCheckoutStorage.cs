using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // This explicit clone action may add one local exclude rule, never stage or edit consumer files.
    internal sealed class DevelopmentLocalCheckoutStorage
    {
        private readonly IDevelopmentGitRunner git;
        private readonly IDevelopmentFileSystem files;

        internal DevelopmentLocalCheckoutStorage(IDevelopmentGitRunner git, IDevelopmentFileSystem files)
        { this.git = git; this.files = files; }

        internal async Task PrepareAsync(string project, string packageId, CancellationToken token)
        {
            project = files.Canonicalize(project);
            string checkout = DevelopmentCheckoutLocation.DefaultPath(project, packageId);
            string parent = Path.GetDirectoryName(checkout);
            DevelopmentSourceStoragePaths.RequireRegular(project, parent);
            var rootResult = await git.RunAsync(project, new[] { "rev-parse", "--show-toplevel" }, token).ConfigureAwait(false);
            string gitRoot = files.Canonicalize(rootResult.RequireSuccess().Trim());
            if (!DevelopmentGitPolicy.ContainsPath(gitRoot, project))
                throw new DevelopmentGitException("Cannot verify the project's Git boundary. Choose an external checkout.");
            string relative = parent.Substring(gitRoot.TrimEnd('/', '\\').Length + 1).Replace('\\', '/');
            var tracked = await git.RunAsync(gitRoot, new[] { "ls-files", "--", relative }, token).ConfigureAwait(false);
            if (tracked.RequireSuccess().Length != 0)
                throw new DevelopmentGitException("The local checkout folder contains project-tracked files. Choose an external checkout; no tracked files were changed.");
            string common = (await git.RunAsync(project, new[] { "rev-parse", "--git-common-dir" }, token).ConfigureAwait(false)).RequireSuccess().Trim();
            common = files.Canonicalize(Path.IsPathRooted(common) ? common : Path.Combine(project, common));
            string info = Path.Combine(common, "info");
            string exclude = Path.Combine(info, "exclude");
            DevelopmentSourceStoragePaths.RequireRegular(common, exclude);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(info);
            using (files.Lock(common, "deucarian-local-checkout.lock"))
            {
                DevelopmentSourceStoragePaths.RequireRegular(common, exclude);
                string rule = "/" + EscapePattern(relative) + "/";
                using (var stream = new FileStream(exclude, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    if (stream.Length > 1048576) throw new DevelopmentGitException("The local exclude file is too large to update safely.");
                    var bytes = new byte[(int)stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int count = stream.Read(bytes, read, bytes.Length - read);
                        if (count == 0) throw new IOException("Local excludes changed while reading.");
                        read += count;
                    }
                    string existing = Encoding.UTF8.GetString(bytes);
                    if (!Array.Exists(existing.Split('\n'), line => line.TrimEnd('\r') == rule))
                    {
                        byte[] addition = Encoding.UTF8.GetBytes("\n# Deucarian local package checkouts (not project source)\n" + rule + "\n");
                        stream.Write(addition, 0, addition.Length);
                        stream.Flush();
                    }
                }
            }
            await RequireIgnoredAsync(project, packageId, token).ConfigureAwait(false);
            DevelopmentSourceStoragePaths.RequireRegular(project, parent);
            Directory.CreateDirectory(parent);
            if (!DevelopmentGitPolicy.SamePath(parent, files.Canonicalize(parent)))
                throw new DevelopmentGitException("The checkout location moved. No repository was cloned.");
        }

        internal async Task RequireIgnoredAsync(string project, string packageId, CancellationToken token)
        {
            string relative = ".deucarian/checkouts/" + packageId;
            var tracked = await git.RunAsync(project, new[] { "ls-files", "--", relative }, token).ConfigureAwait(false);
            if (tracked.RequireSuccess().Length != 0)
                throw new DevelopmentGitException("The checkout is tracked by the project. Stop and review it in your Git client.");
            var ignored = await git.RunAsync(project, new[] { "check-ignore", "--quiet", "--no-index", "--", relative + "/package.json" }, token).ConfigureAwait(false);
            if (!ignored.Success)
                throw new DevelopmentGitException("The project does not ignore this checkout. Restore the local exclude rule or use an external repository.");
        }

        private static string EscapePattern(string value) => value.Replace("\\", "\\\\")
            .Replace("*", "\\*").Replace("?", "\\?").Replace("[", "\\[")
            .Replace("]", "\\]").Replace(" ", "\\ ");
    }
}
