# JorisHoef Package Installer

JorisHoef Package Installer is a small editor-only Unity Package Manager package that adds a custom installer window for JorisHoef packages.

Open it from:

`Tools > JorisHoef > Package Installer`

The installer can install standalone packages, bridge packages, and explicitly declared package samples without making this package a runtime dependency of any other package.

## Installation

Add the installer through Unity Package Manager with a Git URL:

```json
{
  "dependencies": {
    "com.jorishoef.package-installer": "https://github.com/JorisHoef/JorisHoef.Package-Installer.git#main"
  }
}
```

You can also use Unity's Package Manager window:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter the installer Git URL.
4. Open `Tools > JorisHoef > Package Installer`.

## Package Registry

Package entries are loaded from a registry instead of being hardcoded in the installer window.

The installer loads the bundled `PackageRegistry.json` first so it works offline, then tries to refresh from:

`https://raw.githubusercontent.com/JorisHoef/Package-Registry/main/packages.json`

If the remote registry succeeds and validates, the window uses it. If it fails, the bundled registry stays active and the header shows that the remote registry failed.

The current registry includes these package entries:

- Core: Core State, API Helper, Session Helper
- UI: Generic UI Items
- Bridge: Generic UI Items + Core State Bridge, Session Helper + API Helper Bridge

Bridge packages are first-class UPM packages with their own package IDs:

- `com.jorishoef.core-state`
- `com.jorishoef.api-helper`
- `com.jorishoef.session-helper`
- `com.jorishoef.generic-ui-items`
- `com.jorishoef.generic-ui-items.core-state-bridge`
- `com.jorishoef.session-helper.api-helper-bridge`

`Install All` installs all missing registered packages in dependency order. Installing one bridge package automatically installs its missing dependencies first, then installs the bridge.

Package IDs remain branded as `com.jorishoef.*`. Display names are supplied by the registry and used by the installer UI.
Technical details such as package IDs, selected references, installed references, revisions, and raw update messages are available from each row's Advanced foldout.

## Adding Package Definitions

Package entries are data-driven through registry JSON.

To add or change packages, update the remote registry repository and keep the bundled fallback in sync:

- Remote: `https://github.com/JorisHoef/Package-Registry`
- Bundled fallback: `PackageRegistry.json`

The registry schema uses `schemaVersion` 1 and contains:

- `id`: the Unity package name, such as `com.jorishoef.api-helper`.
- `displayName`: the name shown in the installer window.
- `category`: grouping shown in the sidebar. Core, UI, and Bridge are ordered first; unknown categories are shown alphabetically after them.
- `description`: explanatory text shown in the detail pane.
- `stableUrl`: the stable Git URL or UPM identifier passed to `UnityEditor.PackageManager.Client.Add`.
- `developmentUrl`: optional development-channel Git URL or UPM identifier. If this is empty, the Development channel is disabled for that package.
- `dependencies`: package IDs that should be installed before this package is installed. Bridge packages are just packages in the `Bridge` category with dependencies.

Set `stableUrl` and, when available, `developmentUrl` to the UPM identifier or Git URL. Bridge packages should also list their dependency package IDs in `dependencies`.

When an installed Git package can be matched to `#main` or `#develop`, the installer infers the visible channel from the installed package reference. If the installed reference does not match a known channel, the row shows a Custom channel until the user selects Stable or Development.

## Samples And Extras

UPM packages can include `Samples~` folders, but Unity does not import those samples automatically. The installer keeps package installation clean and only imports samples when an explicit sample extra is available and clicked.

The current registry schema does not declare sample extras. Packages without declared extras show no fake samples.

Sample imports are explicit. The installer first tries Unity's Package Manager sample import API, then falls back to copying from the installed package's `Samples~` folder into `Assets/Samples/<Package Display Name>/<Sample Name>`.

If a sample destination already exists, the installer shows it as already imported and does not overwrite it silently.

## Update Checks

`Check for Updates` compares installed registry packages against the selected Stable or Development channel. Git packages are compared by installed revision and the latest revision returned by `git ls-remote`.

Unknown revisions, missing Git, network failures, local/file packages, and non-Git UPM identifiers are reported as check failures instead of blocking the installer.

`Update` and `Update All Installed Packages` reuse Unity Package Manager installation through `Client.Add` with the selected channel URL.

TODO: installer self-update is intentionally out of scope for this version.

## Progress Display

The installer shows step-based progress for package install, bridge install, install-all, single update, update-all, and remove operations.

Progress is counted by package steps because Unity Package Manager does not provide reliable download-byte progress for these Git package operations.

Progress summaries list succeeded, failed, and skipped package steps so multi-package operations do not rely only on console logs.

## Bridge Packages

Bridge packages keep the core packages standalone while providing explicit composition packages for projects that want the combined behavior.

Current bridge package dependencies:

- GenericUIItems CoreState Bridge depends on Generic UI Items and Core State.
- SessionHelper APIHelper Bridge depends on Session Helper and API Helper.

Installing a bridge only requires one click. The installer computes the dependency-first install plan from `PackageDefinition.Dependencies` and sends that ordered package list to Unity Package Manager.

Bridge packages are regular UPM packages, so no scripting define symbols are required for these bridge installs.

When removing a package, the installer warns and disables removal if another installed registered package depends on it. Remove the dependent bridge package first to avoid silently breaking the project.

## Why Editor-Only

This package exists only to help developers install and compose packages inside the Unity Editor. It creates no runtime assembly, has no `Runtime` folder, and should not be referenced by game code.

Keeping the installer editor-only ensures:

- Core State, Generic UI Items, API Helper, and Session Helper remain standalone.
- Projects do not ship installer code in builds.
- No package gains a runtime dependency on this installer.

## Validation Notes

The installer uses:

- `UnityEditor.PackageManager.Client.Add` for package installation.
- `UnityEditor.PackageManager.Client.Remove` for package removal.
- `UnityEditor.PackageManager.Client.List` for installed-package detection.

After installing a package, the installer refreshes installed-package state so installed entries show as installed.
