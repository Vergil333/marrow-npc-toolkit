using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    public enum NpcAvatarRendererCategory
    {
        Unassigned,
        Body,
        Head,
        Hair,
    }

    public enum NpcOptionalAvatarRole
    {
        Neck2,
        LeftScapula,
        RightScapula,
        LeftCarpal,
        RightCarpal,
        LeftUpperArmTwist,
        RightUpperArmTwist,
        LeftForearmTwist,
        RightForearmTwist,
        LeftUpperThighTwist,
        RightUpperThighTwist,
    }

    [Serializable]
    public struct NpcHumanoidBoneBinding
    {
        [SerializeField] private HumanBodyBones role;
        [SerializeField] private string transformPath;

        public HumanBodyBones Role => role;
        public string TransformPath => transformPath;

        public NpcHumanoidBoneBinding(HumanBodyBones humanoidRole, string path)
        {
            role = humanoidRole;
            transformPath = path ?? string.Empty;
        }
    }

    [Serializable]
    public struct NpcAvatarRendererBinding
    {
        [SerializeField] private NpcAvatarRendererCategory category;
        [SerializeField] private string transformPath;

        public NpcAvatarRendererCategory Category => category;
        public string TransformPath => transformPath;

        public NpcAvatarRendererBinding(NpcAvatarRendererCategory rendererCategory, string path)
        {
            category = rendererCategory;
            transformPath = path ?? string.Empty;
        }
    }

    [Serializable]
    public struct NpcOptionalAvatarBinding
    {
        [SerializeField] private NpcOptionalAvatarRole role;
        [SerializeField] private string transformPath;

        public NpcOptionalAvatarRole Role => role;
        public string TransformPath => transformPath;

        public NpcOptionalAvatarBinding(NpcOptionalAvatarRole optionalRole, string path)
        {
            role = optionalRole;
            transformPath = path ?? string.Empty;
        }
    }

    [Serializable]
    public struct NpcAvatarLimbEllipse
    {
        [SerializeField] private Vector2 radii;
        [SerializeField] private Vector2 bias;

        public Vector2 Radii => radii;
        public Vector2 Bias => bias;

        public NpcAvatarLimbEllipse(float xRadius, float xBias, float zRadius, float zBias)
        {
            radii = new Vector2(xRadius, zRadius);
            bias = new Vector2(xBias, zBias);
        }
    }

    [Serializable]
    public struct NpcAvatarBodyFit
    {
        public float HeadTop;
        public float ChinY;
        public float UnderbustY;
        public float WaistY;
        public float HighHipsY;
        public float CrotchBottom;

        public float ForeheadWidth;
        public float JawWidth;
        public float NeckWidth;
        public float ChestWidth;
        public float WaistWidth;
        public float HighHipsWidth;
        public float HipsWidth;

        public Vector2 ForeheadDepth;
        public Vector2 JawDepth;
        public Vector2 NeckDepth;
        public Vector2 SternumDepth;
        public Vector2 ChestDepth;
        public Vector2 WaistDepth;
        public Vector2 HighHipsDepth;
        public Vector2 HipsDepth;
    }

    [CreateAssetMenu(
        fileName = "NpcAvatarSourceProfile",
        menuName = "Marrow NPC Toolkit/NPC Avatar Source Profile",
        order = 9)]
    public sealed class NpcAvatarSourceProfile : ScriptableObject
    {
        [Header("Source Contract")]
        [SerializeField] private GameObject avatarPrefab;
        [SerializeField] private string sourceAssetGuid;
        [SerializeField] private string sourceDependencyHash;
        [SerializeField] private string marrowSdkVersion;
        [SerializeField] private string animatorPath;

        [Header("Stable Prefab Paths")]
        [SerializeField] private List<NpcHumanoidBoneBinding> humanoidBones =
            new List<NpcHumanoidBoneBinding>();
        [SerializeField] private List<NpcAvatarRendererBinding> renderers =
            new List<NpcAvatarRendererBinding>();
        [SerializeField] private List<NpcOptionalAvatarBinding> optionalBones =
            new List<NpcOptionalAvatarBinding>();
        [SerializeField] private string leftWristPath;
        [SerializeField] private string rightWristPath;
        [SerializeField] private string eyeCenterOverridePath;
        [SerializeField] private string jawPath;

        [Header("Official Avatar Body Fit")]
        [SerializeField] private float eyeOffset;
        [SerializeField] private NpcAvatarBodyFit bodyFit;
        [SerializeField] private NpcAvatarLimbEllipse thighUpper;
        [SerializeField] private NpcAvatarLimbEllipse knee;
        [SerializeField] private NpcAvatarLimbEllipse calf;
        [SerializeField] private NpcAvatarLimbEllipse ankle;
        [SerializeField] private NpcAvatarLimbEllipse upperArm;
        [SerializeField] private NpcAvatarLimbEllipse elbow;
        [SerializeField] private NpcAvatarLimbEllipse forearm;
        [SerializeField] private NpcAvatarLimbEllipse wrist;

        public GameObject AvatarPrefab => avatarPrefab;
        public string SourceAssetGuid => sourceAssetGuid;
        public string SourceDependencyHash => sourceDependencyHash;
        public string MarrowSdkVersion => marrowSdkVersion;
        public string AnimatorPath => animatorPath;
        public IReadOnlyList<NpcHumanoidBoneBinding> HumanoidBones => humanoidBones;
        public IReadOnlyList<NpcAvatarRendererBinding> Renderers => renderers;
        public IReadOnlyList<NpcOptionalAvatarBinding> OptionalBones => optionalBones;
        public string LeftWristPath => leftWristPath;
        public string RightWristPath => rightWristPath;
        public string EyeCenterOverridePath => eyeCenterOverridePath;
        public string JawPath => jawPath;
        public float EyeOffset => eyeOffset;
        public NpcAvatarBodyFit BodyFit => bodyFit;
        public NpcAvatarLimbEllipse ThighUpper => thighUpper;
        public NpcAvatarLimbEllipse Knee => knee;
        public NpcAvatarLimbEllipse Calf => calf;
        public NpcAvatarLimbEllipse Ankle => ankle;
        public NpcAvatarLimbEllipse UpperArm => upperArm;
        public NpcAvatarLimbEllipse Elbow => elbow;
        public NpcAvatarLimbEllipse Forearm => forearm;
        public NpcAvatarLimbEllipse Wrist => wrist;

        public void SetSource(
            GameObject prefab,
            string guid,
            string dependencyHash,
            string sdkVersion,
            string animatorTransformPath)
        {
            avatarPrefab = prefab;
            sourceAssetGuid = guid ?? string.Empty;
            sourceDependencyHash = dependencyHash ?? string.Empty;
            marrowSdkVersion = sdkVersion ?? string.Empty;
            animatorPath = animatorTransformPath ?? string.Empty;
        }

        public void SetBindings(
            IEnumerable<NpcHumanoidBoneBinding> boneBindings,
            IEnumerable<NpcAvatarRendererBinding> rendererBindings,
            IEnumerable<NpcOptionalAvatarBinding> optionalBoneBindings,
            string leftWrist,
            string rightWrist,
            string eyeCenter,
            string jaw)
        {
            humanoidBones = new List<NpcHumanoidBoneBinding>(boneBindings);
            renderers = new List<NpcAvatarRendererBinding>(rendererBindings);
            optionalBones = new List<NpcOptionalAvatarBinding>(optionalBoneBindings);
            leftWristPath = leftWrist ?? string.Empty;
            rightWristPath = rightWrist ?? string.Empty;
            eyeCenterOverridePath = eyeCenter ?? string.Empty;
            jawPath = jaw ?? string.Empty;
        }

        public void SetBodyFit(
            float sourceEyeOffset,
            NpcAvatarBodyFit sourceBodyFit)
        {
            eyeOffset = sourceEyeOffset;
            bodyFit = sourceBodyFit;
        }

        public void SetLimbFit(
            NpcAvatarLimbEllipse sourceThighUpper,
            NpcAvatarLimbEllipse sourceKnee,
            NpcAvatarLimbEllipse sourceCalf,
            NpcAvatarLimbEllipse sourceAnkle,
            NpcAvatarLimbEllipse sourceUpperArm,
            NpcAvatarLimbEllipse sourceElbow,
            NpcAvatarLimbEllipse sourceForearm,
            NpcAvatarLimbEllipse sourceWrist)
        {
            thighUpper = sourceThighUpper;
            knee = sourceKnee;
            calf = sourceCalf;
            ankle = sourceAnkle;
            upperArm = sourceUpperArm;
            elbow = sourceElbow;
            forearm = sourceForearm;
            wrist = sourceWrist;
        }
    }
}
