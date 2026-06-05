# JorisHoef Package Installer

JorisHoef Package Installer is a small editor-only Unity Package Manager package that adds a custom installer window for JorisHoef packages.

Open it from:

`Tools > JorisHoef > Package Installer`

The installer can install standalone packages and opt into package integrations without making this package a runtime dependency of any other package.

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

## Included Packages

The first version knows about these package entries:

- Core State
- Generic UI Items
- API Helper
- Session Helper
- Generic UI Items + Core State integration
- Session Helper + API Helper integration

`Install All` installs the standalone packages and enables all integration define symbols.

Package IDs remain branded as `com.jorishoef.*`. Display names are clean user-facing labels used only by the installer UI.
Technical details such as package IDs, selected references, installed references, revisions, and raw update messages are available from each row's Advanced foldout.

## Adding Package Definitions

Package entries are data-driven through `PackageRegistry`.

To add or change packages, edit:

`Editor/PackageRegistry.cs`

Each `PackageDefinition` contains:

- `displayName`: the name shown in the installer window.
- `packageId`: the Unity package name, such as `com.jorishoef.api-helper`.
- `stableUrl`: the stable Git URL or UPM identifier passed to `UnityEditor.PackageManager.Client.Add`.
- `developmentUrl`: optional development-channel Git URL or UPM identifier. If this is empty, Development falls back to Stable.
- `description`: the explanatory text shown in the UI.
- `displayVersion`: optional human-readable version text shown in the UI.
- `extras`: optional package samples or extras shown after the package is installed.
- `dependencies`: package IDs that should be installed before an integration is enabled.
- `scriptingDefineSymbols`: optional symbols added to the selected build target group.

For regular packages, set `stableUrl` and, when available, `developmentUrl` to the UPM identifier or Git URL. For integration entries that only compose other packages, leave the URL fields empty and list the required packages in `dependencies`.

When an installed Git package can be matched to `#main` or `#develop`, the installer infers the visible channel from the installed package reference. If the installed reference does not match a known channel, the row shows a Custom channel until the user selects Stable or Development.

## Samples And Extras

UPM packages can include `Samples~` folders, but Unity does not import those samples automatically. The installer keeps package installation clean and shows a Samples foldout only after an installed package has declared extras.

Sample imports are explicit. The installer first tries Unity's Package Manager sample import API, then falls back to copying from the installed package's `Samples~` folder into `Assets/Samples/<Package Display Name>/<Sample Name>`.

If a sample destination already exists, the installer shows it as already imported and does not overwrite it silently.

## Update Checks

`Check for Updates` compares installed registry packages against the selected Stable or Development channel. Git packages are compared by installed revision and the latest revision returned by `git ls-remote`.

Unknown revisions, missing Git, network failures, local/file packages, and non-Git UPM identifiers are reported as check failures instead of blocking the installer.

`Update` and `Update All Installed Packages` reuse Unity Package Manager installation through `Client.Add` with the selected channel URL.

TODO: installer self-update is intentionally out of scope for this version.

## Progress Display

The installer shows step-based progress for package install, integration install, install-all, single update, and update-all operations.

Progress is counted by package/integration steps because Unity Package Manager does not provide reliable download-byte progress for these Git package operations.

Progress summaries list succeeded, failed, and skipped package steps so multi-package operations do not rely only on console logs.

## Integrations

Integrations keep packages standalone. The installer does not add compile-time references between packages by itself.

An integration definition does two things:

1. Installs its required package dependencies if they are not already installed.
2. Adds the integration's scripting define symbols to the active build target group.

The installer only adds symbols. It never removes user symbols.

The Build Target Group selector in the window controls which build target group receives integration symbols.

Current integration symbols:

- `GENERIC_UI_ITEMS_CORE_STATE`
- `SESSION_HELPER_APIHELPER`

Packages that provide optional integration code should gate that code behind the same symbols in their own asmdefs or source.

## Why Editor-Only

This package exists only to help developers install and compose packages inside the Unity Editor. It creates no runtime assembly, has no `Runtime` folder, and should not be referenced by game code.

Keeping the installer editor-only ensures:

- Core State, Generic UI Items, API Helper, and Session Helper remain standalone.
- Projects do not ship installer code in builds.
- No package gains a runtime dependency on this installer.

## Validation Notes

The installer uses:

- `UnityEditor.PackageManager.Client.Add` for package installation.
- `UnityEditor.PackageManager.Client.List` for installed-package detection.
- `PlayerSettings` scripting define APIs for integration symbols.

After installing a package, the installer refreshes installed-package state so installed entries show as installed.
