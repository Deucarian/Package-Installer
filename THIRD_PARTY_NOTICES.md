# Third-party notices

This notice describes the dependency and distribution inventory for `com.deucarian.package-installer` `1.1.61`. It does not replace the repository's [MIT license](LICENSE.md), and it does not grant rights to software supplied separately.

## Review basis

The reviewed baseline is `origin/main` commit `4843b80a03dedafc0c4755d8873b4bd06de47303`. Its `npm pack --dry-run` inventory contained 135 package files. The tracked and packed inventories were checked for common vendor/third-party directories, compiled binaries and archives, Git submodules, Git LFS pointers, separate license markers, and media/font assets.

That inventory identified no files marked or located as vendored third-party source, no compiled binary/archive candidates, no submodules, no LFS pointers, and no media/font asset candidates. The dependencies below are resolved separately by Unity Package Manager; they are not copied into this repository's package archive.

## Deucarian dependencies (not third-party)

| Package | Version | Relationship | License |
|---|---:|---|---|
| `com.deucarian.editor` | `1.0.0` | Direct package dependency | [MIT](https://github.com/Deucarian/Editor/blob/main/LICENSE.md) |
| `com.deucarian.logging` | `1.0.1` | Direct package dependency | [MIT](https://github.com/Deucarian/Logging/blob/main/LICENSE.md) |

No non-Deucarian package dependency is declared by the reviewed manifest.

## Catalog data

`PackageRegistry.json` is a Deucarian package catalog. Its repository URLs and package IDs are references, not bundled copies of those repositories. Packages installed from the catalog carry their own licenses and third-party notices.

## Host platform

The manifest requires Unity `2021.3`. Unity is not included in this package and is governed by the applicable [Unity Editor Software Terms](https://unity.com/legal/editor-terms-of-service/software).

Re-run the inventory and update this notice whenever dependencies or distributed content change.
