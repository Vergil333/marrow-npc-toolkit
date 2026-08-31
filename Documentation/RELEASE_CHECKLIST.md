# Release checklist

The first three sections are required before publishing a preview. Later
generalization and Windows sections gate those broader claims; they may remain
open only while the release documentation states that coverage is pending.

## Source and legal boundary

- [x] README, changelogs, compatibility, known issues, and preview release notes
      are current for the tested local candidate.
- [x] No game assemblies, extracted prefabs, animations, audio, textures,
      models, hand poses, generated pallets, or private character assets exist.
- [x] License and third-party notices are present.
- [x] Declaration-only compatibility source provenance and publication boundary
      have been reviewed by the repository owner.

Any change under `Packages/` invalidates the recorded archive hashes and
requires repeating the exact-package clean install, provider preflight, and
runtime candidate checks.

## Clean project

- [x] Install the official Marrow SDK 1.2.0 into a clean Unity 2021.3.16f1
      project.
- [x] Install the core and provider from the exact candidate `.tgz` artifacts.
- [x] Install/update the eight project declarations and verify they resolve
      from `Assembly-CSharp`; verify the 32 SDK-side declarations resolve from
      the unmodified official `SLZ.Marrow` assembly.
- [x] Complete provider preflight without relying on the development project.
- [x] Run all Editor tests.

## Exact Quest candidate proof

- [x] Build a fresh Eve NPC from the exact candidate package installation.
- [x] Pack and inspect Quest output.
- [ ] Cold-install and spawn-test the exact output on Quest.
- [ ] Verify movement, damage, knockdown/recovery, death, pooling, grabs, gaze,
      optional jaw, audio, and requested Secondary Motion.

## General humanoid claim gate

This section may remain open for an experimental preview only while public
documentation explicitly says that general Humanoid coverage is unproven.

- [ ] Complete the workflow with a freely distributable non-Eve Humanoid.
- [ ] Verify fit, movement, damage, knockdown/recovery, death, pooling, grabs,
      gaze, optional jaw, audio, and any requested Secondary Motion.

## Windows runtime claim gate

This section may remain open only while Windows runtime verification is listed
as pending and no Windows runtime claim is made.

- [x] Pack and inspect Windows output.
- [ ] Cold-install and test the exact Windows output.

## GitHub Release

- [x] Create both deterministic `.tgz` artifacts.
- [x] Generate SHA-256 checksums.
- [x] Verify installation from each attached artifact.
- [ ] Commit the final validation evidence and verify the tagged commit is
      reachable from `main`.
- [ ] Create the GitHub repository, push `main`, and enable Private
      Vulnerability Reporting.
- [ ] Create an immutable version tag.
- [ ] Verify the tag points at the exact tested commit and that the core and
      provider versions match it.
- [ ] Let the tag workflow create a draft prerelease, then verify its archive
      hashes match the locally tested hashes.
- [ ] Verify both version-pinned Git URLs after the tag exists.
- [x] Add release notes with the current exact Quest status, Eve-only proof scope,
      hostile-pursuit and gaze limitations, Windows status, required SDK/game
      versions, and both SHA-256 values.
- [ ] Mark pre-1.0 releases as GitHub pre-releases.
- [ ] Keep the GitHub release in Draft until required legal and advertised
      runtime checks are accepted.
- [ ] Protect `v*` tags from update or deletion before publishing the draft.
