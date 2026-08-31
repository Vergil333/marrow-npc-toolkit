// Serialized layout only — the game's own class supplies the behaviour.
//
// `MarrowBody.trackerSettings` describes the tracker volumes the interaction
// layer builds when something is grabbed. Our stub omitted the field entirely,
// so the struct was absent from our serialized data and `settings` arrived as a
// **null array** rather than an empty one — the same failure shape as
// `MarrowEntity._behaviours` and `BehaviourPowerLegs.sensors`. Anything that
// indexes it throws, and because grabbing runs through trackers, that breaks
// grabbing for the whole session, not just for this NPC.
//
// Field names, order and types are read off the stock NPC's typetree
// (peasantfemaleb.bundle), which carries three entries with only the first
// active.

using System;
using UnityEngine;

namespace SLZ.Marrow.Interaction
{
    [Serializable]
    public class TrackerSetting
    {
        public bool isActive;
        public int layer;
        public int type;
        public Vector3 center;
        public Vector3 size;
        public float radius;
        public float height;
        public int direction;
    }

    /// Position+rotation the body is restored to inside its entity on spawn.
    [Serializable]
    public class EntityTransformInfo
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public class TrackerSettings
    {
        public int layers;
        public TrackerSetting[] settings;
    }
}
