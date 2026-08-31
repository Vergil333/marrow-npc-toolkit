// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow, CylinderGrip). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names come from a working NPC bundle's typetree. A field that is
// missing here simply keeps the game class's default.

namespace SLZ.Marrow
{
    public class CylinderGrip : UnityEngine.MonoBehaviour
    {
        public byte isThrowable;
        public byte ignoreGripTargetOnAttach;
        public UnityEngine.Collider[] gripColliders;
        public UnityEngine.Collider[] additionalGripColliders;
        public UnityEngine.AnimationCurve handleAmplifyCurve;
        public SLZ.Marrow.HandPose handPose;
        public UnityEngine.Vector3 primaryMovementAxis;
        public UnityEngine.Vector3 secondaryMovementAxis;
        public int gripOptions;
        public float priority;
        public float minBreakForce;
        public float maxBreakForce;
        public float defaultGripDistance;
        public float gripDistanceSqr;
        public float radius;
        public UnityEngine.Transform targetTransform;
        public float rotationLimit;
        public float rotationPriorityBuffer;
        public SLZ.Marrow.HandPose handPoseOnFlippedPrimaryAxis;
        public byte targetFlipOnPrimaryAxis;
        public byte targetFlipOnTertiaryAxis;
        public float dynamicFriction;
        public float staticFriction;
        public float limit;
        public byte hasCapA;
        public byte hasCapB;
        public byte ignoreFlipOnZ;
        public float rotationalFrictionMult;
        public float aspectRatio;
        public byte variableRadius;
        public UnityEngine.AnimationCurve RadiusCurve;
    }
}
