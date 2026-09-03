# Getting Started

## Requirements

- Unity 2021.3.16f1.
- Official Marrow SDK 1.2.0 when using the included Patch 6 provider. The
  maintained Extended SDK is not supported by 0.5.0-preview.1; do not install
  the two SDK variants together.
- A legally usable humanoid character with skinned meshes.

Open **Tools > Marrow NPC Toolkit**.

## The workflow in plain language

Work from top to bottom:

1. **Import Avatar** — check that the visible character and Humanoid rig are
   usable.
2. **Define NPC** — create separate files that hold NPC-specific settings.
3. **Physics & Motion** — review the invisible body, then create an editor
   estimate of standing balance and navigation measurements.
4. **Check NPC Readiness** — inspect the authoring data, generated physics, and
   native-provider support without changing anything.
5. **Build & Test** — generate the native prefab (5A), prepare its stable
   Pallet and Spawnable Crate (5B), and pack the selected platform (5C).
   A confirmed BONELAB spawn and interaction test remains the final proof.

Three similarly named things appear during this process:

- **Source Avatar** is the original visible character. The alignment workspace
  opens it only as a measuring reference.
- **Anatomy Profile** is the asset where collider and joint tuning is saved.
- **Physics Preview** is a newly generated inspection prefab. Its
  `AnimationRoot` contains the untouched visible/animated Avatar; its sibling
  `Physics` hierarchy contains the 16 required body shapes and, when requested,
  a seventeenth Physical Jaw body.

## 1. Import Avatar

**What:** Checks that the character is a supported Marrow Avatar with a usable
Unity Humanoid rig.

**Why:** NPC generation needs stable bone, renderer, wrist, eye, and body-shape
information. Starting from a working Avatar lets the toolkit reuse supported
Marrow setup instead of asking for it again.

**Do this:** Choose the most complete source you already have:

1. `AvatarCrate` — recommended when the Avatar has already been packed.
2. Existing Marrow Avatar prefab — recommended during authoring.
3. Raw FBX/model — assisted route.

For a raw model, choose **Configure Model as Unity Humanoid**. If Unity cannot
map it automatically, use the model importer's **Rig > Configure** screen. Then
choose **Create Marrow Avatar Prefab**.

Use **Open Official Avatar Fine-Tuning** to review body/head/hair renderer
groups, wrists, eyes, and body-shape handles. This is the supported Marrow Avatar
editor; the NPC Toolkit does not duplicate it.

**Done when:** Step 1 says **Ready** and shows no red errors.

## 2. Define NPC

**What:** Creates separate authoring assets for this NPC.

**Why:** Collider and behaviour tuning must survive regeneration without
changing or duplicating the source Avatar.

**Do this:** Choose an authoring folder and click **Create NPC Definition &
Profiles** once Step 1 is Ready. Six assets are made:

- Avatar Source Profile: a stable snapshot of the accepted Avatar setup.
- NPC Definition: feature choices and references to the other profiles.
- Anatomy Profile: 16 canonical physical roles plus an optional jaw.
- Movement Profile: measured standing proportions plus modder-reviewed gait,
  foot, pelvis, and navigation tuning.
- Build Profile: public metadata, platform, output folder, and compatibility ID.
- Audio Profile: a saved category map that tells the NPC which existing clips
  to use for pain, death, effort, impacts, loops, and footsteps.

The source Avatar is not modified. When the source is a Marrow Avatar, the
toolkit reuses its existing pain, death, effort, recovery, high-fall, and
footstep references as an editable starting point. It does not copy or alter an
`AudioClip` or `AudioVarianceData` asset. The suggested conversion is visible:
small/big pain become the matching reactions, dying plus dead become Death,
big effort becomes Jump and the fallback Small Effort, recovery becomes Medium
Effort, high fall becomes Large Effort, and Avatar walk/jog become NPC walk/run
footsteps. Physical Impacts are separate, optional NPC contact sounds. Because
they can also play from a dead ragdoll, use non-vocal body, clothing, or armor
sounds unless post-death vocals are intentional.

NPC Audio starts **Off**. Choose **Use Audio Profile** when the generated NPC
should use the assigned category map. Small Pain, Big Pain, and Death each need
at least one saved clip; every other category is optional and remains silent
when empty. Walking and running footsteps must be supplied together or both
left empty. Step 4 performs the final validation.

Choose **Review / Edit Audio Profile** to select the profile in Unity's
Inspector, where every category and individual clip can be inspected or
replaced. New NPC definitions already receive an Audio Profile automatically.
**Create Missing Audio Profile** is only a repair action for an older or
manually disconnected definition: it creates the small settings asset beside
the definition, assigns it, and reuses supported Avatar clip references when
available. It does not enable NPC Audio by itself.

Use **Re-read Supported Audio from Avatar** after changing the Avatar's audio.
It replaces Small Pain, Big Pain, Death, Jump, Small/Medium/Large Effort, and
Walk/Run with the Avatar's current references. Physical Impacts and other custom
groups stay unchanged. The profile always stores links to existing clips; it
does not copy or edit the audio files.

Later collider, joint, muscle, grip, eye, jaw, audio, and foot tuning belongs in
the profiles, not in a generated prefab.

**Done when:** The toolkit shows an NPC Definition and Anatomy Profile in Step
2.

## 3. Align Physics

**What:** Creates and reviews the character's invisible physical body. These
shapes are used later for contact, impacts, grabbing, falling, and ragdoll
motion. They do not replace the visible mesh.

**Why:** A Humanoid rig tells Unity where the bones are, but it does not fully
describe this character's physical volume. The automatic fit provides a strong
starting point; the visual review catches unusual proportions or bone axes.

### 3A. Create the automatic fit

Choose **3A. Create / Refresh Automatic Fit**. The fitter first revalidates all 16
Humanoid paths and refuses stale source snapshots. It then uses the accepted
Marrow Avatar's body fitting plus the rig's own bone lengths to create a
character-specific physics baseline. When **Physical Jaw** is enabled and the
Avatar has a mapped Humanoid Jaw, the fitter also finds source mesh vertices
whose combined Jaw weight is at least 50% and fits one lower-face Box around
them. It does not guess a jaw from the Head collider.

### 3B. Review it over the Avatar

Choose **3B. Review Physics Alignment** to review it over the actual Avatar:

- Cyan lines are the target Humanoid rig.
- Blue colliders are auto-fitted.
- Green colliders have been reviewed.
- Orange is the selected collider and its Scene handles. Its complete wireframe
  is shown through the Avatar so the mesh cannot hide half of the shape.

The orange rounded capsule/sphere (or orange box on a hand or foot) is the
actual physical shape. The pale rectangular cage and white squares resize its
bounds, large circular rings rotate it, and arrows move it. Those controls are
only editor tools and do not add collision volume.

Review one role at a time:

1. Select a role and choose **Focus Selected**. Turn off **Show other bodies**
   when you want to inspect only that orange shape.
2. Orbit the Scene view; judge the orange shape in 3D, not by whether it looks
   horizontal or vertical on the screen.
3. If it looks sensible, choose **Looks Good - Review & Next**.
4. If it is clearly wrong, use the orange position, rotation, or size handles,
   then review it.
5. Repeat for all 16 required roles, plus Jaw when Physical Jaw is enabled, and
   choose **Save Alignment Profile**.

A good fit is centered on the solid body part, follows it in 3D, and ends near
the neighboring joints. Slight overlap between neighboring colliders is normal.
Fix a shape when it misses most of its body part, crosses into an unrelated
limb, or sticks far outside the character. Do not try to trace the skin exactly;
ignore hair, loose clothing, individual finger/toe outlines, and other small
details.

Useful role checks:

- **Hand:** treat the hand as one simple collision body. Its main length runs
  from wrist toward the fingers, its width runs across the knuckles, and its
  thinnest direction goes through the palm. It may enclose the fingers as one
  envelope; do not fit each finger separately.
- **Foot:** run from heel toward toes and remain centered on the foot/sole; do
  not stand upright along the shin.
- **Head:** cover the solid skull and face core; ignore hair and accessories.
- **Jaw (optional):** cover the movable Jaw bone's weighted lower-face envelope,
  including the chin, mouth, and lower cheeks. It can reach near the nose and
  overlap Head slightly; do not shrink it to the chin alone. Leave the eyes,
  forehead, skull, hair, and neck to other bodies. The hinge follows the
  Avatar's left-right axis and uses conservative native-like opening limits.
  Jaw is a centerline body, so it cannot be mirrored.
- **Hips:** cover the solid inner pelvis around the hip joints and sacrum rather
  than the complete buttock, clothing, or outer silhouette. A short Capsule can
  correctly look spherical when its Height equals its diameter. Radius changes
  width and depth together; Height makes it longer. Slight silhouette underfill
  and overlap with the spine and upper-leg shapes are expected.
- **Spine:** cover the solid body core without widening around arms, hair, or
  loose clothes.
- **Chest:** expect a complete, mostly vertical capsule from the upper ribcage
  toward the base of the neck. Fit the sternum/ribcage core, not breasts,
  shoulders, clothing, or accessories. Slight torso overlap is expected.
- **Arms and legs:** each capsule follows its bone from one joint toward the
  next and stays centered inside the limb.

After carefully adjusting one arm, hand, leg, or foot, select that role and use
the explicit **Mirror Left ... -> Right ...** (or reverse) button to reuse its
collider alignment on the opposite side. Confirm the named source and target in
the dialog. The toolkit mirrors the position and rotation through the Avatar's
center plane in Avatar-root space, so it does not assume that left and right
bones have identical local axes. It then selects the destination for visual
inspection and marks it Reviewed. Only collider shape, dimensions, position,
and rotation are replaced; the destination's Enabled, Allow Auto-Fit, mass,
joint limits, drive, and muscle tuning remain unchanged. Centerline roles such
as Hips, Spine, Chest, Head, and the optional Jaw intentionally cannot be mirrored. Undo is
available until the Anatomy Profile is saved.

Scene edits write to the Anatomy Profile and support Undo/Redo. They do not edit
the source prefab. Use the explicit alignment save button; there is no need to
save the Avatar prefab. A normal refit keeps reviewed roles; **Refit Everything**
is the explicit overwrite action. If the Avatar prefab changes, refresh its
snapshot and review alignment again.

### 3C. Generate and inspect the preview

Choose **3C. Generate / Refresh Physics Preview** to materialize the profile as a
separate prefab under the Build Profile's `Generated` folder. It contains the
Avatar as a nested prefab beneath `AnimationRoot` and a sibling `Physics` tree
with 16 kinematic rigidbodies, primary colliders, and configurable joints, or
17 of each when Physical Jaw is requested. The Jaw body is parented and joined
to Head. Use it to inspect the hierarchy and component values. Do not pack it as an NPC; it
does not yet contain PuppetMaster, native AI, interaction, health, or pooling.
The toolkit records the exact Avatar/alignment input and generated prefab
content. If either changes afterward, Step 4 explains that the preview is stale
and sends you back to Step 3C.

When the preview is current, return to the main toolkit window and complete
Step 3D before running Step 4.

### 3D. Recalculate movement for this Avatar

Choose **3D. Recalculate Movement for This Avatar** after the Physics Preview is
current. It measures a disposable instance of the accepted Humanoid and saves:

- left and right leg lengths as hip-to-knee plus knee-to-foot segments;
- eye, body, sole, hip, and standing-foot measurements;
- foot-forward directions and a body-clearance navigation radius;
- the measurable inputs needed to proportionally adapt the provider's native
  stock-reference standing, navigation, and gait settings.

Old NPC Definitions are upgraded by this same button: the toolkit creates a
persistent Movement Profile beside the Definition, links it with Undo support,
then continues the fit. Recalculation deliberately replaces movement
multipliers from older toolkit versions because those controls had no truthful
native animation or physics preview. A compatible project provider also
prepares persistent patch-specific standing-pose and movement assets; the
toolkit recomputes its recipe fingerprint so stale donor, controller, or
configuration inputs are not reported as current. The Patch 6 native build
uses the configured locomotion clips to align the generated knee hinges to the
retargeted Humanoid's real bend planes without changing the Anatomy Profile.

Step 3D is automatic. It does not claim to simulate BONELAB AI, PuppetMaster,
NavMesh movement, collisions, falling, or recovery in the Unity editor. Test
those behaviors in BONELAB after rebuilding and packing.

**Done when:** All requested shapes have been reviewed and saved, the generated
preview has separate `AnimationRoot` and `Physics` hierarchies with sensible
body placement, and Step 3D reports current automatic movement assets.

## 4. Check NPC Readiness

**What:** Runs a read-only doctor over the accepted Avatar, the Anatomy and
Movement Profiles, the generated preview, and the selected native compatibility
provider.

**Why:** A preview can look plausible while still containing a stale source
receipt, invalid joint axes, a disconnected body, a lost renderer, or no native
provider for the requested BONELAB contract.

**Do this:** Choose **4. Check NPC Readiness** after generating the preview and
refreshing Step 3D. The result separates:

- **Physics ready:** the requested 16- or 17-body set, colliders, and joints are valid and form the
  expected connected hierarchy beneath `Physics`; `AnimationRoot` preserves the
  accepted renderers.
- **Review recommended:** auto-fit shapes that have not yet been visually
  accepted. This warns during drafts and must be finished before release.
- **Native provider:** whether the exact Build Profile contract is available,
  plus independent support for core anatomy, AI/movement, pooling, grips, gaze,
  physical jaw, NPC audio, and Secondary Motion.

If Physical Jaw is requested, a missing Jaw mapping, disabled Jaw role, or
unfitted Jaw box is a targeted blocking error. Turn the feature off only when
the character intentionally has no physical jaw.

Secondary Motion is Off by default. Enable it in Step 2 only when the source
Marrow Avatar has both Breast Soft Body bones assigned. The native provider
uses those two existing assignments automatically and builds the spring-driven
breast bodies without changing the source Avatar. Abdomen and butt Soft Body
assignments are not included. These bodies are provider output, not extra core
colliders to align in Step 3, so Step 4 validates the breast assignments and
reports provider support in a separate capability row. Changing the option also
invalidates an older native-build receipt.

Red physics/readiness errors describe authoring work the modder can correct.
Rows marked **Missing from provider** instead describe native functionality the
installed toolkit/provider cannot generate yet; changing collider alignment
does not fix those gaps.

The doctor only reads. It does not refit, regenerate, save, import, or mark an
asset dirty. In Profile mode it also checks that the Audio Profile and every
configured clip are persistent assets, that the three basic reaction groups are
present, and that footstep pairs and numeric multipliers are valid. Run it again
after changing the Avatar, Anatomy Profile, Audio Profile, preview, Build
Profile, or provider.

**Done when:** Physics is ready and the provider supports every requested
feature. The result then says **Ready for the native-builder handoff**.

That is deliberately not runtime proof. It does not mean a native prefab has
been generated, packed, spawned, interaction-tested, or approved in-headset.

## 5. Build & Test

After Step 4 is ready, run the three substeps in order:

1. **5A. Generate Native NPC Prefab** creates the separate native prefab and a
   build receipt, then verifies a save/reload round trip and a second identical
   build. Expand **Build validation details** only when you need the full audit.
   If that prefab is open, use **Return to Main Scene** first. If authoring
   settings changed, rerun 5A once; you do not need to repeat Physics Alignment.
2. **5B. Prepare Spawnable Crate** creates or updates the exact GUID-bound
   Pallet and Spawnable Crate while preserving their barcodes. It is enabled
   only when the saved 5A build matches the current NPC settings.
3. **5C. Pack Pallet for Quest / Android** or **Windows PC** packs the entire
   Pallet for the Build Profile's selected platform.

When a gaze-enabled generated prefab is open, Unity may draw a very large green
sphere. It is the player-notice/gaze range gizmo, not a collider or the NPC's
scale. Hide Scene Gizmos if desired; do not resize the NPC to match it.

Packing is still not runtime proof. Cold-start BONELAB and verify spawning,
movement, damage, falling and recovery, pooling/respawn, body grabs, gaze, jaw,
and configured audio on the target platform.
