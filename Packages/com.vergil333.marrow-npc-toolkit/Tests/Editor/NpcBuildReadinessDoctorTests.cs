using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcBuildReadinessDoctorTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private string testFolder;

        [SetUp]
        public void SetUp()
        {
            testFolder = "Assets/__MarrowNpcToolkitReadinessTests_"
                         + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(testFolder));
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null)
                    Object.DestroyImmediate(cleanup[index]);
            }
            cleanup.Clear();
            if (!string.IsNullOrWhiteSpace(testFolder))
                AssetDatabase.DeleteAsset(testFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void ValidPreviewHasSixteenUniqueConnectedBodiesAndPreservedRenderers()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 1);
            GameObject preview = CreateValidPreview(rendererCount: 1);
            preview.SetActive(false);

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(report.RigidbodyCount, Is.EqualTo(16));
            Assert.That(report.ColliderCount, Is.EqualTo(16));
            Assert.That(report.JointCount, Is.EqualTo(16));
            Assert.That(report.RendererCount, Is.EqualTo(1));
            Assert.That(report.Issues.Where(IsPreviewError), Is.Empty);
        }

        [Test]
        public void ColliderOwnershipUsesPrimaryMarkerAndCatchesOffsetDuplicates()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            Transform physics = preview.transform.Find("Physics");
            Transform leftMarker = FindBody(
                physics, HumanBodyBones.LeftHand).transform.Find("PrimaryCollider");
            leftMarker.gameObject.AddComponent<SphereCollider>();
            Transform rightMarker = FindBody(
                physics, HumanBodyBones.RightHand).transform.Find("PrimaryCollider");
            Object.DestroyImmediate(rightMarker.GetComponent<Collider>());

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(report.ColliderCount, Is.EqualTo(16),
                "The global count is deliberately balanced to exercise per-body ownership.");
            AssertIssue(report, "PREVIEW_BODY_COLLIDER_INVALID",
                HumanBodyBones.LeftHand);
            AssertIssue(report, "PREVIEW_BODY_COLLIDER_INVALID",
                HumanBodyBones.RightHand);
        }

        [Test]
        public void PreviewRejectsDuplicateBodiesAndDisconnectedJointGraph()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            Transform physics = preview.transform.Find("Physics");
            Rigidbody rightHand = FindBody(physics, HumanBodyBones.RightHand);
            rightHand.GetComponent<ConfigurableJoint>().connectedBody =
                FindBody(physics, HumanBodyBones.Hips);

            var duplicate = Track(new GameObject(HumanBodyBones.RightHand.ToString()));
            duplicate.transform.SetParent(physics, false);
            duplicate.AddComponent<Rigidbody>();
            duplicate.AddComponent<BoxCollider>();
            duplicate.AddComponent<ConfigurableJoint>();

            NpcBuildReadinessReport duplicateReport =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(duplicateReport.Issues.Any(value =>
                value.Code == "PREVIEW_BODY_ROLE_DUPLICATE"), Is.True);
            Assert.That(duplicateReport.ReadyForBuild, Is.False);

            // A duplicated role is intentionally ambiguous, so role-specific
            // joint checks cannot identify one authoritative RightHand.  Once
            // the duplicate is removed, independently prove that the broken
            // graph edge is diagnosed.
            Object.DestroyImmediate(duplicate);
            NpcBuildReadinessReport disconnectedReport =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(disconnectedReport.Issues.Any(value =>
                value.Code == "PREVIEW_JOINT_CONNECTION_INVALID"
                && value.Role == HumanBodyBones.RightHand), Is.True);
            Assert.That(disconnectedReport.ReadyForBuild, Is.False);
        }

        [Test]
        public void PhysicsRootsMustBeDirectSiblings()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            Transform animationRoot = preview.transform.Find("AnimationRoot");
            Transform physicsRoot = preview.transform.Find("Physics");
            physicsRoot.SetParent(animationRoot, false);

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(report.Issues.Any(value =>
                value.Code == "PREVIEW_ROOT_SIBLING_INVALID"
                && value.Message.Contains("Physics")), Is.True);
        }

        [Test]
        public void AnatomyRejectsNonFiniteGeometryMassAxesAndLimits()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            NpcAnatomyProfile anatomy = definition.AnatomyProfile;
            anatomy.FindRole(HumanBodyBones.Hips).MassKilograms = float.NaN;
            anatomy.FindRole(HumanBodyBones.RightHand).ColliderSize =
                new Vector3(0f, 0.1f, 0.1f);
            anatomy.FindRole(HumanBodyBones.Spine).JointSecondaryAxis = Vector3.right;
            anatomy.FindRole(HumanBodyBones.Chest).AngularLowLimits =
                new Vector3(float.PositiveInfinity, -10f, -10f);
            anatomy.FindRole(HumanBodyBones.LeftLowerArm).AngularLowLimits =
                new Vector3(90f, -10f, -10f);
            anatomy.FindRole(HumanBodyBones.LeftLowerArm).AngularHighLimits =
                new Vector3(-90f, 10f, 10f);
            anatomy.FindRole(HumanBodyBones.LeftUpperArm).AngularHighLimits =
                new Vector3(240f, 10f, 10f);

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));

            AssertIssue(report, "BODY_MASS_INVALID", HumanBodyBones.Hips);
            AssertIssue(report, "COLLIDER_SIZE_INVALID", HumanBodyBones.RightHand);
            AssertIssue(report, "JOINT_AXES_PARALLEL", HumanBodyBones.Spine);
            AssertIssue(report, "JOINT_LIMIT_NOT_FINITE", HumanBodyBones.Chest);
            AssertIssue(report, "JOINT_LIMIT_ORDER_INVALID",
                HumanBodyBones.LeftLowerArm);
            AssertIssue(report, "JOINT_LIMIT_RANGE_INVALID",
                HumanBodyBones.LeftUpperArm);
        }

        [Test]
        public void IncompleteReviewIsAWarningRatherThanABlocker()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));

            NpcBuildReadinessIssue review = report.Issues.Single(value =>
                value.Code == "ALIGNMENT_REVIEW_INCOMPLETE");
            Assert.That(review.Severity, Is.EqualTo(NpcBuildReadinessSeverity.Warning));
            Assert.That(report.ReviewedRoleCount, Is.Zero);
            Assert.That(report.Issues.Any(value =>
                value.Code.StartsWith("ANATOMY_", StringComparison.Ordinal)
                && value.Severity == NpcBuildReadinessSeverity.Error), Is.False);

            int errorsBeforeReview = report.ErrorCount;
            foreach (NpcBodyRoleProfile role in definition.AnatomyProfile.BodyRoles)
                role.AlignmentState = NpcAlignmentState.Reviewed;
            NpcBuildReadinessReport reviewed =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));
            Assert.That(reviewed.ErrorCount, Is.EqualTo(errorsBeforeReview));
            Assert.That(reviewed.Issues.Any(value =>
                value.Code == "ALIGNMENT_REVIEW_INCOMPLETE"), Is.False);
        }

        [Test]
        public void PreviewRequiresTheAcceptedRendererCount()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 1);
            GameObject preview = CreateValidPreview(rendererCount: 0);

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(report.Issues.Any(value =>
                value.Code == "PREVIEW_RENDERER_COUNT_INVALID"), Is.True);
        }

        [Test]
        public void BaselineDoctorDistinguishesToolkitUpgradeFromSourceChange()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            FieldInfo toolkitReceipt = typeof(NpcAnatomyProfile).GetField(
                "baselineToolkitVersion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(toolkitReceipt, Is.Not.Null);
            toolkitReceipt.SetValue(definition.AnatomyProfile, "0.2.0");

            NpcBuildReadinessReport upgraded =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            NpcBuildReadinessIssue toolkitIssue = upgraded.Issues.Single(value =>
                value.Code == "ANATOMY_BASELINE_TOOLKIT_OUTDATED");
            Assert.That(toolkitIssue.Message, Does.Contain("0.2.0"));
            Assert.That(toolkitIssue.Message,
                Does.Contain(NpcToolkitVersion.Current));
            Assert.That(toolkitIssue.Message, Does.Contain("Step 3A"));
            Assert.That(upgraded.Issues.Any(value =>
                value.Code == "ANATOMY_BASELINE_SOURCE_CHANGED"), Is.False);

            definition.AnatomyProfile.MarkBaselineFitted("different-source-hash");
            NpcBuildReadinessReport changedSource =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(changedSource.Issues.Any(value =>
                value.Code == "ANATOMY_BASELINE_SOURCE_CHANGED"), Is.True);
            Assert.That(changedSource.Issues.Any(value =>
                value.Code == "ANATOMY_BASELINE_TOOLKIT_OUTDATED"), Is.False);
        }

        [Test]
        public void MovementProfileMustExistBePersistentAndBeFitted()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);

            definition.MovementProfile = null;
            NpcBuildReadinessReport missing =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(missing.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_MISSING"), Is.True);
            Assert.That(missing.ReadyForBuild, Is.False);

            var transient = Track(
                ScriptableObject.CreateInstance<NpcMovementProfile>());
            transient.ResetToDefaults();
            definition.MovementProfile = transient;
            NpcBuildReadinessReport unfitted =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(unfitted.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_NOT_PERSISTENT"), Is.True);
            Assert.That(unfitted.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_UNFITTED"), Is.True);
            Assert.That(unfitted.ReadyForBuild, Is.False);
        }

        [Test]
        public void MovementDoctorDistinguishesSourceAndToolkitStaleness()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            NpcMovementProfile movement = definition.MovementProfile;
            Object pose = movement.ProviderStandingPose;
            Object config = movement.ProviderMovementConfig;

            movement.SetAutoFitMeasurements(
                movement.EyeHeight,
                movement.BodyHeight,
                movement.NavHeight,
                movement.LeftLegLength,
                movement.RightLegLength,
                movement.HipWidth,
                movement.StanceWidth,
                movement.SoleHeight,
                movement.NavRadius,
                movement.NavBaseOffset,
                movement.LeftFootForwardLocal,
                movement.RightFootForwardLocal,
                "different-source-hash",
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(
                    definition));
            movement.SetProviderRecipe(pose, config, "provider-recipe-v1");

            NpcBuildReadinessReport changedSource =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(changedSource.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_SOURCE_CHANGED"), Is.True);
            Assert.That(changedSource.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_TOOLKIT_OUTDATED"), Is.False);

            movement.SetAutoFitMeasurements(
                movement.EyeHeight,
                movement.BodyHeight,
                movement.NavHeight,
                movement.LeftLegLength,
                movement.RightLegLength,
                movement.HipWidth,
                movement.StanceWidth,
                movement.SoleHeight,
                movement.NavRadius,
                movement.NavBaseOffset,
                movement.LeftFootForwardLocal,
                movement.RightFootForwardLocal,
                "source-hash",
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(
                    definition));
            movement.SetProviderRecipe(pose, config, "provider-recipe-v1");
            FieldInfo toolkitReceipt = typeof(NpcMovementProfile).GetField(
                "autoFitToolkitVersion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(toolkitReceipt, Is.Not.Null);
            toolkitReceipt.SetValue(movement, "0.3.0");

            NpcBuildReadinessReport outdated =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(outdated.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_TOOLKIT_OUTDATED"), Is.True);
            Assert.That(outdated.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_SOURCE_CHANGED"), Is.False);
        }

        [Test]
        public void MovementDoctorRejectsPhysicsAlignmentChangedAfterFit()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            var sourceObject = new GameObject("Movement Anatomy Receipt Avatar");
            string sourcePath = testFolder + "/MovementAnatomyReceipt.prefab";
            try
            {
                PrefabUtility.SaveAsPrefabAsset(sourceObject, sourcePath);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string sourceHash = AssetDatabase.GetAssetDependencyHash(sourcePath)
                .ToString();
            definition.AvatarSourceProfile.SetSource(
                source, sourceGuid, sourceHash, "sdk", string.Empty);
            definition.AnatomyProfile.MarkBaselineFitted(sourceHash);
            definition.Initialize(
                source,
                NpcAvatarSourceKind.HumanoidPrefab,
                definition.AvatarSourceProfile,
                definition.AnatomyProfile,
                definition.BuildProfile,
                sourceGuid,
                sourceHash,
                definition.AudioProfile,
                definition.MovementProfile);

            NpcMovementProfile movement = definition.MovementProfile;
            Object pose = movement.ProviderStandingPose;
            Object config = movement.ProviderMovementConfig;
            movement.SetAutoFitMeasurements(
                movement.EyeHeight,
                movement.BodyHeight,
                movement.NavHeight,
                movement.LeftLegLength,
                movement.RightLegLength,
                movement.HipWidth,
                movement.StanceWidth,
                movement.SoleHeight,
                movement.NavRadius,
                movement.NavBaseOffset,
                movement.LeftFootForwardLocal,
                movement.RightFootForwardLocal,
                sourceHash,
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition));
            movement.SetProviderRecipe(pose, config, "provider-recipe-v1");

            NpcBuildReadinessReport current =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(current.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_ALIGNMENT_CHANGED"), Is.False);

            NpcBodyRoleProfile hips =
                definition.AnatomyProfile.FindRole(HumanBodyBones.Hips);
            hips.ColliderCenter += new Vector3(0.01f, 0f, 0f);

            NpcBuildReadinessReport stale =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(stale.Issues.Any(value =>
                value.Code == "MOVEMENT_PROFILE_ALIGNMENT_CHANGED"), Is.True);
            Assert.That(stale.ReadyForBuild, Is.False);
        }

        [Test]
        public void MovementValuesAndProviderRecipeBlockInvalidBuilds()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            NpcMovementProfile movement = definition.MovementProfile;

            movement.WalkSpeed = float.NaN;
            movement.StrideScale = 5f;
            movement.SetProviderRecipe(
                movement.ProviderStandingPose, null, string.Empty);

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(report.Issues.Any(value =>
                value.Code == "MOVEMENT_VALUE_NOT_FINITE"
                && value.Message.Contains("Walk speed")), Is.True);
            Assert.That(report.Issues.Any(value =>
                value.Code == "MOVEMENT_VALUE_OUT_OF_RANGE"
                && value.Message.Contains("Stride scale")), Is.True);
            Assert.That(report.Issues.Any(value =>
                value.Code == "MOVEMENT_PROVIDER_RECIPE_MISSING"), Is.True);
            Assert.That(report.Issues.Any(value =>
                value.Code == "MOVEMENT_PROVIDER_ASSETS_INCOMPLETE"), Is.True);
            Assert.That(report.ReadyForBuild, Is.False);
        }

        [Test]
        public void MovementTuningAndProviderIdentityParticipateInFingerprint()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            NpcMovementProfile movement = definition.MovementProfile;
            string before = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;

            movement.StepRateScale = 1.125f;
            string tuned = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;
            Assert.That(tuned, Is.Not.EqualTo(before));

            var replacement = ScriptableObject.CreateInstance<NpcBuildProfile>();
            replacement.name = "Replacement Provider Movement Config";
            AssetDatabase.CreateAsset(
                replacement, testFolder + "/ReplacementMovementConfig.asset");
            AssetDatabase.SaveAssets();
            movement.SetProviderRecipe(
                movement.ProviderStandingPose,
                replacement,
                movement.ProviderRecipeFingerprint);
            string providerAssetChanged =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, preview).Fingerprint;
            Assert.That(providerAssetChanged, Is.Not.EqualTo(tuned));

            movement.SetProviderRecipe(
                movement.ProviderStandingPose,
                replacement,
                "provider-recipe-v2");
            string recipeChanged = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;
            Assert.That(recipeChanged, Is.Not.EqualTo(providerAssetChanged));
        }

        [Test]
        public void PreviewImporterArtifactChurnDoesNotChangeReadinessFingerprint()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject previewRoot = CreateValidPreview(rendererCount: 0);
            string previewPath = testFolder + "/GeneratedPreview.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                previewRoot, previewPath);
            Assert.That(saved, Is.Not.Null);

            AssetImporter importer = AssetImporter.GetAtPath(previewPath);
            Assert.That(importer, Is.Not.Null);
            importer.assetBundleName = "preview-artifact-a";
            AssetDatabase.WriteImportSettingsIfDirty(previewPath);
            AssetDatabase.ImportAsset(
                previewPath, ImportAssetOptions.ForceSynchronousImport);
            string dependencyBefore = AssetDatabase.GetAssetDependencyHash(
                previewPath).ToString();
            string fingerprintBefore = NpcBuildReadinessDoctor
                .ValidateWithPreview(
                    definition,
                    AssetDatabase.LoadAssetAtPath<GameObject>(previewPath),
                    previewPath)
                .Fingerprint;

            importer = AssetImporter.GetAtPath(previewPath);
            importer.assetBundleName = "preview-artifact-b";
            AssetDatabase.WriteImportSettingsIfDirty(previewPath);
            AssetDatabase.ImportAsset(
                previewPath, ImportAssetOptions.ForceSynchronousImport);
            string dependencyAfter = AssetDatabase.GetAssetDependencyHash(
                previewPath).ToString();
            string fingerprintAfter = NpcBuildReadinessDoctor
                .ValidateWithPreview(
                    definition,
                    AssetDatabase.LoadAssetAtPath<GameObject>(previewPath),
                    previewPath)
                .Fingerprint;

            Assert.That(dependencyAfter, Is.Not.EqualTo(dependencyBefore),
                "The regression needs two distinct Unity artifact hashes.");
            Assert.That(fingerprintAfter, Is.EqualTo(fingerprintBefore));
        }

        [Test]
        public void PreviewSemanticChangeStillChangesReadinessFingerprint()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            string before = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;

            FindBody(preview.transform.Find("Physics"), HumanBodyBones.Hips)
                .mass = 2f;
            string after = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;

            Assert.That(after, Is.Not.EqualTo(before));
        }

        [Test]
        public void MovementTuningInvalidatesStepFourFiveAndPackagingButNotPhysics()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            var sourceObject = new GameObject("Movement Fingerprint Avatar");
            string sourcePath = testFolder + "/MovementFingerprintAvatar.prefab";
            try
            {
                PrefabUtility.SaveAsPrefabAsset(sourceObject, sourcePath);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
            }
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            definition.Initialize(
                source,
                NpcAvatarSourceKind.HumanoidPrefab,
                definition.AvatarSourceProfile,
                definition.AnatomyProfile,
                definition.BuildProfile,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                definition.AudioProfile,
                definition.MovementProfile);

            string physicsBefore =
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition);
            string readinessBefore = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;
            string nativeBefore =
                NpcNativeBuildCoordinator.ComputeNativeInputFingerprint(
                    definition,
                    readinessBefore,
                    "test.provider",
                    NpcCompatibilityCapabilities.CoreAnatomy);
            string packagingBefore =
                NpcPackagingFingerprintUtility.Compute(definition, null);

            definition.MovementProfile.StrideScale = 1.125f;

            string physicsAfter =
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition);
            string readinessAfter = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;
            string nativeAfter =
                NpcNativeBuildCoordinator.ComputeNativeInputFingerprint(
                    definition,
                    readinessAfter,
                    "test.provider",
                    NpcCompatibilityCapabilities.CoreAnatomy);
            string packagingAfter =
                NpcPackagingFingerprintUtility.Compute(definition, null);

            Assert.That(physicsAfter, Is.EqualTo(physicsBefore));
            Assert.That(readinessAfter, Is.Not.EqualTo(readinessBefore));
            Assert.That(nativeAfter, Is.Not.EqualTo(nativeBefore));
            Assert.That(packagingAfter, Is.Not.EqualTo(packagingBefore));
        }

        [Test]
        public void IssuesAndFingerprintAreDeterministicAndInspectionIsReadOnly()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            definition.AnatomyProfile.FindRole(HumanBodyBones.RightFoot)
                .CapsuleHeight = float.NaN;
            string definitionBefore = EditorJsonUtility.ToJson(definition, true);
            string anatomyBefore = EditorJsonUtility.ToJson(
                definition.AnatomyProfile, true);
            string sourceBefore = EditorJsonUtility.ToJson(
                definition.AvatarSourceProfile, true);
            string buildBefore = EditorJsonUtility.ToJson(
                definition.BuildProfile, true);
            string audioBefore = EditorJsonUtility.ToJson(
                definition.AudioProfile, true);
            string movementBefore = EditorJsonUtility.ToJson(
                definition.MovementProfile, true);
            bool definitionDirtyBefore = EditorUtility.IsDirty(definition);
            bool anatomyDirtyBefore = EditorUtility.IsDirty(
                definition.AnatomyProfile);
            bool sourceDirtyBefore = EditorUtility.IsDirty(
                definition.AvatarSourceProfile);
            bool buildDirtyBefore = EditorUtility.IsDirty(
                definition.BuildProfile);
            bool audioDirtyBefore = EditorUtility.IsDirty(
                definition.AudioProfile);
            bool movementDirtyBefore = EditorUtility.IsDirty(
                definition.MovementProfile);
            int previewChildrenBefore = preview.transform.childCount;

            NpcBuildReadinessReport first =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            NpcBuildReadinessReport second =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            CollectionAssert.AreEqual(
                first.Issues.Select(IssueKey).ToArray(),
                second.Issues.Select(IssueKey).ToArray());
            Assert.That(first.Fingerprint, Is.EqualTo(second.Fingerprint));
            Assert.That(EditorJsonUtility.ToJson(definition, true),
                Is.EqualTo(definitionBefore));
            Assert.That(EditorJsonUtility.ToJson(definition.AnatomyProfile, true),
                Is.EqualTo(anatomyBefore));
            Assert.That(EditorJsonUtility.ToJson(definition.AvatarSourceProfile, true),
                Is.EqualTo(sourceBefore));
            Assert.That(EditorJsonUtility.ToJson(definition.BuildProfile, true),
                Is.EqualTo(buildBefore));
            Assert.That(EditorJsonUtility.ToJson(definition.AudioProfile, true),
                Is.EqualTo(audioBefore));
            Assert.That(EditorJsonUtility.ToJson(definition.MovementProfile, true),
                Is.EqualTo(movementBefore));
            Assert.That(preview.transform.childCount, Is.EqualTo(previewChildrenBefore));
            Assert.That(EditorUtility.IsDirty(definition),
                Is.EqualTo(definitionDirtyBefore));
            Assert.That(EditorUtility.IsDirty(definition.AnatomyProfile),
                Is.EqualTo(anatomyDirtyBefore));
            Assert.That(EditorUtility.IsDirty(definition.AvatarSourceProfile),
                Is.EqualTo(sourceDirtyBefore));
            Assert.That(EditorUtility.IsDirty(definition.BuildProfile),
                Is.EqualTo(buildDirtyBefore));
            Assert.That(EditorUtility.IsDirty(definition.AudioProfile),
                Is.EqualTo(audioDirtyBefore));
            Assert.That(EditorUtility.IsDirty(definition.MovementProfile),
                Is.EqualTo(movementDirtyBefore));
        }

        [Test]
        public void SilentModeDoesNotRequirePopulatedOrPersistentAudio()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.AudioProfile = null;

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));

            Assert.That(definition.AudioMode, Is.EqualTo(NpcAudioMode.Silent));
            Assert.That(report.Issues.Where(value =>
                value.Code.StartsWith("AUDIO_", StringComparison.Ordinal)), Is.Empty);
        }

        [Test]
        public void ProfileModeRequiresSavedProfileAndBasicReactionGroups()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.AudioMode = NpcAudioMode.Profile;

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));

            Assert.That(report.Issues.Any(value =>
                value.Code == "AUDIO_PROFILE_NOT_PERSISTENT"), Is.True);
            Assert.That(report.Issues.Count(value =>
                value.Code == "AUDIO_REQUIRED_GROUP_EMPTY"), Is.EqualTo(3));
            Assert.That(report.Issues.Any(value =>
                value.Code == "AUDIO_PROVENANCE_INCOMPLETE"), Is.False);
            Assert.That(report.ReadyForBuild, Is.False);

            definition.AudioProfile = null;
            NpcBuildReadinessReport missing =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));
            Assert.That(missing.Issues.Any(value =>
                value.Code == "AUDIO_PROFILE_MISSING"), Is.True);
        }

        [Test]
        public void AudioAuthoringAndClipOrderChangeFingerprintEvenWhileSilent()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            NpcBuildReadinessReport before =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            definition.AudioProfile.SetProvenance(
                "English", "Source", "Credit", "Permission", "Notes");
            NpcBuildReadinessReport after =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(after.Fingerprint, Is.Not.EqualTo(before.Fingerprint));

            AudioClip first = Track(AudioClip.Create(
                "first", 32, 1, 16000, false));
            AudioClip second = Track(AudioClip.Create(
                "second", 32, 1, 16000, false));
            definition.AudioProfile.SetClips(
                NpcAudioEvent.PainSmall, new[] { first, second });
            string forward = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;
            definition.AudioProfile.SetClips(
                NpcAudioEvent.PainSmall, new[] { second, first });
            string reversed = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;

            Assert.That(reversed, Is.Not.EqualTo(forward));
        }

        [Test]
        public void SecondaryMotionChoiceChangesReadinessFingerprint()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            GameObject preview = CreateValidPreview(rendererCount: 0);
            definition.IncludeSecondaryMotion = false;
            string disabled = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;

            definition.IncludeSecondaryMotion = true;
            string enabled = NpcBuildReadinessDoctor.ValidateWithPreview(
                definition, preview).Fingerprint;

            Assert.That(enabled, Is.Not.EqualTo(disabled));
        }

        [Test]
        public void SecondaryMotionRequiresBothBreastBonesAndExplainsScope()
        {
            var sourceObject = new GameObject("Secondary Motion Avatar");
            var hips = new GameObject("Hips");
            hips.transform.SetParent(sourceObject.transform, false);
            var rightBreast = new GameObject("Right Breast");
            rightBreast.transform.SetParent(hips.transform, false);
            var leftBreast = new GameObject("Left Breast");
            leftBreast.transform.SetParent(hips.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(sourceObject.transform, false);
            SkinnedMeshRenderer renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = hips.transform;
            renderer.bones = new[]
            {
                rightBreast.transform,
                leftBreast.transform,
            };
            var avatar = sourceObject.AddComponent<SLZ.VRMK.Avatar>();
            avatar.bulgeBreast = new SLZ.VRMK.Avatar.SoftBulge
            {
                primaryRt = rightBreast.transform,
            };
            string sourcePath = testFolder + "/SecondaryMotionAvatar.prefab";
            GameObject sourcePrefab = PrefabUtility.SaveAsPrefabAsset(
                sourceObject, sourcePath);
            Object.DestroyImmediate(sourceObject);

            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.AvatarSourceProfile.SetSource(
                sourcePrefab,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                "sdk",
                string.Empty);
            definition.AvatarSourceProfile.SetBindings(
                new[]
                {
                    new NpcHumanoidBoneBinding(
                        HumanBodyBones.Hips, "Hips"),
                },
                new[]
                {
                    new NpcAvatarRendererBinding(
                        NpcAvatarRendererCategory.Body, "Body"),
                },
                Array.Empty<NpcOptionalAvatarBinding>(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            definition.Initialize(
                sourcePrefab,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                definition.AvatarSourceProfile,
                definition.AnatomyProfile,
                definition.BuildProfile,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                definition.AudioProfile,
                definition.MovementProfile);
            definition.IncludeSecondaryMotion = true;
            GameObject preview = CreateValidPreview(rendererCount: 0);

            NpcBuildReadinessReport missing =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            NpcBuildReadinessIssue missingIssue = missing.Issues.Single(value =>
                value.Code == "SECONDARY_MOTION_BREAST_BONES_MISSING");
            Assert.That(missingIssue.Severity,
                Is.EqualTo(NpcBuildReadinessSeverity.Error));
            Assert.That(missingIssue.Message, Does.Contain("left"));
            Assert.That(missingIssue.Message, Does.Contain("Abdomen and butt"));

            GameObject contents = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                SLZ.VRMK.Avatar savedAvatar =
                    contents.GetComponent<SLZ.VRMK.Avatar>();
                savedAvatar.bulgeBreast.secondaryLf =
                    contents.transform.Find("Hips/Left Breast");
                PrefabUtility.SaveAsPrefabAsset(contents, sourcePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            NpcBuildReadinessReport ready =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            NpcBuildReadinessIssue readyIssue = ready.Issues.Single(value =>
                value.Code == "SECONDARY_MOTION_BREAST_BONES_READY");
            Assert.That(readyIssue.Severity,
                Is.EqualTo(NpcBuildReadinessSeverity.Info));
            Assert.That(readyIssue.Message,
                Does.Contain("renderer-skinned Breast Soft Body bones"));
            Assert.That(ready.Issues.Any(value =>
                value.Code == "SECONDARY_MOTION_BREAST_BONES_MISSING"),
                Is.False);
        }

        [Test]
        public void SecondaryMotionRejectsDuplicateUnskinnedAndUnownedBones()
        {
            var sourceObject = new GameObject("Secondary Motion Invalid Avatar");
            var hips = new GameObject("Hips");
            hips.transform.SetParent(sourceObject.transform, false);
            var rightBreast = new GameObject("Right Breast");
            rightBreast.transform.SetParent(hips.transform, false);
            var accessory = new GameObject("Accessory");
            accessory.transform.SetParent(sourceObject.transform, false);
            var leftBreast = new GameObject("Left Breast");
            leftBreast.transform.SetParent(accessory.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(sourceObject.transform, false);
            SkinnedMeshRenderer renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = hips.transform;
            renderer.bones = new[]
            {
                rightBreast.transform,
                leftBreast.transform,
            };
            var avatar = sourceObject.AddComponent<SLZ.VRMK.Avatar>();
            avatar.bulgeBreast = new SLZ.VRMK.Avatar.SoftBulge
            {
                primaryRt = rightBreast.transform,
                secondaryLf = rightBreast.transform,
            };
            string sourcePath = testFolder
                                + "/SecondaryMotionInvalidAvatar.prefab";
            GameObject sourcePrefab = PrefabUtility.SaveAsPrefabAsset(
                sourceObject, sourcePath);
            Object.DestroyImmediate(sourceObject);

            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.AvatarSourceProfile.SetSource(
                sourcePrefab,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                "sdk",
                string.Empty);
            definition.AvatarSourceProfile.SetBindings(
                new[]
                {
                    new NpcHumanoidBoneBinding(
                        HumanBodyBones.Hips, "Hips"),
                },
                new[]
                {
                    new NpcAvatarRendererBinding(
                        NpcAvatarRendererCategory.Body, "Body"),
                },
                Array.Empty<NpcOptionalAvatarBinding>(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            definition.Initialize(
                sourcePrefab,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                definition.AvatarSourceProfile,
                definition.AnatomyProfile,
                definition.BuildProfile,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                definition.AudioProfile,
                definition.MovementProfile);
            definition.IncludeSecondaryMotion = true;
            GameObject preview = CreateValidPreview(rendererCount: 0);

            NpcBuildReadinessReport duplicate =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(duplicate.Issues.Any(value =>
                value.Code == "SECONDARY_MOTION_BREAST_BONES_DUPLICATE"),
                Is.True);

            GameObject contents = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                SLZ.VRMK.Avatar savedAvatar =
                    contents.GetComponent<SLZ.VRMK.Avatar>();
                savedAvatar.bulgeBreast.secondaryLf =
                    contents.transform.Find("Accessory/Left Breast");
                SkinnedMeshRenderer savedRenderer =
                    contents.transform.Find("Body")
                        .GetComponent<SkinnedMeshRenderer>();
                savedRenderer.bones = new[]
                {
                    contents.transform.Find("Hips/Right Breast"),
                };
                PrefabUtility.SaveAsPrefabAsset(contents, sourcePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            NpcBuildReadinessReport unskinned =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(unskinned.Issues.Any(value =>
                value.Code == "SECONDARY_MOTION_BREAST_BONE_NOT_SKINNED"),
                Is.True);

            contents = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                SkinnedMeshRenderer savedRenderer =
                    contents.transform.Find("Body")
                        .GetComponent<SkinnedMeshRenderer>();
                savedRenderer.bones = new[]
                {
                    contents.transform.Find("Hips/Right Breast"),
                    contents.transform.Find("Accessory/Left Breast"),
                };
                PrefabUtility.SaveAsPrefabAsset(contents, sourcePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            NpcBuildReadinessReport unowned =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);
            Assert.That(unowned.Issues.Any(value =>
                value.Code
                    == "SECONDARY_MOTION_BREAST_BONE_OWNER_UNMAPPABLE"),
                Is.True);
        }

        [Test]
        public void SecondaryMotionRejectsBoneOutsideAcceptedAnimationRoot()
        {
            var sourceObject = new GameObject("Secondary Motion Root Boundary");
            var outside = new GameObject("OutsideAnimationRoot");
            outside.transform.SetParent(sourceObject.transform, false);
            var rig = new GameObject("Rig");
            rig.transform.SetParent(outside.transform, false);
            rig.AddComponent<Animator>();
            var hips = new GameObject("Hips");
            hips.transform.SetParent(rig.transform, false);
            var rightBreast = new GameObject("Right Breast");
            rightBreast.transform.SetParent(hips.transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(sourceObject.transform, false);
            SkinnedMeshRenderer renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = hips.transform;
            renderer.bones = new[]
            {
                rightBreast.transform,
                outside.transform,
            };
            var avatar = sourceObject.AddComponent<SLZ.VRMK.Avatar>();
            avatar.bulgeBreast = new SLZ.VRMK.Avatar.SoftBulge
            {
                primaryRt = rightBreast.transform,
                // This is inside the source prefab and maps through the provider's
                // Hips-root fallback, but it is outside the accepted Animator root.
                secondaryLf = outside.transform,
            };
            string sourcePath = testFolder
                                + "/SecondaryMotionRootBoundary.prefab";
            GameObject sourcePrefab = PrefabUtility.SaveAsPrefabAsset(
                sourceObject, sourcePath);
            Object.DestroyImmediate(sourceObject);

            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.AvatarSourceProfile.SetSource(
                sourcePrefab,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                "sdk",
                "OutsideAnimationRoot/Rig");
            definition.AvatarSourceProfile.SetBindings(
                new[]
                {
                    new NpcHumanoidBoneBinding(
                        HumanBodyBones.Hips,
                        "OutsideAnimationRoot/Rig/Hips"),
                },
                new[]
                {
                    new NpcAvatarRendererBinding(
                        NpcAvatarRendererCategory.Body, "Body"),
                },
                Array.Empty<NpcOptionalAvatarBinding>(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            definition.Initialize(
                sourcePrefab,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                definition.AvatarSourceProfile,
                definition.AnatomyProfile,
                definition.BuildProfile,
                AssetDatabase.AssetPathToGUID(sourcePath),
                AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(),
                definition.AudioProfile,
                definition.MovementProfile);
            definition.IncludeSecondaryMotion = true;

            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));

            Assert.That(report.Issues.Any(value =>
                value.Code
                    == "SECONDARY_MOTION_BREAST_BONE_OUTSIDE_ANIMATION_ROOT"),
                Is.True);
        }

        [Test]
        public void RequestedPhysicalJawUsesTargetedBlockingErrors()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.IncludePhysicalJaw = true;

            NpcBuildReadinessReport missingAndDisabled =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));

            AssertIssue(
                missingAndDisabled,
                "PHYSICAL_JAW_MAPPING_MISSING",
                HumanBodyBones.Jaw);
            AssertIssue(
                missingAndDisabled,
                "PHYSICAL_JAW_DISABLED",
                HumanBodyBones.Jaw);
            Assert.That(missingAndDisabled.ExpectedRoleCount, Is.EqualTo(17));
            Assert.That(missingAndDisabled.ReadyForBuild, Is.False);

            definition.AnatomyProfile.OptionalJaw.Enabled = true;
            NpcBuildReadinessReport unfitted =
                NpcBuildReadinessDoctor.ValidateWithPreview(
                    definition, CreateValidPreview(rendererCount: 0));
            AssertIssue(
                unfitted,
                "PHYSICAL_JAW_UNFITTED",
                HumanBodyBones.Jaw);
        }

        [Test]
        public void RequestedPhysicalJawPreviewAcceptsSeventeenthBodyUnderHead()
        {
            NpcDefinition definition = CreateDefinition(rendererCount: 0);
            definition.IncludePhysicalJaw = true;
            definition.AvatarSourceProfile.SetBindings(
                new NpcHumanoidBoneBinding[0],
                new NpcAvatarRendererBinding[0],
                new NpcOptionalAvatarBinding[0],
                string.Empty,
                string.Empty,
                string.Empty,
                "Rig/Head/Jaw");
            NpcBodyRoleProfile jaw = definition.AnatomyProfile.OptionalJaw;
            jaw.Enabled = true;
            jaw.AlignmentState = NpcAlignmentState.AutoFit;
            jaw.ColliderShape = NpcColliderShape.Box;
            jaw.ColliderSize = new Vector3(0.1f, 0.07f, 0.06f);
            jaw.JointAxis = Vector3.right;
            jaw.JointSecondaryAxis = Vector3.up;
            jaw.AngularXMotion = NpcJointMotion.Limited;
            jaw.AngularYMotion = NpcJointMotion.Limited;
            jaw.AngularZMotion = NpcJointMotion.Locked;
            jaw.AngularLowLimits = new Vector3(-28f, -10f, 0f);
            jaw.AngularHighLimits = new Vector3(0f, 10f, 0f);
            jaw.JointDriveMaxForce = 36f;
            jaw.MuscleSpring = 5000000f;
            jaw.MuscleDamper = 100000f;
            jaw.MuscleWeight = 1f;

            GameObject preview = CreateValidPreview(
                rendererCount: 0, includePhysicalJaw: true);
            NpcBuildReadinessReport report =
                NpcBuildReadinessDoctor.ValidateWithPreview(definition, preview);

            Assert.That(report.ExpectedRoleCount, Is.EqualTo(17));
            Assert.That(report.RigidbodyCount, Is.EqualTo(17));
            Assert.That(report.ColliderCount, Is.EqualTo(17));
            Assert.That(report.JointCount, Is.EqualTo(17));
            Assert.That(report.Issues.Where(IsPreviewError), Is.Empty);
        }

        private NpcDefinition CreateDefinition(int rendererCount)
        {
            var source = Track(ScriptableObject.CreateInstance<NpcAvatarSourceProfile>());
            source.SetSource(null, "source-guid", "source-hash", "sdk", string.Empty);
            source.SetBindings(
                new NpcHumanoidBoneBinding[0],
                Enumerable.Range(0, rendererCount).Select(index =>
                    new NpcAvatarRendererBinding(
                        NpcAvatarRendererCategory.Body,
                        "Visual" + index)),
                new NpcOptionalAvatarBinding[0],
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

            var anatomy = Track(ScriptableObject.CreateInstance<NpcAnatomyProfile>());
            anatomy.ResetToHumanoidDefaults();
            foreach (NpcBodyRoleProfile role in anatomy.BodyRoles)
            {
                role.AlignmentState = NpcAlignmentState.AutoFit;
                role.MassKilograms = Mathf.Max(role.MassKilograms, 0.1f);
                role.JointAxis = Vector3.right;
                role.JointSecondaryAxis = Vector3.up;
                if (role.ColliderShape == NpcColliderShape.Box)
                    role.ColliderSize = new Vector3(0.1f, 0.2f, 0.1f);
                else
                {
                    role.CapsuleRadius = 0.05f;
                    role.CapsuleHeight = 0.2f;
                }
            }
            anatomy.MarkBaselineFitted("source-hash");

            var build = Track(ScriptableObject.CreateInstance<NpcBuildProfile>());
            build.Initialize("Tester", "Readiness", "Assets/Readiness");
            var audio = Track(ScriptableObject.CreateInstance<NpcAudioProfile>());
            NpcMovementProfile movement = CreateValidMovementProfile();
            var definition = Track(ScriptableObject.CreateInstance<NpcDefinition>());
            definition.Initialize(
                null,
                NpcAvatarSourceKind.MarrowAvatarPrefab,
                source,
                anatomy,
                build,
                "source-guid",
                "source-hash",
                audio,
                movement);
            definition.IncludePhysicalJaw = false;
            return definition;
        }

        private NpcMovementProfile CreateValidMovementProfile()
        {
            string suffix = Guid.NewGuid().ToString("N");
            var pose = ScriptableObject.CreateInstance<NpcBuildProfile>();
            pose.name = "Provider Standing Pose";
            AssetDatabase.CreateAsset(
                pose, testFolder + "/StandingPose-" + suffix + ".asset");
            var config = ScriptableObject.CreateInstance<NpcBuildProfile>();
            config.name = "Provider Movement Config";
            AssetDatabase.CreateAsset(
                config, testFolder + "/MovementConfig-" + suffix + ".asset");

            var movement = ScriptableObject.CreateInstance<NpcMovementProfile>();
            movement.name = "Movement Profile";
            movement.ResetToDefaults();
            movement.SetAutoFitMeasurements(
                1.6f,
                1.72f,
                1.7f,
                0.82f,
                0.84f,
                0.3f,
                0.26f,
                0.02f,
                0.38f,
                0f,
                Vector3.forward,
                Vector3.forward,
                "source-hash",
                string.Empty);
            movement.SetProviderRecipe(pose, config, "provider-recipe-v1");
            AssetDatabase.CreateAsset(
                movement, testFolder + "/Movement-" + suffix + ".asset");
            AssetDatabase.SaveAssets();
            return movement;
        }

        private GameObject CreateValidPreview(
            int rendererCount,
            bool includePhysicalJaw = false)
        {
            var root = Track(new GameObject("NPC Physics Preview"));
            var animationRoot = new GameObject("AnimationRoot");
            animationRoot.transform.SetParent(root.transform, false);
            var physicsRoot = new GameObject("Physics");
            physicsRoot.transform.SetParent(root.transform, false);

            for (int index = 0; index < rendererCount; index++)
            {
                var visual = new GameObject("Visual" + index);
                visual.transform.SetParent(animationRoot.transform, false);
                visual.AddComponent<SkinnedMeshRenderer>();
            }

            HumanBodyBones[] roles = includePhysicalJaw
                ? NpcHumanoidGraph.CanonicalRoles
                    .Concat(new[] { HumanBodyBones.Jaw })
                    .ToArray()
                : NpcHumanoidGraph.CanonicalRoles;
            var bodies = new Dictionary<HumanBodyBones, Rigidbody>();
            foreach (HumanBodyBones role in roles)
            {
                Transform parent = physicsRoot.transform;
                if (TryGetParent(role, out HumanBodyBones parentRole))
                    parent = bodies[parentRole].transform;
                var bodyObject = new GameObject(role.ToString());
                bodyObject.transform.SetParent(parent, false);
                Rigidbody body = bodyObject.AddComponent<Rigidbody>();
                body.mass = 1f;
                var colliderHolder = new GameObject("PrimaryCollider");
                colliderHolder.transform.SetParent(bodyObject.transform, false);
                colliderHolder.AddComponent<BoxCollider>().size = Vector3.one * 0.1f;
                bodies[role] = body;
            }

            foreach (HumanBodyBones role in roles)
            {
                ConfigurableJoint joint = bodies[role]
                    .gameObject.AddComponent<ConfigurableJoint>();
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                if (TryGetParent(role, out HumanBodyBones parentRole))
                    joint.connectedBody = bodies[parentRole];
            }
            return root;
        }

        private static bool TryGetParent(
            HumanBodyBones role,
            out HumanBodyBones parent)
        {
            if (role == HumanBodyBones.Jaw)
            {
                parent = HumanBodyBones.Head;
                return true;
            }
            return NpcHumanoidGraph.TryGetParent(role, out parent);
        }

        private static Rigidbody FindBody(Transform physics, HumanBodyBones role)
        {
            return physics.GetComponentsInChildren<Rigidbody>(true).Single(value =>
                value.name == role.ToString());
        }

        private T Track<T>(T value) where T : Object
        {
            cleanup.Add(value);
            return value;
        }

        private static bool IsPreviewError(NpcBuildReadinessIssue issue)
        {
            return issue.Severity == NpcBuildReadinessSeverity.Error
                   && issue.Code.StartsWith("PREVIEW_", StringComparison.Ordinal);
        }

        private static void AssertIssue(
            NpcBuildReadinessReport report,
            string code,
            HumanBodyBones role)
        {
            Assert.That(report.Issues.Any(value =>
                value.Code == code && value.Role == role), Is.True,
                "Expected " + code + " for " + role + ".");
        }

        private static string IssueKey(NpcBuildReadinessIssue issue)
        {
            return issue.Severity + "|" + issue.Code + "|" + issue.Role
                   + "|" + issue.Message;
        }
    }
}
