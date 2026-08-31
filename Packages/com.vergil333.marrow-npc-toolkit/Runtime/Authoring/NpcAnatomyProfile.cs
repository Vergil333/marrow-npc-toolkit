using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    public enum NpcColliderShape
    {
        Capsule,
        Box,
        Sphere,
    }

    public enum NpcAlignmentState
    {
        Unseeded,
        AutoFit,
        Reviewed,
    }

    public enum NpcJointMotion
    {
        Locked,
        Limited,
        Free,
    }

    [Serializable]
    public sealed class NpcBodyRoleProfile
    {
        [SerializeField] private HumanBodyBones role;
        [SerializeField] private bool enabled = true;
        [SerializeField] private NpcAlignmentState alignmentState;

        [Header("Collider")]
        [SerializeField] private bool autoFitCollider = true;
        [SerializeField] private NpcColliderShape colliderShape = NpcColliderShape.Capsule;
        [SerializeField] private Vector3 colliderCenter;
        [SerializeField] private Quaternion colliderLocalRotation = Quaternion.identity;
        [SerializeField] private Vector3 colliderSize;
        [SerializeField] private float capsuleRadius;
        [SerializeField] private float capsuleHeight;
        [SerializeField, Range(0, 2)] private int capsuleDirection = 1;

        [Header("Body and Joint")]
        [SerializeField, Min(0f)] private float massKilograms;
        [SerializeField] private Vector3 jointAxis = Vector3.right;
        [SerializeField] private Vector3 jointSecondaryAxis = Vector3.up;
        [SerializeField] private NpcJointMotion angularXMotion = NpcJointMotion.Limited;
        [SerializeField] private NpcJointMotion angularYMotion = NpcJointMotion.Limited;
        [SerializeField] private NpcJointMotion angularZMotion = NpcJointMotion.Limited;
        [SerializeField] private Vector3 angularLowLimits;
        [SerializeField] private Vector3 angularHighLimits;
        [SerializeField, Min(0f)] private float jointDriveMaxForce;

        [Header("Muscle")]
        [SerializeField, Min(0f)] private float muscleSpring;
        [SerializeField, Min(0f)] private float muscleDamper;
        [SerializeField, Range(0f, 1f)] private float muscleWeight = 1f;

        public HumanBodyBones Role => role;
        public bool Enabled { get => enabled; set => enabled = value; }
        public NpcAlignmentState AlignmentState
        {
            get => alignmentState;
            set => alignmentState = value;
        }
        public bool AutoFitCollider { get => autoFitCollider; set => autoFitCollider = value; }
        public NpcColliderShape ColliderShape { get => colliderShape; set => colliderShape = value; }
        public Vector3 ColliderCenter { get => colliderCenter; set => colliderCenter = value; }
        public Quaternion ColliderLocalRotation
        {
            get => Mathf.Abs(colliderLocalRotation.x) < 0.000001f
                   && Mathf.Abs(colliderLocalRotation.y) < 0.000001f
                   && Mathf.Abs(colliderLocalRotation.z) < 0.000001f
                   && Mathf.Abs(colliderLocalRotation.w) < 0.000001f
                ? Quaternion.identity
                : colliderLocalRotation;
            set => colliderLocalRotation = value;
        }
        public Vector3 ColliderSize { get => colliderSize; set => colliderSize = value; }
        public float CapsuleRadius { get => capsuleRadius; set => capsuleRadius = value; }
        public float CapsuleHeight { get => capsuleHeight; set => capsuleHeight = value; }
        public int CapsuleDirection { get => capsuleDirection; set => capsuleDirection = value; }
        public float MassKilograms { get => massKilograms; set => massKilograms = value; }
        public Vector3 JointAxis { get => jointAxis; set => jointAxis = value; }
        public Vector3 JointSecondaryAxis { get => jointSecondaryAxis; set => jointSecondaryAxis = value; }
        public NpcJointMotion AngularXMotion { get => angularXMotion; set => angularXMotion = value; }
        public NpcJointMotion AngularYMotion { get => angularYMotion; set => angularYMotion = value; }
        public NpcJointMotion AngularZMotion { get => angularZMotion; set => angularZMotion = value; }
        public Vector3 AngularLowLimits { get => angularLowLimits; set => angularLowLimits = value; }
        public Vector3 AngularHighLimits { get => angularHighLimits; set => angularHighLimits = value; }
        public float JointDriveMaxForce { get => jointDriveMaxForce; set => jointDriveMaxForce = value; }
        public float MuscleSpring { get => muscleSpring; set => muscleSpring = value; }
        public float MuscleDamper { get => muscleDamper; set => muscleDamper = value; }
        public float MuscleWeight { get => muscleWeight; set => muscleWeight = value; }

        public NpcBodyRoleProfile(HumanBodyBones humanoidRole)
        {
            role = humanoidRole;
        }
    }

    [CreateAssetMenu(
        fileName = "NpcAnatomyProfile",
        menuName = "Marrow NPC Toolkit/NPC Anatomy Profile",
        order = 11)]
    public sealed class NpcAnatomyProfile : ScriptableObject
    {
        public static readonly HumanBodyBones[] CanonicalHumanoidRoles =
            NpcHumanoidGraph.CanonicalRoles.ToArray();

        [SerializeField] private List<NpcBodyRoleProfile> bodyRoles = new List<NpcBodyRoleProfile>();
        [SerializeField] private NpcBodyRoleProfile optionalJaw =
            new NpcBodyRoleProfile(HumanBodyBones.Jaw) { Enabled = false };

        [Header("Character Landmarks")]
        [SerializeField] private Vector3 leftSoleLocal;
        [SerializeField] private Vector3 rightSoleLocal;
        [SerializeField] private Vector3 eyeCenterLocal;
        [SerializeField] private Vector3 jawClosedReferenceLocal;
        // Added after the original position-only landmark. Keep the older field
        // intact so existing Anatomy assets deserialize without migration.
        [SerializeField] private Quaternion jawClosedLocalRotation = Quaternion.identity;

        [Header("Baseline Receipt")]
        [SerializeField, HideInInspector] private string baselineSourceDependencyHash;
        [SerializeField, HideInInspector] private string baselineToolkitVersion;

        public IReadOnlyList<NpcBodyRoleProfile> BodyRoles => bodyRoles;
        public NpcBodyRoleProfile OptionalJaw => optionalJaw;
        public Vector3 LeftSoleLocal { get => leftSoleLocal; set => leftSoleLocal = value; }
        public Vector3 RightSoleLocal { get => rightSoleLocal; set => rightSoleLocal = value; }
        public Vector3 EyeCenterLocal { get => eyeCenterLocal; set => eyeCenterLocal = value; }
        public Vector3 JawClosedReferenceLocal
        {
            get => jawClosedReferenceLocal;
            set => jawClosedReferenceLocal = value;
        }
        public Quaternion JawClosedLocalRotation
        {
            get => Mathf.Abs(jawClosedLocalRotation.x) < 0.000001f
                   && Mathf.Abs(jawClosedLocalRotation.y) < 0.000001f
                   && Mathf.Abs(jawClosedLocalRotation.z) < 0.000001f
                   && Mathf.Abs(jawClosedLocalRotation.w) < 0.000001f
                ? Quaternion.identity
                : jawClosedLocalRotation;
            set => jawClosedLocalRotation = value;
        }
        public string BaselineSourceDependencyHash => baselineSourceDependencyHash;
        public string BaselineToolkitVersion => baselineToolkitVersion;
        public int FittedRoleCount => bodyRoles == null
            ? 0
            : bodyRoles.Count(IsRoleFitted);
        public bool HasFittedBaseline => HasCanonicalRoleSet()
                                         && FittedRoleCount == CanonicalHumanoidRoles.Length;

        public void ResetToHumanoidDefaults()
        {
            bodyRoles = CanonicalHumanoidRoles
                .Select(CreateDefaultRole)
                .ToList();
            optionalJaw = CreateDefaultJaw();
            jawClosedReferenceLocal = Vector3.zero;
            jawClosedLocalRotation = Quaternion.identity;
            baselineSourceDependencyHash = string.Empty;
            baselineToolkitVersion = string.Empty;
        }

        public bool HasCanonicalRoleSet()
        {
            if (bodyRoles == null || bodyRoles.Count != CanonicalHumanoidRoles.Length)
                return false;

            var actual = new HashSet<HumanBodyBones>(bodyRoles.Select(value => value.Role));
            return actual.Count == CanonicalHumanoidRoles.Length
                   && CanonicalHumanoidRoles.All(actual.Contains);
        }

        public void SeedMissingHumanoidDefaults()
        {
            if (!HasCanonicalRoleSet())
            {
                ResetToHumanoidDefaults();
                return;
            }

            foreach (NpcBodyRoleProfile role in bodyRoles)
            {
                NpcBodyRoleProfile seed = CreateDefaultRole(role.Role);
                if (role.MassKilograms <= 0f) role.MassKilograms = seed.MassKilograms;
                if (role.MuscleSpring <= 0f) role.MuscleSpring = seed.MuscleSpring;
                if (role.MuscleDamper <= 0f) role.MuscleDamper = seed.MuscleDamper;
                if (role.JointDriveMaxForce <= 0f)
                    role.JointDriveMaxForce = seed.JointDriveMaxForce;
                if (role.AlignmentState != NpcAlignmentState.Unseeded) continue;
                role.MuscleWeight = seed.MuscleWeight;
                role.AngularXMotion = seed.AngularXMotion;
                role.AngularYMotion = seed.AngularYMotion;
                role.AngularZMotion = seed.AngularZMotion;
                role.AngularLowLimits = seed.AngularLowLimits;
                role.AngularHighLimits = seed.AngularHighLimits;
                role.ColliderShape = seed.ColliderShape;
            }

            if (optionalJaw == null || optionalJaw.Role != HumanBodyBones.Jaw)
                optionalJaw = CreateDefaultJaw();
            else
            {
                NpcBodyRoleProfile jawSeed = CreateDefaultJaw();
                if (optionalJaw.MassKilograms <= 0f)
                    optionalJaw.MassKilograms = jawSeed.MassKilograms;
                if (optionalJaw.MuscleSpring <= 0f)
                    optionalJaw.MuscleSpring = jawSeed.MuscleSpring;
                if (optionalJaw.MuscleDamper <= 0f)
                    optionalJaw.MuscleDamper = jawSeed.MuscleDamper;
                if (optionalJaw.JointDriveMaxForce <= 0f)
                    optionalJaw.JointDriveMaxForce = jawSeed.JointDriveMaxForce;
                if (optionalJaw.AlignmentState == NpcAlignmentState.Unseeded)
                {
                    optionalJaw.MuscleWeight = jawSeed.MuscleWeight;
                    optionalJaw.AngularXMotion = jawSeed.AngularXMotion;
                    optionalJaw.AngularYMotion = jawSeed.AngularYMotion;
                    optionalJaw.AngularZMotion = jawSeed.AngularZMotion;
                    optionalJaw.AngularLowLimits = jawSeed.AngularLowLimits;
                    optionalJaw.AngularHighLimits = jawSeed.AngularHighLimits;
                    optionalJaw.ColliderShape = jawSeed.ColliderShape;
                }
            }
        }

        public NpcBodyRoleProfile FindRole(HumanBodyBones role)
        {
            return bodyRoles?.FirstOrDefault(value => value.Role == role);
        }

        public void MarkBaselineFitted(string sourceDependencyHash)
        {
            baselineSourceDependencyHash = sourceDependencyHash ?? string.Empty;
            baselineToolkitVersion = NpcToolkitVersion.Current;
        }

        public bool BaselineMatches(string sourceDependencyHash)
        {
            return HasFittedBaseline
                   && string.Equals(
                       baselineToolkitVersion,
                       NpcToolkitVersion.Current,
                       StringComparison.Ordinal)
                   && string.Equals(
                       baselineSourceDependencyHash,
                       sourceDependencyHash ?? string.Empty,
                       StringComparison.Ordinal);
        }

        private static bool IsRoleFitted(NpcBodyRoleProfile role)
        {
            if (role == null || !role.Enabled || role.MassKilograms <= 0f
                             || role.AlignmentState == NpcAlignmentState.Unseeded)
                return false;
            if (role.ColliderShape == NpcColliderShape.Capsule)
                return role.CapsuleRadius > 0f
                       && role.CapsuleHeight >= role.CapsuleRadius * 2f;
            if (role.ColliderShape == NpcColliderShape.Sphere)
                return role.CapsuleRadius > 0f;
            Vector3 size = role.ColliderSize;
            return size.x > 0f && size.y > 0f && size.z > 0f;
        }

        private static NpcBodyRoleProfile CreateDefaultRole(HumanBodyBones role)
        {
            var value = new NpcBodyRoleProfile(role)
            {
                MassKilograms = 65f * MassFraction(role),
                MuscleSpring = 5000000f,
                MuscleDamper = role == HumanBodyBones.Hips ? 70000f : 100000f,
                MuscleWeight = MuscleWeight(role),
                JointDriveMaxForce = JointForce(role),
            };

            SetJointContract(value, role);
            if (role == HumanBodyBones.LeftHand || role == HumanBodyBones.RightHand
                || role == HumanBodyBones.LeftFoot || role == HumanBodyBones.RightFoot)
                value.ColliderShape = NpcColliderShape.Box;
            return value;
        }

        private static NpcBodyRoleProfile CreateDefaultJaw()
        {
            // Keep the dormant defaults behavior-compatible with the original
            // 16-body authoring path. The accepted jaw contract is applied only
            // when Physical Jaw is requested and successfully fit.
            var jaw = new NpcBodyRoleProfile(HumanBodyBones.Jaw)
            {
                Enabled = false,
                MassKilograms = 1.1765f,
                MuscleSpring = 5000000f,
                MuscleDamper = 100000f,
                MuscleWeight = 0.1f,
            };
            return jaw;
        }

        private static void SetJointContract(NpcBodyRoleProfile value, HumanBodyBones role)
        {
            if (role == HumanBodyBones.Hips)
            {
                value.AngularXMotion = NpcJointMotion.Free;
                value.AngularYMotion = NpcJointMotion.Free;
                value.AngularZMotion = NpcJointMotion.Free;
                return;
            }

            value.AngularXMotion = NpcJointMotion.Limited;
            value.AngularYMotion = NpcJointMotion.Limited;
            value.AngularZMotion = NpcJointMotion.Limited;
            float xLow;
            float xHigh;
            float y;
            float z;
            switch (role)
            {
                case HumanBodyBones.Spine:
                    xLow = -30f; xHigh = 10f; y = 25f; z = 35f; break;
                case HumanBodyBones.Chest:
                    xLow = -30f; xHigh = 10f; y = 25f; z = 15f; break;
                case HumanBodyBones.Head:
                    xLow = -50f; xHigh = 50f; y = 45f; z = 45f; break;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                    xLow = -35f; xHigh = 135f; y = 70f; z = 70f; break;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                    xLow = -36.9f; xHigh = 103.1f; y = 18f; z = 18f; break;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                    xLow = -45f; xHigh = 25f; y = 72f; z = 72f; break;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                    xLow = -35f; xHigh = 120f; y = 35f; z = 45f; break;
                case HumanBodyBones.LeftLowerLeg:
                    xLow = -150f; xHigh = 0f; y = 10f; z = 14.1f;
                    value.AngularYMotion = NpcJointMotion.Locked;
                    value.AngularZMotion = NpcJointMotion.Locked;
                    break;
                case HumanBodyBones.RightLowerLeg:
                    xLow = -140f; xHigh = 0f; y = 10f; z = 14.1f;
                    value.AngularYMotion = NpcJointMotion.Locked;
                    value.AngularZMotion = NpcJointMotion.Locked;
                    break;
                default:
                    xLow = -45f; xHigh = 40f; y = 30f; z = 30f; break;
            }
            value.AngularLowLimits = new Vector3(xLow, -y, -z);
            value.AngularHighLimits = new Vector3(xHigh, y, z);
        }

        private static float MassFraction(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.Hips: return 0.1694f;
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest: return 0.1572f;
                case HumanBodyBones.Head: return 0.0549f;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm: return 0.027f;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm: return 0.016f;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand: return 0.0066f;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg: return 0.0988f;
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg: return 0.0465f;
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return 0.0267f;
                default: return 0f;
            }
        }

        private static float MuscleWeight(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest: return 0.75f;
                case HumanBodyBones.Head: return 0.5f;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm: return 0.3f;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm: return 0.2f;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand: return 0.1f;
                default: return 1f;
            }
        }

        private static float JointForce(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.Hips: return 1260f;
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest: return 500f;
                case HumanBodyBones.Head: return 80f;
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm: return 100f;
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm: return 80f;
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand: return 40f;
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg: return 720f;
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg: return 540f;
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot: return 400f;
                default: return 0f;
            }
        }
    }
}
