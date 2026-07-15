using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.PackageInstaller.Editor.Tests
{
    internal sealed class PackageGraphEmptyStateTests
    {
        [Test]
        public void EmptyState_SubtreeIsExcludedFromViewportLeftPanCapture()
        {
            VisualElement emptyState = new VisualElement();
            emptyState.AddToClassList("dpi-ecosystem-graph__empty-state");
            Label title = new Label("No matches");
            Button action = new Button { text = "Clear search" };
            Label nestedActionContent = new Label("Nested action content");
            action.Add(nestedActionContent);
            emptyState.Add(title);
            emptyState.Add(action);

            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(emptyState));
            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(title));
            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(action));
            Assert.IsFalse(PackageGraphViewport.IsLeftPanTargetForTests(nestedActionContent));

            foreach (VisualElement target in new VisualElement[] { emptyState, title, action, nestedActionContent })
            {
                Assert.IsFalse(PackageGraphViewport.ShouldConsiderPanForTests(target, button: 0));
                Assert.IsFalse(PackageGraphViewport.ShouldConsiderPanForTests(target, button: 0, altKey: true));
                Assert.IsFalse(PackageGraphViewport.ShouldConsiderPanForTests(target, button: 1));
                Assert.IsFalse(PackageGraphViewport.ShouldConsiderPanForTests(target, button: 2));
            }
        }

        [Test]
        public void EmptyState_ShowAllPackagesPreservesSearchAndEnablesBothStatuses()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: false);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: false,
                showNotInstalled: false);
            int filterChangedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++);
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Show all packages", action.text);

            InvokePointerClick(action);

            Assert.AreEqual("logging", filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsTrue(filterState.ShowNotInstalled);
            Assert.AreEqual(1, filterChangedCount);
        }

        [Test]
        public void EmptyState_ClearSearchPreservesStatusFiltersAndGroupFocus()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: false);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "no-such-package",
                showInstalled: true,
                showNotInstalled: false);
            int filterChangedCount = 0;
            int rootFocusedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++,
                () => rootFocusedCount++,
                "infrastructure");
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Clear search", action.text);

            InvokePointerClick(action);

            Assert.AreEqual(string.Empty, filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsFalse(filterState.ShowNotInstalled);
            Assert.AreEqual(1, filterChangedCount);
            Assert.AreEqual(0, rootFocusedCount);
        }

        [Test]
        public void EmptyState_ShowMatchingPackagesPreservesQueryAndEnablesRelevantStatus()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: false);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: true,
                showNotInstalled: false);
            int filterChangedCount = 0;
            int rootFocusedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++,
                () => rootFocusedCount++,
                "infrastructure");
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Show matching packages", action.text);

            InvokePointerClick(action);

            Assert.AreEqual("logging", filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsTrue(filterState.ShowNotInstalled);
            Assert.AreEqual(1, filterChangedCount);
            Assert.AreEqual(0, rootFocusedCount);
        }

        [Test]
        public void EmptyState_ShowMatchingPackagesCanEnableInstalledWithoutDisablingNotInstalled()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: true);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: false,
                showNotInstalled: true);
            int filterChangedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++);
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Show matching packages", action.text);

            InvokePointerClick(action);

            Assert.AreEqual("logging", filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsTrue(filterState.ShowNotInstalled);
            Assert.AreEqual(1, filterChangedCount);
        }

        [Test]
        public void EmptyState_DirectCategoryMatchRemainsAvailableWhenItsPackagesAreStatusHidden()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: true);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "Infrastructure",
                showInstalled: false,
                showNotInstalled: true);
            PackageGraphView view = CreateView(graph, filterState, null);
            VisualElement emptyState = FindByClass<VisualElement>(
                view,
                "dpi-ecosystem-graph__empty-state");

            Assert.AreEqual(DisplayStyle.None, emptyState.style.display.value);
        }

        [Test]
        public void EmptyState_StatusOnlyMissUsesShowMatchingPackages()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: false);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                string.Empty,
                showInstalled: true,
                showNotInstalled: false);
            int filterChangedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++);
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Show matching packages", action.text);

            InvokePointerClick(action);

            Assert.AreEqual(string.Empty, filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsTrue(filterState.ShowNotInstalled);
            Assert.AreEqual(1, filterChangedCount);
        }

        [Test]
        public void EmptyState_SearchAllGroupsPreservesQueryAndInvokesRootFocus()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: false, includeTheming: true);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "theming",
                showInstalled: true,
                showNotInstalled: true);
            int filterChangedCount = 0;
            int rootFocusedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++,
                () => rootFocusedCount++,
                "infrastructure");
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Search all groups", action.text);

            InvokePointerClick(action);

            Assert.AreEqual("theming", filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsTrue(filterState.ShowNotInstalled);
            Assert.AreEqual(0, filterChangedCount);
            Assert.AreEqual(1, rootFocusedCount);
        }

        [Test]
        public void EmptyState_GroupScopedMatchElsewhereUsesSearchAllGroupsEvenWhenStatusHidden()
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: true, includeTheming: true);
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState(
                "theming",
                showInstalled: true,
                showNotInstalled: false);
            int rootFocusedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                null,
                () => rootFocusedCount++,
                "infrastructure");
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual("Search all groups", action.text);

            InvokePointerClick(action);

            Assert.AreEqual("theming", filterState.SearchText);
            Assert.IsTrue(filterState.ShowInstalled);
            Assert.IsFalse(filterState.ShowNotInstalled);
            Assert.AreEqual(1, rootFocusedCount);
        }

        [Test]
        public void EmptyState_EmptyRegistryHasNoFilterAction()
        {
            PackageGraphModel graph = new PackageGraphBuilder(_ => false)
                .Build(Array.Empty<PackageDefinition>());
            PackageVisibilityFilterState filterState = new PackageVisibilityFilterState();
            int filterChangedCount = 0;
            int rootFocusedCount = 0;
            PackageGraphView view = CreateView(
                graph,
                filterState,
                () => filterChangedCount++,
                () => rootFocusedCount++);
            VisualElement emptyState = FindByClass<VisualElement>(
                view,
                "dpi-ecosystem-graph__empty-state");
            Label title = FindByClass<Label>(view, "dpi-ecosystem-graph__empty-title");
            Button action = FindEmptyStateAction(view);

            Assert.AreEqual(DisplayStyle.Flex, emptyState.style.display.value);
            Assert.AreEqual(
                "No package entries are available in the active registry.",
                title.text);
            Assert.AreEqual(DisplayStyle.None, action.style.display.value);
            Assert.AreEqual(string.Empty, action.text);

            InvokePointerClick(action);

            Assert.AreEqual(0, filterChangedCount);
            Assert.AreEqual(0, rootFocusedCount);
        }

        [TestCase(KeyCode.Return)]
        [TestCase(KeyCode.KeypadEnter)]
        [TestCase(KeyCode.Space)]
        public void EmptyState_EnterAndSpaceActivateEveryContextualActionExactlyOnce(KeyCode keyCode)
        {
            PackageGraphModel graph = CreateGraph(loggingInstalled: false, includeTheming: true);

            PackageVisibilityFilterState showAllState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: false,
                showNotInstalled: false);
            int showAllChanged = 0;
            Button showAll = FindEmptyStateAction(CreateView(
                graph,
                showAllState,
                () => showAllChanged++));
            InvokeKeyboard(showAll, keyCode);
            Assert.IsTrue(showAllState.ShowInstalled);
            Assert.IsTrue(showAllState.ShowNotInstalled);
            Assert.AreEqual(1, showAllChanged);

            PackageVisibilityFilterState clearSearchState = new PackageVisibilityFilterState(
                "no-such-package",
                showInstalled: true,
                showNotInstalled: false);
            int clearSearchChanged = 0;
            Button clearSearch = FindEmptyStateAction(CreateView(
                graph,
                clearSearchState,
                () => clearSearchChanged++));
            InvokeKeyboard(clearSearch, keyCode);
            Assert.AreEqual(string.Empty, clearSearchState.SearchText);
            Assert.AreEqual(1, clearSearchChanged);

            PackageVisibilityFilterState showMatchingState = new PackageVisibilityFilterState(
                "logging",
                showInstalled: true,
                showNotInstalled: false);
            int showMatchingChanged = 0;
            Button showMatching = FindEmptyStateAction(CreateView(
                graph,
                showMatchingState,
                () => showMatchingChanged++));
            InvokeKeyboard(showMatching, keyCode);
            Assert.IsTrue(showMatchingState.ShowNotInstalled);
            Assert.AreEqual(1, showMatchingChanged);

            PackageVisibilityFilterState searchAllState = new PackageVisibilityFilterState(
                "theming",
                showInstalled: true,
                showNotInstalled: true);
            int rootFocusedCount = 0;
            Button searchAll = FindEmptyStateAction(CreateView(
                graph,
                searchAllState,
                null,
                () => rootFocusedCount++,
                "infrastructure"));
            InvokeKeyboard(searchAll, keyCode);
            Assert.AreEqual("theming", searchAllState.SearchText);
            Assert.AreEqual(1, rootFocusedCount);
        }

        private static PackageGraphView CreateView(
            PackageGraphModel graph,
            PackageVisibilityFilterState filterState,
            Action filterChanged,
            Action rootFocused = null,
            string focusedGroupId = null)
        {
            PackageGraphView view = new PackageGraphView(
                _ => { },
                (_, __) => { },
                selectionCleared: null,
                rootFocused: rootFocused,
                groupFocused: null,
                filterState: filterState,
                filterChanged: filterChanged);
            HashSet<string> visiblePackageIds =
                PackageVisibilityFilter.CreateStatusVisiblePackageIdSet(graph, filterState);
            PackageGraphSearchState searchState =
                PackageGraphSearchIndex.Create(graph, filterState);

            view.SetGraph(
                graph,
                string.Empty,
                string.Empty,
                focusedGroupId ?? string.Empty,
                actionsEnabled: true,
                visiblePackageIds,
                searchState,
                PackageVisibilityFilter.CalculateCounts(graph, filterState),
                hiddenRelatedCount: 0);
            return view;
        }

        private static PackageGraphModel CreateGraph(
            bool loggingInstalled,
            bool includeTheming = false)
        {
            List<PackageDefinition> packages = new List<PackageDefinition>
            {
                CreatePackage(
                    "Deucarian Logging",
                    "com.deucarian.logging",
                    "Core",
                    "infrastructure")
            };

            if (includeTheming)
            {
                packages.Add(CreatePackage(
                    "Deucarian Theming",
                    "com.deucarian.theming",
                    "UI",
                    "ui-presentation"));
            }

            return new PackageGraphBuilder(packageId =>
                    loggingInstalled &&
                    string.Equals(
                        packageId,
                        "com.deucarian.logging",
                        StringComparison.OrdinalIgnoreCase))
                .Build(packages);
        }

        private static PackageDefinition CreatePackage(
            string displayName,
            string packageId,
            string category,
            string groupId)
        {
            return new PackageDefinition(
                displayName,
                packageId,
                "https://github.com/Deucarian/" + displayName.Replace("Deucarian ", string.Empty) + ".git#main",
                displayName + " package.",
                Array.Empty<string>(),
                PackageType.Core,
                "https://github.com/Deucarian/" + displayName.Replace("Deucarian ", string.Empty) + ".git#develop",
                category: category,
                groupId: groupId);
        }

        private static void InvokePointerClick(Button action)
        {
            Event mouseDown = new Event
            {
                type = EventType.MouseDown,
                button = 0,
                mousePosition = Vector2.one
            };

            using (MouseDownEvent downEvent = MouseDownEvent.GetPooled(mouseDown))
            {
                action.SendEvent(downEvent);
            }

            Event mouseUp = new Event
            {
                type = EventType.MouseUp,
                button = 0,
                mousePosition = Vector2.one
            };

            using (MouseUpEvent upEvent = MouseUpEvent.GetPooled(mouseUp))
            {
                action.SendEvent(upEvent);
            }
        }

        private static void InvokeKeyboard(Button action, KeyCode keyCode)
        {
            Assert.IsTrue(action.focusable);

            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', keyCode, EventModifiers.None))
            {
                action.SendEvent(evt);
            }
        }

        private static Button FindEmptyStateAction(VisualElement root)
        {
            return FindByClass<Button>(root, "dpi-ecosystem-graph__empty-action");
        }

        private static TElement FindByClass<TElement>(VisualElement root, string className)
            where TElement : VisualElement
        {
            return root.Query<TElement>(className: className)
                .ToList()
                .Single();
        }
    }
}
