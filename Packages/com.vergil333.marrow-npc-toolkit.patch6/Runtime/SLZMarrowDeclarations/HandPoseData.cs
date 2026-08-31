// Serialized layout only — the game's own class supplies the behaviour.
//
// **This was a MonoBehaviour.** That is not a subtle bug: a MonoBehaviour
// cannot exist as a standalone asset, so it could never have been assigned to
// `handPoser.OpenHand` at all. It is a ScriptableObject.
//
// No field layout is recoverable: the stock NPC references SLZ's four
// HandPoseData assets externally (m_FileID 70-73), so no bundle we can read
// carries a typetree for the type, and the OBB's deduped_assets_handpose folder
// holds only weapon-grip HandPose assets, which are a different type.
//
// That is acceptable here because the failure was a *null*, not bad content:
// `SubBehaviourHandPose.CopyPoseData(data, ...)` threw because `data` was null.
// A valid, empty asset of the right type gives it something to copy from —
// the hands land in a neutral pose rather than a authored one.

namespace SLZ.Marrow.Data
{
    public class HandPoseData : UnityEngine.ScriptableObject
    {
        public UnityEngine.Quaternion hand2;
        public UnityEngine.Vector3 handleInHandPos;
        public UnityEngine.Quaternion handleInHandRot;
        public UnityEngine.Quaternion thumb1;
        public UnityEngine.Vector3 thumb23;
        public UnityEngine.Quaternion index1;
        public UnityEngine.Vector3 index23;
        public UnityEngine.Quaternion middle1;
        public UnityEngine.Vector3 middle23;
        public UnityEngine.Quaternion ring1;
        public UnityEngine.Vector3 ring23;
        public UnityEngine.Quaternion pinky1;
        public UnityEngine.Vector3 pinky23;
    }
}
