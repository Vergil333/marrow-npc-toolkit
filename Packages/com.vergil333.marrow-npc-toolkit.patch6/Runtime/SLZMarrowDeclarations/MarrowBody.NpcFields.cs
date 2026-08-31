// The SDK ships MarrowBody as a *partial* class carrying only Entity and _tags.
// The game's build of the same class also serializes the physics references —
// _rigidbody, _colliders, _trackers — which is why a prefab authored against the
// public SDK cannot express them, and an NPC body ends up registered with no
// rigidbody or colliders attached to it.
//
// Being partial is what makes this fixable without touching SLZ's own file: the
// missing serialized fields are declared here, in the same class, so Unity lays
// them out under the names the game expects and typetree remapping fills them in
// at load. No behaviour is added — these are storage only.
namespace SLZ.Marrow.Interaction
{
    public partial class MarrowBody
    {
        // A pool restores this on spawn; absent, it arrives null and
        // OnPoolInitialize throws. Populated from the live Rigidbody.
        public RigidbodyInfo _defaultRigidbodyInfo;

        // Read by the zone culling system. Absent, MarrowEntity.OnCullResolve
        // throws every time this entity is culled or restored — which is what
        // made the head, and sometimes the whole body, vanish depending on
        // viewing distance and angle.
        public UnityEngine.Bounds _bounds;

        // Null `settings` here breaks grabbing session-wide; see TrackerSettings.cs.
        public TrackerSettings trackerSettings;

        [field: UnityEngine.SerializeField]
        public EntityTransformInfo InitInEntityTransform { get; set; }


        [UnityEngine.SerializeField] private UnityEngine.Rigidbody _rigidbody;
        [UnityEngine.SerializeField] private UnityEngine.Collider[] _colliders;
        [UnityEngine.SerializeField] private Tracker[] _trackers;
        [UnityEngine.SerializeField] private UnityEngine.Collider[] _triggers;
        [UnityEngine.SerializeField] private MarrowBody[] _bodiesToIgnore;
        [UnityEngine.SerializeField] private UnityEngine.Collider[] _collidersToIgnore;

        // Serialized as <Name>k__BackingField, which is not a name C# lets you
        // declare directly — an auto-property with [field: SerializeField]
        // produces exactly that layout. HasRigidbody is the important one: the
        // stock NPC sets it true on every body, and a body that reports having
        // no rigidbody has nothing for the interaction system to act on.
        [field: UnityEngine.SerializeField] public bool HasRigidbody { get; set; }
        [field: UnityEngine.SerializeField] public bool isCenterOfMassOverride { get; set; }
        [field: UnityEngine.SerializeField] public UnityEngine.Vector3 CenterOfMass { get; set; }
    }
}
