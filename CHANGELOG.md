# Changelog

## 1.1.14 - 2026-06-16

- Added a dismissible first-run notice that recommends Deucarian Bootstrap for setup and repair.
- Documented Bootstrap as the official first-time setup path while keeping direct Package Installer installs supported for advanced/manual setups.
- Updated the Package Installer window footer version to match the package version.

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
