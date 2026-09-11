using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageInstallerLoadingTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator LocalReadRunsOffTheEditorThreadAndPollingDoesNotWait()
        {
            int editorThread = Thread.CurrentThread.ManagedThreadId;
            int readerThread = editorThread;
            var released = new ManualResetEventSlim();
            var finished = new ManualResetEventSlim();
            var load = new PackageRegistryLocalLoad(() =>
            {
                readerThread = Thread.CurrentThread.ManagedThreadId;
                try
                {
                    released.Wait(TimeSpan.FromSeconds(10));
                    return EmptySnapshot();
                }
                finally { finished.Set(); }
            });
            try
            {
                Assert.IsFalse(load.TryComplete(out _), "Polling must return while the reader is blocked.");
                released.Set();
                double until = UnityEditor.EditorApplication.timeSinceStartup + 10;
                while (!load.TryComplete(out _) && UnityEditor.EditorApplication.timeSinceStartup < until)
                    yield return null;
                Assert.IsTrue(load.TryComplete(out var snapshot));
                Assert.AreNotEqual(editorThread, readerThread);
                Assert.AreEqual("Synthetic empty catalog", snapshot.Result.ErrorMessage);
            }
            finally
            {
                released.Set();
                load.Abandon();
                if (finished.Wait(TimeSpan.FromSeconds(2))) { released.Dispose(); finished.Dispose(); }
            }
        }

        [UnityTest]
        public IEnumerator WorkerFailureBecomesACompletedFailureResult()
        {
            var load = new PackageRegistryLocalLoad(() => throw new InvalidOperationException("Read failed"));
            double until = UnityEditor.EditorApplication.timeSinceStartup + 10;
            while (!load.TryComplete(out _) && UnityEditor.EditorApplication.timeSinceStartup < until)
                yield return null;
            Assert.IsTrue(load.TryComplete(out var snapshot));
            Assert.IsFalse(snapshot.Result.IsValid);
            Assert.That(snapshot.Result.ErrorMessage, Is.EqualTo("Read failed"));
        }

        [UnityTest]
        public IEnumerator RealBundledCatalogCanBeParsedOnTheWorker()
        {
            var loader = new PackageRegistryLoader();
            string path = PackageRegistryLoader.ResolveBundledRegistryPath();
            var load = new PackageRegistryLocalLoad(() =>
                new PackageRegistryLocalLoad.Snapshot(loader.LoadBundled(path), string.Empty));
            double until = UnityEditor.EditorApplication.timeSinceStartup + 10;
            while (!load.TryComplete(out _) && UnityEditor.EditorApplication.timeSinceStartup < until)
                yield return null;
            Assert.IsTrue(load.TryComplete(out var snapshot));
            Assert.IsTrue(snapshot.Result.IsValid, snapshot.Result.ErrorMessage);
            Assert.Greater(snapshot.Result.Registry.packages.Length, 20);
        }

        [Test]
        public void PageOpensWithLoadingStateWhileCatalogReaderIsBlockedAndCanCloseImmediately()
        {
            var released = new ManualResetEventSlim();
            var finished = new ManualResetEventSlim();
            var loader = new PackageRegistryLoader();
            PackageRegistryProvider.SetLoaderForTests(loader);
            var load = new PackageRegistryLocalLoad(() =>
            {
                try { released.Wait(TimeSpan.FromSeconds(10)); return EmptySnapshot(); }
                finally { finished.Set(); }
            });
            loader.LocalLoad = load;
            PackageInstallerWindow window = null;
            try
            {
                window = ScriptableObject.CreateInstance<PackageInstallerWindow>();
                var root = new VisualElement();
                Invoke(window, "BuildPage", root);
                Assert.IsTrue(PackageRegistryProvider.IsLocalLoading);
                Assert.IsEmpty(PackageRegistryProvider.All);
                Assert.IsFalse(PackageRegistryProvider.IsRemoteRefreshing);
                PackageRegistryProvider.CaptureCachedStatus();
                Assert.AreSame(load, loader.LocalLoad, "Status contributors must not fall back to a synchronous read.");
                Assert.NotNull(root.Q("workspace-navigation"));
                var search = root.Q<TextField>("installer-package-search");
                Assert.NotNull(search, "Package filtering must not replace global tool navigation.");
                Assert.That(search.parent.ClassListContains("dw-package-filters"), Is.True);
                Assert.That(root.Query<TextField>().ToList().Count, Is.GreaterThanOrEqualTo(2));
                Assert.That(root.Query<Label>().ToList().Any(label => label.text.Contains("Loading")), Is.True);
                Assert.IsNull(Field(window, "_graphView"), "Opening the list must not construct the graph.");
                Assert.IsFalse(root.Q("installer-project-channel").enabledInHierarchy);
                Invoke(window, "UpdateViewVisibility");
                Assert.That(root.Q("installer-loading").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(((VisualElement)Field(window, "_listViewContainerHost")).style.display.value, Is.EqualTo(DisplayStyle.None),
                    "Visibility refresh must not reveal the empty list behind the loading state.");
                Invoke(window, "RefreshGraphView", "loading regression");
                Assert.IsNull(Field(window, "_cachedPackageGraph"));
                UnityEngine.Object.DestroyImmediate(window);
                window = null;
                PackageRegistryProvider.ResetForTests();
                Assert.IsNull(loader.LocalLoad, "Reset must detach the obsolete worker result.");
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                PackageRegistryProvider.ResetForTests();
                released.Set();
                if (finished.Wait(TimeSpan.FromSeconds(2))) { released.Dispose(); finished.Dispose(); }
            }
        }

        [Test]
        public void RowsArriveInBatchesAndNewSearchReplacesAnUnfinishedBatch()
        {
            PackageRegistryProvider.ResetForTests();
            PackageRegistryProvider.EnsureLoaded();
            var window = ScriptableObject.CreateInstance<PackageInstallerWindow>();
            try
            {
                var root = new VisualElement();
                Invoke(window, "BuildPage", root);
                var detection = (PackageDetectionService)Field(window, "_packageDetectionService");
                typeof(PackageDetectionService).GetProperty("HasSuccessfulRefresh").SetValue(detection, true);
                var workspace = Field(window, "_workspace");
                workspace.GetType().GetField("category", Private).SetValue(workspace, 1);
                Invoke(workspace, "Refresh");
                var rows = root.Q("workspace-collection-rows");
                Assert.AreEqual(0, rows.childCount, "Refresh should schedule rows, not build them inline.");
                int total = PackageRegistryProvider.All.Count;
                for (int i = 0; i < 100 && rows.childCount == 0; i++) Invoke(workspace, "PumpRows");
                Assert.Greater(rows.childCount, 0);
                Assert.Less(rows.childCount, total);
                root.Q<TextField>("installer-package-search").SetValueWithoutNotify(string.Empty);
                workspace.GetType().GetField("search", Private).SetValue(workspace, "no-such-package-async-test");
                Invoke(workspace, "Refresh");
                for (int i = 0; i < 100; i++) Invoke(workspace, "PumpRows");
                Assert.AreEqual(0, rows.childCount, "An obsolete batch must not append rows after a new search.");
                Assert.IsNull(Field(window, "_graphView"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                PackageRegistryProvider.ResetForTests();
            }
        }

        private static object Field(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
        private static void Invoke(object target, string name, params object[] arguments) =>
            target.GetType().GetMethods(Private).Single(method => method.Name == name &&
                method.GetParameters().Length == arguments.Length).Invoke(target, arguments);
        private static PackageRegistryLocalLoad.Snapshot EmptySnapshot() => new PackageRegistryLocalLoad.Snapshot(
            PackageRegistryLoadResult.Failure(PackageRegistrySource.Bundled, "Synthetic empty catalog"), string.Empty);
    }
}
