// NOTE: derives from MarrowBehaviour, not MonoBehaviour. MarrowEntity._behaviours
// is a MarrowBehaviour[]; while these stubs derived from MonoBehaviour, Unity
// silently refused every assignment into that array and shipped 17 nulls.
// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow, InteractableHost). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names come from a working NPC bundle's typetree. A field that is
// missing here simply keeps the game class's default.

namespace SLZ.Marrow
{
    public class InteractableHost : SLZ.Marrow.Interaction.MarrowBehaviour
    {
        // The grab system. Absent, it arrives null on every host and breaks
        // grabbing session-wide — see VirtualController.cs.
        [field: UnityEngine.SerializeField]
        public SLZ.Marrow.Interaction.VirtualController VirtualController { get; set; }

        public SLZ.Marrow.Interaction.MarrowEntity marrowEntity;
        public SLZ.Marrow.InteractableHostManager manager;
        public byte ignoreBodyOnGrab;
        [field: UnityEngine.SerializeField] public byte IsStatic { get; set; }
    }
}
