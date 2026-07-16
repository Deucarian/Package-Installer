# Changelog

## 1.1.65 - 2026-07-15

- Moved Package Installer toolbar, responsive, drawer, footer, and IMGUI workbench presentation onto the shared `com.deucarian.editor` 1.0.2 contract.
- Preserved the released Package Installer hierarchy, geometry, interaction states, breakpoints, and graph-specific behavior through visual-equivalence contracts.

## 1.1.64 - 2026-07-15

- Fixed graph empty-state recovery actions so their pointer and keyboard activation is not intercepted by viewport panning.
- Kept root and category geometry stable while search emphasizes matches and status filters hide packages without reflowing their remaining siblings.
- Added truthful recovery actions for lexical misses, status-hidden matches, disabled visibility filters, and group-scoped misses.

## 1.1.63 - 2026-07-15

- Restored a contextual ecosystem-wide `Update all (N)` action in the root overview whenever update results are available.
- Temporarily disabled List View and route all view requests to Ecosystem Graph while keeping the list implementation available for later refinement.
- Required Git-backed Unity review and validation hosts so branch testing cannot silently use stale local package worktrees.

## 1.1.62 - 2026-07-13

- Added immutable, dependency-aware operation plans with contextual preflight for bulk, multi-step, migration, downgrade, fallback, conflict, and destructive operations while keeping ordinary one-step installs immediate.
- Added failure propagation, independent-root continuation, request-boundary cancellation, exact-target recovery records, and explicit Resume, Restart, and Discard handling after assembly reloads.
- Retried transient atomic file-replacement collisions for recovery and registry-cache state without deleting a valid destination.
- Hardened registry and update networking with validated last-known-good caching, bounded concurrency, request timeouts, cancellation, deduplication, and stale-generation suppression.
- Cancel installer-owned remote registry refreshes when the Unity editor quits so network continuations cannot outlive Mono shutdown.
- Tightened installed-source channel detection to match the normalized Git remote, package path, and ref together, and added registry self-dependency and cycle rejection.
- Staged fallback sample imports beneath `Library`, fully validated them before an atomic move into `Assets`, and guaranteed cleanup without overwriting existing imports.
- Improved package-specific graph behavior for responsive widths, keyboard navigation, focus/search restoration, route isolation, transient Checking state, missing relationships, and dense-relation overflow.
- Kept graph rendering compatible with Unity 2021.3 through a package-specific mesh fallback while preserving native Painter2D rendering on newer editors.
- Added actual installed/package-lock reverse-dependency removal warnings, a chronological activity/result surface with copyable details, and contextual recovery actions.
- Added normal push and pull-request Unity EditMode CI using the minimum Unity 2021.3 fixture and exact Editor/Logging package revisions.

## 1.1.64 - 2026-07-15

- Fixed graph empty-state recovery actions so their pointer and keyboard activation is not intercepted by viewport panning.
- Kept root and category geometry stable while search emphasizes matches and status filters hide packages without reflowing their remaining siblings.
- Added truthful recovery actions for lexical misses, status-hidden matches, disabled visibility filters, and group-scoped misses.

## 1.1.63 - 2026-07-15

- Restored a contextual ecosystem-wide `Update all (N)` action in the root overview whenever update results are available.
- Temporarily disabled List View and route all view requests to Ecosystem Graph while keeping the list implementation available for later refinement.
- Required Git-backed Unity review and validation hosts so branch testing cannot silently use stale local package worktrees.

## 1.1.62 - 2026-07-13

- Added immutable, dependency-aware operation plans with contextual preflight for bulk, multi-step, migration, downgrade, fallback, conflict, and destructive operations while keeping ordinary one-step installs immediate.
- Added failure propagation, independent-root continuation, request-boundary cancellation, exact-target recovery records, and explicit Resume, Restart, and Discard handling after assembly reloads.
- Retried transient atomic file-replacement collisions for recovery and registry-cache state without deleting a valid destination.
- Hardened registry and update networking with validated last-known-good caching, bounded concurrency, request timeouts, cancellation, deduplication, and stale-generation suppression.
- Cancel installer-owned remote registry refreshes when the Unity editor quits so network continuations cannot outlive Mono shutdown.
- Tightened installed-source channel detection to match the normalized Git remote, package path, and ref together, and added registry self-dependency and cycle rejection.
- Staged fallback sample imports beneath `Library`, fully validated them before an atomic move into `Assets`, and guaranteed cleanup without overwriting existing imports.
- Improved package-specific graph behavior for responsive widths, keyboard navigation, focus/search restoration, route isolation, transient Checking state, missing relationships, and dense-relation overflow.
- Kept graph rendering compatible with Unity 2021.3 through a package-specific mesh fallback while preserving native Painter2D rendering on newer editors.
- Added actual installed/package-lock reverse-dependency removal warnings, a chronological activity/result surface with copyable details, and contextual recovery actions.
- Added normal push and pull-request Unity EditMode CI using the minimum Unity 2021.3 fixture and exact Editor/Logging package revisions.

## 1.1.61 - 2026-07-13

- Consolidated Ecosystem Graph group navigation into the right-side Groups list with shared graph selection state.
- Moved generic ambient glass, wallpaper, and glass sheen styling to `com.deucarian.editor` while keeping Package Installer graph-specific styling local.
- Simplified Ecosystem Overview group rows and added left-edge attention styling for graph group cards.
- Removed the user-facing graph timing diagnostics menu so the only Package Installer menu item is `Tools/Deucarian/Package Installer`.
- Centralized selected stable/development channel state behind a project-scoped package-management preference shared with Bootstrap.
- Added manifest/package-lock state signatures so installed-package refreshes are invalidated when Unity Package Manager changes package state outside the installer window.
- Tightened graph route obstacle validation so package-to-package routes can leave or enter their own category while still avoiding unrelated category obstacles.
- Added assembly-aware Package Installer self-update reconciliation, persisted reload-pending state, self-last multi-package ordering, and safe queue resume after domain reloads.
- Added explicit Git source-migration status for every registry-installed Deucarian package without querying npm metadata; Package Installer migrations are handed to Bootstrap while other packages queue their selected catalog Git URL directly.
- Added actionable reload recovery with a `Retry Script Reload` action when Unity Package Manager resolved a newer installer but the previous assembly is still running.
- Added configurable, delayed update checks that run at most once per editor session.
- Added catalog-projection validation to Package Installer CI.
- Documented Bootstrap/manual one-time recovery for legacy npm `1.1.12` and for a compile-blocked first hop from an already-running `1.1.60` assembly; MVID-based reload recovery is available after `1.1.61` has loaded.

## 1.1.60 - 2026-06-23

- Updated bundled fallback registry URLs for the promoted gameplay, suite, and template packages so stable installs use `#main` and development installs use `#develop`.

## 1.1.59 - 2026-06-23

- Synced the bundled fallback registry with the Phase 2B Idle Auto Defense template entry and template group hierarchy.
- Added template package classification so template entries stay visible and individually installable while `Install All` skips them.
- Added registry coverage for template group parsing, template entry parsing, suite dependency mapping, and template install-all filtering.

## 1.1.58 - 2026-06-22

- Updated the exact `com.deucarian.logging` dependency to `1.0.1`.

## 1.1.57 - 2026-06-22

- Added Deucarian Common to the bundled fallback registry.
- Updated bundled Object Loading, UI Binding, and UI Flow dependencies to include Common.

## 1.1.56 - 2026-06-22

- Updated the bundled fallback registry so UI Flow declares its direct Deucarian Logging dependency.

## 1.1.55 - 2026-06-22

- Replaced package graph card zoom states with explicit IconOnly, Micro, Compact, and Full semantic presentation levels.
- Recalculated graph layout, collision, Fit bounds, and package ego rectangles from active presentation metrics instead of hidden full-size rows.
- Shortened graph-only titles by removing redundant Deucarian prefixes while preserving full names in details and tooltips.

## 1.1.54 - 2026-06-22

- Added Ambient Glass v1 shell layers with fixed subtle glow, grain, vignette, and readability overlay support.
- Added a viewport-fixed reactive graph spotlight for root, category, package, and attention contexts.
- Refined graph/panel glass styling and two-pass relationship edge rendering without changing graph geometry.

## 1.1.53 - 2026-06-20

- Suspended structural search/status projection while PackageEgo is active so focused package layouts always use the full ecosystem graph.
- Kept the left category rail global during search while highlighting or dimming categories by structural match context.
- Scoped filtered category focus to the active category and removed relationship expansion from structural search hover.

## 1.1.52 - 2026-06-20

- Replaced dim-only Ecosystem Graph search with a structurally pruned filtered graph mode.
- Added recursive filtered hierarchy layout, pruned category rail rendering, and hover-only relationship context reveals.
- Preserved graph navigation/camera state while searching and restored the previous focus/camera when search is cleared.

## 1.1.51 - 2026-06-20

- Rebuilt the Last Operation Summary drawer as persistent UI Toolkit content so expanded details remain visible and scrollable.
- Added explicit graph route kinds and obstacle-aware package relationship routing around package cards and category context nodes.
- Stopped Suite membership routes from using animated direction cues and kept package relationships separate from structural category membership.

## 1.1.50 - 2026-06-20

- Replaced package ego contextual category placement with deterministic owning/provider/dependent/integration/companion rails.
- Routed focus-mode category membership through structural buses for multi-package category groups.
- Integrated package and category back hints into shared caption/header styling and trimmed graph hover tooltips.

## 1.1.49 - 2026-06-19

- Render category status rings with explicit full-circle geometry for empty and single-status categories so one-color rings are continuous.
- Bundled hard dependency and Integration relationships between the same package pair onto one shared route with layered cable/flow rendering.
- Centralized operation drawer/footer spacing tokens and improved expanded drawer message wrapping without changing the footer layout.

## 1.1.48 - 2026-06-19

- Routed focused relationship edges through deterministic ports with shared fan-out trunks for multi-target integration/dependency zones.
- Replaced boxed active-node back buttons with non-interactive chevron hints while keeping the active node body as the back target.
- Aligned the operation drawer and footer on a shared compact spacing grid and kept operation status authoritative in the footer.

## 1.1.47 - 2026-06-19

- Removed wheel-driven hierarchy enter/exit navigation so mouse wheel input only performs cursor-centered graph camera zoom.
- Added active category/package back affordances for the click-again-to-go-back hierarchy flow.
- Replaced noisy category hub counters/badges with external status summaries and proportional installed/not-installed/attention status rings.

## 1.1.46 - 2026-06-19

- Removed the duplicate CSS ring-guide orbit renderer so root and category orbits are drawn only by the graph Painter2D orbit layer.
- Added node visual-state driven transition frames with opacity and scale so entering category children fan outward instead of spawning as full-size center stacks.
- Started hierarchy-exit wheel intent slightly above the hard minimum zoom while preserving multi-notch commit behavior.

## 1.1.45 - 2026-06-19

- Reworked hierarchy preview frames to derive category hub anchors, rendered orbits, child nodes, and spokes from one animated group center/radius state.
- Kept zoom-enter previews locked to the captured hovered category so moving layout frames do not cancel the intended navigation.
- Added regression coverage for animated-anchor camera evaluation and mid-transition child projection onto the visible category orbit.

## 1.1.44 - 2026-06-19

- Forced category orbit guides to use one scalar world-space radius and derive square render rects from it.
- Animated category orbit radii alongside group centers during graph layout and hierarchy preview transitions.
- Added regression coverage for scalar ring-guide bounds so visible orbits remain circular.

## 1.1.43 - 2026-06-19

- Added deliberate wheel zoom-enter previews for structural category nodes using the shared anchored hierarchy transition path.
- Enlarged graph package card metrics and row styling so Standard and Full cards reserve space for category, status, channel, and safe action rows.
- Moved the fixed Deucarian wallpaper into a clipped application shell with a top safe scrim below the editor chrome.

## 1.1.42 - 2026-06-19

- Reworked Ecosystem Graph search into a strict structural finder for category names, category paths, package names, and package IDs.
- Kept graph layout stable while typing by previewing direct matches and structural context instead of filtering nodes out of the canvas.
- Synchronized category hover between the left rail and graph category/package context without triggering navigation or relationship expansion.

## 1.1.41 - 2026-06-19

- Restyled the expanded Last Operation Summary drawer as a compact Deucarian frosted-glass panel above the fixed footer.
- Removed the default grey IMGUI scroll background from the operation drawer while keeping summary text fully opaque.
- Added drawer height regression coverage so small summaries stay compact and long summaries cap with internal scrolling.

## 1.1.40 - 2026-06-19

- Added cancellable wheel zoom-through hierarchy exits for package, nested category, top-level category, and root graph modes.
- Kept hierarchy exit previews anchored to the navigation target with coordinated node and camera interpolation.
- Added regression coverage for exit-wheel thresholds, reverse-wheel cancellation, and one-level commit behavior.

## 1.1.39 - 2026-06-19

- Coordinated Ecosystem Graph navigation so layout movement and camera pan/zoom use one anchored transition.
- Added nested Experience & Interaction categories for UI & Presentation and World Interaction.
- Fixed root overview navigation state so the rail, breadcrumb, details, and graph focus clear together.

## 1.1.38 - 2026-06-19

- Restored the Package Installer footer as a real UI Toolkit row with status, summary, details toggle, and version children.
- Rebound footer status and operation summary updates so List View, Ecosystem Graph, and drawer toggles keep the row populated.
- Added footer hierarchy regression coverage for content, version, responsive visibility, and Show/Hide Details state.

## 1.1.37 - 2026-06-19

- Separated category hub anchors from captions so orbit centers, spokes, Fit, and Center target the circular hub.
- Added explicit wide/compact/narrow Package Installer responsive modes with narrow stacked graph details.
- Kept the Deucarian wallpaper fixed to the EditorWindow viewport while graph and details layouts resize.

## 1.1.36 - 2026-06-19

- Polished Ecosystem Graph navigation with empty-canvas left-drag panning and context-aware right-click menus.
- Reduced overview package footprints and dynamic zoom-out limits so Fit can frame dense hierarchy views.
- Clarified structural category display, package kind terminology, and persistent membership cues in graph focus modes.
- Tightened the persistent installer footer into one centered row with stable status, summary, details toggle, and version alignment.

## 1.1.35 - 2026-06-19

- Added adaptive Ecosystem Graph package node presentation levels and compact overview metrics.
- Tightened root/category orbit sizing from active node footprints so overview Fit lands at a more readable zoom.
- Updated package detail terminology to separate structural hierarchy from package role/type.

## 1.1.34 - 2026-06-19

- Made opening the Package Installer window refetch the remote registry instead of only using the first registry refresh from the editor session.

## 1.1.33 - 2026-06-19

- Made manual update checks refetch the remote registry before computing package update state.

## 1.1.32 - 2026-06-19

- Synced the bundled fallback registry with the remote registry entry for Deucarian UI Flow.
- Made the Package Installer Refresh button refetch the remote registry instead of only refreshing installed-package detection.

## 1.1.31 - 2026-06-19

- Removed redundant graph-space sibling category lists now covered by the pinned category rail.
- Added contextual category summaries for package ego mode and tightened empty-canvas navigation safety.
- Reworked active graph bounds so Fit/Center target the current hierarchy context instead of stale or incomplete node bounds.
- Kept the category rail pinned across overview, category focus, nested category focus, and package ego modes.
- Synced the bundled fallback registry with the optimized Object Loading API integration metadata.
- Added Object Loading API Integration as an optional Object Loading companion without making it a required dependency.

## 1.1.30 - 2026-06-19

- Refined Ecosystem Graph hierarchy visuals so root/category systems use strict circular symbols and a single visible orbit boundary.
- Added a pinned category rail for group/package focus and clearer breadcrumb/current-segment behavior.
- Added plain package hierarchy context to graph cards and package details while removing remaining capsule-like graph controls.

## 1.1.29 - 2026-06-19

- Reclassified the Ecosystem Graph into Infrastructure, State & Data, Runtime Services, Experience & Interaction, Tools & Quality, Integrations, and Suites.
- Added group discs, membership spokes, hover isolation, context-sensitive legends, and semantic zoom classes for the hierarchical graph.
- Replaced rounded Package Installer status badges with compact flat icon-and-text indicators and expanded the empty graph details pane into an ecosystem overview dashboard.

## 1.1.28 - 2026-06-18

- Added structural Ecosystem Graph groups with recursive-ready group metadata and package `groupId` assignments.
- Reworked the graph overview into a Deucarian root hub with top-level group orbits and local child package orbits.
- Added group focus navigation, breadcrumbs, group detail panels, and collapsed group summaries for unrelated package ego context.

## 1.1.27 - 2026-06-18

- Made Ecosystem Graph optional companion edges non-directional with static dotted styling.
- Updated optional companion legend copy to clarify companions are recommended alongside, not required.
- Added tests confirming optional companions are ignored by dependency install planning.

## 1.1.26 - 2026-06-18

- Added shared Installed / Not installed visibility filters and package search across List View and Ecosystem Graph.
- Filtered graph rendering now hides package nodes, shortcut actions, and relationship edges while preserving semantic overview positions.
- Added graph empty states, visible counts, and hidden-related package indicators for filtered focus mode.

## 1.1.25 - 2026-06-18

- Made Ecosystem Graph the default Package Installer view and placed it before List View in the toggle.
- Refined semantic wheel framing with viewport-aware overview radius, tighter Fit bounds, and a stronger Deucarian hub.
- Improved graph node status language with persistent status rails, marker chips, and low-zoom-safe focus actions.

## 1.1.24 - 2026-06-18

- Reworked the Ecosystem Graph overview into an earlier deterministic semantic wheel with registry-driven sectors.
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
- Documented the Package Installer bootstrap menu behavior before the shared tool menu was standardized.
- Added package license metadata.

## 1.1.4 - 2026-06-15

- Added Deucarian Editor to the bundled fallback package registry.

## 1.1.3 - 2026-06-15

- Documented Package Installer menu grouping while other packages own their own Deucarian menu groups.

## 1.1.2 - 2026-06-15

- Let the package list use the full available window height by moving operation summary panels under the details pane.

## 1.1.1 - 2026-06-15

- Log failed package update checks as errors instead of warnings.
- Keep successful, up-to-date, and update-available checks on normal info logging.
