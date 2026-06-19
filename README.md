# Deucarian Package Installer

## Overview

Deucarian Package Installer is a small editor-only Unity Package Manager package that adds a custom installer window for Deucarian packages.

Open it from:

```text
Tools > Deucarian > Package Installer
```

## Deucarian Menu

The installer keeps its Unity Editor entry point at `Tools > Deucarian > Package Installer`. This package does not own the Theming, Logging, Object Loading, Session, or Selection menu groups; those packages provide their own package-local menu items under the shared `Tools > Deucarian` menu.

The UI Toolkit preview base is a development-only window available from:

```text
Tools > Deucarian > Development > Package Installer Preview
```

The preview is separate from the real installer and does not replace the existing production entry point.

The installer can install standalone packages, integration packages, and explicitly declared package samples without making this package a runtime dependency of any other package.

Package ID: `com.deucarian.package-installer`

## Installation

Add the installer through Unity Package Manager with a Git URL:

```json
{
  "dependencies": {
    "com.deucarian.package-installer": "https://github.com/Deucarian/Package-Installer.git#main"
  }
}
```

For development builds, use:

```json
"com.deucarian.package-installer": "https://github.com/Deucarian/Package-Installer.git#develop"
```

You can also use Unity's Package Manager window:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter the installer Git URL.
4. Open `Tools > Deucarian > Package Installer`.

The package requires Unity `2021.3` or newer and depends on `com.deucarian.editor` and `com.deucarian.logging`.

## Logging

This package uses `com.deucarian.logging` for diagnostics and `com.deucarian.editor` for shared Deucarian editor chrome, styles, icons, and status badges.

Package Installer diagnostics use stable package categories: `PackageInstaller`, `PackageInstaller.Registry`, `PackageInstaller.Install`, `PackageInstaller.Samples`, and `PackageInstaller.UpdateChecks`. Configure Deucarian Logging filters by category and level to isolate registry loading, install/remove operations, sample imports, or update checks. Entries flow through the shared ring buffer for recent-diagnostic inspection and remain compatible with future telemetry sinks.

## Usage

Use the installer window to install standalone packages, install packages with their registered dependencies, import package samples explicitly, and check installed Git packages for updates.

## Package Registry

Package entries are loaded from a registry instead of being hardcoded in the installer window.

The installer loads the bundled `PackageRegistry.json` first so it works offline, then tries to refresh from:

`https://raw.githubusercontent.com/Deucarian/Package-Registry/main/packages.json`

If the remote registry succeeds and validates, the window uses it. If it fails, the bundled registry stays active and the header shows that the remote registry failed.

The header `Refresh` and `Check Updates` buttons refetch the remote registry, so newly registered packages and package reference changes can appear without restarting Unity.

Remote registry validation also checks each package entry against the target package's `package.json` name so installed-package detection uses Unity's exact package IDs. If a target manifest cannot be fetched, the validation message includes the exact `package.json` URL that failed.

The current bundled fallback registry includes these Ecosystem Graph groups:

- Infrastructure: Editor, Logging
- State & Data: Core State
- Runtime Services: API, Session, Object Loading
- Experience & Interaction: UI Binding, UI Flow, Theming, Object Selection
- Tools & Quality: Package Installer, Diagnostics
- Integrations: UI Binding + Core State Integration, Object Loading API Integration, Object Selection + Core State Integration, Session + API Integration
- Suites: Selection Suite

Registered packages are first-class UPM packages with their own package IDs:

- `com.deucarian.core-state`
- `com.deucarian.api`
- `com.deucarian.logging`
- `com.deucarian.object-loading`
- `com.deucarian.session`
- `com.deucarian.ui-binding`
- `com.deucarian.ui-flow`
- `com.deucarian.theming`
- `com.deucarian.object-selection`
- `com.deucarian.editor`
- `com.deucarian.ui-binding.core-state-integration`
- `com.deucarian.object-loading.api-integration`
- `com.deucarian.object-selection.core-state-integration`
- `com.deucarian.session.api-integration`
- `com.deucarian.selection-suite`
- `com.deucarian.diagnostics`
- `com.deucarian.package-installer`

`Install All` installs all missing registered packages in dependency order. Single install, reinstall, single update, and update-all operations install missing registered Deucarian dependencies first, then install the requested package.

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
- `category`: grouping shown in the sidebar. Core, UI, World, Integration, and Suites are ordered first; unknown categories are shown alphabetically after them.
- `description`: explanatory text shown in the detail pane.
- `stableUrl`: the stable Git URL or UPM identifier passed to `UnityEditor.PackageManager.Client.Add`.
- `developmentUrl`: optional development-channel Git URL or UPM identifier. If this is empty, the Development channel is disabled for that package.
- `dependencies`: package IDs that should be installed before this package is installed, reinstalled, or updated. Integration packages are just packages in the `Integration` category with dependencies.
- `optionalCompanions`: package IDs shown as optional integrations that should not be installed as required dependencies.
- `groupId`: optional structural Ecosystem Graph group ID. Supported top-level IDs are `infrastructure`, `state-data`, `runtime-services`, `experience-interaction`, `tools-quality`, `integrations`, and `suites`. If omitted, the graph falls back to `ecosystemGroup`, category, package type, and known package IDs.
- `ecosystemGroup`: legacy optional overview-wheel sector override retained for older registries. Old values are normalized to the current group IDs without creating duplicate visible groups.
- `overviewOrder`: optional positive integer used to order packages within their structural group orbit.
- `integrationTargets`: optional package IDs used to place Integration nodes near the systems they connect.
- `suiteMembers`: optional package IDs used to place Suite nodes near the packages they compose.

Set `stableUrl` and, when available, `developmentUrl` to the UPM identifier or Git URL. Integration packages should also list their dependency package IDs in `dependencies`.

When an installed Git package can be matched to `#main` or `#develop`, including common forms such as `#refs/heads/main`, the installer infers the visible channel from the installed package reference. If the installed reference does not match a known channel, the row shows a Custom channel until the user selects Stable or Development.

## Samples and Extras

UPM packages can include `Samples~` folders, but Unity does not import those samples automatically. The installer keeps package installation clean and only imports samples when a sample's `Import` button is clicked.

For installed packages, the installer resolves the package through Unity Package Manager metadata, reads its `package.json`, and displays entries from the `samples` array under the package detail view. Each row shows the sample `displayName`, `description`, import status, and an explicit import action.

Sample imports are explicit. The installer first tries Unity's Package Manager sample import API, then falls back to a bounded copy from the installed package's `Samples~` folder into `Assets/Samples/<Package Display Name>/<Version>/<Sample Name>`.

If a sample destination already exists, the installer shows it as already imported and does not overwrite it silently.

## Tests

Run the package's EditMode tests in Unity. The registry tests validate bundled fallback parsing, package ID consistency, dependency references, and the explicit package entries needed for bootstrap installs.

## Update Checks

`Check for Updates` is source-aware. Git-installed packages are compared by installed revision and the latest revision returned by `git ls-remote`. Scoped-registry/npm-installed packages are compared by installed package version and the npmjs `latest` dist-tag.

Unknown installed revisions or versions are shown as "Cannot determine update" while the package remains installed. Missing Git metadata, unavailable registry metadata, local/file packages, and embedded packages stay neutral instead of marking the row as failed.

The installer can also check for updates automatically when Unity starts and when the Package Installer window opens. Startup checks run at most once per editor session, and window-open checks are throttled so reopening the window does not repeatedly hit remotes. These settings are stored in `EditorPrefs` and can be toggled from the window header.

`Update` and `Update All Installed Packages` reuse Unity Package Manager installation through `Client.Add` with the selected channel URL after dependency-first planning has installed any missing registered Deucarian dependencies.

The installer package itself is included in update discovery when it is installed in the current project.

## Progress Display

The installer shows step-based progress for package install, integration install, install-all, single update, update-all, and remove operations.

Progress is counted by package steps because Unity Package Manager does not provide reliable download-byte progress for these Git package operations.

Progress summaries list succeeded, failed, and skipped package steps so multi-package operations do not rely only on console logs.

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

The Git repository URLs still use their existing `Bridge.git` names until those GitHub repositories are manually renamed.

When removing a package, the installer warns and disables removal if another installed registered package depends on it. Remove the dependent integration package first to avoid silently breaking the project.

## Public API

This package is editor-only and exposes no runtime API for game code.

The user-facing entry point is the Unity menu item:

```text
Deucarian/Package Installer
```

The implementation is split into internal editor classes:

- `PackageInstallerWindow`: IMGUI window and coordination.
- `PackageInstallerPreviewWindow`: UI Toolkit preview window for the future ecosystem browser direction.
- `PackageRegistryProvider`, `PackageRegistryLoader`, and `PackageRegistryValidator`: bundled and remote registry loading.
- `PackageDefinition`, `PackageChannel`, and `PackageExtraDefinition`: installer data models.
- `PackageInstallService`: Unity Package Manager install, update, and remove operations.
- `PackageDependencyInstaller`: dependency-first package install sequencing.
- `PackageDetectionService`: installed package detection through `Client.List`.
- `PackageUpdateCheckService`: Git revision comparison for installed Git packages.
- `PackageSampleImportService`: explicit sample import through Unity sample APIs or a safe copy fallback.

## UI Toolkit Preview Assets

Package-specific UI Toolkit files for the preview live in:

- `Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uxml`
- `Editor/UI/PackageInstaller/PackageInstallerPreviewWindow.uss`

Shared Deucarian UI assets live in `com.deucarian.editor`, not in this package:

- Logo: `com.deucarian.editor/Editor/Assets/Logos/DeucarianPlaceholderLogo.png`
- Package Installer hero: `com.deucarian.editor/Editor/Assets/Images/DeucarianPackageInstallerPlaceholderHero.png`
- Default package icon: `com.deucarian.editor/Editor/Assets/Icons/DeucarianPackagePlaceholderIcon.png`

Package Installer loads those shared assets through `DeucarianEditorUIResources` and keeps only package-specific UXML/USS in this package.

## Why Editor-Only

This package exists only to help developers install and compose packages inside the Unity Editor. It creates no runtime assembly, has no `Runtime` folder, and should not be referenced by game code.

Keeping the installer editor-only ensures:

- Core State, UI Binding, API, Object Loading, and Session remain standalone.
- Projects do not ship installer code in builds.
- No package gains a runtime dependency on this installer.

## Versioning

Current package version: `1.1.31`.

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

## Limitations

- This package is editor-only. It has no `Runtime` folder and should not be referenced by game code.
- The installer uses a bundled registry first and a remote registry refresh when available; it does not auto-discover GitHub repositories.
- Only Git branch update checks are supported today.
- The installer cannot know download-byte progress for Git packages.
- Sample import avoids silent overwrite; there is no overwrite UI in this version.

## License

See [LICENSE.md](LICENSE.md).
