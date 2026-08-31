using NUnit.Framework;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcMovementProfileTests
    {
        [Test]
        public void DefaultsAreSafeAndRemainExplicitlyUnfitted()
        {
            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            try
            {
                profile.ResetToDefaults();

                Assert.That(profile.AlignmentState,
                    Is.EqualTo(NpcAlignmentState.Unseeded));
                Assert.That(profile.HasFittedMeasurements, Is.False);
                Assert.That(profile.StanceWidthScale, Is.EqualTo(1f));
                Assert.That(profile.StrideScale, Is.EqualTo(1f));
                Assert.That(profile.StepHeightScale, Is.EqualTo(1f));
                Assert.That(profile.StepRateScale, Is.EqualTo(1f));
                Assert.That(profile.WalkSpeed, Is.EqualTo(2f));
                Assert.That(profile.Acceleration, Is.EqualTo(2.6f));
                Assert.That(profile.AngularSpeed, Is.EqualTo(120f));
                Assert.That(profile.StoppingDistance, Is.EqualTo(1f));
                Assert.That(profile.StartingHostility, Is.EqualTo(0f));
                Assert.That(profile.HostilityAfterTypicalHit,
                    Is.EqualTo(0.25f));
                Assert.That(profile.RetaliationVengefulness, Is.EqualTo(1f));
                Assert.That(profile.ProviderStandingPose, Is.Null);
                Assert.That(profile.ProviderMovementConfig, Is.Null);
                Assert.That(profile.ProviderRecipeFingerprint, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void HostilityResponseConvertsTypicalHitToNativeVengefulness()
        {
            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            try
            {
                profile.ResetToDefaults();
                profile.StartingHostility = 0.2f;
                profile.HostilityAfterTypicalHit = 0.7f;

                Assert.That(profile.TypicalHitHostilityGain,
                    Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(profile.RetaliationVengefulness,
                    Is.EqualTo(2f).Within(0.0001f));
                Assert.That(
                    Mathf.MoveTowards(
                        profile.StartingHostility,
                        1f,
                        0.25f * profile.RetaliationVengefulness),
                    Is.EqualTo(profile.HostilityAfterTypicalHit)
                        .Within(0.0001f));

                profile.HostilityAfterTypicalHit = -1f;
                Assert.That(profile.HostilityAfterTypicalHit,
                    Is.EqualTo(profile.StartingHostility));
                profile.StartingHostility = 2f;
                Assert.That(profile.StartingHostility, Is.EqualTo(1f));
                Assert.That(profile.HostilityAfterTypicalHit, Is.EqualTo(1f));
                Assert.That(profile.RetaliationVengefulness, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void HostilityResponseChangesNativeBuildMovementFingerprint()
        {
            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            try
            {
                profile.ResetToDefaults();
                string defensive =
                    NpcBuildReadinessDoctor.ComputeMovementFingerprint(profile);

                profile.HostilityAfterTypicalHit = 0.5f;
                string pursuit =
                    NpcBuildReadinessDoctor.ComputeMovementFingerprint(profile);

                Assert.That(pursuit, Is.Not.EqualTo(defensive));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AutoFitStoresOneCoherentMeasurementReceipt()
        {
            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            var pose = ScriptableObject.CreateInstance<NpcBuildProfile>();
            var movementConfig = ScriptableObject.CreateInstance<NpcBuildProfile>();
            try
            {
                profile.SetProviderRecipe(pose, movementConfig, "old-recipe");
                profile.SetAutoFitMeasurements(
                    1.62f,
                    1.72f,
                    1.70f,
                    0.82f,
                    0.86f,
                    0.31f,
                    0.27f,
                    0.03f,
                    0.39f,
                    -0.01f,
                    new Vector3(0f, 0f, 3f),
                    new Vector3(0f, 0f, 4f),
                    "source-hash",
                    "authoring-hash");

                Assert.That(profile.AlignmentState,
                    Is.EqualTo(NpcAlignmentState.AutoFit));
                Assert.That(profile.MeanLegLength, Is.EqualTo(0.84f).Within(0.0001f));
                Assert.That(profile.LeftFootForwardLocal, Is.EqualTo(Vector3.forward));
                Assert.That(profile.RightFootForwardLocal, Is.EqualTo(Vector3.forward));
                Assert.That(profile.AutoFitSourceDependencyHash,
                    Is.EqualTo("source-hash"));
                Assert.That(profile.AutoFitAuthoringFingerprint,
                    Is.EqualTo("authoring-hash"));
                Assert.That(profile.AutoFitToolkitVersion,
                    Is.EqualTo(NpcToolkitVersion.Current));
                Assert.That(profile.AutoFitMatches(
                    "source-hash", "authoring-hash"), Is.True);
                Assert.That(profile.AutoFitMatches(
                    "changed-hash", "authoring-hash"), Is.False);
                Assert.That(profile.AutoFitMatches(
                    "source-hash", "changed-authoring"), Is.False);
                Assert.That(profile.ProviderStandingPose, Is.Null);
                Assert.That(profile.ProviderMovementConfig, Is.Null);
                Assert.That(profile.ProviderRecipeFingerprint, Is.Empty);

                profile.MarkReviewed();
                Assert.That(profile.AlignmentState,
                    Is.EqualTo(NpcAlignmentState.Reviewed));
                Assert.That(profile.AutoFitMatches(
                    "source-hash", "authoring-hash"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(pose);
                Object.DestroyImmediate(movementConfig);
            }
        }

        [Test]
        public void ProviderRecipeAssignmentIsAtomicAndResettable()
        {
            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            var pose = ScriptableObject.CreateInstance<NpcBuildProfile>();
            var movementConfig = ScriptableObject.CreateInstance<NpcBuildProfile>();
            try
            {
                profile.SetProviderRecipe(
                    pose, movementConfig, "  provider-recipe-v1  ");

                Assert.That(profile.ProviderStandingPose, Is.SameAs(pose));
                Assert.That(profile.ProviderMovementConfig,
                    Is.SameAs(movementConfig));
                Assert.That(profile.ProviderRecipeFingerprint,
                    Is.EqualTo("provider-recipe-v1"));

                profile.ClearProviderRecipe();

                Assert.That(profile.ProviderStandingPose, Is.Null);
                Assert.That(profile.ProviderMovementConfig, Is.Null);
                Assert.That(profile.ProviderRecipeFingerprint, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(pose);
                Object.DestroyImmediate(movementConfig);
            }
        }
    }
}
