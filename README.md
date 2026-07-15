# Deucarian Package Installer

## What this is

`com.deucarian.package-installer` is a small editor-only Unity Package Manager package that adds a custom installer window for Deucarian packages.

It is the Deucarian ecosystem front door for installing standalone packages, integration packages, suite packages, templates, and explicitly declared package samples from Package Registry metadata.

Current package version: `1.1.64`.

## When to use it

- You want to browse and install Deucarian UPM packages from inside Unity.
- You need dependency-first install/update flows for registered Deucarian packages.
- You need to switch between stable Git `#main` and development Git `#develop` channels.
- You want explicit sample import actions after package installation.
- You want package status, update checks, and advanced package-reference details.

## When not to use it

- Do not use this package as a runtime dependency; it is editor-only and has no runtime assembly.
- Do not use it as the source of registry governance; Package Registry owns package metadata.
- Do not use it as a generic graph framework or generic Unity Package Manager replacement.
- Do not put package-specific runtime behavior, diagnostics ownership, or editor shell ownership here.

## 60-second quick start

Open it from:

```text
Tools > Deucarian > Package Installer
```

Select Stable or Development, choose a package, and click `Install`. Ordinary safe one-package actions start immediately; a contextual preflight appears only for bulk, multi-step, source-migration, downgrade, fallback, conflict, or destructive operations. Import samples only when the package detail view shows a sample you explicitly want.

## Deucarian Menu

The installer keeps its Unity Editor entry point at `Tools > Deucarian > Package Installer`. This package does not own the Theming, Logging, Object Loading, Session, or Selection menu groups; those packages provide their own package-local menu items under the shared `Tools > Deucarian` menu.

The installer can install standalone packages, integration packages, and explicitly declared package samples without making this package a runtime dependency of any other package.

Package ID: `com.deucarian.package-installer`

## Install

Stable:

```json
"com.deucarian.package-installer": "https://github.com/Deucarian/Package-Installer.git#main"
```

Development:

```json
"com.deucarian.package-installer": "https://github.com/Deucarian/Package-Installer.git#develop"
```

npm/scoped-registry distribution is deferred for now. Use Git URLs until the manual release process is finalized.

You can also use Unity's Package Manager window:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter the installer Git URL.
4. Open `Tools > Deucarian > Package Installer`.

## Unity compatibility

Requires Unity 2021.3 or newer.

Dependencies:

- `com.deucarian.editor` for shared Deucarian editor chrome, styles, icons, and status badges.
- `com.deucarian.logging` for installer diagnostics.

## Logging

This package uses `com.deucarian.logging` for diagnostics and `com.deucarian.editor` for shared Deucarian editor chrome, styles, icons, and status badges.

Package Installer diagnostics use stable package categories: `PackageInstaller`, `PackageInstaller.Registry`, `PackageInstaller.Install`, `PackageInstaller.Samples`, and `PackageInstaller.UpdateChecks`. Configure Deucarian Logging filters by category and level to isolate registry loading, install/remove operations, sample imports, or update checks. Entries flow through the shared ring buffer for recent-diagnostic inspection and remain compatible with future telemetry sinks.

## Usage

Use the installer window to install standalone packages, install packages with their registered dependencies, import package samples explicitly, and check installed packages for updates or source migrations.

## Package Registry

Package entries are loaded from a registry instead of being hardcoded in the installer window.

The installer loads the bundled `PackageRegistry.json` first so it works offline, then tries to refresh from:

`https://raw.githubusercontent.com/Deucarian/Package-Registry/main/packages.json`

The installer then loads a validated last-known-good remote cache from `Library/Deucarian/PackageInstaller` when one exists and starts a fresh remote request. Invalid, canceled, timed-out, or offline responses never replace a valid cache. If neither cached nor fresh remote data is valid, the bundled registry stays active and the header explains the fallback.

The registry is the source of truth for stable Git `#main` URLs and development Git `#develop` URLs. Git tags, GitHub releases, and npm/scoped-registry publication are deferred until a separate deliberate release wave.

Opening the Package Installer window and using the header `Refresh` or `Check Updates` buttons refetch the remote registry, so newly registered packages and package reference changes can appear without restarting Unity.

Remote registry validation also checks each package entry against the target package's `package.json` name so installed-package detection uses Unity's exact package IDs. If a target manifest cannot be fetched, the validation message includes the exact `package.json` URL that failed.

The current bundled fallback registry includes these Ecosystem Graph groups:

- Infrastructure: Editor, Logging
- State & Data: Core State
- Runtime Services: API, Session, Object Loading, Monetization
- Experience & Interaction: UI Binding, UI, UI Flow, Theming, XR UI, Camera Navigation, Object Selection
- Tools & Quality: Package Installer, Diagnostics, Game Content Authoring
- Integrations: UI Binding + Core State Integration, Object Loading API Integration, Object Selection + Core State Integration, Session + API Integration, XR UI Theming Integration
- Gameplay: Gameplay Foundation, Persistence, Progression, Combat, Encounters, World Spawning, World Navigation, Defense Games, Attacks, Projectiles, Weapon Systems, Auto Defense, Run Upgrades, Idle Progression
- Suites: Selection Suite, Auto Defense Suite
- Templates: Idle Auto Defense, Survivors, Movement FPS

Registered packages are first-class UPM packages with their own package IDs:

- `com.deucarian.core-state`
- `com.deucarian.api`
- `com.deucarian.logging`
- `com.deucarian.object-loading`
- `com.deucarian.session`
- `com.deucarian.monetization`
- `com.deucarian.ui-binding`
- `com.deucarian.ui`
- `com.deucarian.ui-flow`
- `com.deucarian.theming`
- `com.deucarian.xr-ui`
- `com.deucarian.camera-navigation`
- `com.deucarian.object-selection`
- `com.deucarian.editor`
- `com.deucarian.game-content-authoring`
- `com.deucarian.ui-binding.core-state-integration`
- `com.deucarian.object-loading.api-integration`
- `com.deucarian.object-selection.core-state-integration`
- `com.deucarian.session.api-integration`
- `com.deucarian.xr-ui.theming-integration`
- `com.deucarian.selection-suite`
- `com.deucarian.diagnostics`
- `com.deucarian.package-installer`
- `com.deucarian.gameplay-foundation`
- `com.deucarian.persistence`
- `com.deucarian.progression`
- `com.deucarian.combat`
- `com.deucarian.encounters`
- `com.deucarian.world-spawning`
- `com.deucarian.world-navigation`
- `com.deucarian.defense-games`
- `com.deucarian.attacks`
- `com.deucarian.projectiles`
- `com.deucarian.weapon-systems`
- `com.deucarian.auto-defense`
- `com.deucarian.run-upgrades`
- `com.deucarian.idle-progression`
- `com.deucarian.test-automation`
- `com.deucarian.auto-defense-suite`
- `com.deucarian.template.game.idle-auto-defense`
- `com.deucarian.template.game.survivors`
- `com.deucarian.template.game.movement-fps`

`Install All` installs all missing non-template registered packages in dependency order. Template packages remain visible and individually installable, but global install-all operations skip them so starter projects are not pulled in as normal runtime or system packages. Single install, reinstall, single update, and update-all operations install missing registered Deucarian dependencies first, then install the requested package.

Package IDs remain branded as `com.deucarian.*`. Display names are supplied by the registry and used by the installer UI.
Technical details such as package IDs, selected references, installed references, revisions, and raw update messages are available from each row's Advanced foldout.

## Adding Package Definitions

Package entries are data-driven through registry JSON.

To add or change packages, update the remote registry repository and keep the bundled fallback in sync:

- Remote: `https://github.com/Deucarian/Package-Registry`
- Bundled fallback: `PackageRegistry.json`

The registry schema uses `schemaVersion` 1 and contains:

- `groups`: optional structural graph groups. Each group has `id`, `displayName`, optional `parentGroupId`, `description`, `sortOrder`, `iconKey`, and `styleKey`.
- `id`: the Unity package name, such as `com.deucarian.api`. This must exactly match the target package's `package.json` `name` value.
- `displayName`: the name shown in the installer window.
- `category`: legacy compatibility/package-role metadata used by older registries and List View fallback grouping. The Ecosystem Graph and package details use `groupId` hierarchy as the user-facing structural source of truth.
- `description`: explanatory text shown in the detail pane.
- `stableUrl`: the stable Git URL or UPM identifier passed to `UnityEditor.PackageManager.Client.Add`.
- `developmentUrl`: optional development-channel Git URL or UPM identifier. If this is empty, the Development channel is disabled for that package.
- `dependencies`: package IDs that should be installed before this package is installed, reinstalled, or updated. Integration packages are regular packages with the `Integration` package role and hard dependencies.
- `optionalCompanions`: package IDs shown as optional integrations that should not be installed as required dependencies.
- `groupId`: optional structural Ecosystem Graph group ID. Supported top-level IDs are `infrastructure`, `state-data`, `runtime-services`, `experience-interaction`, `tools-quality`, `integrations`, `gameplay`, `suites`, and `templates`. Registry-provided child groups such as `gameplay-foundations`, `gameplay-systems`, `gameplay-simulation`, `gameplay-frameworks`, `templates-games`, `templates-games-idle-auto-defense`, `templates-games-survivors`, and `templates-games-movement-fps` are supported through `parentGroupId`. If omitted, the graph falls back to `ecosystemGroup`, category, package type, and known package IDs.
- `ecosystemGroup`: legacy optional overview-wheel sector override retained for older registries. Old values are normalized to the current group IDs without creating duplicate visible groups.
- `overviewOrder`: optional positive integer used to order packages within their structural group orbit.
- `integrationTargets`: optional package IDs used to place Integration nodes near the systems they connect.
- `suiteMembers`: optional package IDs used to place Suite nodes near the packages they compose.

Set `stableUrl` and, when available, `developmentUrl` to the UPM identifier or Git URL. Integration packages should also list their dependency package IDs in `dependencies`.

Promoted packages in the bundled registry use stable Git `#main` URLs and development Git `#develop` URLs. For future pre-stable bootstrap packages whose GitHub repository does not yet have `main`, the bundled registry may intentionally set `stableUrl` equal to the verified `developmentUrl`.

When an installed Git package matches a registered channel's normalized repository remote, optional package path, and ref together, including common ref forms such as `#refs/heads/main`, the installer infers the visible channel. Forks, local sources, and mismatched package paths remain Custom even if their branch is named `main` or `develop`.

## Samples and Extras

UPM packages can include `Samples~` folders, but Unity does not import those samples automatically. The installer keeps package installation clean and only imports samples when a sample's `Import` button is clicked.

For installed packages, the installer resolves the package through Unity Package Manager metadata, reads its `package.json`, and displays entries from the `samples` array under the package detail view. Each row shows the sample `displayName`, `description`, import status, and an explicit import action.

Sample imports are explicit. The installer first tries Unity's Package Manager sample import API. Its fallback copies into staging beneath `Library`, validates the complete file set and content, then atomically moves the staged sample into `Assets/Samples/<Package Display Name>/<Version>/<Sample Name>`. Failed or canceled imports clean their staging data and never expose a partial sample at the final destination.

If a sample destination already exists, the installer shows it as already imported and does not overwrite it silently.

## Validation

Run the shared package validator from the repository root:

```powershell
python C:/Repositories/Package-Registry/Tools/deucarian_package_validator.py --registry-root C:/Repositories/Package-Registry --repository-root . --config deucarian-package.json
```

Run the package's EditMode tests in Unity after code or assembly definition changes. The registry tests validate bundled fallback parsing, package ID consistency, dependency references, and the explicit package entries needed for bootstrap installs.

Documentation-only updates should still pass:

```powershell
git diff --check
```

## Update Checks

`Check for Updates` is source-aware. Git-installed packages are compared by installed revision and the latest revision returned by `git ls-remote`. Every registry-installed Deucarian package is reported as a Git source migration using the selected catalog URL; the installer does not query npm metadata or dist-tags for this decision.

For non-installer packages, `Migrate to Git` queues the selected stable or development catalog URL directly. Package Installer never silently migrates itself: `Open Bootstrap` uses `Tools > Deucarian > Bootstrap > Open Bootstrapper`, and if Bootstrap is absent the UI and logs provide the exact Bootstrap Git URL and menu instructions. A source migration remains actionable even when target SHA or `package.json` metadata cannot be fetched.

Legacy npm Package Installer `1.1.12` cannot display that new migration action because its already-loaded assembly predates this flow. Install Bootstrap from `https://github.com/Deucarian/Bootstrap.git#main` (or `#develop`), open `Tools > Deucarian > Bootstrap > Open Bootstrapper`, and let Bootstrap replace only Package Installer after resolving Editor and Logging from Git. Existing `scopedRegistries` entries are detected read-only and are not removed or rewritten.

Unknown Git revisions are shown as "Cannot determine update" while the package remains installed. Missing Git metadata, local/file packages, and embedded packages stay neutral instead of marking the row as failed.

The installer can also check for updates automatically when Unity starts and when the Package Installer window opens. Startup checks wait until Unity is not compiling or updating, run at most once per editor session, cache their statuses, and log actionable results plus a completion summary without opening a modal or installing anything. Window-open checks are throttled so reopening the window does not repeatedly hit remotes. These settings are stored in `EditorPrefs` and can be toggled from the window header.

`Update` and the contextual `Update all (N)` action reuse Unity Package Manager installation through `Client.Add` with the selected channel URL after dependency-first planning has installed any missing registered Deucarian dependencies. After `Check Updates` finds actionable updates, return to the root `Deucarian` / `Ecosystem Overview` context to use `Update all (N)`.

The installer package itself is included in update discovery when it is installed in the current project. Multi-package operations place Package Installer last. After a successful self-update, the installer persists the source assembly version and module identity across domain reloads; if Unity Package Manager resolved the new package while the previous assembly is still running, the row shows `Reload pending` with a `Retry Script Reload` action.

The reload-pending state machine first ships in `1.1.61`; an already-running `1.1.60` assembly cannot retroactively display it during that one transition. If compilation blocks the first `1.1.60` to `1.1.61` reload, recover through Bootstrap or migrate the manifest to the selected Installer Git URL, fix compilation, and reload scripts. Once `1.1.61` has loaded, MVID-based recovery applies to subsequent self-updates even when the package version text is unchanged.

## Progress Display

The installer shows step-based progress for package install, integration install, install-all, single update, update-all, and remove operations.

Progress is counted by package steps because Unity Package Manager does not provide reliable download-byte progress for these Git package operations.

Progress summaries use one chronological activity/result stream with copyable details. They list succeeded, failed, skipped, blocked, and canceled steps so multi-package operations do not rely only on console logs.

Failed prerequisites block their transitive dependents while unrelated requested roots continue. Cancellation lets the active Unity Package Manager request settle but submits no additional requests.

Interrupted plans are stored project-locally beneath `Library/Deucarian/PackageInstaller`. After reload, the installer waits for installed-package and registry refreshes, then offers Resume, Restart, or Discard. Exact saved URLs are reusable only while the registry fingerprint still matches; registry drift requires review of a freshly resolved plan.

## Ecosystem Graph UX

The graph keeps its default toolbar compact and progressively discloses package, group, channel, and attention actions only where they apply. Existing controls wrap at wide, compact, and narrow widths without adding permanent toolbar controls.

Ecosystem Graph is currently the only enabled view. List View remains implemented internally but its toggle is hidden and any stale List View request resolves back to Ecosystem Graph.

Packages, groups, summaries, breadcrumbs, and back targets support keyboard focus. Enter or Space activates the focused target, while Escape clears search before backing out of package or group focus. Hover and keyboard focus share route preview behavior, and related-node previews isolate the route to the selected package. Missing registry relationships are diagnostic nodes, and dense relation sets use adaptive wrapping plus a `+N` overflow summary instead of overlapping cards.

Search preserves the graph's spatial map: root and category positions stay fixed, direct matches and their category path are emphasized, and unrelated results are muted. Installed and Not installed remain visibility filters, but filtered package slots stay reserved so the remaining graph does not reflow. Empty search/filter states provide a contextual recovery action without turning the graph into a new permanent control surface.

## Integration Packages

Integration packages keep the core packages standalone while providing explicit composition packages for projects that want the combined behavior.

Current integration package dependencies:

- UIBinding CoreState Integration depends on UI Binding and Core State.
- Object Loading API Integration depends on Object Loading and API.
- ObjectSelection CoreState Integration depends on Object Selection and Core State.
- Session API Integration depends on Session and API.

Installing a package only requires one click. The installer computes the dependency-first install plan from `PackageDefinition.Dependencies`, skips dependencies that are already installed, fails clearly on unavailable or circular dependencies, and sends the ordered package list to Unity Package Manager.

Integration packages are regular UPM packages, so no scripting define symbols are required for these integration installs.

### Migration From Bridge IDs

The Package Installer now shows only the Integration package IDs. Remove or replace old bridge IDs in Unity `Packages/manifest.json`:

- `com.deucarian.session.api-bridge` -> `com.deucarian.session.api-integration`
- `com.deucarian.object-loading.api-bridge` -> `com.deucarian.object-loading.api-integration`
- `com.deucarian.ui-binding.core-state-bridge` -> `com.deucarian.ui-binding.core-state-integration`
- `com.deucarian.object-selection.core-state-bridge` -> `com.deucarian.object-selection.core-state-integration`

Current Integration repository URLs use Integration repository names. The old Bridge package IDs above remain listed only for manifest migration.

When removing a package, the installer warns if another installed package depends on it. It resolves reverse dependencies from current Unity Package Manager/package-lock data first and uses registry relationships only as a fallback. Removal remains available after explicit confirmation so you can resolve unusual project states, but remove the dependents first unless the breakage is intentional.

## Public API

This package is editor-only and exposes no runtime API for game code.

The user-facing entry point is the Unity menu item:

```text
Tools/Deucarian/Package Installer
```

The implementation is split into internal editor classes:

- `PackageInstallerWindow`: UI Toolkit editor shell and coordination, with IMGUI containers for the package list and graph details.
- `PackageInstallerStateRepository`: project-scoped selected-channel state shared with Bootstrap and manifest/package-lock invalidation signatures.
- `PackageRegistryProvider`, `PackageRegistryLoader`, and `PackageRegistryValidator`: bundled and remote registry loading.
- `PackageDefinition`, `PackageChannel`, and `PackageExtraDefinition`: installer data models.
- `PackageInstallService`: Unity Package Manager install, update, and remove operations.
- `PackageDependencyInstaller`: dependency-first package install sequencing.
- `PackageDetectionService`: installed package detection through `Client.List`.
- `PackageUpdateCheckService`: Git revision comparison for installed Git packages.
- `PackageSampleImportService`: explicit sample import through Unity sample APIs or a safe copy fallback.

## Why Editor-Only

This package exists only to help developers install and compose packages inside the Unity Editor. It creates no runtime assembly, has no `Runtime` folder, and should not be referenced by game code.

Keeping the installer editor-only ensures:

- Core State, UI Binding, API, Object Loading, and Session remain standalone.
- Projects do not ship installer code in builds.
- No package gains a runtime dependency on this installer.

## Versioning

Current package version: `1.1.64`.

Branch strategy:

- `main`: stable installer branch.
- `develop`: development installer branch.

Use branch refs for active development and stable release tags when tags are available.

Channel-visible package changes must bump the package's `package.json` version. If `develop` and
`main` point to different package content, their package versions should not misleadingly appear
identical unless the package content is intentionally unchanged. Package Installer compares both
the installed/target revision and the installed/target package version; when a channel switch is
available but the package version did not change, it surfaces a warning so maintainers can catch
missed version bumps before publishing.

## Validation Notes

The installer uses:

- `UnityEditor.PackageManager.Client.Add` for package installation and update.
- `UnityEditor.PackageManager.Client.Remove` for package removal.
- `UnityEditor.PackageManager.Client.List` for installed-package detection.

After installing, updating, or removing a package, the installer refreshes installed-package state so entries show their current status.

## Troubleshooting

- Package list looks stale: close and reopen the Package Installer window, then refresh the remote registry if network access is available.
- Install or update is blocked: check the selected channel, package dependency list, and Unity Package Manager console output for the first failed package in the dependency-first plan.
- A removal warning lists installed dependents: remove those dependents first, or deliberately confirm removal if you are repairing an unusual project state.
- Update status is unknown: the installed package may be embedded, local/file-based, missing Git metadata, or unavailable from the current network.
- Package Installer shows `Reload pending`: fix any Console compilation errors, then use `Retry Script Reload` so Unity can load the resolved installer assembly.
- A registry-installed Package Installer shows `Source migration available`: install/open Bootstrap and use `Tools > Deucarian > Bootstrap > Open Bootstrapper`; Package Installer does not self-migrate silently.
- Legacy npm `1.1.12`, or a compile-blocked first hop from `1.1.60`, cannot expose the new recovery UI from its old running assembly: use Bootstrap or the selected Git manifest URL once, then reload scripts.
- Samples do not import: confirm the package is installed, then import the sample explicitly from the package details panel or Unity Package Manager.

## Limitations

- This package is editor-only. It has no `Runtime` folder and should not be referenced by game code.
- The installer uses a bundled registry first and a remote registry refresh when available; it does not auto-discover GitHub repositories.
- Update comparisons use Git references; registry-installed Deucarian packages are offered an explicit migration to their catalog Git URL.
- The installer cannot know download-byte progress for Git packages.
- Sample import avoids silent overwrite; there is no overwrite UI in this version.

## Architecture / Contributor Notes

- [AGENTS.md](AGENTS.md) contains repository-specific ownership and Codex guidance.
- Deucarian architecture rules live in [Package Registry](https://github.com/Deucarian/Package-Registry/blob/develop/ARCHITECTURE.md).
- Capability ownership is tracked in [CAPABILITY_OWNERSHIP.md](https://github.com/Deucarian/Package-Registry/blob/develop/CAPABILITY_OWNERSHIP.md).

## License

See [LICENSE.md](LICENSE.md).
