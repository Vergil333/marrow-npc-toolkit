# Changelog

## Unreleased

## 0.5.0-preview.1

- Prepared the first public preview package for version-pinned Git and tarball
  installation.
- Declared the supported Marrow SDK and Addressables package dependencies.
- Added public repository, documentation, changelog, issue, and license links.
- Clarified that complete native generation requires a separately installed,
  matching compatibility provider and that the core package ships no extracted
  BONELAB content.

- Added opt-in breast Secondary Motion for source Marrow Avatars with both
  Breast Soft Body bones assigned. New definitions remain safely Off by
  default; Step 2 explains automatic discovery, while Step 4 requires two
  distinct renderer-skinned breast bones below the accepted AnimationRoot with
  canonical physical owners and reports the independent provider capability.
  Readiness plus native receipts become stale when the choice changes. Abdomen
  and butt Soft Body assignments are intentionally outside this module.
- Added an author-facing hostility response control to the automatic movement
  step. It shows starting hostility and the hostility reached after a typical
  25%-health hit, marks PowerLegs' 0.50 combat-pursuit boundary, and explains
  that crossing the boundary permits pursuit without guaranteeing navigation.
  The friendly default starts at 0 and reaches 0.25 after a typical hit.
- Hardened generated gaze startup with a native delayed pool-initialization
  event. It initializes the existing physical-eye gaze pair after spawn and
  selects the player without depending solely on a level-load animation event.
- Replaced the Step 3D static Motion & Balance review and unpreviewed gait dials
  with one automatic, deterministic Avatar measurement and stock-reference
  movement adaptation action. Legacy manual multipliers are cleared on refit;
  BONELAB remains the runtime proof.
- Separated the compatible native behaviour template from the official stock
  locomotion reference. The provider copies the stock LiteLoco gait curves and
  grounder values, scales them from the Avatar-to-stock leg-length ratio, and
  validates the adapted recipe after the generated prefab is saved and reloaded.
- Added provider-side Humanoid locomotion sampling that derives each generated
  knee hinge from the most stable flexed frames in the configured Loco clips,
  preserving joint limits and source/anatomy authoring while preventing an
  animation from driving the knee into its forbidden hinge direction. Saved
  prefab validation repeats the sampled bend-plane check for both knees.
- Added the stock NPC melee-damage contract to every generated physical body:
  one blood-surface `ImpactProperties` and one `VisualDamageReceiver` wired to
  the root visual-damage controller. Compatibility probing, saved-prefab
  validation, semantic fingerprints, and the two-pass native-shell smoke now
  reject missing or stale damage receivers.
- Corrected Physical Jaw runtime generation to rebuild the canonical
  Avatar-right hinge frame instead of preserving a stale opposite axis, and to
  drive PuppetMaster through a dedicated closed-pose target. The target derives
  each Avatar's configured-idle bias and applies the explicit 0.5647-degree
  Patch 6 settling calibration measured on the accepted v75/v77 runtime; saved
  axis, target, movement-pose, and two-pass fingerprints are validated.
- Added a persistent Movement Profile for new definitions and a one-button,
  Undo-aware migration for existing definitions.
- Added a provider-neutral movement-authoring boundary that prepares persistent
  project-local standing/config assets outside the native-build transaction and
  validates a recomputed recipe fingerprint before reporting them current.

- Simplified the Physics Alignment Scene guide to role-specific fit advice and
  kept the handle/color legend in one dedicated help section instead of
  repeating conversation-specific explanations in every edit view.
- Step 2 now explains Audio Profiles as saved event-to-clip maps, provides a
  direct Review/Edit action, summarizes required and optional assignments, and
  names exactly what Avatar re-reading replaces. Audio provenance metadata no
  longer appears as a Step 4 readiness warning.
- Successful 5A output is now compact: detailed provider messages, paths, and
  fingerprints are collapsed under Technical build details. Opening the
  generated NPC frames its visible renderers rather than a large gaze-range
  gizmo, leaving Step 5B immediately visible.
- Step 5 now distinguishes a missing, current, and out-of-date native build
  before enabling 5B or 5C. An open generated prefab gets a safe Return to Main
  Scene action, stale authoring inputs request one 5A update without repeating
  Physics Alignment, and action errors are no longer duplicated globally.
- Step 5 now keeps a durable native-build receipt, creates or updates a
  GUID-bound Marrow Pallet and Spawnable Crate without regenerating barcodes,
  and packs the complete Pallet for the selected Quest or Windows target.
- Packing stops on failed Marrow project validation, then temporarily switches
  to the selected Unity target when needed and restores the starting target
  afterward. The toolkit checks the catalog, catalog hash, pallet metadata,
  shared MonoScripts bundle, and expected Spawnable bundles while continuing
  to report BONELAB runtime proof as a separate requirement.
- On macOS, Windows Pallet packing now uses Unity's installed Windows Mono
  cross-build profile only for the official Addressables build, then restores
  both the starting build target and the project's validated Standalone
  scripting backend. This avoids compiling Collections against unavailable
  Windows-IL2CPP reference assemblies while leaving the project configuration
  unchanged.
- Native, readiness, and publication metadata now have separate deterministic
  fingerprints, so changing a title, version, or target platform requests a
  repack without pretending the physics/native prefab itself became invalid.
- Readiness and packaging no longer consume Unity's unstable imported-artifact
  hashes for regenerated Physics Preview and native prefabs. Deterministic
  semantic fingerprints now own identity, while the existing receipt hashes
  remain strict integrity gates, so identical rebuilds retain the same native
  and packaging fingerprints without weakening edit detection.
- Added public Physical Jaw authoring: majority-Jaw-weighted mesh vertices seed
  one lower-face Box, a root-derived left-right hinge frame, accepted
  -28..0-degree opening limits, and preserved closed-pose landmarks. Reviewed
  manual Jaw alignment survives normal refits.
- Physics Alignment now exposes requested Jaw as a guided optional seventeenth
  role with normal handles/review/reset controls and an explicit centerline
  no-mirror explanation. Physics Preview and Step 4 dynamically validate a
  16- or 17-body graph, with Jaw parented to Head and targeted blockers for a
  missing mapping, disabled role, or unfitted Jaw.
- Baseline and Physics Preview command-line smoke tests now default explicitly
  to the backward-compatible 16-body route and accept
  `-npcToolkitIncludeJaw true` for a deliberate 17-body Jaw pass.
- Added the public `NpcAudioProfile` authoring contract for the 16 native NPC
  event groups, optional loops, walk/run footsteps, mixing inputs, and explicit
  source/credit/license provenance.
- NPC definitions now own a separate persistent Audio Profile but default to
  explicit `Silent` mode, including old definitions. Marrow Avatar audio can be
  reused as editable references without copying or modifying any audio asset.
- Step 2 exposes Off/Use Audio Profile selection, profile inspection, and an explicit
  Avatar-reference refresh. Profile readiness requires saved Small Pain, Big
  Pain, and Death groups, validates every configured clip and footstep pair, and
  does not claim provider Audio support.
- Audio inputs now participate in deterministic readiness fingerprints,
  command-line asset receipts, and native-build mutation guards. No audio asset
  is distributed by the toolkit and the Patch 6 provider remains unchanged in
  this slice.

## 0.4.2

- Corrected automatic Foot fitting for high-heel and other steep Humanoid toe
  chains. Foot boxes now run heel-to-toe on the Avatar ground plane, retain the
  authored toe-out angle, keep a sole-sized thickness, and sit on the ground
  instead of following the raw toe bone almost upright.
- Added an explicit Mirror Selected to Opposite Side workflow for arms, hands,
  legs, and feet. Collider pose is reflected in the nested Humanoid Animator's
  Avatar space instead of copying incompatible bone-local fields; the target's
  mass, joints, drive, muscles, Enabled, and Auto-Fit settings are preserved.
  The operation names source and target, warns before replacing reviewed work,
  supports Undo, marks the destination Reviewed, and selects it for inspection.
- Added a Continue to Step 4 handoff after Physics Preview generation. It opens
  the main toolkit with the same NPC Definition, clears cached readiness, and
  scrolls to the check. Step 4 now separates author-fixable physics errors from
  missing native-provider capabilities that require toolkit/provider work.

## 0.4.1

- Corrected automatic Hips, Spine, and Chest fitting to turn the official
  Avatar skin envelope into a smaller internal collision core.
- Torso capsules now begin at their Humanoid joint and extend upward, avoiding
  the oversized Hips shape spilling into both upper legs.
- Added clearer visual guidance for distinguishing collider bounds from Unity's
  large rotation handles.
- Fixed Scene-view review buttons being intercepted by collider handles by
  processing the guide first and disabling 3D handle input beneath it; an
  accepted role now advances after the GUI event and shows the next role and
  reviewed count explicitly.
- Added a full-window vertical scrollbar to Physics Alignment while keeping the
  NPC Definition selector fixed and the 16-role table independently scrollable.
- Added live collider-shape explanations and explicit dimension labels, including
  why a short Hips capsule can look spherical, which controls make it longer,
  and why the fit should follow the inner pelvis rather than the full silhouette.
- Corrected inverted wireframe arcs that made full capsule colliders look folded
  inward like half-spheres. The selected orange body is now drawn as a complete
  X-ray shape, while optional surrounding bodies remain depth-tested context.

## 0.4.0

- Added a generated-preview receipt so Step 4 rejects stale alignment/source
  inputs and detects manual edits to the Physics Preview.
- Added a public patch-provider build boundary with exact capability selection,
  isolated staging, two-pass determinism checks, post-save validation, and
  GUID-preserving prefab commit/rollback.
- Added the private Patch 6 `CoreAnatomy` provider milestone: one MarrowEntity,
  16 MarrowBodies, 16 MarrowJoints, and the exact 16-muscle PuppetMaster order.
  It explicitly reports that AI, PowerLegs, pooling, grips, gaze, jaw, audio,
  packing, and runtime proof are not implemented.
- Expanded Step 4 checks for native muscle/drive tuning and made the Patch 6
  probe verify every serialized field written by the anatomy provider.
- Added a transient native-shell smoke receipt covering repeated fingerprints,
  prefab GUID preservation, saved component/array counts, source stability, and
  cleanup.
- Verified repeated deterministic fitter, preview, and native-shell transactions
  against the reference Avatar without source mutation or character-name
  branches.

## 0.3.0

- Added a read-only Step 4 readiness doctor for the source rig, current anatomy
  receipt, finite collider/joint data, preview hierarchy, connected 16-body
  graph, and preserved renderers.
- Added deterministic readiness issue ordering, fingerprints, and a structured
  command-line receipt that also proves authoring assets stayed unchanged.
- Added a public compatibility-probe boundary that reports Avatar SDK and native
  NPC-provider availability separately, with independent anatomy, AI, pooling,
  grips, gaze, jaw, and audio capabilities.
- Added a guided readiness panel that reports physics readiness, visual-review
  status, requested provider features, and the exact boundary before native
  generation and runtime proof.

## 0.2.1

- Added a plain-language workflow overview, next-action guidance, per-step
  what/do/done explanations, and an explicit alignment review/save/preview flow.
- Added role-specific visual-fit guidance so hands, feet, head, torso, and limb
  colliders can be reviewed without prior ragdoll-authoring knowledge.
- Made hand auto-fit use the knuckle/finger envelope for a stable 3D frame and
  width instead of letting one farthest fingertip dictate the box orientation.
- Added a Scene-view guide that identifies the source Avatar reference, shows
  the selected role's fit rule, and exposes Review Next and Save actions.
- Made fitting receipts version-aware so existing auto-fit roles clearly ask
  for a refresh when fitting rules change.

## 0.2.0

- Added deterministic validation for the canonical 16-role Humanoid graph,
  prefab paths, semantic ancestry, renderer paths, and Avatar source drift.
- Added an Avatar-measurement-based collider fitter with stable fingerprints.
- Added real mass, joint-limit, muscle, and joint-force seeds from the proven
  Eve baseline while keeping character dimensions source-derived.
- Added an Undo-aware Physics Alignment workspace with target-rig overlays,
  collider position/rotation/bounds handles, per-role status, and protected
  reviewed edits.
- Added a separate explicit native muscle order so runtime wiring cannot infer
  array order from the authoring role set.
- Added a structured baseline smoke test that verifies deterministic repeated
  fitting and confirms the source Avatar dependency hash remains unchanged.
- Added generation of a clearly labeled Unity-only physics preview prefab with
  separate `AnimationRoot` and `Physics` siblings, 16 rigidbodies, 16 primary
  colliders, and 16 configurable joints. It intentionally contains no native
  NPC behaviour, Marrow interaction, PuppetMaster, pallet, or crate wiring.

## 0.1.0

- Added the first guided Unity editor window.
- Added Humanoid and Marrow Avatar intake validation.
- Added one-click Humanoid importer configuration for model assets.
- Added creation of an original Marrow Avatar prefab using the official Avatar component.
- Added direct intake from an existing AvatarCrate.
- Added persistent Avatar-source, NPC definition, anatomy, and build profile assets.
- Added snapshots of Humanoid paths, renderer categories, optional bones, and official Avatar body fitting.
- Added a command-line Avatar intake smoke test for clean-project and compatibility checks.
- Added the canonical 16-role humanoid anatomy profile with an optional jaw role.
