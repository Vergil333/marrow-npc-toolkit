using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    /// <summary>
    /// Avatar-specific movement measurements and author-controlled locomotion
    /// tuning. The profile stays free of provider types; providers may keep a
    /// stable reference to their generated standing-pose asset through Object.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NpcMovementProfile",
        menuName = "Marrow NPC Toolkit/NPC Movement Profile",
        order = 12)]
    public sealed class NpcMovementProfile : ScriptableObject
    {
        public const float TypicalHitHealthFraction = 0.25f;
        public const float CombatPursuitThreshold = 0.5f;

        [Header("Automatic Humanoid Measurements")]
        [SerializeField] private float eyeHeight;
        [SerializeField] private float bodyHeight;
        [SerializeField] private float navHeight;
        [SerializeField] private float leftLegLength;
        [SerializeField] private float rightLegLength;
        [SerializeField] private float meanLegLength;
        [SerializeField] private float hipWidth;
        [SerializeField] private float stanceWidth;
        [SerializeField] private float soleHeight;
        [SerializeField] private float navRadius;
        [SerializeField] private float navBaseOffset;
        [SerializeField] private Vector3 leftFootForwardLocal;
        [SerializeField] private Vector3 rightFootForwardLocal;

        [Header("Movement Tuning")]
        [SerializeField] private float pelvisHeightOffset;
        [SerializeField] private float stanceWidthScale = 1f;
        [SerializeField] private float leftFootYawCorrectionDegrees;
        [SerializeField] private float rightFootYawCorrectionDegrees;
        [SerializeField] private float strideScale = 1f;
        [SerializeField] private float stepHeightScale = 1f;
        [SerializeField] private float stepRateScale = 1f;
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float acceleration = 2.6f;
        [SerializeField] private float angularSpeed = 120f;
        [SerializeField] private float stoppingDistance = 1f;

        [Header("Response To Damage")]
        [SerializeField, Range(0f, 1f)] private float startingHostility;
        [SerializeField, Range(0f, 1f)]
        private float hostilityAfterTypicalHit = 0.25f;

        [Header("Alignment Receipt")]
        [SerializeField] private NpcAlignmentState alignmentState;
        [SerializeField, HideInInspector] private string autoFitSourceDependencyHash;
        [SerializeField, HideInInspector] private string autoFitAuthoringFingerprint;
        [SerializeField, HideInInspector] private string autoFitToolkitVersion;

        [Header("Provider Receipt")]
        [SerializeField, HideInInspector] private Object providerStandingPose;
        [SerializeField, HideInInspector] private Object providerMovementConfig;
        [SerializeField, HideInInspector] private string providerRecipeFingerprint;

        public float EyeHeight => eyeHeight;
        public float BodyHeight => bodyHeight;
        public float NavHeight => navHeight;
        public float LeftLegLength => leftLegLength;
        public float RightLegLength => rightLegLength;
        public float MeanLegLength => meanLegLength;
        public float HipWidth => hipWidth;
        public float StanceWidth => stanceWidth;
        public float SoleHeight => soleHeight;
        public float NavRadius => navRadius;
        public float NavBaseOffset => navBaseOffset;
        public Vector3 LeftFootForwardLocal => leftFootForwardLocal;
        public Vector3 RightFootForwardLocal => rightFootForwardLocal;

        public float PelvisHeightOffset
        {
            get => pelvisHeightOffset;
            set => pelvisHeightOffset = value;
        }

        public float StanceWidthScale
        {
            get => stanceWidthScale;
            set => stanceWidthScale = value;
        }

        public float LeftFootYawCorrectionDegrees
        {
            get => leftFootYawCorrectionDegrees;
            set => leftFootYawCorrectionDegrees = value;
        }

        public float RightFootYawCorrectionDegrees
        {
            get => rightFootYawCorrectionDegrees;
            set => rightFootYawCorrectionDegrees = value;
        }

        public float StrideScale
        {
            get => strideScale;
            set => strideScale = value;
        }

        public float StepHeightScale
        {
            get => stepHeightScale;
            set => stepHeightScale = value;
        }

        public float StepRateScale
        {
            get => stepRateScale;
            set => stepRateScale = value;
        }

        public float WalkSpeed
        {
            get => walkSpeed;
            set => walkSpeed = value;
        }

        public float Acceleration
        {
            get => acceleration;
            set => acceleration = value;
        }

        public float AngularSpeed
        {
            get => angularSpeed;
            set => angularSpeed = value;
        }

        public float StoppingDistance
        {
            get => stoppingDistance;
            set => stoppingDistance = value;
        }

        /// <summary>
        /// PowerLegs aggression at spawn. Zero keeps the guided baseline
        /// friendly; advanced profiles may deliberately choose another value.
        /// </summary>
        public float StartingHostility
        {
            get => Mathf.Clamp01(startingHostility);
            set
            {
                startingHostility = Mathf.Clamp01(value);
                hostilityAfterTypicalHit = Mathf.Max(
                    startingHostility,
                    Mathf.Clamp01(hostilityAfterTypicalHit));
            }
        }

        /// <summary>
        /// Aggression reached when an NPC at StartingHostility receives damage
        /// equal to 25 percent of maximum health. This user-facing value is
        /// converted to PowerLegs' vengefulness by the native provider.
        /// </summary>
        public float HostilityAfterTypicalHit
        {
            get => Mathf.Clamp(
                hostilityAfterTypicalHit,
                StartingHostility,
                1f);
            set => hostilityAfterTypicalHit = Mathf.Clamp(
                value,
                StartingHostility,
                1f);
        }

        public float TypicalHitHostilityGain =>
            HostilityAfterTypicalHit - StartingHostility;

        /// <summary>
        /// PowerLegs applies damage / max health * vengefulness. A typical hit
        /// is defined as 25 percent of maximum health, so multiplying the
        /// requested gain by four reproduces the selected result exactly.
        /// </summary>
        public float RetaliationVengefulness =>
            TypicalHitHostilityGain / TypicalHitHealthFraction;

        public NpcAlignmentState AlignmentState
        {
            get => alignmentState;
            set => alignmentState = value;
        }

        public string AutoFitSourceDependencyHash =>
            autoFitSourceDependencyHash ?? string.Empty;
        public string AutoFitAuthoringFingerprint =>
            autoFitAuthoringFingerprint ?? string.Empty;
        public string AutoFitToolkitVersion => autoFitToolkitVersion ?? string.Empty;
        public Object ProviderStandingPose => providerStandingPose;
        public Object ProviderMovementConfig => providerMovementConfig;
        public string ProviderRecipeFingerprint =>
            providerRecipeFingerprint ?? string.Empty;

        public bool HasFittedMeasurements =>
            alignmentState != NpcAlignmentState.Unseeded;

        /// <summary>
        /// Restores deterministic public defaults and removes all fit/provider
        /// receipts. Newly created profiles intentionally remain Unseeded until
        /// the movement fitter measures the accepted Avatar.
        /// </summary>
        public void ResetToDefaults()
        {
            eyeHeight = 0f;
            bodyHeight = 0f;
            navHeight = 0f;
            leftLegLength = 0f;
            rightLegLength = 0f;
            meanLegLength = 0f;
            hipWidth = 0f;
            stanceWidth = 0f;
            soleHeight = 0f;
            navRadius = 0f;
            navBaseOffset = 0f;
            leftFootForwardLocal = Vector3.zero;
            rightFootForwardLocal = Vector3.zero;

            pelvisHeightOffset = 0f;
            stanceWidthScale = 1f;
            leftFootYawCorrectionDegrees = 0f;
            rightFootYawCorrectionDegrees = 0f;
            strideScale = 1f;
            stepHeightScale = 1f;
            stepRateScale = 1f;
            walkSpeed = 2f;
            acceleration = 2.6f;
            angularSpeed = 120f;
            stoppingDistance = 1f;
            startingHostility = 0f;
            hostilityAfterTypicalHit = 0.25f;

            alignmentState = NpcAlignmentState.Unseeded;
            autoFitSourceDependencyHash = string.Empty;
            autoFitAuthoringFingerprint = string.Empty;
            autoFitToolkitVersion = string.Empty;
            ClearProviderRecipe();
        }

        /// <summary>
        /// Stores one complete, internally consistent automatic fit. Mean leg
        /// length and direction normalization are owned here so every fitter and
        /// provider observes the same values.
        /// </summary>
        public void SetAutoFitMeasurements(
            float fittedEyeHeight,
            float fittedBodyHeight,
            float fittedNavHeight,
            float fittedLeftLegLength,
            float fittedRightLegLength,
            float fittedHipWidth,
            float fittedStanceWidth,
            float fittedSoleHeight,
            float fittedNavRadius,
            float fittedNavBaseOffset,
            Vector3 fittedLeftFootForwardLocal,
            Vector3 fittedRightFootForwardLocal,
            string sourceDependencyHash,
            string authoringFingerprint)
        {
            eyeHeight = fittedEyeHeight;
            bodyHeight = fittedBodyHeight;
            navHeight = fittedNavHeight;
            leftLegLength = fittedLeftLegLength;
            rightLegLength = fittedRightLegLength;
            meanLegLength = (fittedLeftLegLength + fittedRightLegLength) * 0.5f;
            hipWidth = fittedHipWidth;
            stanceWidth = fittedStanceWidth;
            soleHeight = fittedSoleHeight;
            navRadius = fittedNavRadius;
            navBaseOffset = fittedNavBaseOffset;
            leftFootForwardLocal = NormalizeDirection(
                fittedLeftFootForwardLocal);
            rightFootForwardLocal = NormalizeDirection(
                fittedRightFootForwardLocal);
            autoFitSourceDependencyHash = sourceDependencyHash ?? string.Empty;
            autoFitAuthoringFingerprint = authoringFingerprint ?? string.Empty;
            autoFitToolkitVersion = NpcToolkitVersion.Current;
            alignmentState = NpcAlignmentState.AutoFit;
            ClearProviderRecipe();
        }

        public void MarkReviewed()
        {
            if (alignmentState != NpcAlignmentState.Unseeded)
                alignmentState = NpcAlignmentState.Reviewed;
        }

        public bool AutoFitMatches(
            string sourceDependencyHash,
            string authoringFingerprint)
        {
            return HasFittedMeasurements
                   && string.Equals(
                       autoFitSourceDependencyHash,
                       sourceDependencyHash ?? string.Empty,
                       StringComparison.Ordinal)
                   && string.Equals(
                       autoFitAuthoringFingerprint,
                       authoringFingerprint ?? string.Empty,
                       StringComparison.Ordinal)
                   && string.Equals(
                       autoFitToolkitVersion,
                       NpcToolkitVersion.Current,
                       StringComparison.Ordinal);
        }

        public void SetProviderRecipe(
            Object standingPose,
            Object movementConfig,
            string recipeFingerprint)
        {
            providerStandingPose = standingPose;
            providerMovementConfig = movementConfig;
            providerRecipeFingerprint = (recipeFingerprint ?? string.Empty).Trim();
        }

        public void ClearProviderRecipe()
        {
            providerStandingPose = null;
            providerMovementConfig = null;
            providerRecipeFingerprint = string.Empty;
        }

        private void OnValidate()
        {
            startingHostility = Mathf.Clamp01(startingHostility);
            hostilityAfterTypicalHit = Mathf.Clamp(
                hostilityAfterTypicalHit,
                startingHostility,
                1f);
        }

        private static Vector3 NormalizeDirection(Vector3 value)
        {
            if (!IsFinite(value) || value.sqrMagnitude <= 0.000001f)
                return value;
            return value.normalized;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
