// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow.PuppetMasta, SkinnedBoneRebind). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names come from a working NPC bundle's typetree. A field that is
// missing here simply keeps the game class's default.

namespace SLZ.Marrow.PuppetMasta
{
    public class SkinnedBoneRebind : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Transform[] bones;
        public byte[] rebindBone;
        public UnityEngine.SkinnedMeshRenderer skinnedMeshRenderer;
        public UnityEngine.Mesh meshToRead;
        public UnityEngine.Mesh meshToWrite;
    }
}
