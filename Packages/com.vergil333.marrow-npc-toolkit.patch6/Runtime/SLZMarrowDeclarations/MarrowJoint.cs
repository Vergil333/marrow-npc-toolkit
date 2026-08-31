// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow.Interaction, MarrowJoint). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names come from a working NPC bundle's typetree. A field that is
// missing here simply keeps the game class's default.

namespace SLZ.Marrow.Interaction
{
    public class MarrowJoint : UnityEngine.MonoBehaviour
    {
        // Same story as MarrowBody's: read on pool spawn, null without it.
        public ConfigJointInfo _defaultConfigJointInfo;

        public SLZ.Marrow.Interaction.MarrowBody _bodyA;
        public SLZ.Marrow.Interaction.MarrowBody _bodyB;
        public UnityEngine.ConfigurableJoint _configurableJoint;
        public SLZ.Marrow.Interaction.MarrowEntity _entity;
    }
}
