// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow.AI, AIBrain). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names come from a working NPC bundle's typetree. A field that is
// missing here simply keeps the game class's default.

namespace SLZ.Marrow.AI
{
    public class AIBrain : UnityEngine.MonoBehaviour
    {
        public SLZ.Marrow.Pool.Poolee _poolee;
        public SLZ.Marrow.PuppetMasta.BehaviourBaseNav behaviour;
        public SLZ.Marrow.PuppetMasta.PuppetMaster puppetMaster;
        public byte dontClearBaseConfig;
        public byte isDead;
    }
}
