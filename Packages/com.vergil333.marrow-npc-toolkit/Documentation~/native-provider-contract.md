# Native NPC Build Provider Contract

The public package never references patch-derived `Assembly-CSharp` types. A
project-local provider implements `INpcNativeBuildProvider`, discovers the
exact supported types and serialized fields in `Probe()`, and uses reflection
or `SerializedObject` only inside its build and validation callbacks.

## Ownership boundary

The toolkit owns:

- Step 4 readiness and capability requirements;
- deterministic provider selection;
- two isolated generation passes;
- the unpacked Physics Preview clone supplied as `OutputRoot`;
- staging, prefab saving, fingerprint comparison, commit, cleanup, and rollback;
- preservation of an existing output prefab's `.meta` file and GUID;
- a durable package-owned native-build receipt stored beside the output prefab.

The provider owns:

- one exact compatibility profile and a stable provider ID;
- native component creation and serialized reference binding;
- explicit runtime ordering such as PuppetMaster muscles;
- a semantic structural fingerprint covering every native field it writes;
- read-only validation of the saved and reloaded prefab using that same
  fingerprint;
- clear provider messages when the detected patch contract is incomplete.

A provider must mutate only `context.OutputRoot`. It must not save, import,
move, or delete assets, and it must treat `context.Definition` and all referenced
authoring/source assets as read-only. `OutputRoot` is completely unpacked before
the callback, so changes cannot accidentally be applied to the Physics Preview
or source Avatar prefab.

## Selection

`NpcNativeBuildProviderRegistry.Resolve()` matches the Build Profile's exact
compatibility profile ID, probes required capabilities, and refuses ambiguity.
If more than one capable provider matches, the caller must supply the exact
provider ID. Type discovery order is never used as an implicit preference.

An anatomy-shell provider can initially advertise only `CoreAnatomy`. It must
not claim AI, pooling, grips, gaze, jaw, audio, or secondary motion until it
actually creates and validates those contracts. `SecondaryMotion` means the
provider reads the two populated Breast Soft Body bone assignments from the
source Marrow Avatar, generates its own spring-driven output bodies, and
includes every generated binding and tuning value in its semantic fingerprint.
It does not imply abdomen or butt Soft Body support.

## Transaction

`NpcNativeBuildCoordinator.Build()` performs this sequence:

1. Run the read-only Step 4 physics doctor.
2. Resolve one matching provider with the requested capabilities.
3. Snapshot authoring JSON, dirty state, and input dependency hashes.
4. Configure, save, force-import, reload, and provider-validate Pass 1 in an
   isolated staging folder. Require its post-save semantic fingerprint to equal
   the provider's pre-save fingerprint and reject validation-time mutation.
5. Recheck every guarded input.
6. Repeat the same configure/save/reload/validate flow for Pass 2 from a fresh
   preview instance.
7. Recheck inputs and compare core plus provider fingerprints.
8. Commit Pass 1 only if both pass receipts match.
9. After the committed prefab reloads, create its durable
   `<PrefabName>.NativeBuildReceipt.asset` sidecar. It records the NPC Definition
   asset/GUID, definition and build-input fingerprints, provider ID, requested
   capabilities, compatibility profile, prefab GUID/dependency hash, provider
   and combined output fingerprints, toolkit version, and UTC build time.
10. Treat the prefab and durable receipt as one transaction. On rebuild, replace
    only their serialized bytes while retaining both old `.meta` files/GUIDs. If
    either asset cannot be written, imported, reloaded, or validated, restore
    both previous assets; a failed first build leaves neither output behind.
11. Delete both staging passes in all outcomes.

No generated prefab or new durable receipt is committed when validation,
probing, provider execution, input protection, or determinism fails. A
successful durable receipt is still only editor-generation proof; packing and
BONELAB runtime testing remain separate.

## Durable receipt boundary

`NpcNativeBuildReceiptUtility.LoadForPrefab()` locates the sidecar and
`Validate()` checks it without importing, saving, dirtying, or repairing any
asset. Validation proves that the recorded prefab still exists at the recorded
path and retains the recorded GUID and dependency hash. Callers may also supply
the currently expected Definition, definition fingerprint, build-input
fingerprint, provider ID, and capability set to reject a stale receipt.

The receipt intentionally stores `DefinitionFingerprint` separately from
`InputFingerprint`. The coordinator supplies the Step 4 readiness fingerprint
as the former. Readiness/native input includes the generated asset folder and
exact compatibility identity because those choose the output/provider contract,
but excludes publication `Version` and `TargetPlatform`.

`NpcPackagingFingerprintUtility.Compute()` owns the separate packing identity.
It covers author, pallet/crate titles, description, version, target platform,
generated folder, compatibility identity, and stable native-receipt fields.
Receipt asset bytes, `BuiltAtUtc`, and Unity's imported prefab dependency hash
are intentionally excluded: two successful rebuilds with identical native
prefab GUID, provider contract, and semantic output fingerprint produce the same
packaging fingerprint. The receipt validator still compares the current prefab
dependency hash with its recorded value before Step 5B, so this separation does
not weaken edit detection.

Providers are trusted Unity Editor code, not a security sandbox. The coordinator
detects normal serialized authoring changes and restores in-memory
ScriptableObject state, but it cannot guarantee rollback if a provider violates
the contract by directly writing source files or calling arbitrary AssetDatabase
save APIs. Install only reviewed providers; the public API prohibition on asset
writes is part of their compatibility contract.

## Minimal provider shape

```csharp
internal sealed class PatchProvider : INpcNativeBuildProvider
{
    public string ProviderId => "vendor.patch.provider";
    public string DisplayName => "Patch Provider";
    public string CompatibilityProfileId => "exact-contract-id";

    public NpcCompatibilityProbeResult Probe()
    {
        // Resolve and verify every required type/field without changing assets.
        return NpcCompatibilityProbeResult.Available(
            NpcCompatibilityCapabilities.CoreAnatomy);
    }

    public NpcNativeBuildProviderResult ConfigureStagedPrefab(
        NpcNativeBuildContext context)
    {
        // Add native components by Type and bind them with SerializedObject.
        // Modify context.OutputRoot only; never call AssetDatabase.SaveAssets().
        return NpcNativeBuildProviderResult.Succeeded(
            "semantic-hash-of-all-provider-owned-bindings");
    }

    public NpcNativeBuildProviderResult ValidateSavedPrefab(
        NpcNativeBuildValidationContext context)
    {
        // Inspect the reloaded prefab without mutating it. Re-resolve every
        // owned component/reference/array and recompute the identical hash.
        return NpcNativeBuildProviderResult.Succeeded(
            "semantic-hash-of-all-provider-owned-bindings");
    }

}
```
