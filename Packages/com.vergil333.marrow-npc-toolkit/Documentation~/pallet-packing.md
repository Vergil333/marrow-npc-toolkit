# Step 5C — Pack the Pallet

Step 5C runs the official Marrow Pallet packer for the platform selected in the
NPC Build Profile. It packs the whole Pallet because its crates share generated
script data; it never tries to pack only one crate in isolation.

## What the modder does

1. Complete Step 5A and Step 5B.
2. Check whether the Build Profile targets **Quest / Android** or **Windows PC**.
3. Click **5C. Pack Pallet for ...**. The toolkit temporarily switches to the
   selected platform when needed, packs, and returns Unity to the platform you
   started from.
4. Read the result and use **Show Packed Files** if you want to inspect them.

The toolkit runs Marrow's project validation before changing platform. Output
lookup follows Marrow's evaluated active Addressables profile, so a valid
custom Pallet build path is checked and shown instead of assuming the default
`BuiltPallets` folder. Unity's starting platform is restored even when packing
fails; a restoration problem is reported as a failed Step 5C result instead of
being hidden.

When Windows content is cross-packed from macOS, Unity 2021.3.16f1 supplies a
Windows Mono build profile rather than a Windows IL2CPP cross-compiler. After
Marrow validation passes, the toolkit sets that profile while Standalone is
inactive, switches to Windows, packs, returns to the starting platform, and
only then restores the original Standalone scripting backend. If Windows or
another Standalone target is already active with IL2CPP, Step 5C asks you to
return Unity to Quest / Android once before trying again. This changes neither
the generated NPC assets nor the platform you started from.

## What a successful result means

The official packer returned without an error and the output contains exactly
one catalog JSON, one catalog hash, one Pallet JSON, one shared MonoScripts
bundle, and at least as many Spawnable bundles as Spawnable Crates in the
Pallet. Preview bundles are reported separately so missing spawn-gun thumbnails
are visible without being confused with the NPC's runtime bundle.

This proves the package files were produced. It does **not** prove BONELAB can
spawn the NPC, that locomotion and recovery work, or that pooling and grabs are
correct. Those checks require a cold in-game runtime test on each platform.
