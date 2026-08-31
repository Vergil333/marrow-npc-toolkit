# Provider setup

The provider intentionally has two independent preflight boundaries:

1. Required Patch 6 component declarations must exist in the exact assembly
   identity used by BONELAB runtime serialization.
2. Required behavior, locomotion, pose, grip, and physics reference inputs must
   be selected explicitly from content the project owner may lawfully use.

The package never searches a disk for extracted content and never embeds a
developer machine's GUIDs.

Open **Project Settings > Marrow NPC Toolkit > Patch 6 Behaviour**. First click
**Install Patch 6 Project Declarations** and wait for Unity to compile the
eight scripts created at
`Assets/MarrowNpcToolkit/Patch6Declarations/AssemblyCSharp`. The package's 32
SDK-side declarations stay in the package and join the official `SLZ.Marrow`
assembly through an assembly-definition reference.

After compilation, assign the required project-local inputs and save the
settings. Then return to
**Tools > Marrow NPC Toolkit** and run Step 4. Every provider capability must
show Ready before Step 5A can generate a complete NPC.

Do not copy the development project's settings JSON: GUIDs are local to one
Unity project and its current values reference assets that are not distributed.

If an installed declaration differs from the package, the installer requires
confirmation and creates a dated backup before updating it. Removing the UPM
package does not delete the declarations from `Assets`; remove them explicitly
after uninstalling the provider if they are no longer needed.
