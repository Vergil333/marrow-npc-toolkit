# Validation evidence

## 0.5.0-preview.1 local candidate — 2026-08-31

Completed against Unity 2021.3.16f1 and the official Marrow SDK 1.2.0:

- Static release-tree, manifest, Unity metadata, provider-identity, declaration,
  private-content, and local-path validation passed.
- Both UPM archives reproduced byte-for-byte across consecutive builds.
- A fresh Unity fixture installed the exact two `.tgz` archives.
- The explicit declaration bootstrap installed all eight project declarations;
  the 32 SDK-side declarations compiled into `SLZ.Marrow` without modifying the
  official SDK package.
- The provider assembly loaded, all required type identities resolved, and
  exactly one `vergil333.bonelab-patch6` provider registered.
- The complete Editor suite passed: 106 tests, 106 passed, 0 failed, 0 skipped.

Candidate archive SHA-256 values:

```text
53b3bc1c31b9a2b97be02194ce3f66ee3873aa525eb29c1b410d2861cabba63e  com.vergil333.marrow-npc-toolkit-0.5.0-preview.1.tgz
423dd3e6fb2cc86b994432fd40764e4f52be3464c8d519e7bf5897ccfe4952ee  com.vergil333.marrow-npc-toolkit.patch6-0.5.0-preview.1.tgz
```

On 2026-08-31, the repository owner reviewed and accepted public distribution
of the metadata-derived declaration-only schemas under the boundary documented
in this repository. This records the owner's publication decision; it is not a
claim of third-party approval or a legal opinion.

On 2026-08-31, provider preflight also passed in a separate clean authoring
fixture using the exact two archives and official SDK versions above. The
fixture imported a filtered private dependency closure for the project-local
behaviour, locomotion, controller, config, pose, grip, and physics-material
inputs. It excluded the development project's declaration scripts and derived
all 14 settings afresh from the imported asset paths. The registered provider
reported every capability (`All`), and the complete Editor suite then passed
again: 106 tests, 106 passed, 0 failed, 0 skipped. No private reference asset,
path, GUID, or generated pallet is included in this repository or its package
archives. Both archive hashes remained unchanged after the run.

The final archives were rebuilt a second time and reproduced the same SHA-256
values. A fresh exact-archive fixture generated Eve from the reviewed anatomy
and automatic movement inputs, prepared the existing GUID-bound Pallet and
Spawnable Crate, and completed both platform packs:

```text
Pallet: Vergil333.EveNoMaskNPC
Spawnable: Vergil333.EveNoMaskNPC.Spawnable.EveNoMaskNPC
Quest / Android: 6/6 expected files, packaging fingerprint fd7bc55647da2ba2202407358ee75b40
Windows PC:      6/6 expected files, packaging fingerprint 887cb04133d35dad4cb7238d55e2ce68
```

The macOS Windows pack used Unity's installed Windows Mono cross-build profile
only for the official Addressables content build, then restored both the
starting Android target and the validated Standalone backend. This follows the
content-pack portion of Marrow's own Pack-for-PC flow; it is packaging evidence,
not Windows BONELAB runtime proof.

The Windows transaction was then repeated unchanged from Android / IL2CPP.
Both Windows runs produced the same baseline, preview, movement, movement
recipe, readiness, native-input, native-output, native-prefab GUID, packaging,
and six-file output identities, and both returned Unity to Android / IL2CPP.
Unity's imported preview and native-prefab artifact hashes changed between the
runs; the stable semantic fingerprints remained identical, while the saved
receipt still validated each current prefab hash as an integrity gate. This is
the intended separation between deterministic content identity and Unity's
platform/cache-specific import identity.

Still required before publishing the GitHub preview:

- A cold Quest spawn, movement, damage, pooling, interaction, jaw, gaze, audio,
  and Secondary Motion test of the installed exact Android output.
- Enabling GitHub Private Vulnerability Reporting after the remote repository
  exists.

Deferred claim gates are intentionally documented as pending: a freely
distributable non-Eve Humanoid and clean Windows BONELAB runtime verification.
