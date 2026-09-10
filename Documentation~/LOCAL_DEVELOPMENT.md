# Develop an installed package locally

Open **Control Center → Packages → Package development**, or select an installed
package in Installer and choose **Develop locally**. This page requires Editor
1.8.0. The compact history is context for reviewing a package; advanced Git
operations remain in your external Git client.

1. Choose an installed catalog package. Enter an absent folder to clone its
   canonical repository from `develop`, or explicitly inspect an existing root.
   Check the origin, branch, installed revision, repository revision and path.
2. Choose **Connect local source** and review the existing changes. The project
   temporarily uses a local package reference. Unity resolution and compilation
   are tracked independently of Git state. After a reload, return to the package
   and use **Check source / recover**, then inspect its saved checkout.
3. Create a feature branch from the clean checkout, or select an existing clean
   feature branch explicitly. Use the IDE/folder actions to edit and run your
   package's validator and Unity tests. Refresh changes before reviewing diffs.
4. Select changed files and stage them explicitly. The confirmation includes
   related Unity `.meta` and rename paths. Review the staged diff, enter a message,
   and commit staged content. Normal Git hooks run. Existing staging from before
   the page attached, or unexpected external index edits, blocks index actions
   and requires external review; it is never absorbed into this commit.
5. Fetch to refresh ahead/behind information. Push names the validated origin and
   exact feature branch. A rejection remains a failure; resolve it in your Git
   client and refresh. Open a pull request into `develop` through your normal
   repository workflow. This page never merges or force-pushes.
6. **Restore original source** restores the exact original direct reference, or
   removes the temporary override if the package was transitive. The clone,
   commits and uncommitted files remain on disk. Source restoration is available
   even if the local checkout is missing.

## State, boundaries and recovery

The page only changes an explicitly validated package repository. It rejects the
consumer repository, its ancestors and linked worktrees, PackageCache, nested
package layouts, mismatched package IDs, and ambiguous origin fetch/push URLs.
First-version remotes are canonical credential-free GitHub HTTPS/SSH repositories.
Git uses existing authentication; this page stores no password or token.

Managed sessions block Installer update, reinstall, removal and update-all from
silently changing their source. Session records live in the project's ignored
`Library/Deucarian/PackageInstaller/Development` folder and survive normal domain
reloads and editor restarts. The journal stores only this package's reversible
reference edit and hashes, not other manifest configuration. Claims in the Git
common directory prevent another project from reusing a claimed checkout. An
independent linked worktree can be used when its branch and checkout are not in
use by another session. Claims are released only after source restoration.

Canceled or failed Unity work does not mean Unity stopped a submitted request.
The session remains protected and shows recovery until the selected source and
compilation are confirmed. Use **Check source / recover** after addressing errors,
or restore the saved source. No automatic Git retry, discard or clone deletion
occurs. Do not delete Library during a session: that also deletes its recovery
journal. If it was deleted, restore the consumer manifest from your own baseline
and inspect the checkout claim with your Git client before manually clearing it.

The temporary `file:` reference contains a machine-local absolute path. Unity
requires this to resolve the chosen folder. Restore before committing consumer
configuration; this page never stages or commits consumer files. Concurrent
manifest edits observed since inspection prevent connect. Restore preserves
unrelated edits by changing only this package's reference. Atomic replacement and
project locking coordinate Installer operations; an uncooperative external writer
can still race the last filesystem replacement, so preserve your normal Git
baseline and inspect external conflicts.

## First-version limits

- Requires Git 2.23 or newer. Windows processes are contained in a Job Object;
  POSIX systems require `setsid` for process-group containment. If the required
  transport is unavailable, Git operations fail closed. No OS authentication or
  security settings are changed.

- No automatic worktree creation, nested package repositories, embedded-source
  migration, rebase, reset/clean, merging, force push or conflict editor.
- Staging operates on whole files with explicit Unity metadata pairing. It does
  not stage individual lines. Binary files use an explicit external review path.
- History and diffs are bounded. Sensitive-looking lines are hidden from in-page
  diff/history presentation; local source files are not modified by that filter.
- Returning after a reload preserves source recovery. Pre-existing staging is
  still treated conservatively and must be finished in your external Git client.
- Source-ready status confirms resolution and compilation; it is not a claim that
  package tests passed. Run the owning package's tests before pushing.
- Finish currently restores the original reference; choosing a different pushed
  revision/channel is a subsequent explicit Installer action after restoration.

## Validation fixtures

Installer and Editor validation hosts remain canonical Git-pinned. Only an
isolated disposable target fixture may temporarily use a local source for this
feature's connect/restore tests. Fixture Git changes push only to local bare
remotes. This exception is documented in `AGENTS.md` and Package Registry's
`Documentation~/PACKAGE_LOCAL_DEVELOPMENT.md`.
