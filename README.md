# JorisHoef Package Installer

## Overview

JorisHoef Package Installer is an editor-only Unity Package Manager package that adds a custom installer window for the JorisHoef UPM package ecosystem.

Open it from:

```text
Tools > JorisHoef > Package Installer
```

The installer can install standalone packages, choose Stable or Development channels per package, check for Git updates, enable optional integration define symbols, and explicitly import package samples after a package is installed.

Package ID: `com.jorishoef.package-installer`

## Installation

Add the installer through Unity Package Manager with a Git URL:

```json
{
  "dependencies": {
    "com.jorishoef.package-installer": "https://github.com/JorisHoef/JorisHoef.Package-Installer.git#main"
  }
}
```

For development builds, use:

```json
"com.jorishoef.package-installer": "https://github.com/JorisHoef/JorisHoef.Package-Installer.git#develop"
```

You can also use Unity's Package Manager window:

1. Open `Window > Package Manager`.
2. Select `+ > Add package from git URL...`.
3. Enter the installer Git URL.
4. Open `Tools > JorisHoef > Package Installer`.

The package requires Unity `2021.3` or newer and has no package dependencies.

## Core Concepts

Package entries are data-driven in `Editor/PackageRegistry.cs`.

Each standalone package definition contains:

- Display name
- Package ID
- Stable URL
- Development URL when verified
- Description
- Optional display version
- Optional sample/extras metadata

Each integration definition contains:

- Display name
- Integration package ID used only by the installer
- Required package IDs
- Scripting define symbols to add after dependencies are installed and detected

The installer keeps target packages standalone. It does not add runtime dependencies between packages and it does not create a runtime assembly.

## Public API

This package is editor-only and exposes no runtime API for game code.

The user-facing entry point is the Unity menu item:

```text
Tools/JorisHoef/Package Installer
```

The implementation is split into internal editor classes:

- `PackageInstallerWindow`: IMGUI window and coordination.
- `PackageRegistry`: local package and integration definitions.
- `PackageDefinition`, `PackageChannel`, and `PackageExtraDefinition`: installer data models.
- `PackageInstallService`: Unity Package Manager install and update operations through `Client.Add`.
- `PackageDetectionService`: installed package detection through `Client.List`.
- `PackageUpdateCheckService`: Git revision comparison for installed Git packages.
- `ScriptingDefineService`: selected build target group define-symbol updates.
- `IntegrationInstaller`: dependency install sequencing and integration symbol gating.
- `PackageSampleImportService`: explicit sample import through Unity sample APIs or a safe copy fallback.

## Packages

The registry currently knows about these standalone packages:

- `Core State` (`com.jorishoef.core.state`)
- `Generic UI Items` (`com.jorishoef.generic-ui-items`)
- `API Helper` (`com.jorishoef.api-helper`)
- `Session Helper` (`com.jorishoef.session-helper`)

Each package row has its own Stable/Development selector. Development falls back to Stable when a development URL is missing. Installed packages are shown as installed regardless of channel; the installer does not try to infer installed package versions beyond the current update-check metadata.

Technical details such as package IDs, selected references, installed references, revisions, and raw update messages are available from each row's `Advanced` foldout.

## Samples

The Package Installer itself has no `Samples~` folder.

It can import samples from installed packages when their `PackageDefinition` declares extras. The current registry declares these sample extras:

- Core State: `Standalone Repository Selection`
- Generic UI Items: `Basic Usage`
- API Helper: `API Helper Example Scene`
- Session Helper: `Basic Session Usage`
- Session Helper: `APIHelper Integration`

Samples are hidden until the package is installed. Import is explicit; package installation does not auto-import samples.

Import behavior:

1. The installer first tries Unity's `UnityEditor.PackageManager.UI.Sample` API.
2. If that API is unavailable or cannot find the sample, it copies from the installed package's `Samples~` folder into `Assets/Samples/<Package Display Name>/<Sample Name>`.
3. Existing destinations are treated as already imported and are not overwritten silently.

## Integrations

The registry currently exposes two integration buttons:

- `Generic UI Items + Core State integration`
- `Session Helper + API Helper integration`

Integration install flow:

1. Install required packages that are missing.
2. Refresh installed-package detection.
3. Add the integration scripting define symbols only after required packages are installed and detected.

Current integration symbols:

- `GENERIC_UI_ITEMS_CORE_STATE`
- `SESSION_HELPER_APIHELPER`

The installer only adds symbols; it never removes user symbols.

`SESSION_HELPER_APIHELPER` enables the real optional APIHelper adapter inside Session Helper when APIHelper is installed. `GENERIC_UI_ITEMS_CORE_STATE` is available for packages or project code that gate their own composition code behind that symbol; Generic UI Items does not currently include a Core State adapter assembly.

## Update Checking

`Check for Updates` compares installed registry packages against each package's selected Stable or Development channel.

For Git package URLs, update checks compare:

- Installed revision from Unity package metadata and lock files.
- Latest branch revision from `git ls-remote`.

`Update` and `Update All Installed Packages` reuse Unity Package Manager installation through `Client.Add` with the selected channel URL. `Update All` lists exactly which installed packages have available updates before running.

Local/file packages, non-Git identifiers, missing Git, unknown revisions, and network failures are reported as check failures instead of blocking installer use.

## Progress

The installer shows step-based progress for:

- Installing one package
- Installing integrations
- Installing all packages
- Updating one package
- Updating all installed packages with available updates
- Importing samples

Progress is counted by package, integration, or sample steps because Unity Package Manager does not provide reliable download-byte progress for Git package operations.

## Versioning

Current package version: `0.1.0`.

Branch strategy:

- `main`: stable installer branch.
- `develop`: development installer branch.

Use branch refs for active development and stable release tags when tags are available.

## Limitations

- This package is editor-only. It has no `Runtime` folder and should not be referenced by game code.
- Package definitions are local C# data in `PackageRegistry`, not remote manifests.
- Only Git branch update checks are supported today.
- The installer cannot know download-byte progress for Git packages.
- Sample import avoids silent overwrite; there is no overwrite UI in this version.
- Define symbols only have an effect when installed packages or project code actually contain code gated by those symbols.
