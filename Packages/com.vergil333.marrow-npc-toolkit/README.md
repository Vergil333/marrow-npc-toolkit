# Marrow NPC Toolkit (Unofficial)

Unity authoring tools for turning a valid Humanoid character into a native-style
BONELAB NPC while preserving the character's own mesh, materials, skin weights,
bindposes, proportions, eye bones, optional jaw, and explicitly requested
breast secondary motion.

The core package is provider-neutral and deliberately separate from Marrow SDK
implementations. This preview's included Patch 6 provider supports only a clean
official Marrow SDK 1.2.0 project. Do not combine it with the maintained
Extended SDK. Supporting another SDK route requires a separately implemented
and tested compatibility provider; none is included in 0.5.0-preview.1.

## Current v0.5 preview milestone

Open **Tools > Marrow NPC Toolkit** in Unity.

The window explains the full workflow, shows the next recommended action, and
defines what each step does, why it is needed, and when it is complete. The
alignment workspace includes a good-fit checklist, role-specific guidance, a
reviewed count, explicit profile saving, and a review-next flow.

1. **Import Avatar** accepts an existing `AvatarCrate`, a Marrow Avatar prefab,
   or a normal model asset. An existing official Avatar is the recommended path.
2. A model can be configured as Unity Humanoid and converted into an original
   prefab with the official `SLZ.VRMK.Avatar` component.
3. The intake validator checks the same required Humanoid bone contract used by
   Marrow SDK, along with renderers, wrists, eye references, scale, and prefab
   readiness.
4. **Create NPC Definition** writes persistent Avatar-source, definition,
   anatomy, movement, build, and audio profiles. The source profile snapshots stable Humanoid
   bone paths, renderer groups, wrists, eye/jaw references, optional twist
   bones, and official body-fit measurements so generation starts from the work
   already completed for the Avatar. The Audio Profile may reuse existing
   Avatar clip references, but no clip is copied and NPC Audio starts in the
   explicit backward-compatible **Silent** mode.
   Secondary Motion also starts **Off**. When enabled, the native provider uses
   the two Breast Soft Body bones already assigned on the source Marrow Avatar;
   it does not modify that Avatar or include abdomen/butt Soft Body assignments.
5. **Create / Refresh Baseline** resolves the 16 canonical bodies from Unity's
   Humanoid mapping and fits collider dimensions from this Avatar's eye height,
   body widths/depths, limb ellipses, and bone lengths. When Physical Jaw is
   requested, a mapped Jaw adds a tunable seventeenth Box derived only from
   source vertices with at least 50% combined Jaw weight.
6. **Open Physics Alignment Workspace** opens the source prefab read-only for
   NPC purposes and draws the target skeleton plus editable collider handles.
   Position, rotation, size, mass, and joint data are written only to the
   anatomy profile with Undo. Reviewed roles are protected from normal refits.
   A reviewed arm, hand, leg, or foot collider can be mirrored to its opposite
   side in Avatar space without replacing that side's body or joint tuning.
7. **Generate Unity Physics Preview** creates a separate inspectable prefab with
   sibling `AnimationRoot` and `Physics` hierarchies, the untouched nested Avatar,
   and 16 kinematic bodies/colliders/joints, or 17 with the optional Jaw parented
   to Head. This verifies how the profile will
   materialize without claiming native NPC/runtime readiness.
8. **Automatic Movement Adaptation** measures bilateral leg lengths,
   standing/sole landmarks, foot directions, and navigation clearance, then
   proportionally prepares the provider's stock-reference locomotion assets.
   There are no required movement dials or static review gate. The Patch 6
   provider also derives each generated knee hinge from the actual retargeted
   locomotion clips; BONELAB movement and recovery testing remains runtime proof.
9. **Check NPC Readiness** performs a read-only audit of the source, anatomy,
   preview graph, renderer preservation, and selected native-provider
   capabilities. It reports visual review separately and fingerprints the
   result; it does not modify an authoring asset. Secondary Motion has its own
   capability row because provider-generated breast spring bodies are separate
   from the 16- or 17-body Physics Alignment set.

Physics Previews now carry an authoring and content receipt. Step 4 refuses a
preview if the Avatar or Anatomy Profile changed after Step 3C, or if the
generated prefab was edited by hand.

The public package owns a deterministic native-provider transaction: two fresh
isolated passes, post-save validation, fingerprint comparison, a durable build
receipt, commit, and rollback of existing output. The separately installed
Patch 6 provider implements a 16-body baseline plus optional seventeenth
Physical Jaw, AI/PowerLegs, pooling, player body grabs, renderer rebinding,
gaze, profile-driven NPC audio, and opt-in breast Secondary Motion. Its source
is distributed in the separate Patch 6 provider package; the donor/reference
assets selected by an author remain project-owned and are not distributed.

Step 5 can now prepare stable GUID-bound Pallet/SpawnableCrate assets and pack
the whole Pallet for Quest or Windows with output completeness checks. Editor
generation and packing are still not BONELAB runtime proof; cold spawn,
movement, damage, recovery, pooling, and interaction tests remain required.

## Intended workflow

`Import Avatar -> Define NPC -> Align Physics -> Recalculate Movement -> Validate -> Build & Test`

See [Getting Started](Documentation~/getting-started.md) and the
[Implementation Roadmap](Documentation~/roadmap.md) for the precise milestone
boundary.

The generated NPC will use normal Marrow pallet/crate content. The toolkit's
authoring assets are not meant to be referenced by the packed NPC prefab, so a
native-behaviour build can remain content-only and require no custom runtime DLL.

## Public-distribution boundary

Do not add BONELAB game assemblies, extracted prefabs, controllers, animation
clips, audio clips, or other game assets to this package. Audio Profiles store
references and provenance only; authors remain responsible for distribution
rights. Patch-specific declarations and
serialized compatibility data require a separately reviewed compatibility
provider.

The current compatibility target is BONELAB Patch 6 / official Marrow SDK
1.2.0. The maintained Extended SDK is not supported by the included provider;
adding it would require a separate provider pinned and tested against exact
Extended SDK commits.

This core package does not generate a complete native NPC on its own. Install a
matching compatibility provider, such as the separately distributed Patch 6
provider, before Step 5A. The provider must supply its own legally distributable
code and must not redistribute extracted BONELAB content.
