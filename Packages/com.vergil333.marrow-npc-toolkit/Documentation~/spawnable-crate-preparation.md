# Step 5B — Prepare Spawnable Crate

Step 5B turns a successfully generated native NPC prefab into an official
Marrow `SpawnableCrate` inside a `Pallet`. It does not pack or publish the
Pallet.

## Author workflow

1. Finish Step 4 and run **5A. Generate Native NPC Prefab**.
2. If the generated NPC is open in Prefab Mode, use **Return to Main Scene**
   before updating it. Unity will handle any Save/Discard/Cancel choice.
3. Confirm Step 5 says **Prefab generated**. If it says **Rebuild 5A**, run 5A
   once to synchronize changed authoring settings; Physics Alignment does not
   need to be repeated.
4. Run **5B. Prepare Spawnable Crate**.
5. Check the Pallet title/barcode, Spawnable Crate title/barcode, and native
   Main Asset shown in the toolkit window.

On the first run, the toolkit uses `Pallet.CreatePallet` and
`Crate.CreateCrateT<SpawnableCrate>` and saves both assets under the Build
Profile's generated folder. Their Unity asset GUIDs are then stored in hidden
Build Profile fields. Runtime authoring data stores GUID strings only and does
not depend on Marrow types.

Later runs resolve only those two saved GUIDs. They update public metadata and
the Crate Main Asset, restore all `Crate.Pallet` backlinks, and keep both
existing barcodes unchanged. Titles are never used to find or replace assets.
If a saved GUID is missing, points at the wrong type, or only one binding is
present, Step 5B stops and explains the binding problem.

The write is transactional. The Pallet, Crate, and Build Profile are reloaded
and checked after saving. A failed update restores their previous bytes and
GUIDs; a failed first creation removes its newly created assets and empty
folders.

The packaging fingerprint contains public metadata, target platform, both
asset bindings, and stable native-receipt identity. It excludes the receipt's
timestamp and Unity's platform/cache-specific imported prefab hash, so an
otherwise identical native rebuild does not create needless packaging churn.
The separate native-receipt validator still rejects a prefab whose current
dependency hash no longer matches its latest successful build.

Step 5B and 5C remain disabled unless the native receipt matches the current
NPC Definition, profiles, requested features, and generated prefab. This check
is read-only; it prevents an older generated NPC from being packaged after an
authoring change.
