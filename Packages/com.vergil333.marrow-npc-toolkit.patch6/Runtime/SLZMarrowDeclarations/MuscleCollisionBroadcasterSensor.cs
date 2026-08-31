// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow.PuppetMasta, MuscleCollisionBroadcasterSensor). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names come from a working NPC bundle's typetree. A field that is
// missing here simply keeps the game class's default.

namespace SLZ.Marrow.PuppetMasta
{
    public class MuscleCollisionBroadcasterSensor : UnityEngine.MonoBehaviour
    {
        public SLZ.Marrow.PuppetMasta.PuppetMaster puppetMaster;
        public int muscleIndex;
        public byte isGrounded;
        public UnityEngine.Vector3 groundNormal;
        public UnityEngine.Vector3 _totalImpulse;
        public float totalMass;
        public float additionalMass;
    }
}
