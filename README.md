# Marrow NPC Toolkit

An unofficial Unity authoring toolkit for adapting a valid Humanoid Marrow
Avatar into a native-style BONELAB NPC, with guided physics alignment,
automatic movement adaptation, deterministic validation, and Quest or Windows
pallet packing.

This repository is an experimental public preview for BONELAB Patch 6. The
official Marrow SDK 1.2.0 lists custom Spawnables as unsupported, so every game
update remains a compatibility boundary and successful Unity generation is not
runtime proof.

## Packages

- `com.vergil333.marrow-npc-toolkit` is the clean provider-neutral authoring
  package.
- `com.vergil333.marrow-npc-toolkit.patch6` is the optional exact Patch 6
  compatibility provider. It includes declaration-only type schemas and an
  explicit project bootstrap, but no game binaries or extracted content.

## Supported preview environment

- Unity 2021.3.16f1
- Official Marrow SDK 1.2.0
- BONELAB Patch 6
- Quest packing and an Eve development build were runtime-tested during
  development; the exact public candidate still requires its release cold test
- Windows packing is supported; Windows BONELAB runtime verification is pending

Only Eve has completed the current live runtime matrix. This preview does not
yet claim that every Humanoid will work correctly.

## Installation

First create or open an official Marrow SDK 1.2.0 project. In Unity, open
**Window > Package Manager**, choose **Add package from git URL**, and install
the core package from a version tag:

```text
https://github.com/Vergil333/marrow-npc-toolkit.git?path=/Packages/com.vergil333.marrow-npc-toolkit#v0.5.0-preview.1
```

Install the matching Patch 6 provider second:

```text
https://github.com/Vergil333/marrow-npc-toolkit.git?path=/Packages/com.vergil333.marrow-npc-toolkit.patch6#v0.5.0-preview.1
```

Then open **Project Settings > Marrow NPC Toolkit > Patch 6 Behaviour** and
click **Install Patch 6 Project Declarations**. Unity copies eight
declaration-only scripts into the project's `Assets` tree so their runtime
assembly identity matches BONELAB. Assign the separate project-local reference
inputs only after Unity finishes compiling.

Finally open **Tools > Marrow NPC Toolkit**. See
[Installation](Documentation/INSTALLATION.md) before configuring the provider.

## Important boundaries

- The maintained Extended SDK is not supported by this provider. Do not install
  it together with the official SDK.
- Do not publish generated authoring profiles, source character assets, or
  provider reference assets unless you have distribution rights.
- Do not treat editor generation or pallet packing as proof that an NPC works
  in BONELAB.
- Generated NPC pallets are separate mods and do not belong in this source
  repository.

## Current known limitations

- Only one substantially tested character is represented in the current proof,
  and the exact public candidate has not yet completed its release cold test.
- Hostile pursuit has not been accepted as reliable.
- Gaze can occasionally initialize with an incorrect first look direction.
- Windows output has not yet received a clean BONELAB runtime test.
- Physical Jaw and breast Secondary Motion depend on valid source mappings.
- The compatibility provider is patch-sensitive and intentionally rejects an
  unknown type or serialized-field schema.

See [Known Issues](Documentation/KNOWN_ISSUES.md),
[Compatibility](Documentation/COMPATIBILITY.md), and the core package's
[Getting Started](Packages/com.vergil333.marrow-npc-toolkit/Documentation~/getting-started.md).
The exact checks completed for the current local candidate are recorded in
[Validation Evidence](Documentation/VALIDATION_EVIDENCE.md).

## License

Original source code is available under the [MIT License](LICENSE.md). That
license does not grant rights to Unity, BONELAB, Marrow, character assets, or
other third-party content. See [Third-party notices](THIRD_PARTY_NOTICES.md).
