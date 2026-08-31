# BONELAB Patch 6 Provider

This optional package supplies the exact-schema native NPC generation provider
for Marrow NPC Toolkit 0.5.0-preview.1 and BONELAB Patch 6.

It is unofficial compatibility tooling. The official Marrow SDK 1.2.0 lists
custom Spawnables as unsupported.

## What this package contains

- Patch 6 type and serialized-field compatibility probes.
- Declaration-only Patch 6 compatibility schemas: 32 are added to the official
  `SLZ.Marrow` assembly through Unity's assembly-reference mechanism, and eight
  are installed into the project's `Assembly-CSharp` by an explicit setup
  action.
- Deterministic native anatomy, behavior, movement, pooling, interaction,
  player body-grab, gaze, Physical Jaw, audio, damage, and optional breast
  Secondary Motion generation and validation.
- A project settings page for explicit reference inputs.

## What this package does not contain

- BONELAB assemblies or binaries.
- Extracted NPC prefabs, controllers, animation clips, hand poses, materials,
  audio, textures, models, or other game content.
- A license to redistribute any input selected by an author.

Open **Project Settings > Marrow NPC Toolkit > Patch 6 Behaviour**, install the
project declarations, wait for Unity to compile, then assign the separate
project-local reference inputs. The provider remains unavailable until the
exact assembly/type probe and every selected input pass preflight.

The maintained Extended SDK is not supported by 0.5.0-preview.1. Use a clean
project with the official Marrow SDK 1.2.0.

See [Provider setup](Documentation~/provider-setup.md) before attempting Step
3D or Step 5A.
