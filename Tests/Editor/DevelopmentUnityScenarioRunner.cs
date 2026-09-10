using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.PackageInstaller.Editor.Development;
using UnityEditor;
using UnityEngine;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class DevelopmentUnityScenarioRunner
    {
        private const string PackageId = "com.deucarian.common";
        private const string Branch = "feature/validation-scenario";
        private readonly string _root, _consumer, _seed, _remote, _checkout, _statePath;
        private readonly DevelopmentGitProcessRunner _git;
        private readonly DevelopmentFileSystem _files;
        private readonly DevelopmentRepositoryService _repositories;
        private readonly PackageDevelopmentSourceService _sources;
        private DevelopmentUnityScenarioState _state;

        internal DevelopmentUnityScenarioRunner(string root, string consumer)
        {
            _root = Path.GetFullPath(root); _consumer = Path.GetFullPath(consumer); _seed = Path.Combine(_root, "Seed");
            _remote = Path.Combine(_root, "CommonFixture.git"); _checkout = Path.Combine(_root, "Checkout");
            _statePath = Path.Combine(_root, "state.json");
            _git = new DevelopmentGitProcessRunner(); _files = new DevelopmentFileSystem();
            _repositories = new DevelopmentRepositoryService(_git, _files, true);
            _sources = DevelopmentSourceComposition.Create(consumer);
        }

        internal async Task RunAsync()
        {
            Require(DevelopmentUnityScenario.Allowed(), "The scenario gate is not satisfied.");
            Require(DevelopmentGitPolicy.SamePath(_files.Canonicalize(_consumer), _consumer), "The fixture consumer must not be a junction.");
            DevelopmentSourceStoragePaths.RequireRegular(_root, _statePath);
            _state = File.Exists(_statePath) ? JsonUtility.FromJson<DevelopmentUnityScenarioState>(File.ReadAllText(_statePath)) :
                new DevelopmentUnityScenarioState { Phase = "setup" };
            ++_state.Entries;
            Save();
            if (_state.Phase == "complete") { VerifyReport(); return; }
            if (_state.Phase == "setup") await PrepareAsync();
            if (_state.Phase == "connecting")
            {
                var session = await _sources.RecoverAsync(PackageId, CancellationToken.None);
                Require(session.State == DevelopmentSourceState.Connected, session.Message);
                await RequireMarkerAsync("seed");
                _state.LocalCompilationVerified = true;
                var workspace = await WorkspaceAsync();
                await workspace.CreateBranchAsync(Branch, CancellationToken.None);
                _state.Phase = "editing";
                Save();
            }
            if (_state.Phase == "editing")
            {
                // Save the next phase before an edit can trigger a compilation/domain reload.
                _state.Phase = "compiled-edit";
                Save();
                Write(_checkout, "Runtime/Marker.cs", Marker("edited"));
                Write(_checkout, "Runtime/Marker.cs.meta", ScriptMeta("ee80fdab54ed448c886f24aa6a273e41", "scenario-edited"));
                Write(_checkout, "Unselected.txt", "Uncommitted work must remain after restore.\n");
                AssetDatabase.Refresh();
            }
            if (_state.Phase == "compiled-edit")
            {
                await RequireMarkerAsync("edited");
                Require(!EditorUtility.scriptCompilationFailed, "Fixture compilation reported errors.");
                _state.EditedCompilationVerified = true;
                _state.Phase = "git";
                Save();
            }
            if (_state.Phase == "git") await CommitAndPushAsync();
            if (_state.Phase == "restoring")
            {
                var session = await _sources.RestoreAsync(PackageId, CancellationToken.None);
                Require(session.State == DevelopmentSourceState.Restored, session.Message);
                await VerifyRestorationAsync();
            }
            VerifyReport();
        }

        private async Task PrepareAsync()
        {
            byte[] manifest = File.ReadAllBytes(Path.Combine(_consumer, "Packages", "manifest.json"));
            string reference = new SourceManifestEdit(manifest).GetReference(PackageId);
            Require(reference != null && reference.StartsWith("https://github.com/Deucarian/Common.git#", StringComparison.Ordinal),
                "The disposable consumer must start with Common referenced by canonical Git URL.");
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Single(info => info.name == PackageId);
            Require(package.source == UnityEditor.PackageManager.PackageSource.Git, "Common must initially resolve from Git.");
            _state.OriginalManifestHash = SourceManifestEdit.Hash(manifest);
            _state.InstalledReference = reference; _state.InstalledPath = package.resolvedPath;
            await InitializeConsumerBaselineAsync();
            _state.ConsumerHead = await Git(_consumer, "rev-parse", "HEAD");
            _state.ConsumerIndex = await Git(_consumer, "ls-files", "--stage", "-z");
            Require(!Directory.Exists(_seed) && !Directory.Exists(_remote) && !Directory.Exists(_checkout),
                "The scenario fixture paths already exist. Preserve them and choose a reviewed new scenario run.");
            Directory.CreateDirectory(_seed);
            await Git(_seed, "init", "--initial-branch=develop");
            await ConfigureIdentity(_seed);
            Write(_seed, "package.json", "{\"name\":\"com.deucarian.common\",\"version\":\"" + package.version + "\",\"displayName\":\"Disposable Common validation fixture\",\"unity\":\"2021.3\"}\n");
            Write(_seed, "package.json.meta", AssetMeta("b748f41ae0d74cf1aa59a9c1cd963510", "PackageManifestImporter"));
            Write(_seed, "Runtime/Fixture.asmdef", "{\"name\":\"Deucarian.PackageDevelopment.Fixture\",\"autoReferenced\":true}\n");
            Write(_seed, "Runtime/Fixture.asmdef.meta", AssetMeta("d0f9b85479e443f1a5f73c9e5e8cd8de", "AssemblyDefinitionImporter"));
            Write(_seed, "Runtime/Marker.cs", Marker("seed"));
            Write(_seed, "Runtime/Marker.cs.meta", ScriptMeta("ee80fdab54ed448c886f24aa6a273e41", ""));
            Write(_seed, "Runtime.meta", "fileFormatVersion: 2\nguid: 3a91de97c67c49388305d961040e409d\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n");
            Write(_seed, "Unselected.txt", "Seed unselected file.\n");
            Write(_seed, "Unselected.txt.meta", AssetMeta("210e6db5b7494e7398bfb0162b7bbfd2", "TextScriptImporter"));
            await Git(_seed, "add", "--", "package.json", "package.json.meta", "Runtime", "Runtime.meta", "Unselected.txt", "Unselected.txt.meta");
            await Git(_seed, "commit", "-m", "Seed disposable Unity package fixture");
            await Git(_root, "init", "--bare", _remote);
            await Git(_seed, "remote", "add", "origin", _remote);
            await Git(_seed, "push", "-u", "origin", "develop");
            DevelopmentRepository repository = await _repositories.CloneAsync(PackageId, _remote, _checkout, _consumer, CancellationToken.None);
            _state.CloneHead = repository.Head; _state.CloneVerified = true;
            await ConfigureIdentity(_checkout);
            DevelopmentRepository reused = await _repositories.PreviewAsync(PackageId, _checkout, _consumer, _remote, CancellationToken.None);
            Require(reused.Head == repository.Head && reused.Root == repository.Root, "Explicit reuse changed the selected clone.");
            _state.ReuseVerified = true;
            PackageDevelopmentSession session = _sources.Prepare(PackageId, reused.Root, reused.CommonDirectory, reference, package.resolvedPath, "Git");
            _state.Phase = "connecting";
            Save();
            await _sources.ConnectAsync(session, CancellationToken.None);
        }

        private async Task CommitAndPushAsync()
        {
            DevelopmentGitWorkspace workspace = await WorkspaceAsync();
            DevelopmentGitSnapshot snapshot = await workspace.RefreshAsync(CancellationToken.None);
            var selected = DevelopmentGitPolicy.IncludeMeta(new[] { "Runtime/Marker.cs" }, snapshot.Files);
            Require(selected.Count == 2 && selected.Contains("Runtime/Marker.cs.meta"), "Selected staging must explicitly include the changed meta relationship.");
            await workspace.StageAsync(selected, CancellationToken.None);
            string staged = await Git(_checkout, "diff", "--cached", "--name-only");
            Require(staged.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(new[] { "Runtime/Marker.cs", "Runtime/Marker.cs.meta" }),
                "Staging included unexpected files.");
            _state.Commit = await workspace.CommitAsync("Validate selected package source workflow", CancellationToken.None);
            string committedFiles = await Git(_checkout, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD");
            Require(!committedFiles.Contains("Unselected.txt") && committedFiles.Contains("Runtime/Marker.cs.meta"), "Commit changed unselected content.");
            _state.SelectedCommitVerified = true;
            await workspace.PushAsync(CancellationToken.None);
            string remoteHead = (await Git(_root, "ls-remote", _remote, "refs/heads/" + Branch)).Split('\t')[0];
            Require(remoteHead == _state.Commit, "The local fixture remote did not receive the selected package commit.");
            _state.FixturePushVerified = true;
            _state.Phase = "restoring";
            Save();
        }

        private async Task VerifyRestorationAsync()
        {
            await RequireOriginalAssemblyAsync();
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Single(info => info.name == PackageId);
            Require(package.source == UnityEditor.PackageManager.PackageSource.Git, "Common did not return to Git source.");
            _state.OriginalSourceRestored = true;
            _state.ExactManifestRestored = SourceManifestEdit.Hash(File.ReadAllBytes(Path.Combine(_consumer, "Packages", "manifest.json"))) == _state.OriginalManifestHash;
            _state.LocalWorkPreserved = await Git(_checkout, "rev-parse", "HEAD") == _state.Commit &&
                File.ReadAllText(Path.Combine(_checkout, "Unselected.txt")) == "Uncommitted work must remain after restore.\n" &&
                (await Git(_checkout, "status", "--porcelain")).Contains("Unselected.txt");
            _state.ConsumerGitUnchanged = await Git(_consumer, "rev-parse", "HEAD") == _state.ConsumerHead &&
                await Git(_consumer, "ls-files", "--stage", "-z") == _state.ConsumerIndex;
            _state.Phase = "complete";
            Save();
        }

        private async Task<DevelopmentGitWorkspace> WorkspaceAsync()
        {
            var repository = await _repositories.PreviewAsync(PackageId, _checkout, _consumer, _remote, CancellationToken.None);
            var session = _sources.GetSessions().Single(item => item.PackageId == PackageId);
            var workspace = new DevelopmentGitWorkspace(repository, _repositories, _git, _files, () => _sources.AssertCheckoutOwned(session));
            await workspace.RefreshAsync(CancellationToken.None);
            return workspace;
        }

        private async Task InitializeConsumerBaselineAsync()
        {
            var result = await _git.RunAsync(_consumer, new[] { "rev-parse", "--show-toplevel" }, CancellationToken.None);
            if (result.Success) { Require(DevelopmentGitPolicy.SamePath(Path.GetFullPath(result.Output.Trim()), _consumer), "Consumer inherits a parent Git repository."); return; }
            Require(result.Failure == DevelopmentGitProcessRunner.NotRepository, "Consumer Git state could not be inspected.");
            await Git(_consumer, "init", "--initial-branch=fixture/baseline");
            await ConfigureIdentity(_consumer);
            await Git(_consumer, "add", "--", "Packages/manifest.json", "Packages/packages-lock.json", "ProjectSettings/ProjectVersion.txt");
            await Git(_consumer, "commit", "-m", "Disposable Unity consumer baseline");
        }

        private async Task RequireMarkerAsync(string expected)
        {
            double started = EditorApplication.timeSinceStartup;
            while (EditorApplication.timeSinceStartup - started < 120)
            {
                // Exact editor-only validation reflection, scoped to this synthetic assembly/type/property.
                Type marker = Type.GetType("Deucarian.PackageDevelopment.Fixture.Marker, Deucarian.PackageDevelopment.Fixture");
                string value = marker?.GetProperty("Value", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                if (value == expected && !EditorApplication.isCompiling && !EditorApplication.isUpdating) return;
                if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Fixture marker failed compilation.");
                await Task.Delay(250);
            }
            throw new InvalidOperationException("The compiled fixture marker did not expose the expected value.");
        }

        private async Task RequireOriginalAssemblyAsync()
        {
            double started = EditorApplication.timeSinceStartup;
            while (EditorApplication.timeSinceStartup - started < 120)
            {
                Type original = Type.GetType("Deucarian.Common.UnityObjectUtility, Deucarian.Common");
                Type fixture = Type.GetType("Deucarian.PackageDevelopment.Fixture.Marker, Deucarian.PackageDevelopment.Fixture");
                if (original != null && fixture == null && !EditorApplication.isCompiling && !EditorApplication.isUpdating) return;
                if (EditorUtility.scriptCompilationFailed) throw new InvalidOperationException("Original Common source reported compilation errors.");
                await Task.Delay(250);
            }
            throw new InvalidOperationException("The original Common assembly was not restored after source resolution.");
        }

        private void VerifyReport()
        {
            Require(_state.Phase == "complete" && _state.CloneVerified && _state.ReuseVerified && _state.LocalCompilationVerified &&
                _state.EditedCompilationVerified && _state.SelectedCommitVerified && _state.FixturePushVerified &&
                _state.OriginalSourceRestored && _state.ExactManifestRestored && _state.LocalWorkPreserved && _state.ConsumerGitUnchanged,
                "One or more Unity scenario acceptance checks failed; inspect state.json.");
        }

        private Task<string> Git(string directory, params string[] arguments) => GitCore(directory, arguments);
        private async Task<string> GitCore(string directory, string[] arguments) =>
            (await _git.RunAsync(directory, arguments, CancellationToken.None)).RequireSuccess().Trim();
        private async Task ConfigureIdentity(string directory)
        {
            await Git(directory, "config", "user.name", "Disposable Package Validation");
            await Git(directory, "config", "user.email", "package-validation@example.invalid");
        }
        private void Save() => File.WriteAllText(_statePath, JsonUtility.ToJson(_state, true));
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Write(string root, string relative, string text)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }
        private static string Marker(string value) => "namespace Deucarian.PackageDevelopment.Fixture { public static class Marker { public static string Value => \"" + value + "\"; } }\n";
        private static string AssetMeta(string guid, string importer) => "fileFormatVersion: 2\nguid: " + guid + "\n" + importer + ":\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n";
        private static string ScriptMeta(string guid, string data) => "fileFormatVersion: 2\nguid: " + guid + "\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: " + data + "\n  assetBundleName:\n  assetBundleVariant:\n";
    }
}
