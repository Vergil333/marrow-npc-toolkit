using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Movement;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Immutable movement values consumed by one native-build pass. A present
    /// Movement Profile is authoritative and must carry a current provider
    /// recipe. Definitions created before movement authoring existed retain an
    /// explicit, warned legacy path until they are upgraded.
    /// </summary>
    internal sealed class Patch6MovementBuildSettings
    {
        private const float PowerRoamRatio = 1.35f / 2f;
        private const float PowerAgroRatio = 1.25f / 2f;
        private const float PowerEngagedRatio = 1f / 2f;
        private const float ConfigRoamRatio = 1.9f / 2f;
        private const float ConfigAgroRatio = 1.75f / 2f;
        private const float BehaviourAngularRatio = 180f / 120f;
        public NpcMovementProfile Profile { get; }
        public UnityEngine.Object StandingPose { get; }
        public UnityEngine.Object MovementConfig { get; }
        public string ProviderRecipeFingerprint { get; }
        public bool UsesLegacyFallback { get; }

        public float MeanLegLength { get; }
        public float SoleHeight { get; }
        public float NavRadius { get; }
        public float NavHeight { get; }
        public float NavBaseOffset { get; }
        public Vector3 LeftFootForwardLocal { get; }
        public Vector3 RightFootForwardLocal { get; }
        public float PelvisHeightOffset { get; }
        public float StanceWidthScale { get; }
        public float LeftFootYawCorrectionDegrees { get; }
        public float RightFootYawCorrectionDegrees { get; }
        public float StrideScale { get; }
        public float StepHeightScale { get; }
        public float StepRateScale { get; }
        public float WalkSpeed { get; }
        public float Acceleration { get; }
        public float AngularSpeed { get; }
        public float StoppingDistance { get; }
        public float StartingHostility { get; }
        public float HostilityAfterTypicalHit { get; }
        public float RetaliationVengefulness { get; }
        public float PowerRoamSpeed => WalkSpeed * PowerRoamRatio;
        public float PowerAgroSpeed => WalkSpeed * PowerAgroRatio;
        public float PowerEngagedSpeed => WalkSpeed * PowerEngagedRatio;
        public float PowerAngularSpeed => AngularSpeed * BehaviourAngularRatio;
        public float ConfigRoamSpeed => WalkSpeed * ConfigRoamRatio;
        public float ConfigAgroSpeed => WalkSpeed * ConfigAgroRatio;
        public float ConfigAngularSpeed => AngularSpeed * BehaviourAngularRatio;

        private Patch6MovementBuildSettings(
            NpcMovementProfile profile,
            UnityEngine.Object standingPose,
            UnityEngine.Object movementConfig,
            string providerRecipeFingerprint,
            bool usesLegacyFallback)
        {
            Profile = profile;
            StandingPose = standingPose;
            MovementConfig = movementConfig;
            ProviderRecipeFingerprint = providerRecipeFingerprint ?? string.Empty;
            UsesLegacyFallback = usesLegacyFallback;
            if (profile == null)
            {
                // Preserve the existing native shell contract for definitions
                // which predate movement authoring.
                StartingHostility = 0f;
                HostilityAfterTypicalHit = 1f;
                RetaliationVengefulness = 10f;
                return;
            }

            MeanLegLength = profile.MeanLegLength;
            SoleHeight = profile.SoleHeight;
            NavRadius = profile.NavRadius;
            NavHeight = profile.NavHeight;
            NavBaseOffset = profile.NavBaseOffset;
            LeftFootForwardLocal = profile.LeftFootForwardLocal;
            RightFootForwardLocal = profile.RightFootForwardLocal;
            PelvisHeightOffset = profile.PelvisHeightOffset;
            StanceWidthScale = profile.StanceWidthScale;
            LeftFootYawCorrectionDegrees = profile.LeftFootYawCorrectionDegrees;
            RightFootYawCorrectionDegrees = profile.RightFootYawCorrectionDegrees;
            StrideScale = profile.StrideScale;
            StepHeightScale = profile.StepHeightScale;
            StepRateScale = profile.StepRateScale;
            WalkSpeed = profile.WalkSpeed;
            Acceleration = profile.Acceleration;
            AngularSpeed = profile.AngularSpeed;
            StoppingDistance = profile.StoppingDistance;
            StartingHostility = profile.StartingHostility;
            HostilityAfterTypicalHit = profile.HostilityAfterTypicalHit;
            RetaliationVengefulness = profile.RetaliationVengefulness;
        }

        internal static Patch6MovementBuildSettings Resolve(
            NpcDefinition definition,
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved legacy,
            Type baseConfigType,
            Type enemyPoseType,
            bool physicalJaw,
            bool logLegacyWarning)
        {
            if (definition == null)
                throw new InvalidOperationException(
                    "Movement resolution requires an NPC Definition.");
            NpcMovementProfile profile = definition.MovementProfile;
            if (profile == null)
            {
                if (logLegacyWarning)
                    Debug.LogWarning(
                        "PATCH6_MOVEMENT_LEGACY_FALLBACK: This NPC Definition "
                        + "predates Movement Profiles. The native build is using "
                        + "the project-level standing pose, config, navigation, "
                        + "and gait values. Create and prepare a Movement Profile "
                        + "before treating locomotion as Avatar-specific.");
                return new Patch6MovementBuildSettings(
                    null,
                    legacy.StandingIdle,
                    legacy.BaseEnemyConfig,
                    "legacy-project-behaviour-settings",
                    true);
            }

            ValidatePersistent(profile, typeof(NpcMovementProfile),
                "Movement Profile");
            string currentSourceHash =
                MarrowNpcToolkitPatch6CompatibilityProbe
                    .CurrentMovementSourceDependencyHash(definition);
            string currentAuthoringFingerprint =
                MarrowNpcToolkitPatch6CompatibilityProbe
                    .CurrentMovementAuthoringFingerprint(definition);
            if (!profile.HasFittedMeasurements
                || !profile.AutoFitMatches(
                    currentSourceHash,
                    currentAuthoringFingerprint))
                throw new InvalidOperationException(
                    "The Movement Profile is missing or stale. Refit movement "
                    + "from the accepted Avatar before building the native NPC.");
            ValidateProfileValues(profile);
            ValidatePersistent(
                profile.ProviderStandingPose,
                enemyPoseType,
                "provider standing pose");
            ValidatePersistent(
                profile.ProviderMovementConfig,
                baseConfigType,
                "provider movement config");
            if (string.IsNullOrWhiteSpace(profile.ProviderRecipeFingerprint))
                throw new InvalidOperationException(
                    "The Movement Profile has no provider recipe. Prepare its "
                    + "Patch 6 movement assets before building the native NPC.");

            ValidatePoseCount(
                profile.ProviderStandingPose, physicalJaw ? 17 : 16);
            string expectedRecipe =
                MarrowNpcToolkitPatch6CompatibilityProbe
                    .ComputeMovementRecipeFingerprint(
                        definition,
                        profile,
                        profile.ProviderStandingPose,
                        profile.ProviderMovementConfig,
                        legacy);
            if (!string.Equals(
                    expectedRecipe,
                    profile.ProviderRecipeFingerprint,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The Movement Profile provider recipe is stale. Prepare "
                    + "Patch 6 movement again before building the native NPC.");

            return new Patch6MovementBuildSettings(
                profile,
                profile.ProviderStandingPose,
                profile.ProviderMovementConfig,
                profile.ProviderRecipeFingerprint,
                false);
        }

        internal static void ValidateProfileValues(NpcMovementProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            foreach (KeyValuePair<string, float> value in
                     new Dictionary<string, float>
                     {
                         ["MeanLegLength"] = profile.MeanLegLength,
                         ["NavRadius"] = profile.NavRadius,
                         ["NavHeight"] = profile.NavHeight,
                         ["StanceWidthScale"] = profile.StanceWidthScale,
                         ["StrideScale"] = profile.StrideScale,
                         ["StepHeightScale"] = profile.StepHeightScale,
                         ["StepRateScale"] = profile.StepRateScale,
                         ["WalkSpeed"] = profile.WalkSpeed,
                         ["Acceleration"] = profile.Acceleration,
                         ["AngularSpeed"] = profile.AngularSpeed,
                     })
                if (!Finite(value.Value) || value.Value <= 0f)
                    throw new InvalidOperationException(
                        "Movement Profile " + value.Key
                        + " must be positive and finite.");
            if (!Finite(profile.StoppingDistance)
                || profile.StoppingDistance < 0f)
                throw new InvalidOperationException(
                    "Movement Profile StoppingDistance must be nonnegative "
                    + "and finite.");
            if (!Finite(profile.StartingHostility)
                || !Finite(profile.HostilityAfterTypicalHit)
                || profile.StartingHostility < 0f
                || profile.StartingHostility > 1f
                || profile.HostilityAfterTypicalHit
                    < profile.StartingHostility
                || profile.HostilityAfterTypicalHit > 1f
                || !Finite(profile.RetaliationVengefulness)
                || profile.RetaliationVengefulness < 0f
                || profile.RetaliationVengefulness > 4f)
                throw new InvalidOperationException(
                    "Movement Profile hostility must stay between zero and "
                    + "one, and the post-hit value cannot be lower than its "
                    + "starting value.");
            foreach (KeyValuePair<string, float> value in
                     new Dictionary<string, float>
                     {
                         ["SoleHeight"] = profile.SoleHeight,
                         ["NavBaseOffset"] = profile.NavBaseOffset,
                         ["PelvisHeightOffset"] = profile.PelvisHeightOffset,
                         ["LeftFootYawCorrectionDegrees"] =
                             profile.LeftFootYawCorrectionDegrees,
                         ["RightFootYawCorrectionDegrees"] =
                             profile.RightFootYawCorrectionDegrees,
                     })
                if (!Finite(value.Value))
                    throw new InvalidOperationException(
                        "Movement Profile " + value.Key + " must be finite.");
            foreach (KeyValuePair<string, Vector3> value in
                     new Dictionary<string, Vector3>
                     {
                         ["LeftFootForwardLocal"] = profile.LeftFootForwardLocal,
                         ["RightFootForwardLocal"] = profile.RightFootForwardLocal,
                     })
                if (!Finite(value.Value)
                    || Vector3.ProjectOnPlane(value.Value, Vector3.up)
                           .sqrMagnitude < 0.5f)
                    throw new InvalidOperationException(
                        "Movement Profile " + value.Key
                        + " must be a finite horizontal direction.");
        }

        private static void ValidatePoseCount(
            UnityEngine.Object pose,
            int expected)
        {
            var serialized = new SerializedObject(pose);
            SerializedProperty positions = serialized.FindProperty("posePositions");
            SerializedProperty rotations = serialized.FindProperty("poseRotations");
            if (positions == null || rotations == null
                || !positions.isArray || !rotations.isArray
                || positions.arraySize != expected
                || rotations.arraySize != expected)
                throw new InvalidOperationException(
                    "The provider standing pose must contain exactly " + expected
                    + " positions and rotations in native muscle order.");
        }

        private static void ValidatePersistent(
            UnityEngine.Object value,
            Type expectedType,
            string label)
        {
            if (value == null || expectedType == null
                || !expectedType.IsInstanceOfType(value)
                || !EditorUtility.IsPersistent(value)
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(value)))
                throw new InvalidOperationException(
                    "The " + label + " is not the expected persistent asset.");
        }

        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool Finite(Vector3 value)
        {
            return Finite(value.x) && Finite(value.y) && Finite(value.z);
        }
    }

    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        public NpcMovementAuthoringValidationResult Validate(
            NpcDefinition definition,
            NpcMovementProfile profile)
        {
            try
            {
                ValidateMovementAuthoringRequest(definition, profile);
                BehaviourTypes types = BehaviourTypes.Resolve();
                MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                    RequireBehaviourSettings(
                        types, definition.IncludePhysicalJaw);
                ValidateBehaviourTemplate(settings.BehaviourTemplate, types);
                ValidateLocomotionReference(settings.LocomotionReference, types);
                ValidateBehaviourController(settings.AnimatorController);
                ValidateBaseEnemyConfig(settings.BaseEnemyConfig);
                Patch6MovementBuildSettings movement =
                    Patch6MovementBuildSettings.Resolve(
                        definition,
                        settings,
                        types.BaseEnemyConfig,
                        types.EnemyPoseData,
                        definition.IncludePhysicalJaw,
                        false);
                return NpcMovementAuthoringValidationResult.Current(
                    movement.ProviderRecipeFingerprint,
                    "Patch 6 movement assets and recipe match the current "
                    + "Avatar, measurements, controller, and donor settings.");
            }
            catch (Exception exception)
            {
                return NpcMovementAuthoringValidationResult.Stale(
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        public NpcMovementAuthoringResult Prepare(
            NpcDefinition definition,
            NpcMovementProfile profile)
        {
            ProtectedAssetState profileSnapshot = null;
            ScriptableObject pose = null;
            ScriptableObject config = null;
            ProtectedAssetState poseBackup = null;
            ProtectedAssetState configBackup = null;
            string posePath = string.Empty;
            string configPath = string.Empty;
            bool poseCreated = false;
            bool configCreated = false;
            ProtectedMovementSources protectedSources = null;
            try
            {
                ValidateMovementAuthoringRequest(definition, profile);
                profileSnapshot = ProtectedAssetState.Capture(profile, false);
                BehaviourTypes types = BehaviourTypes.Resolve();
                MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings =
                    RequireBehaviourSettings(types, definition.IncludePhysicalJaw);
                ValidateBehaviourTemplate(settings.BehaviourTemplate, types);
                ValidateLocomotionReference(settings.LocomotionReference, types);
                ValidateBehaviourController(settings.AnimatorController);
                ValidateBaseEnemyConfig(settings.BaseEnemyConfig);
                protectedSources =
                    ProtectedMovementSources.Capture(
                        definition, settings, ResolveConfiguredIdleClip(
                            settings.AnimatorController));
                string profilePath = AssetDatabase.GetAssetPath(profile)
                    .Replace('\\', '/');
                string folder = Path.GetDirectoryName(profilePath)
                    ?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(folder))
                    throw new InvalidOperationException(
                        "The Movement Profile must be saved under Assets.");
                string profileGuid = AssetDatabase.AssetPathToGUID(profilePath);
                if (string.IsNullOrWhiteSpace(profileGuid))
                    throw new InvalidOperationException(
                        "The Movement Profile has no stable asset GUID.");
                string stem = SanitizeMovementAssetName(
                    Path.GetFileNameWithoutExtension(profilePath))
                    + "." + profileGuid.Substring(0, 12);
                posePath = folder + "/" + stem + ".Patch6StandingPose.asset";
                configPath = folder + "/" + stem + ".Patch6MovementConfig.asset";

                pose = LoadOrCreateDerivedAsset(
                    posePath,
                    profile,
                    settings.StandingIdle,
                    types.EnemyPoseData,
                    out poseCreated,
                    out poseBackup);
                config = LoadOrCreateDerivedAsset(
                    configPath,
                    profile,
                    settings.BaseEnemyConfig,
                    types.BaseEnemyConfig,
                    out configCreated,
                    out configBackup);

                CaptureConfiguredIdlePose(
                    definition,
                    settings.AnimatorController,
                    pose);
                ConfigureMovementConfig(config, profile);
                EditorUtility.SetDirty(pose);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssetIfDirty(pose);
                AssetDatabase.SaveAssetIfDirty(config);
                AssetDatabase.ImportAsset(posePath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.ImportAsset(configPath, ImportAssetOptions.ForceUpdate);
                pose = AssetDatabase.LoadAssetAtPath(posePath, types.EnemyPoseData)
                    as ScriptableObject;
                config = AssetDatabase.LoadAssetAtPath(configPath, types.BaseEnemyConfig)
                    as ScriptableObject;
                if (pose == null || config == null)
                    throw new InvalidOperationException(
                        "Unity did not reload both generated movement assets.");
                RequireMainObjectNameMatchesFilename(pose, posePath);
                RequireMainObjectNameMatchesFilename(config, configPath);

                string fingerprint = ComputeMovementRecipeFingerprint(
                    definition, profile, pose, config, settings);
                protectedSources.RequireUnchanged();
                profileSnapshot.RequireUnchanged();
                profile.SetProviderRecipe(pose, config, fingerprint);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                if (profile.ProviderStandingPose != pose
                    || profile.ProviderMovementConfig != config
                    || !string.Equals(
                        profile.ProviderRecipeFingerprint,
                        fingerprint,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "The Movement Profile did not retain the complete provider recipe.");

                return NpcMovementAuthoringResult.Succeeded(
                    fingerprint,
                    "Prepared a per-Avatar Patch 6 Idle2 standing pose and "
                    + "movement config without modifying the source Avatar or "
                    + "shared behaviour assets.");
            }
            catch (Exception exception)
            {
                var failureMessages = new List<string>
                {
                    exception.GetType().Name + ": " + exception.Message,
                };
                CaptureRollbackFailure(failureMessages, "standing pose", () =>
                    RollBackDerivedAsset(
                        posePath, poseBackup, poseCreated));
                CaptureRollbackFailure(failureMessages, "movement config", () =>
                    RollBackDerivedAsset(
                        configPath, configBackup, configCreated));
                if (protectedSources != null)
                    CaptureRollbackFailure(
                        failureMessages,
                        "protected source assets",
                        protectedSources.Restore);
                if (profile != null)
                    CaptureRollbackFailure(failureMessages, "Movement Profile", () =>
                        profileSnapshot?.Restore());
                return NpcMovementAuthoringResult.Failed(
                    failureMessages.ToArray());
            }
        }

        private static void CaptureRollbackFailure(
            ICollection<string> messages,
            string label,
            Action rollback)
        {
            try
            {
                rollback();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                messages.Add(
                    "ROLLBACK_FAILED " + label + ": "
                    + exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void ValidateMovementAuthoringRequest(
            NpcDefinition definition,
            NpcMovementProfile profile)
        {
            if (definition == null || profile == null
                || definition.MovementProfile != profile)
                throw new InvalidOperationException(
                    "Movement preparation requires the Movement Profile assigned "
                    + "to the selected NPC Definition.");
            if (definition.SourceAvatar == null
                || definition.AvatarSourceProfile == null
                || definition.AnatomyProfile == null
                || definition.BuildProfile == null)
                throw new InvalidOperationException(
                    "The NPC Definition is missing a source, mapping, anatomy, or "
                    + "build profile required by movement preparation.");
            if (!profile.HasFittedMeasurements
                || !profile.AutoFitMatches(
                    CurrentMovementSourceDependencyHash(definition),
                    CurrentMovementAuthoringFingerprint(definition)))
                throw new InvalidOperationException(
                    "Fit movement from the current accepted Avatar before "
                    + "preparing Patch 6 provider assets.");
            Patch6MovementBuildSettings.ValidateProfileValues(profile);
            if (!EditorUtility.IsPersistent(profile)
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(profile)))
                throw new InvalidOperationException(
                    "Save the Movement Profile under Assets before preparing it.");
        }

        private static ScriptableObject LoadOrCreateDerivedAsset(
            string path,
            NpcMovementProfile requestedOwner,
            UnityEngine.Object donor,
            Type expectedType,
            out bool created,
            out ProtectedAssetState backup)
        {
            created = false;
            backup = null;
            string assetName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(assetName))
                throw new InvalidOperationException(
                    "A derived movement asset requires a filename.");
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && !expectedType.IsInstanceOfType(existing))
                throw new InvalidOperationException(
                    path + " already exists with an incompatible asset type.");
            RequireUnsharedDerivedAsset(existing, path, requestedOwner);
            var value = existing as ScriptableObject;
            if (value == null)
            {
                value = ScriptableObject.CreateInstance(expectedType);
                value.name = assetName;
                AssetDatabase.CreateAsset(value, path);
                created = true;
            }
            else
            {
                // Capture both saved bytes/meta and any unsaved in-memory state.
                // A transient CopySerialized clone cannot recover an existing
                // asset exactly if Unity fails to reload it after the write.
                backup = ProtectedAssetState.Capture(value, false);
            }
            EditorUtility.CopySerialized(donor, value);
            value.name = assetName;
            return value;
        }

        private static void RequireMainObjectNameMatchesFilename(
            UnityEngine.Object value,
            string path)
        {
            string expected = Path.GetFileNameWithoutExtension(path);
            if (value == null
                || !string.Equals(value.name, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Derived movement asset " + path
                    + " must use its filename as the Unity main-object name.");
        }

        private static void RollBackDerivedAsset(
            string path,
            ProtectedAssetState backup,
            bool created)
        {
            if (created && !string.IsNullOrWhiteSpace(path))
            {
                AssetDatabase.DeleteAsset(path);
                if (AssetDatabase.LoadMainAssetAtPath(path) != null
                    || !string.IsNullOrWhiteSpace(
                        AssetDatabase.AssetPathToGUID(path))
                    || ProtectedAssetState.AssetOrMetaFileExists(path))
                    throw new InvalidOperationException(
                        "Movement preparation could not remove newly created "
                        + "derived asset " + path + " during rollback.");
                return;
            }
            if (backup != null)
                backup.Restore();
        }

        private static void RequireUnsharedDerivedAsset(
            UnityEngine.Object existing,
            string path,
            NpcMovementProfile requestedOwner)
        {
            if (existing == null) return;
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:NpcMovementProfile"))
            {
                string profilePath = AssetDatabase.GUIDToAssetPath(guid);
                NpcMovementProfile owner = AssetDatabase.LoadAssetAtPath<
                    NpcMovementProfile>(profilePath);
                if (owner != null && owner != requestedOwner
                    && (owner.ProviderStandingPose == existing
                        || owner.ProviderMovementConfig == existing))
                    throw new InvalidOperationException(
                        "Derived movement asset " + path + " is still owned by "
                        + profilePath + "; refusing to overwrite it.");
            }
        }

        private static void ConfigureMovementConfig(
            ScriptableObject config,
            NpcMovementProfile profile)
        {
            var serialized = new SerializedObject(config);
            SetFloat(serialized, "roamSpeed", profile.WalkSpeed * (1.9f / 2f));
            SetFloat(serialized, "agroedSpeed", profile.WalkSpeed * (1.75f / 2f));
            foreach (string angular in new[]
                     {
                         "roamAngSpeed", "agroedAngSpeed",
                     })
                SetFloat(serialized, angular, profile.AngularSpeed * (180f / 120f));
            // The runtime override config is applied after deserialization and
            // overwrites the component health leaves. Keep both values derived
            // from the author-facing typical-hit response.
            SetFloat(
                serialized,
                "healthSettings.aggression",
                profile.StartingHostility);
            SetFloat(
                serialized,
                "healthSettings.vengefulness",
                profile.RetaliationVengefulness);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ValidateBaseEnemyConfig(config);
            serialized.UpdateIfRequiredOrScript();
            if (Math.Abs(Require(
                    serialized,
                    "healthSettings.aggression").floatValue
                    - profile.StartingHostility) > 0.0001f
                || Math.Abs(Require(
                    serialized,
                    "healthSettings.vengefulness").floatValue
                    - profile.RetaliationVengefulness) > 0.0001f)
                throw new InvalidOperationException(
                    "The derived movement config did not retain the native "
                    + "hostility response selected by the author.");
        }

        private static void CaptureConfiguredIdlePose(
            NpcDefinition definition,
            RuntimeAnimatorController controller,
            ScriptableObject pose)
        {
            AnimationClip idle = ResolveConfiguredIdleClip(controller);
            Scene scene = default;
            GameObject authoringRoot = null;
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                authoringRoot = new GameObject("Patch6MovementAuthoring");
                SceneManager.MoveGameObjectToScene(authoringRoot, scene);
                Transform animationRoot = new GameObject("AnimationRoot").transform;
                animationRoot.SetParent(authoringRoot.transform, false);
                GameObject avatar = PrefabUtility.InstantiatePrefab(
                    definition.SourceAvatar, scene) as GameObject;
                if (avatar == null)
                    throw new InvalidOperationException(
                        "Unity could not instantiate the accepted Avatar prefab.");
                avatar.transform.SetParent(animationRoot, false);
                Transform animatorHolder = string.IsNullOrWhiteSpace(
                        definition.AvatarSourceProfile.AnimatorPath)
                    ? avatar.transform
                    : avatar.transform.Find(
                        definition.AvatarSourceProfile.AnimatorPath);
                Animator animator = animatorHolder == null
                    ? null : animatorHolder.GetComponent<Animator>();
                if (animator == null || animator.avatar == null
                    || !animator.avatar.isHuman)
                    throw new InvalidOperationException(
                        "The accepted Animator path does not resolve a Humanoid "
                        + "on the clean authoring instance.");

                Quaternion jawCorrection = definition.IncludePhysicalJaw
                    ? ComputeJawTargetCorrection(definition)
                    : Quaternion.identity;
                idle.SampleAnimation(animator.gameObject, 0f);
                Transform sampledJawTarget = null;
                if (definition.IncludePhysicalJaw)
                {
                    Transform sampledJaw = ResolveMovementJaw(
                        avatar.transform,
                        definition.AvatarSourceProfile);
                    var targetObject = new GameObject(JawMuscleTargetName);
                    sampledJawTarget = targetObject.transform;
                    sampledJawTarget.SetParent(sampledJaw, false);
                    sampledJawTarget.localPosition = Vector3.zero;
                    sampledJawTarget.localRotation = jawCorrection;
                    sampledJawTarget.localScale = Vector3.one;
                    targetObject.layer = sampledJaw.gameObject.layer;
                }
                IReadOnlyList<HumanBodyBones> order = definition.IncludePhysicalJaw
                    ? NpcHumanoidGraph.NativeMuscleOrder
                        .Concat(new[] { HumanBodyBones.Jaw }).ToArray()
                    : NpcHumanoidGraph.NativeMuscleOrder;
                var targets = new List<Transform>(order.Count);
                foreach (HumanBodyBones role in order)
                    targets.Add(role == HumanBodyBones.Jaw
                        ? sampledJawTarget
                        : ResolveMovementBone(
                            avatar.transform,
                            definition.AvatarSourceProfile,
                            role));
                Transform hips = targets[0];
                var serialized = new SerializedObject(pose);
                SerializedProperty positions = Require(serialized, "posePositions");
                SerializedProperty rotations = Require(serialized, "poseRotations");
                positions.arraySize = targets.Count;
                rotations.arraySize = targets.Count;
                for (int index = 0; index < targets.Count; index++)
                {
                    Transform frame = index == 0 ? animationRoot : hips;
                    Transform target = targets[index];
                    positions.GetArrayElementAtIndex(index).vector3Value =
                        frame.InverseTransformPoint(target.position);
                    rotations.GetArrayElementAtIndex(index).quaternionValue =
                        (Quaternion.Inverse(frame.rotation) * target.rotation)
                        .normalized;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                float maxPositionError = 0f;
                float maxAngleError = 0f;
                for (int index = 0; index < targets.Count; index++)
                {
                    Transform frame = index == 0 ? animationRoot : hips;
                    maxPositionError = Mathf.Max(
                        maxPositionError,
                        Vector3.Distance(
                            frame.TransformPoint(
                                positions.GetArrayElementAtIndex(index).vector3Value),
                            targets[index].position));
                    maxAngleError = Mathf.Max(
                        maxAngleError,
                        Quaternion.Angle(
                            frame.rotation
                            * rotations.GetArrayElementAtIndex(index)
                                .quaternionValue,
                            targets[index].rotation));
                }
                if (maxPositionError > 0.00001f || maxAngleError > 0.001f)
                    throw new InvalidOperationException(
                        "The sampled Idle2 standing pose failed its native-space "
                        + "round trip: " + maxPositionError.ToString("R") + " m / "
                        + maxAngleError.ToString("R") + " degrees.");
            }
            finally
            {
                if (authoringRoot != null)
                    UnityEngine.Object.DestroyImmediate(authoringRoot);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private const int LocomotionKneeSampleCount = 24;
        private const float MinimumLocomotionKneeFlexion = 0.05f;
        private const float KneeHingeAlignmentDot = 0.999f;

        /// <summary>
        /// Aligns only the generated physical knee hinge bases to the actual
        /// Humanoid-retargeted locomotion plane. This deliberately happens in
        /// the Patch 6 provider after the public Anatomy Profile has produced a
        /// valid preview and before Marrow captures its native joint caches.
        /// </summary>
        private static void AlignGeneratedKneeHingesToLocomotion(
            NpcDefinition definition,
            Animator stagedAnimator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RuntimeAnimatorController controller)
        {
            IReadOnlyDictionary<HumanBodyBones, KneeBendSample> samples =
                SampleRetargetedLocomotionKnees(definition, controller);
            foreach (HumanBodyBones lowerRole in new[]
                     {
                         HumanBodyBones.LeftLowerLeg,
                         HumanBodyBones.RightLowerLeg,
                     })
            {
                NativeRole role = RequireGeneratedKneeRole(roles, lowerRole);
                ConfigurableJoint joint = role.Joint;
                ValidateOneSidedGeneratedKneeLimit(lowerRole, joint);

                Vector3 desiredWorld = stagedAnimator.transform
                    .TransformDirection(samples[lowerRole].AnimatorLocalNormal)
                    .normalized;
                if (!IsFinite(desiredWorld)
                    || desiredWorld.sqrMagnitude < 0.999f)
                    throw new InvalidOperationException(
                        lowerRole + " produced a non-finite locomotion bend plane.");

                Vector3 oldSecondaryWorld = role.Body.TransformDirection(
                    joint.secondaryAxis).normalized;
                Vector3 secondaryWorld = oldSecondaryWorld
                    - desiredWorld * Vector3.Dot(oldSecondaryWorld, desiredWorld);
                if (secondaryWorld.sqrMagnitude < 0.000001f)
                {
                    HumanBodyBones upperRole = lowerRole
                        == HumanBodyBones.LeftLowerLeg
                        ? HumanBodyBones.LeftUpperLeg
                        : HumanBodyBones.RightUpperLeg;
                    Vector3 limbWorld = role.Target.position
                                        - roles[upperRole].Target.position;
                    secondaryWorld = limbWorld
                        - desiredWorld * Vector3.Dot(limbWorld, desiredWorld);
                }
                if (!IsFinite(secondaryWorld)
                    || secondaryWorld.sqrMagnitude < 0.000001f)
                    throw new InvalidOperationException(
                        lowerRole + " cannot form an orthogonal secondary hinge axis.");
                secondaryWorld.Normalize();

                // Only these two basis vectors change. The existing negative-X
                // flexion range, motions, drives, anchors, bodies, and target
                // pose remain exactly as authored by the accepted preview.
                joint.axis = role.Body.InverseTransformDirection(
                    desiredWorld).normalized;
                joint.secondaryAxis = role.Body.InverseTransformDirection(
                    secondaryWorld).normalized;
            }
            ValidateGeneratedKneeHingesAgainstSamples(
                stagedAnimator, roles, samples);
        }

        /// <summary>
        /// Recomputes the controller-derived contract without changing the
        /// saved prefab. Used after save/reload so a stale or lost knee basis
        /// cannot pass merely because the rest of the native shell is valid.
        /// </summary>
        private static void ValidateGeneratedKneeHingesToLocomotion(
            NpcDefinition definition,
            Animator stagedAnimator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            RuntimeAnimatorController controller)
        {
            ValidateGeneratedKneeHingesAgainstSamples(
                stagedAnimator,
                roles,
                SampleRetargetedLocomotionKnees(definition, controller));
        }

        private static void ValidateGeneratedKneeHingesAgainstSamples(
            Animator stagedAnimator,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            IReadOnlyDictionary<HumanBodyBones, KneeBendSample> samples)
        {
            foreach (HumanBodyBones lowerRole in new[]
                     {
                         HumanBodyBones.LeftLowerLeg,
                         HumanBodyBones.RightLowerLeg,
                     })
            {
                NativeRole role = RequireGeneratedKneeRole(roles, lowerRole);
                ValidateOneSidedGeneratedKneeLimit(lowerRole, role.Joint);
                Vector3 desiredWorld = stagedAnimator.transform
                    .TransformDirection(samples[lowerRole].AnimatorLocalNormal)
                    .normalized;
                Vector3 actualWorld = role.Body.TransformDirection(
                    role.Joint.axis).normalized;
                float dot = Vector3.Dot(actualWorld, desiredWorld);
                float orthogonality = Mathf.Abs(Vector3.Dot(
                    role.Joint.axis.normalized,
                    role.Joint.secondaryAxis.normalized));
                if (!IsFinite(dot) || dot < KneeHingeAlignmentDot
                                   || !IsFinite(orthogonality)
                                   || orthogonality > 0.0001f)
                    throw new InvalidOperationException(
                        lowerRole + " physical hinge opposes the retargeted "
                        + "locomotion bend plane (dot="
                        + dot.ToString("R", CultureInfo.InvariantCulture)
                        + ", orthogonality="
                        + orthogonality.ToString("R", CultureInfo.InvariantCulture)
                        + "). Rebuild the native NPC.");
            }
        }

        private static NativeRole RequireGeneratedKneeRole(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            HumanBodyBones lowerRole)
        {
            if (roles == null || !roles.TryGetValue(lowerRole, out NativeRole role)
                              || role == null || role.Body == null
                              || role.Target == null || role.Joint == null)
                throw new InvalidOperationException(
                    lowerRole + " generated knee inputs are incomplete.");
            return role;
        }

        private static void ValidateOneSidedGeneratedKneeLimit(
            HumanBodyBones lowerRole,
            ConfigurableJoint joint)
        {
            if (joint.angularXMotion != ConfigurableJointMotion.Limited
                || joint.lowAngularXLimit.limit >= -1f
                || Mathf.Abs(joint.highAngularXLimit.limit) > 0.001f)
                throw new InvalidOperationException(
                    lowerRole + " must retain the native one-sided negative-X "
                    + "flexion limit before locomotion hinge alignment.");
        }

        private static IReadOnlyDictionary<HumanBodyBones, KneeBendSample>
            SampleRetargetedLocomotionKnees(
                NpcDefinition definition,
                RuntimeAnimatorController controller)
        {
            if (definition == null || definition.SourceAvatar == null
                                   || definition.AvatarSourceProfile == null)
                throw new InvalidOperationException(
                    "Locomotion knee sampling requires an accepted source Avatar snapshot.");
            IReadOnlyList<AnimationClip> clips =
                ResolveConfiguredLocomotionClips(controller);
            Scene scene = default;
            GameObject instance = null;
            LocalTransformPose[] originalPose = null;
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                instance = PrefabUtility.InstantiatePrefab(
                    definition.SourceAvatar, scene) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException(
                        "Unity could not instantiate the accepted Avatar for "
                        + "locomotion knee sampling.");
                instance.name = definition.SourceAvatar.name
                                + " [Locomotion Knee Sampling Read Only]";
                instance.hideFlags = HideFlags.HideAndDontSave;
                Transform animatorHolder = string.IsNullOrWhiteSpace(
                        definition.AvatarSourceProfile.AnimatorPath)
                    ? instance.transform
                    : instance.transform.Find(
                        definition.AvatarSourceProfile.AnimatorPath);
                Animator animator = animatorHolder == null
                    ? null : animatorHolder.GetComponent<Animator>();
                if (animator == null || animator.avatar == null
                                     || !animator.avatar.isHuman)
                    throw new InvalidOperationException(
                        "The accepted Animator path does not resolve a Humanoid "
                        + "while sampling locomotion knees.");
                Transform leftUpper = animator.GetBoneTransform(
                    HumanBodyBones.LeftUpperLeg);
                Transform leftLower = animator.GetBoneTransform(
                    HumanBodyBones.LeftLowerLeg);
                Transform leftFoot = animator.GetBoneTransform(
                    HumanBodyBones.LeftFoot);
                Transform rightUpper = animator.GetBoneTransform(
                    HumanBodyBones.RightUpperLeg);
                Transform rightLower = animator.GetBoneTransform(
                    HumanBodyBones.RightLowerLeg);
                Transform rightFoot = animator.GetBoneTransform(
                    HumanBodyBones.RightFoot);
                if (leftUpper == null || leftLower == null || leftFoot == null
                    || rightUpper == null || rightLower == null || rightFoot == null)
                    throw new InvalidOperationException(
                        "The accepted Humanoid is missing a leg bone required "
                        + "for locomotion knee sampling.");

                originalPose = instance.GetComponentsInChildren<Transform>(true)
                    .Select(value => new LocalTransformPose(value)).ToArray();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.enabled = true;
                var best = new Dictionary<HumanBodyBones, KneeBendSample>
                {
                    [HumanBodyBones.LeftLowerLeg] = default,
                    [HumanBodyBones.RightLowerLeg] = default,
                };
                foreach (AnimationClip clip in clips)
                {
                    for (int sampleIndex = 0;
                         sampleIndex < LocomotionKneeSampleCount;
                         sampleIndex++)
                    {
                        RestoreLocalTransformPose(originalPose);
                        float normalizedTime = (sampleIndex + 0.5f)
                                               / LocomotionKneeSampleCount;
                        clip.SampleAnimation(
                            animator.gameObject,
                            clip.length * normalizedTime);
                        ConsiderKneeBendSample(
                            animator,
                            leftUpper,
                            leftLower,
                            leftFoot,
                            HumanBodyBones.LeftLowerLeg,
                            clip,
                            normalizedTime,
                            best);
                        ConsiderKneeBendSample(
                            animator,
                            rightUpper,
                            rightLower,
                            rightFoot,
                            HumanBodyBones.RightLowerLeg,
                            clip,
                            normalizedTime,
                            best);
                    }
                }
                foreach (HumanBodyBones role in best.Keys.ToArray())
                {
                    KneeBendSample sample = best[role];
                    if (!sample.IsValid
                        || sample.NormalizedFlexion
                            < MinimumLocomotionKneeFlexion)
                        throw new InvalidOperationException(
                            "No finite, nondegenerate " + role
                            + " bend plane was found in the configured Loco clips. "
                            + "The provider cannot safely guess a knee hinge.");
                }
                return best;
            }
            finally
            {
                if (originalPose != null)
                    RestoreLocalTransformPose(originalPose);
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void ConsiderKneeBendSample(
            Animator animator,
            Transform upper,
            Transform lower,
            Transform foot,
            HumanBodyBones lowerRole,
            AnimationClip clip,
            float normalizedTime,
            IDictionary<HumanBodyBones, KneeBendSample> best)
        {
            Vector3 upperSegment = lower.position - upper.position;
            Vector3 lowerSegment = foot.position - lower.position;
            float denominator = upperSegment.magnitude * lowerSegment.magnitude;
            Vector3 cross = Vector3.Cross(upperSegment, lowerSegment);
            float flexion = denominator > 0.0000001f
                ? cross.magnitude / denominator
                : 0f;
            if (!IsFinite(flexion) || !IsFinite(cross)
                                    || flexion <= best[lowerRole].NormalizedFlexion
                                    || cross.sqrMagnitude < 0.00000001f)
                return;
            Vector3 localNormal = animator.transform.InverseTransformDirection(
                cross.normalized).normalized;
            if (!IsFinite(localNormal) || localNormal.sqrMagnitude < 0.999f)
                return;
            best[lowerRole] = new KneeBendSample(
                localNormal,
                flexion,
                StableLocomotionClipId(clip),
                normalizedTime);
        }

        private static IReadOnlyList<AnimationClip>
            ResolveConfiguredLocomotionClips(
                RuntimeAnimatorController runtimeController)
        {
            AnimatorOverrideController overrides =
                runtimeController as AnimatorOverrideController;
            RuntimeAnimatorController baseRuntime = overrides == null
                ? runtimeController
                : overrides.runtimeAnimatorController;
            AnimatorController controller = baseRuntime as AnimatorController;
            if (controller == null)
                throw new InvalidOperationException(
                    "The Patch 6 controller is not inspectable.");
            var states = new Dictionary<string, List<AnimatorState>>(
                StringComparer.Ordinal);
            foreach (AnimatorControllerLayer layer in controller.layers)
                CollectBehaviourControllerStates(layer.stateMachine, states);
            if (!states.TryGetValue("Loco", out List<AnimatorState> matches)
                || matches.Count != 1)
                throw new InvalidOperationException(
                    "The configured controller must contain exactly one Loco state.");
            var clipsById = new Dictionary<string, AnimationClip>(
                StringComparer.Ordinal);
            CollectPersistentLocomotionClips(
                matches[0].motion, overrides, clipsById);
            if (clipsById.Count == 0)
                throw new InvalidOperationException(
                    "The configured Loco state contains no persistent animation clips.");
            return clipsById.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => value.Value).ToArray();
        }

        private static void CollectPersistentLocomotionClips(
            Motion motion,
            AnimatorOverrideController overrides,
            IDictionary<string, AnimationClip> clipsById)
        {
            if (motion is AnimationClip original)
            {
                AnimationClip replacement = overrides == null
                    ? null : overrides[original];
                AnimationClip clip = replacement != null ? replacement : original;
                if (clip.length <= 0f || !EditorUtility.IsPersistent(clip)) return;
                clipsById[StableLocomotionClipId(clip)] = clip;
                return;
            }
            if (!(motion is BlendTree tree)) return;
            foreach (ChildMotion child in tree.children)
                CollectPersistentLocomotionClips(
                    child.motion, overrides, clipsById);
        }

        private static string StableLocomotionClipId(AnimationClip clip)
        {
            if (clip == null
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    clip, out string guid, out long localId)
                || string.IsNullOrWhiteSpace(guid))
                throw new InvalidOperationException(
                    "A configured Loco clip has no stable asset identity.");
            return guid + ":" + localId.ToString(
                CultureInfo.InvariantCulture);
        }

        private static void RestoreLocalTransformPose(
            IEnumerable<LocalTransformPose> pose)
        {
            foreach (LocalTransformPose value in pose)
            {
                if (value.Transform == null) continue;
                value.Transform.localPosition = value.LocalPosition;
                value.Transform.localRotation = value.LocalRotation;
                value.Transform.localScale = value.LocalScale;
            }
        }

        private readonly struct LocalTransformPose
        {
            public Transform Transform { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }

            public LocalTransformPose(Transform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }
        }

        private readonly struct KneeBendSample
        {
            public Vector3 AnimatorLocalNormal { get; }
            public float NormalizedFlexion { get; }
            public string ClipId { get; }
            public float NormalizedTime { get; }
            public bool IsValid => !string.IsNullOrWhiteSpace(ClipId)
                                   && NormalizedFlexion > 0f;

            public KneeBendSample(
                Vector3 animatorLocalNormal,
                float normalizedFlexion,
                string clipId,
                float normalizedTime)
            {
                AnimatorLocalNormal = animatorLocalNormal;
                NormalizedFlexion = normalizedFlexion;
                ClipId = clipId ?? string.Empty;
                NormalizedTime = normalizedTime;
            }
        }

        private static AnimationClip ResolveConfiguredIdleClip(
            RuntimeAnimatorController runtimeController)
        {
            AnimatorOverrideController overrides =
                runtimeController as AnimatorOverrideController;
            RuntimeAnimatorController baseRuntime = overrides == null
                ? runtimeController
                : overrides.runtimeAnimatorController;
            AnimatorController controller = baseRuntime as AnimatorController;
            if (controller == null)
                throw new InvalidOperationException(
                    "The Patch 6 controller is not inspectable.");
            var states = new Dictionary<string, List<AnimatorState>>(
                StringComparer.Ordinal);
            foreach (AnimatorControllerLayer layer in controller.layers)
                CollectBehaviourControllerStates(layer.stateMachine, states);
            if (!states.TryGetValue("Idle2", out List<AnimatorState> matches)
                || matches.Count != 1)
                throw new InvalidOperationException(
                    "The configured controller must contain exactly one Idle2 state.");
            AnimationClip clip = FirstBehaviourControllerClip(
                matches[0].motion, overrides);
            if (clip == null)
                throw new InvalidOperationException(
                    "The configured Idle2 state has no persistent motion clip.");
            return clip;
        }

        private static Transform ResolveMovementBone(
            Transform avatarRoot,
            NpcAvatarSourceProfile source,
            HumanBodyBones role)
        {
            NpcHumanoidBoneBinding[] matches = source.HumanoidBones
                .Where(value => value.Role == role).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    "The accepted Avatar snapshot must contain one " + role
                    + " path while sampling Idle2.");
            Transform value = string.IsNullOrWhiteSpace(matches[0].TransformPath)
                ? avatarRoot
                : avatarRoot.Find(matches[0].TransformPath);
            if (value == null)
                throw new InvalidOperationException(
                    "The accepted " + role + " path no longer resolves while "
                    + "sampling Idle2.");
            return value;
        }

        private static Transform ResolveMovementJaw(
            Transform avatarRoot,
            NpcAvatarSourceProfile source)
        {
            Transform value = source == null
                              || string.IsNullOrWhiteSpace(source.JawPath)
                ? null
                : avatarRoot.Find(source.JawPath);
            if (value == null)
                throw new InvalidOperationException(
                    "Physical Jaw is enabled but the accepted Jaw path does not "
                    + "resolve while sampling Idle2.");
            return value;
        }

        internal static string ComputeMovementRecipeFingerprint(
            NpcDefinition definition,
            NpcMovementProfile profile,
            UnityEngine.Object standingPose,
            UnityEngine.Object movementConfig,
            MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings)
        {
            AnimationClip idle = ResolveConfiguredIdleClip(
                settings.AnimatorController);
            string sourcePath = AssetDatabase.GetAssetPath(
                definition.SourceAvatar).Replace('\\', '/');
            string currentSourceHash = CurrentMovementSourceDependencyHash(
                definition);
            string currentAuthoringFingerprint =
                CurrentMovementAuthoringFingerprint(definition);
            if (!string.Equals(
                    profile.AutoFitAuthoringFingerprint,
                    currentAuthoringFingerprint,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The Movement Profile no longer matches the current Physics "
                    + "Alignment authoring fingerprint. Refit movement before "
                    + "preparing Patch 6 assets.");
            var text = new StringBuilder(2048);
            text.Append("patch6-movement-recipe-v8|")
                .Append(AssetDatabase.AssetPathToGUID(sourcePath)).Append('|')
                .Append(currentSourceHash).Append('|')
                .Append(currentAuthoringFingerprint).Append('|')
                .Append(definition.IncludePhysicalJaw ? "jaw17|" : "body16|")
                .Append(AssetReceipt(settings.AnimatorController)).Append('|')
                .Append(AssetReceipt(idle)).Append('|')
                .Append(AssetReceipt(settings.BehaviourTemplate)).Append('|')
                .Append(AssetReceipt(settings.LocomotionReference)).Append('|')
                .Append(AssetReceipt(settings.StandingIdle)).Append('|')
                .Append(AssetReceipt(settings.BaseEnemyConfig)).Append('|')
                .Append(AssetReceipt(standingPose)).Append('|')
                .Append(AssetReceipt(movementConfig)).Append('|');
            foreach (float value in new[]
                     {
                         profile.EyeHeight, profile.BodyHeight, profile.NavHeight,
                         profile.LeftLegLength, profile.RightLegLength,
                         profile.MeanLegLength, profile.HipWidth,
                         profile.StanceWidth, profile.SoleHeight,
                         profile.NavRadius, profile.NavBaseOffset,
                         profile.PelvisHeightOffset, profile.StanceWidthScale,
                         profile.LeftFootYawCorrectionDegrees,
                         profile.RightFootYawCorrectionDegrees,
                         profile.StrideScale, profile.StepHeightScale,
                         profile.StepRateScale, profile.WalkSpeed,
                         profile.Acceleration, profile.AngularSpeed,
                         profile.StoppingDistance, profile.StartingHostility,
                         profile.HostilityAfterTypicalHit,
                         profile.RetaliationVengefulness,
                     })
                text.Append(MovementFloat(value)).Append(',');
            AppendMovementVector(text, profile.LeftFootForwardLocal);
            AppendMovementVector(text, profile.RightFootForwardLocal);
            return Hash128.Compute(text.ToString()).ToString();
        }

        internal static string CurrentMovementSourceDependencyHash(
            NpcDefinition definition)
        {
            if (definition == null || definition.SourceAvatar == null
                || definition.AvatarSourceProfile == null)
                throw new InvalidOperationException(
                    "Movement preparation requires an accepted source Avatar snapshot.");
            string path = AssetDatabase.GetAssetPath(definition.SourceAvatar)
                ?.Replace('\\', '/');
            string guid = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(path);
            string currentHash = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(path).ToString();
            if (string.IsNullOrWhiteSpace(guid)
                || string.IsNullOrWhiteSpace(currentHash)
                || definition.AvatarSourceProfile.AvatarPrefab
                    != definition.SourceAvatar
                || !string.Equals(
                    definition.AvatarSourceProfile.SourceAssetGuid,
                    guid,
                    StringComparison.Ordinal)
                || !string.Equals(
                    definition.AvatarSourceProfile.SourceDependencyHash,
                    currentHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The source Avatar changed after its accepted snapshot. "
                    + "Refresh the Avatar snapshot before preparing movement.");
            return currentHash;
        }

        internal static string CurrentMovementAuthoringFingerprint(
            NpcDefinition definition)
        {
            string fingerprint =
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition);
            if (string.IsNullOrWhiteSpace(fingerprint))
                throw new InvalidOperationException(
                    "The accepted Avatar and Physics Alignment did not produce "
                    + "a stable movement authoring fingerprint.");
            return fingerprint;
        }

        private static string AssetReceipt(UnityEngine.Object value)
        {
            if (value == null
                || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out string guid, out long localId)
                || string.IsNullOrWhiteSpace(guid))
                throw new InvalidOperationException(
                    "Movement recipe contains a non-persistent asset.");
            string path = AssetDatabase.GetAssetPath(value);
            return guid + ":" + localId + ":"
                   + AssetDatabase.GetAssetDependencyHash(path);
        }

        private static string MovementFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void AppendMovementVector(
            StringBuilder text,
            Vector3 value)
        {
            text.Append(MovementFloat(value.x)).Append(',')
                .Append(MovementFloat(value.y)).Append(',')
                .Append(MovementFloat(value.z)).Append('|');
        }

        private static string SanitizeMovementAssetName(string value)
        {
            value = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(value)
                ? "NpcMovement"
                : value.Trim();
        }

        /// <summary>
        /// Captures the exact source/profile/settings assets used to derive the
        /// provider recipe. Sampling is allowed to mutate only a disposable
        /// prefab instance in a PreviewScene; any dirty flag, serialized object,
        /// or saved dependency-hash change in these sources rejects the recipe.
        /// </summary>
        private sealed class ProtectedMovementSources
        {
            private readonly IReadOnlyList<ProtectedAssetState> _states;

            private ProtectedMovementSources(
                IReadOnlyList<ProtectedAssetState> states)
            {
                _states = states;
            }

            internal static ProtectedMovementSources Capture(
                NpcDefinition definition,
                MarrowNpcToolkitPatch6BehaviourSettings.Resolved settings,
                AnimationClip idle)
            {
                var values = new UnityEngine.Object[]
                {
                    definition,
                    definition.SourceAvatar,
                    definition.AvatarSourceProfile,
                    definition.AnatomyProfile,
                    definition.BuildProfile,
                    settings.BehaviourTemplate,
                    settings.LocomotionReference,
                    settings.AnimatorController,
                    idle,
                    settings.BaseEnemyConfig,
                    settings.StandingIdle,
                    settings.OpenHand,
                    settings.Fist,
                    settings.Pistol,
                    settings.PistolOffhand,
                    settings.PlantedFootMaterial,
                    settings.LiftedFootMaterial,
                };
                return new ProtectedMovementSources(
                    values.Where(value => value != null)
                        .Distinct()
                        // The Definition depends on the Movement Profile and its
                        // provider assets, which this transaction intentionally
                        // updates. Guard its own bytes/JSON/dirty state, but do
                        // not mistake that expected transitive dependency change
                        // for a Definition mutation.
                        .Select(value => ProtectedAssetState.Capture(
                            value, value != definition))
                        .ToArray());
            }

            internal void RequireUnchanged()
            {
                foreach (ProtectedAssetState state in _states)
                    state.RequireUnchanged();
            }

            internal void Restore()
            {
                var failures = new List<Exception>();
                foreach (ProtectedAssetState state in _states)
                    try
                    {
                        // Restore every direct asset/object state before
                        // checking dependency receipts. A protected controller
                        // may depend on a protected clip that appears later in
                        // this list, so checking its dependency hash during the
                        // first pass would report a false rollback failure.
                        state.Restore(false);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                foreach (ProtectedAssetState state in _states)
                    try
                    {
                        state.RequireUnchanged();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception);
                    }
                if (failures.Count != 0)
                    throw new AggregateException(
                        "One or more protected movement sources could not be "
                        + "restored.",
                        failures);
            }
        }

        private sealed class ProtectedAssetState
        {
            private readonly string _path;
            private readonly string _guid;
            private readonly long _localId;
            private readonly Hash128 _dependencyHash;
            private readonly bool _dirty;
            private readonly string _serialized;
            private readonly bool _guardDependencyHash;
            private readonly string _absolutePath;
            private readonly byte[] _assetBytes;
            private readonly string _absoluteMetaPath;
            private readonly bool _metaExisted;
            private readonly byte[] _metaBytes;

            private ProtectedAssetState(
                string path,
                string guid,
                long localId,
                Hash128 dependencyHash,
                bool dirty,
                string serialized,
                bool guardDependencyHash,
                string absolutePath,
                byte[] assetBytes,
                string absoluteMetaPath,
                bool metaExisted,
                byte[] metaBytes)
            {
                _path = path;
                _guid = guid;
                _localId = localId;
                _dependencyHash = dependencyHash;
                _dirty = dirty;
                _serialized = serialized;
                _guardDependencyHash = guardDependencyHash;
                _absolutePath = absolutePath;
                _assetBytes = assetBytes;
                _absoluteMetaPath = absoluteMetaPath;
                _metaExisted = metaExisted;
                _metaBytes = metaBytes;
            }

            internal static ProtectedAssetState Capture(
                UnityEngine.Object value,
                bool guardDependencyHash)
            {
                string path = AssetDatabase.GetAssetPath(value)
                    ?.Replace('\\', '/');
                if (!EditorUtility.IsPersistent(value)
                    || string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException(
                        "Movement preparation source " + value.name
                        + " is not a persistent asset.");
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        value, out string guid, out long localId)
                    || string.IsNullOrWhiteSpace(guid))
                    throw new InvalidOperationException(
                        "Movement preparation source " + value.name
                        + " has no stable asset identifier.");
                string absolutePath = AbsoluteProjectAssetPath(path);
                if (!File.Exists(absolutePath))
                    throw new InvalidOperationException(
                        "Movement preparation source file is missing: " + path
                        + ".");
                string absoluteMetaPath = absolutePath + ".meta";
                bool metaExisted = File.Exists(absoluteMetaPath);
                return new ProtectedAssetState(
                    path,
                    guid,
                    localId,
                    AssetDatabase.GetAssetDependencyHash(path),
                    EditorUtility.IsDirty(value),
                    EditorJsonUtility.ToJson(value, true),
                    guardDependencyHash,
                    absolutePath,
                    File.ReadAllBytes(absolutePath),
                    absoluteMetaPath,
                    metaExisted,
                    metaExisted
                        ? File.ReadAllBytes(absoluteMetaPath)
                        : Array.Empty<byte>());
            }

            internal void RequireUnchanged()
            {
                UnityEngine.Object current = ResolveCurrent();
                if (!AssetFileMatches(_absolutePath, _assetBytes)
                    || !MetaFileMatches()
                    || _guardDependencyHash
                       && AssetDatabase.GetAssetDependencyHash(_path)
                           != _dependencyHash
                    || EditorUtility.IsDirty(current) != _dirty
                    || !string.Equals(
                        EditorJsonUtility.ToJson(current, true),
                        _serialized,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Movement preparation modified protected source asset "
                        + _path + ".");
            }

            internal void Restore(bool verifyDependencyReceipt = true)
            {
                bool assetFileChanged = !AssetFileMatches(
                    _absolutePath, _assetBytes);
                bool metaFileChanged = !MetaFileMatches();
                UnityEngine.Object currentBeforeImport = null;
                string serializedBeforeImport = null;
                bool dirtyBeforeImport = false;
                try
                {
                    currentBeforeImport = ResolveCurrent();
                    serializedBeforeImport = EditorJsonUtility.ToJson(
                        currentBeforeImport, true);
                    dirtyBeforeImport = EditorUtility.IsDirty(
                        currentBeforeImport);
                }
                catch
                {
                    // A modified or missing meta file can make the original
                    // GUID temporarily unresolvable. Restoring the captured
                    // files and synchronously importing them below repairs it.
                    if (!assetFileChanged && !metaFileChanged)
                        throw;
                }

                bool serializedChanged = currentBeforeImport == null
                    || !string.Equals(
                        serializedBeforeImport,
                        _serialized,
                        StringComparison.Ordinal);
                bool dirtyChanged = currentBeforeImport == null
                    || dirtyBeforeImport != _dirty;
                bool dependencyChanged = _guardDependencyHash
                    && AssetDatabase.GetAssetDependencyHash(_path)
                        != _dependencyHash;
                if (!assetFileChanged
                    && !metaFileChanged
                    && !serializedChanged
                    && !dirtyChanged
                    && !dependencyChanged)
                    return;

                bool diskChanged = false;
                if (assetFileChanged)
                    diskChanged = RestoreFile(_absolutePath, _assetBytes);
                if (_metaExisted)
                {
                    if (metaFileChanged)
                        diskChanged |= RestoreFile(
                            _absoluteMetaPath, _metaBytes);
                }
                else if (File.Exists(_absoluteMetaPath))
                {
                    File.Delete(_absoluteMetaPath);
                    diskChanged = true;
                }
                if (diskChanged)
                    AssetDatabase.ImportAsset(
                        _path,
                        ImportAssetOptions.ForceUpdate
                        | ImportAssetOptions.ForceSynchronousImport);

                // Re-resolve by the captured GUID/local ID after an import.
                // Unity may replace the managed wrapper, so retaining _value
                // here can overwrite or inspect a stale object.
                UnityEngine.Object current = ResolveCurrent();
                if (!string.Equals(
                        EditorJsonUtility.ToJson(current, true),
                        _serialized,
                        StringComparison.Ordinal))
                    EditorJsonUtility.FromJsonOverwrite(_serialized, current);
                if (EditorUtility.IsDirty(current) != _dirty)
                {
                    if (_dirty)
                        EditorUtility.SetDirty(current);
                    else
                        EditorUtility.ClearDirty(current);
                }

                if (verifyDependencyReceipt)
                    RequireUnchanged();
                else if (!AssetFileMatches(_absolutePath, _assetBytes)
                         || !MetaFileMatches()
                         || !string.Equals(
                             EditorJsonUtility.ToJson(current, true),
                             _serialized,
                             StringComparison.Ordinal)
                         || EditorUtility.IsDirty(current) != _dirty)
                    throw new InvalidOperationException(
                        "Movement preparation could not restore protected source "
                        + _path + " exactly.");
            }

            private UnityEngine.Object ResolveCurrent()
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(_guid)
                    ?.Replace('\\', '/');
                if (!string.Equals(guidPath, _path, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Movement preparation source identifier no longer resolves "
                        + "to " + _path + ".");
                foreach (UnityEngine.Object candidate in
                         AssetDatabase.LoadAllAssetsAtPath(_path))
                {
                    if (candidate == null
                        || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            candidate,
                            out string candidateGuid,
                            out long candidateLocalId)
                        || !string.Equals(
                            candidateGuid, _guid, StringComparison.Ordinal)
                        || candidateLocalId != _localId)
                        continue;
                    return candidate;
                }
                throw new InvalidOperationException(
                    "Movement preparation could not resolve protected source "
                    + _path + " by its captured GUID/local ID.");
            }

            private bool MetaFileMatches()
            {
                return _metaExisted
                    ? AssetFileMatches(_absoluteMetaPath, _metaBytes)
                    : !File.Exists(_absoluteMetaPath);
            }

            private static bool AssetFileMatches(
                string path,
                byte[] expected)
            {
                return File.Exists(path)
                    && File.ReadAllBytes(path).SequenceEqual(expected);
            }

            internal static bool AssetOrMetaFileExists(string assetPath)
            {
                string absolutePath = AbsoluteProjectAssetPath(assetPath);
                return File.Exists(absolutePath)
                    || File.Exists(absolutePath + ".meta");
            }

            private static bool RestoreFile(string path, byte[] expected)
            {
                if (File.Exists(path)
                    && File.ReadAllBytes(path).SequenceEqual(expected))
                    return false;
                File.WriteAllBytes(path, expected);
                return true;
            }

            private static string AbsoluteProjectAssetPath(string assetPath)
            {
                string projectRoot = Directory.GetParent(Application.dataPath)
                    ?.FullName;
                if (string.IsNullOrWhiteSpace(projectRoot))
                    throw new InvalidOperationException(
                        "Could not resolve the Unity project root while guarding "
                        + assetPath + ".");
                string absolute = Path.GetFullPath(
                    Path.Combine(projectRoot, assetPath));
                string prefix = projectRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
                if (!absolute.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Protected movement source resolves outside the Unity "
                        + "project: " + assetPath + ".");
                return absolute;
            }
        }
    }
}
