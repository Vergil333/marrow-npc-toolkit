# Known issues

## Hostile pursuit

The hostility response can enter BONELAB's combat state, but navigation toward
the player has not been accepted as reliable. Do not advertise this preview as
a complete enemy-NPC authoring solution.

## Initial gaze direction

Most tested spawns acquire the player, but an occasional spawn may begin by
looking left or may delay player acquisition. This is a runtime-known issue,
not proof that eye mappings are missing.

## General humanoid coverage

Automatic fitting is measurement-driven and contains no intended
character-name branch. Nevertheless, only Eve has completed the present live
test matrix. Very different proportions, bone orientations, footwear, jaw rigs,
and skinning can expose unsupported cases.

## Windows runtime proof

The exact candidate can switch Unity to `StandaloneWindows64` and produce a
structurally complete packed Pallet. On macOS, the toolkit temporarily uses
Unity's installed Windows Mono profile only for the Addressables content build
and afterward restores both Unity's starting build target and the validated
Standalone backend. On macOS, start this transaction from Quest / Android when
the Standalone backend is IL2CPP; Step 5C explains this if a Standalone target
is already active. The current preview still requires a clean Windows BONELAB
spawn and interaction test before Windows can be marked runtime-verified.

## Patch sensitivity

The official Marrow SDK 1.2.0 lists custom Spawnables as unsupported. The Patch
6 provider uses an exact compatibility contract and may stop at preflight after
a game or SDK update. This is safer than generating an asset against an unknown
schema.

## Maintained Extended SDK

The Patch 6 provider targets the official Marrow SDK 1.2.0. The maintained
Extended SDK already declares overlapping types with incompatible inheritance,
field storage, and nesting, so installing both routes causes conflicts or a
failed exact-schema probe. A future Extended SDK route would require a separate
provider pinned and tested against exact Extended SDK commits.
