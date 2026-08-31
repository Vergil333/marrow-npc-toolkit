# Official SDK clean-install fixture

This directory records the supported clean-project test but deliberately does
not vendor Unity, Marrow SDK, BONELAB content, or a generated Unity `Library`.

For each preview candidate:

1. Create a new Unity 2021.3.16f1 Marrow SDK 1.2.0 project.
2. Install both packages from the exact candidate tag or `.tgz` artifacts.
3. Run all package Editor tests.
4. Complete provider preflight without copying the development project's GUID
   settings.
5. Rebuild the reference NPC from the exact packages and cold-test the platform
   advertised as runtime-supported.

Before claiming general Humanoid or Windows runtime coverage, also complete a
freely distributable non-Eve test and a clean Windows BONELAB test. Those claim
gates may remain deferred only while the public documentation says so.

Record the exact results in the GitHub Release notes. Do not commit the created
Unity project or proprietary test inputs here.

The public fixture includes three command-line probes under `Assets/Editor`:

- `ReleaseSmokeProbe` verifies the clean declaration bootstrap, assembly type
  ownership, and single registered Patch 6 provider.
- `ProviderPreflightProbe` accepts project-local reference asset paths as
  command-line inputs, writes fresh GUID settings, and verifies that the
  provider supplies every required capability. Its JSON output can contain
  private input paths and GUIDs, so keep that output outside the repository.
- `ExactCandidateBuildProbe` rebuilds, validates, prepares, packs, and records
  a supplied project-local NPC Definition for either Quest or Windows. The NPC
  Definition and generated pallet remain private test inputs and outputs.
