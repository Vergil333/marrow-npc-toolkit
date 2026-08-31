# Installation

## Requirements

- Unity 2021.3.16f1 with Android Build Support for Quest output.
- Windows Build Support for Windows PC output. On macOS, install the available
  **Windows Build Support (Mono)** module; the toolkit uses it only while
  cross-packing Pallet content, then restores both the starting build target
  and the project's Standalone scripting backend.
- A project created from or correctly configured for the official Marrow SDK
  1.2.0.
- A local BONELAB installation for the platform you intend to test.

The first public preview supports the official Marrow SDK only. The maintained
Extended SDK has conflicting declaration shapes and is not supported by
0.5.0-preview.1.

## Install from a Git tag

Open **Window > Package Manager**, click **+**, and choose
**Add package from git URL**.

Install the core package first:

```text
https://github.com/Vergil333/marrow-npc-toolkit.git?path=/Packages/com.vergil333.marrow-npc-toolkit#v0.5.0-preview.1
```

Install the provider second:

```text
https://github.com/Vergil333/marrow-npc-toolkit.git?path=/Packages/com.vergil333.marrow-npc-toolkit.patch6#v0.5.0-preview.1
```

## Complete Patch 6 provider setup

Open **Project Settings > Marrow NPC Toolkit > Patch 6 Behaviour** and click
**Install Patch 6 Project Declarations**. This explicit action creates eight
declaration-only scripts at:

```text
Assets/MarrowNpcToolkit/Patch6Declarations/AssemblyCSharp
```

Unity requires those files under `Assets` so their assembly identity is
`Assembly-CSharp`, matching BONELAB serialization. The installer does not copy
game code or assets. If installed declarations differ from the package, it
requires confirmation and makes a dated backup before updating them.

After Unity recompiles, return to the same settings page and assign the
project-local behavior, locomotion, controller, pose, grip, and material inputs
that you may lawfully use. The repository deliberately supplies none of those
content assets.

Do not install from an unpinned `main` branch for a production mod. A tag keeps
the authoring and provider contracts together.

## Install from a GitHub Release

Download both `.tgz` files from the same release. In Package Manager choose
**Add package from tarball** and install the core package before the provider.
Do not mix versions.

## Update

Replace both version tags in the project's `Packages/manifest.json`, or install
both newer tagged URLs through Package Manager. Read the changelogs before
rebuilding. A provider or fitting-rule update can intentionally make saved
receipts stale.

## Remove

Remove the Patch 6 provider first, then remove the core package through Package
Manager. Existing NPC Definition, profile, generated prefab, Pallet, and Crate
assets remain project assets and are not deleted automatically.

The provider also leaves
`Assets/MarrowNpcToolkit/Patch6Declarations/AssemblyCSharp` in place so removing
a package never silently deletes project files. Delete that folder manually
only after the provider has been removed and its generated NPCs are no longer
being rebuilt.

Back up the project before manually removing generated assets.
