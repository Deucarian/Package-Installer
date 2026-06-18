# Changelog

## 1.1.26 - 2026-06-18

- Added shared Installed / Not installed visibility filters and package search across List View and Ecosystem Graph.
- Filtered graph rendering now hides package nodes, shortcut actions, and relationship edges while preserving semantic overview positions.
- Added graph empty states, visible counts, and hidden-related package indicators for filtered focus mode.

## 1.1.25 - 2026-06-18

- Made Ecosystem Graph the default Package Installer view and placed it before List View in the toggle.
- Refined semantic wheel framing with viewport-aware overview radius, tighter Fit bounds, and a stronger Deucarian hub.
- Improved graph node status language with persistent status rails, marker chips, and low-zoom-safe focus actions.

## 1.1.24 - 2026-06-18

- Reworked the Ecosystem Graph overview into a deterministic semantic wheel with Foundation, Services / Runtime, Experience / UI / World, and Tools / Quality sectors.
- Added optional `ecosystemGroup` and `overviewOrder` registry metadata with deterministic fallback grouping for older registries.
- Positioned Integration and Suite nodes from target/member circular means while preserving the existing strict ego-focus layout.

## 1.1.23 - 2026-06-18

- Tightened Ecosystem Graph focus mode into a strict selected-package ego layout with fixed dependency, dependent, integration, companion, and suite bands.
- Collapsed unrelated focus-mode packages into a single compact summary pill instead of scattered dimmed cards.
- Limited focus-mode relationship context to direct selected-package edges so unrelated graph edges stay hidden.

## 1.1.22 - 2026-06-18

- Simplified Ecosystem Graph focus mode by removing floating semantic zone labels and large suite composition regions.
- Kept suites as normal graph nodes while showing suite membership through focused nodes and edges.
- Reduced focused suite/member edge clutter so non-suite package focus stays cleaner.

## 1.1.21 - 2026-06-18

- Allowed Ecosystem Graph shortcut actions for the selected package and directly related packages in stable focus mode.
- Kept overview and unrelated dimmed graph nodes free of direct package action buttons.
- Tightened the overview orbit with simplified package cards, stronger Deucarian hub styling, and clearer sector labels.
- Compactly stacked unrelated focus-mode packages with simplified dimmed cards.
- Calmed graph relationship rendering with thinner Integration cables, smaller curves, and slower directional markers.

## 1.1.20 - 2026-06-18

- Limited Ecosystem Graph node action buttons to the selected package so first-click graph navigation cannot accidentally run install/update/reinstall actions.
- Kept selected-node actions disabled while graph layout transitions are animating.

## 1.1.19 - 2026-06-17

- Refined the Ecosystem Graph overview into one deterministic Deucarian-centered package orbit with sector labels.
- Moved unrelated focus-mode packages into a compact dimmed side stack.
- Locked graph node selection, hover focus, and node actions while layout transitions are animating.

## 1.1.18 - 2026-06-17

- Added explicit overview and focus layout modes for the Ecosystem Graph.
- Kept the overview as deterministic Deucarian-centered rings for core, runtime/tooling, integrations, and suites.
- Added ego focus targets that center the selected package and group providers, dependents, integrations, companions, and suites into stable zones.
- Animated node and edge transitions between overview and focus layouts.

## 1.1.17 - 2026-06-17

- Anchored the Package Installer operation footer as the final fixed row in the window layout.
- Moved Show Details / Hide Details and the package version label into the persistent footer.
- Split the operation details drawer into a compact row above the footer so it expands upward and consumes main content height.

## 1.1.16 - 2026-06-17

- Renamed Package Installer registry, graph, and UI terminology from Bridge packages to Integration packages.
- Updated bundled fallback registry package IDs, categories, relationship metadata, and tests to use the new integration package identities.
- Added migration coverage for the old bridge package IDs that are replaced by integration package IDs.

## 1.1.15 - 2026-06-17

- Kept the Package Installer footer version visible while operation details are expanded.
- Compacted the graph operation details drawer so small summaries no longer reserve excessive empty height.

## 1.1.14 - 2026-06-16

- Made update checks source-aware so npm/scoped-registry packages compare installed package versions against npmjs `latest` instead of requiring Git revisions.
- Kept Git-installed packages on the existing revision comparison path while treating missing Git revisions as neutral "Cannot determine update" states.
- Reduced startup update-check noise when package detection or registry loading has not produced installed Deucarian packages yet.

## 1.1.13 - 2026-06-15

- Updated the Deucarian Logging dependency to `0.2.6` for npmjs scoped-registry publishing.

## 1.1.12 - 2026-06-15

- Migrated Package Installer editor chrome, sections, status badges, icon usage, and shared colors/styles onto `com.deucarian.editor`.
- Updated the Deucarian Editor dependency to `0.1.2`.

## 1.1.11 - 2026-06-15

- Updated the Deucarian Logging dependency to `0.2.5` for the restored Unity `2021.3` first-time install graph.

## 1.1.10 - 2026-06-15

- Declared Package Installer dependencies on Deucarian Editor and Deucarian Logging for bootstrap-aware installs.
- Synced the bundled fallback registry's Package Installer dependency metadata with the main registry.

## 1.1.9 - 2026-06-15

- Removed the duplicated Source panel from package details and kept source diagnostics under Advanced.
- Moved current and last operation feedback into a shared bottom status bar with an expandable summary drawer.
- Persisted Advanced foldout and operation drawer state per Unity project.
- Standardized package logging on com.deucarian.logging.
- Added Package Installer log categories for registry, install, sample import, and update-check diagnostics.

## 1.1.8 - 2026-06-15

- Kept manually selected Stable or Development channels from being overwritten by auto-detected Custom install references during update checks.
- Recognized common Git branch reference forms such as `refs/heads/main` and `origin/develop` when inferring installed package channels.

## 1.1.7 - 2026-06-15

- Installed registry dependencies before requested packages for first installs, reinstalls, single updates, and update-all operations.
- Added dependency install planning for missing, already installed, duplicate, and circular dependency cases with explicit logs.
- Reported the exact target `package.json` URL when remote registry package-name validation cannot fetch or read a package manifest.

## 1.1.6 - 2026-06-15

- Moved the Package Installer editor entry point under `Tools > Deucarian > Package Installer`.
- Kept the installer self-contained with no dependency on `com.deucarian.editor`.

## 1.1.5 - 2026-06-15

- Synced the bundled fallback registry with the main registry for Deucarian Editor, Logging, and Theming.
- Declared bundled fallback dependency ordering from Logging and Theming to Deucarian Editor.
- Documented the Package Installer's legacy `Deucarian > Package Installer` menu path as an intentional bootstrap exception.
- Added package license metadata.

## 1.1.4 - 2026-06-15

- Added Deucarian Editor to the bundled fallback package registry.

## 1.1.3 - 2026-06-15

- Documented that the Package Installer remains at `Deucarian > Package Installer` while other packages own their own `Deucarian` menu groups.

## 1.1.2 - 2026-06-15

- Let the package list use the full available window height by moving operation summary panels under the details pane.

## 1.1.1 - 2026-06-15

- Log failed package update checks as errors instead of warnings.
- Keep successful, up-to-date, and update-available checks on normal info logging.
