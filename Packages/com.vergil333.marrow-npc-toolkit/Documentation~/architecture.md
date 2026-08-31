# Architecture

## Product boundary

The NPC Toolkit is a separate Unity package. It owns the guided UI, persistent
profiles, deterministic builders, alignment tools, validation, and test
receipts. It does not own or redistribute Marrow, Extended SDK, game assemblies,
or extracted game content.

An imported Marrow Avatar is the shared front door. The core authoring model is
designed around the shared `SLZ.VRMK.Avatar` contract, but a complete NPC build
still requires a provider tested against the exact SDK and game schema.

NPC-only types are behind a compatibility-provider boundary:

- Official provider: consumes separately reviewed declarations for one exact
  BONELAB patch and refuses unknown schemas.
- A future Extended provider would need to consume a pinned Extended
  installation and ship no copied Extended files. No such provider is included
  in 0.5.0-preview.1.

The generated NPC prefab must contain no NPC Toolkit component and no reference
to an authoring profile. It remains content that binds to BONELAB runtime types.

## Authoring data

`NpcAvatarSourceProfile` captures stable prefab-relative paths and the official
Avatar fitting. `NpcAnatomyProfile` stores NPC-specific physics tuning.
`NpcMovementProfile` stores provider-neutral Humanoid measurements and
references plus a fingerprint for an optional provider-owned movement recipe.
Automatic recalculation replaces legacy unpreviewed multipliers instead of
treating them as a required modder review. The public profile never names a
game movement type.
`NpcBuildProfile` stores package metadata and the selected compatibility
contract. `NpcAudioProfile` stores stable NPC event groups, footsteps, optional
loops, numeric mixing inputs, and distribution provenance. It contains asset
references, never copied audio content. `NpcDefinition` ties the profiles
together and explicitly selects either `Silent` or `Profile` audio mode.
It also owns the opt-in Secondary Motion choice. That choice requests a native
provider capability; it is not a new Anatomy Profile role. The current contract
uses exactly the two Breast Soft Body bone assignments already stored on the
source Marrow Avatar. It does not imply abdomen or butt Soft Body support.

Movement fitting uses the accepted Humanoid bindings and Anatomy landmarks on a
disposable Avatar instance. It is automatic authoring, not a simulation of
native AI, animation, physics, falling, or recovery.

Patch-specific standing-pose or movement-config generation is an explicit
Step 3D authoring action through `INpcMovementAuthoringProvider`, outside the
native prefab build transaction. Its `Prepare` method owns only provider output;
its read-only `Validate` method recomputes the recipe fingerprint so donor,
controller, configuration, or tuning drift cannot pass merely because old
object references remain assigned. The Patch 6 native provider samples stable
flexed frames from the configured Humanoid locomotion clips and aligns only the
generated physical knee hinge bases before Marrow caches them. Joint limits,
the public Anatomy Profile, and the source Avatar remain unchanged.

Generated assets are outputs, never the source of truth. Rebuilding twice from
the same input and profiles must produce the same hierarchy and serialized
contract. Readiness and native-build input guards include the full ordered Audio
Profile content even while the definition is Silent, so later activation cannot
reuse a stale receipt and a provider cannot mutate authoring audio during a
build transaction.

Audio authoring and runtime Audio capability are separate contracts. The public
profile may be prepared without requesting provider support. A future or
patch-specific provider may advertise Audio only when it can map and validate
the requested fields in the saved prefab; merely having clips in a profile is
not evidence of runtime support. Generated NPC prefabs must not reference the
Audio Profile or toolkit runtime assembly.

Avatar Soft Body authoring and generated NPC secondary motion are likewise
separate contracts. The readiness doctor requires two distinct breast bones
below the accepted AnimationRoot, verifies that both are used by a source
SkinnedMeshRenderer, and confirms that each resolves to a canonical physical
owner. The provider owns spring-body creation and saved-prefab validation.
Toggling the option changes readiness and requested-capability identity so an
older native receipt cannot remain current.

The canonical 16-role collection is an authoring set and display order. The
native PowerLegs/PuppetMaster muscle array has its own explicit legs-first order;
providers must consume that contract and never derive runtime ordering from a
set or inspector list.

## Alignment safety

Automatic fitting instantiates the accepted Avatar only as a hidden read-only
measurement source. It writes body geometry, axes, limits, forces, and landmarks
to `NpcAnatomyProfile`, records the source dependency hash, and destroys the
measurement instance. Scene alignment draws Handles over the prefab stage but
does not add components or modify source transforms, renderers, meshes, or
materials.
