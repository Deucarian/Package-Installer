# Deucarian Package Installer Agent Notes

Package ID: `com.deucarian.package-installer`
Repository: `Deucarian/Package-Installer`

Follow the canonical Deucarian governance docs in [Package Registry](https://github.com/Deucarian/Package-Registry/blob/develop/ARCHITECTURE.md), especially capability ownership and dependency rules.

## Ownership

This package owns:

- Package install/update/remove UI, registry channel handling, dependency-first install flow, and package-specific ecosystem visualization.

Registered capabilities:
- `package-management`

This package must not own:

- Generic graph framework, generic UI framework, registry governance source, or package runtime behavior.

## Dependencies

Allowed dependency shape:

- May depend on Editor and Logging for installer editor shell and logging.

Required dependencies and why:

- `com.deucarian.editor`: shared editor shell/resources.
- `com.deucarian.logging`: package logging facade and diagnostics output.

Optional/version-defined dependencies:

- None.

Architecture exceptions:

- None.

## Policies

- Logging: Use Logging for installer diagnostics; no direct Unity Debug calls.
- Common: Add Common only if installer production code directly uses approved runtime cleanup.
- Editor UI: Use shared Editor shell/resources instead of local copies.
- Diagnostics: Do not become Diagnostics; installer may display package status only.
- Testing: Keep graph/installer tests package-specific and preserve install/update/remove behavior.

## Package Sample Rules

- Treat package samples as product-quality playable demos, not disconnected prototypes.
- Do not create prototypes outside the package sample area.
- Prefer improving the existing sample scene over creating a new scene.
- The sample scene is the source of truth: pressing Play must demonstrate the feature, or the task is not done.
- Wire every new gameplay script, prefab, data asset, UI element, and effect into the sample before completion.
- Keep samples generic and reusable unless the task explicitly asks for a themed game.
- Use behavior-based sample content names such as `Enemy_Basic`, `Enemy_Fast`, `Attack_Rapid`, and `Upgrade_Damage`.
- Do not overbuild architecture before the sample is playable; a clean framework without a playable sample is a failed task.
- If a package sample is read-only, create or update the imported editable sample under `Assets/Samples` and document that path.

## Unity Package Layout

- Do not edit files under `Library/PackageCache`, generated `Library` content, or imported third-party package source.
- Put package-owned reusable code in the package runtime/editor folders.
- Put playable demo scenes, prefabs, UI, data assets, and content in the package sample area.
- If a sample must be imported into `Assets/Samples` for editing or testing, make that explicit in the handoff.

## Validation

Run the shared validator before committing:

```powershell
python C:/Repositories/Package-Registry/Tools/deucarian_package_validator.py --registry-root C:/Repositories/Package-Registry --repository-root . --config deucarian-package.json
```

Also run existing repository tests when changing code or asmdefs. Documentation-only updates should still run `git diff --check`.

## Codex Guidance

- Inspect current files before changing anything.
- Work on `develop`; do not edit or merge `main` unless the task is promotion-only.
- Do not edit `Library/PackageCache`.
- Do not guess package versions or dependency versions.
- Do not add package dependencies casually; update asmdefs, `package.json`, `deucarian-package.json`, Package Registry, and fallback catalogs together when a dependency is truly required.
- Do not create local copies of shared helpers.
- Keep commits focused and report exactly what changed and what was validated.

## Before Adding Code

- Confirm the change fits this package's ownership boundary.
- Reuse existing local patterns and helpers.
- Avoid broad refactors without audit support.
- Preserve runtime/editor behavior unless the task explicitly asks to change it.

## Before Adding A Dependency

- Is the capability already owned by that package?
- Is it used by production code, editor code, sample code, or tests?
- Does the asmdef reference match `package.json`?
- Does `deucarian-package.json` need updating?
- Does Package Registry need updating?
- Does Package Installer fallback catalog need updating?
- Does Bootstrap fallback catalog need updating?
- Are exact versions propagated without guessing?

## Before Adding A Helper

- Is this package the capability owner?
- Is this behavior repeated in at least three production packages?
- Is there an existing owner package?
- Should this remain local?
- Has the audit been updated?

## Debug And Unity Object Lifetime

- Use Deucarian Logging for package diagnostics; direct Unity Debug calls are forbidden.
- Do not copy Common lifetime helpers. Add Common only if production code directly owns transient Unity object cleanup.
- Test fixture teardown may use `DestroyImmediate` directly.
