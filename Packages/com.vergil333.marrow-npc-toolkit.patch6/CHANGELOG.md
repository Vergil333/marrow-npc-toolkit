# Changelog

## 0.5.0-preview.1

- Created the separately versioned public Patch 6 provider package.
- Moved the existing exact-schema provider source behind its own Editor
  assembly and stable public provider identity.
- Kept every donor/reference input project-local and excluded all extracted
  BONELAB content from the package.
- Added 32 declaration-only schemas to the official `SLZ.Marrow` assembly via
  `.asmref` and an explicit, backup-aware installer for the eight required
  `Assembly-CSharp` project declarations.
- Declared the maintained Extended SDK unsupported for this exact provider.
