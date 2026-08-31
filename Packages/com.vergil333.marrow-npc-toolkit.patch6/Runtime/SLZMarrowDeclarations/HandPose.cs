// Hand pose asset. Written by hand rather than generated: a stock NPC references
// its poses externally instead of embedding them, so the generator observed no
// instance and emitted an empty class — and Unity then discarded every field of
// the pose assets on import, leaving HandPoseAnimator.GetHandleInHandNeutral
// dereferencing nothing.
//
// A ScriptableObject, not a MonoBehaviour: these are .asset files.
//
// The eight fields and their order are read from the typetree of a stock hand
// pose in the game's own bundle. Order matters — Unity serialises in declaration
// order, and the layout has to match what the game expects.
namespace SLZ.Marrow
{
    public class HandPose : UnityEngine.ScriptableObject
    {
        public UnityEngine.Vector3 primaryAxis;
        public UnityEngine.Vector3 secondaryAxis;
        public UnityEngine.Vector3 wristOffset;
        public float swingRotationLimit;
        public float twistRotationLimit;
        public PoseDataGroup[] poseData;
        public UnityEngine.Quaternion leftOffsetRotation;
        public UnityEngine.Quaternion rightOffsetRotation;
    }
}
