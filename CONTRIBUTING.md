# Contributing

Contributions should preserve the toolkit's provider boundary and deterministic
build guarantees.

1. Do not commit extracted game content, proprietary character content,
   generated pallets, build output, Unity `Library`, or local project settings.
2. Keep patch-specific code in its matching compatibility provider.
3. Add or update an Editor test for behavior changes.
4. Update the relevant changelog and documentation.
5. State exactly which Unity, Marrow SDK, BONELAB patch, platform, and runtime
   behavior were tested.

Bug reports should include the toolkit/provider versions, Unity version, SDK
source and version, target platform, readiness messages, and whether the result
was observed in Unity or in BONELAB.
