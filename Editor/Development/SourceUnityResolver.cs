using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Composed on the editor thread. UPM requests cannot be canceled, so cancellation
    // stops follow-up work only after an in-flight request settles (or the bounded timeout).
    internal sealed class DevelopmentUnitySourceResolver : IDevelopmentSourceResolver
    {
        public Task<DevelopmentSourceResolution> ResolveAsync(DevelopmentSourceExpectation expectation,
            bool requestResolution, CancellationToken cancellationToken)
        {
            if (requestResolution) Client.Resolve();
            TaskCompletionSource<DevelopmentSourceResolution> completion =
                new TaskCompletionSource<DevelopmentSourceResolution>();
            ListRequest request = null;
            double started = EditorApplication.timeSinceStartup;
            double nextProbe = started + 1.0;
            double quietSince = -1;
            EditorApplication.CallbackFunction tick = null;
            Action<DevelopmentResolutionState, string> finish = (state, message) =>
            {
                EditorApplication.update -= tick;
                completion.TrySetResult(new DevelopmentSourceResolution(state, message));
            };
            tick = () =>
            {
                try
                {
                    double now = EditorApplication.timeSinceStartup;
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) quietSince = -1;
                    if (now - started > 120)
                    {
                        finish(DevelopmentResolutionState.Pending,
                            "Unity has not confirmed source resolution and compilation yet. Recover after it settles; cancellation does not roll back the manifest.");
                        return;
                    }
                    if (request == null)
                    {
                        if (now < nextProbe || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                        request = Client.List(true, true);
                    }
                    if (!request.IsCompleted) return;
                    if (request.Status == StatusCode.Failure)
                    {
                        finish(DevelopmentResolutionState.Failed,
                            "Unity could not resolve the package list. Inspect the Package Manager error, then recover or restore this session.");
                        return;
                    }
                    UnityEditor.PackageManager.PackageInfo package =
                        request.Result.FirstOrDefault(item => item.name == expectation.PackageId);
                    bool sourceMatches = DevelopmentSourceMatchPolicy.Matches(package?.packageId,
                        package?.source.ToString(), package?.resolvedPath, package?.version, expectation);
                    if (!sourceMatches)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            finish(DevelopmentResolutionState.Pending,
                                "Cancellation requested. Unity has not confirmed the selected source; recover or restore when it settles.");
                            return;
                        }
                        request = null;
                        nextProbe = now + 1.0;
                        return;
                    }
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                    if (EditorUtility.scriptCompilationFailed)
                    {
                        finish(DevelopmentResolutionState.Failed,
                            "The source resolved, but scripts have compilation errors. Fix the errors and recover, or restore the original source.");
                        return;
                    }
                    if (quietSince < 0) quietSince = now;
                    if (now - quietSince < 2.0) return;
                    finish(DevelopmentResolutionState.Ready,
                        "The selected source resolved; the editor is idle with no reported compilation errors.");
                }
                catch (Exception)
                {
                    finish(DevelopmentResolutionState.Failed,
                        "Unity could not confirm the selected source. Recover after Package Manager and compilation have settled.");
                }
            };
            EditorApplication.update += tick;
            return completion.Task;
        }

    }

    internal static class DevelopmentSourceComposition
    {
        internal static PackageDevelopmentSourceService Create(string projectRoot)
        {
            PackageInstallerAtomicFileCommitter committer = PackageInstallerAtomicFileCommitter.Shared;
            return new PackageDevelopmentSourceService(projectRoot,
                new DevelopmentSourceFileSystem(projectRoot, committer),
                new PackageDevelopmentSessionStore(projectRoot, committer),
                new DevelopmentUnitySourceResolver(), new DevelopmentCheckoutClaims(committer));
        }
    }
}
