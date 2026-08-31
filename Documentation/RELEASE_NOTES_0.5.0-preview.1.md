# Marrow NPC Toolkit 0.5.0-preview.1

This is the first public preview of an unofficial guided Unity toolkit for
adapting a valid Humanoid Marrow Avatar into a native-style BONELAB NPC.

## Environment

- Unity 2021.3.16f1
- Official Marrow SDK 1.2.0
- BONELAB Patch 6
- Quest / Android content packing
- Windows PC content packing; BONELAB Windows runtime proof is still pending

## Included packages

Download the two attached `.tgz` files and install them in this order from
Unity's Package Manager using **Add package from tarball**:

1. `com.vergil333.marrow-npc-toolkit-0.5.0-preview.1.tgz`
2. `com.vergil333.marrow-npc-toolkit.patch6-0.5.0-preview.1.tgz`

The version-pinned Git URLs are an alternative to the attached files. Install
the core toolkit first, then the Patch 6 provider:

1. Core toolkit:
   `https://github.com/Vergil333/marrow-npc-toolkit.git?path=/Packages/com.vergil333.marrow-npc-toolkit#v0.5.0-preview.1`
2. Patch 6 provider:
   `https://github.com/Vergil333/marrow-npc-toolkit.git?path=/Packages/com.vergil333.marrow-npc-toolkit.patch6#v0.5.0-preview.1`

Open **Project Settings > Marrow NPC Toolkit > Patch 6 Behaviour** and install
the eight declaration-only compatibility scripts into the consuming project's
`Assets` tree. Let Unity compile them, return to the same settings page, and
then assign the required project-local provider inputs. No game assemblies,
extracted game content, source character assets, generated NPCs, or private
provider reference assets are included.

## What is in the preview

- Guided source-Avatar setup and canonical Humanoid rig checks
- Per-body physics alignment with reusable Anatomy Profiles
- Automatic avatar-proportional movement tuning
- Patch 6 native NPC generation for movement, damage, ragdoll and recovery,
  player grabs, gaze, optional physical jaw, audio, and supported Secondary
  Motion mappings
- Deterministic readiness, saved-reload, and two-pass build validation
- GUID-stable Pallet and Spawnable Crate preparation
- Quest and Windows content packing

## Validation scope

- Both attached package archives reproduced byte-for-byte across consecutive
  builds.
- A clean Unity fixture installed the exact attached packages and passed the
  Patch 6 provider preflight.
- All 106 Editor tests passed in both the package test project and the clean
  exact-package fixture.
- The exact package installation rebuilt and packed Eve for Quest and Windows;
  both outputs contain all six expected pallet files.
- Two unchanged Windows cross-packs produced the same semantic build and
  packaging fingerprints and restored Android / IL2CPP after each run.
- The exact Quest pallet was generated for deployment. Its cold Quest 3 spawn
  and interaction confirmation remains a draft-release gate.

Only Eve has completed the development runtime matrix. This preview is intended
for Humanoid Avatars, but it does not yet claim that every Humanoid will work
correctly.

## Known limitations

- Hostile pursuit is not accepted as reliable.
- Gaze can occasionally initialize looking left or acquire the player late.
- Physical Jaw and breast Secondary Motion require valid source mappings.
- Windows output is structurally verified but has not completed a clean
  BONELAB Windows runtime test.
- The official Marrow SDK 1.2.0 describes custom Spawnables as unsupported;
  this provider is Patch 6-specific and game updates remain compatibility
  boundaries.

## SHA-256

```text
53b3bc1c31b9a2b97be02194ce3f66ee3873aa525eb29c1b410d2861cabba63e  com.vergil333.marrow-npc-toolkit-0.5.0-preview.1.tgz
423dd3e6fb2cc86b994432fd40764e4f52be3464c8d519e7bf5897ccfe4952ee  com.vergil333.marrow-npc-toolkit.patch6-0.5.0-preview.1.tgz
```

See the repository's Compatibility, Known Issues, Installation, and Validation
Evidence documents before using the preview.
