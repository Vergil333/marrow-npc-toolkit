// As with MarrowBody, the SDK ships MarrowEntity as a *partial* class carrying
// only _tags, while the game's build also serializes the entity's registry of
// what belongs to it: its bodies, its joints, the anchor body and the poolee.
//
// That registry is what makes an entity more than a marker. Without _bodies, the
// interaction system sees an entity owning nothing, and no amount of correctly
// wired MarrowBody components makes the object grabbable.
//
// Declared here so a prefab can express them. Storage only, no behaviour.
namespace SLZ.Marrow.Interaction
{
    public partial class MarrowEntity
    {
        [UnityEngine.SerializeField] private MarrowBody[] _bodies;
        [UnityEngine.SerializeField] private MarrowJoint[] _joints;
        [UnityEngine.SerializeField] private MarrowBody _anchorBody;
        [UnityEngine.SerializeField] private SLZ.Marrow.Pool.Poolee _poolee;
        [UnityEngine.SerializeField] private MarrowBehaviour[] _behaviours;
        [UnityEngine.SerializeField] private UnityEngine.Vector3 _originalScale;
    }
}
