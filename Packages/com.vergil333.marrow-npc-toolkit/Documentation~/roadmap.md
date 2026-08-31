# Implementation Roadmap

## Milestone 1 — Avatar intake

- Accept AvatarCrate, Marrow Avatar prefab, and raw model inputs.
- Validate the official required Humanoid contract.
- Hand off body fitting to the official Avatar editor.
- Persist source, anatomy, and build profiles.

## Milestone 2 — NPC baseline and alignment

- Done: canonical role graph, source-drift checks, Avatar-fit collider baseline,
  deterministic fingerprint, target-rig/collider Scene overlay, Undo-aware
  profile editing, protected reviewed roles, and a separate Unity-only physics
  preview hierarchy without runtime-only NPC components.
- Done: root-space side mirroring, high-heel-safe Foot fitting, selected-shape
  X-ray rendering, guided review, and optional majority-weighted Physical Jaw
  authoring with dynamic 16/17-body previews.
- Next: add joint-limit arcs and foot/contact rays, then broaden skin-weighted
  fitting and per-module tuning profiles.

## Milestone 3 — NPC Doctor and deterministic build

- Done: read-only preflight for source drift, anatomy geometry/axes/limits,
  preview sibling roots, the connected 16-body graph, renderer preservation,
  deterministic issue receipts, and native-provider capability reporting.
- Done in the separately distributed Patch 6 provider: dynamic 16/17-body native
  anatomy, AI/PowerLegs, locomotion, health, pooling, body grabs, renderer
  rebinding, gaze, optional Physical Jaw, profile-driven NPC audio, and opt-in
  breast Secondary Motion sourced from the Avatar's two Breast Soft Body bones.
- Done: two fresh builds, post-save/reload semantic validation, deterministic
  fingerprints, GUID-preserving prefab/receipt commit, rollback, and a
  transient reference-Avatar editor smoke without character-name branches.
- Next: keep adding provider-negative fixtures and clean-project coverage. A
  successful editor transaction still must not be described as runtime proof.

## Milestone 4 — Generalization gate

- In progress: the fitter and native provider now consume provider-neutral
  Humanoid measurements without character-name branches. Cross-avatar proof
  still requires a suitable second reference Humanoid and an in-game movement
  matrix.
- Keep the official-SDK clean-install fixture current. Treat a pinned Extended
  environment as a future, separately implemented provider route.
- Provide a freely distributable sample and troubleshooting documentation.

## Milestone 5 — Build and proof

- Done: create/update GUID-bound Pallets and SpawnableCrates through supported
  APIs while preserving barcodes, then pack the selected target and validate
  the catalog, shared scripts, and Spawnable bundle inventory.
- Report editor validation, packing, spawn verification, interaction
  verification, and author approval as separate statuses.
- Run the live matrix: stand, walk/turn/chase, damage, grabs, knockdown/recovery,
  death, despawn/respawn, pooling, multiple NPCs, audio, and optional modules.

Packing alone is never treated as runtime proof.
