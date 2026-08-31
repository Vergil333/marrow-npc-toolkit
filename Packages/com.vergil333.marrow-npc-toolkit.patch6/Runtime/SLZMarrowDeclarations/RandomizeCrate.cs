using SLZ.Marrow.Warehouse;
using UnityEngine;

namespace SLZ.Marrow.Zones
{
    // Patch 6 field layout recovered from the shipped SLZ.Marrow assembly.
    // The editor needs the declaration so it can serialize this component;
    // BONELAB resolves it to its own native implementation at runtime.
    [RequireComponent(typeof(CrateSpawner))]
    public class RandomizeCrate : SpawnDecorator
    {
        public SpawnableCrateReference[] crates;

        [Tooltip("If this is part of a CrateSpawnSequencer set this to false")]
        public bool spawnOnStart;
    }
}
