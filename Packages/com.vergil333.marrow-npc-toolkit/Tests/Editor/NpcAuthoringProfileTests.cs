using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcAuthoringProfileTests
    {
        [Test]
        public void AnatomyDefaultsContainSixteenUniqueCanonicalRoles()
        {
            var profile = ScriptableObject.CreateInstance<NpcAnatomyProfile>();
            try
            {
                profile.ResetToHumanoidDefaults();
                Assert.That(profile.HasCanonicalRoleSet(), Is.True);
                Assert.That(profile.BodyRoles.Count, Is.EqualTo(16));
                Assert.That(profile.BodyRoles.Select(value => value.Role).Distinct().Count(),
                    Is.EqualTo(16));
                Assert.That(profile.OptionalJaw.Role, Is.EqualTo(HumanBodyBones.Jaw));
                Assert.That(profile.OptionalJaw.Enabled, Is.False);
                Assert.That(profile.BodyRoles.All(value => value.MassKilograms > 0f), Is.True);
                Assert.That(profile.BodyRoles.All(value =>
                    value.AlignmentState == NpcAlignmentState.Unseeded), Is.True);
                Assert.That(profile.BodyRoles.Sum(value => value.MassKilograms),
                    Is.EqualTo(63.8235f).Within(0.0002f));
                Assert.That(profile.OptionalJaw.MassKilograms,
                    Is.EqualTo(1.1765f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void BuildProfileStartsWithPinnedCompatibilityContract()
        {
            var profile = ScriptableObject.CreateInstance<NpcBuildProfile>();
            try
            {
                profile.Initialize("Tester", "Example", "Assets/Example");
                Assert.That(profile.Author, Is.EqualTo("Tester"));
                Assert.That(profile.CrateTitle, Is.EqualTo("Example NPC"));
                Assert.That(profile.TargetPlatform, Is.EqualTo(NpcTargetPlatform.Quest));
                Assert.That(profile.CompatibilityProfileId,
                    Is.EqualTo(NpcToolkitVersion.InitialCompatibilityProfile));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AudioProfileUsesStableEventOrderAndCopiesAssignedArrays()
        {
            var profile = ScriptableObject.CreateInstance<NpcAudioProfile>();
            AudioClip first = AudioClip.Create("first", 32, 1, 16000, false);
            AudioClip second = AudioClip.Create("second", 32, 1, 16000, false);
            try
            {
                var source = new[] { first };
                profile.SetClips(NpcAudioEvent.PainSmall, source);
                source[0] = second;

                Assert.That(
                    System.Enum.GetValues(typeof(NpcAudioEvent))
                        .Cast<NpcAudioEvent>(),
                    Is.EqualTo(new[]
                    {
                        NpcAudioEvent.Agro,
                        NpcAudioEvent.UnAgro,
                        NpcAudioEvent.PainSmall,
                        NpcAudioEvent.PainBig,
                        NpcAudioEvent.Death,
                        NpcAudioEvent.JumpCharge,
                        NpcAudioEvent.Jump,
                        NpcAudioEvent.SmallEffort,
                        NpcAudioEvent.MediumEffort,
                        NpcAudioEvent.LargeEffort,
                        NpcAudioEvent.Attack1,
                        NpcAudioEvent.AttackLand1,
                        NpcAudioEvent.Attack2,
                        NpcAudioEvent.ImpactHead,
                        NpcAudioEvent.ImpactSpine,
                        NpcAudioEvent.ImpactLimb,
                    }));
                Assert.That(profile.PainSmall, Is.EqualTo(new[] { first }));
                Assert.That(profile.HasBasicReactions, Is.False);
                Assert.That(profile.HasFootsteps, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DefinitionInitializesSilentEvenWhenAudioProfileExists()
        {
            var audio = ScriptableObject.CreateInstance<NpcAudioProfile>();
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            try
            {
                definition.Initialize(
                    null,
                    NpcAvatarSourceKind.MarrowAvatarPrefab,
                    null,
                    null,
                    null,
                    "source-guid",
                    "source-hash",
                    audio);

                Assert.That(definition.AudioProfile, Is.SameAs(audio));
                Assert.That(definition.AudioMode, Is.EqualTo(NpcAudioMode.Silent));
                Assert.That(definition.IncludeNpcAudio, Is.False);
                Assert.That(definition.IncludeSecondaryMotion, Is.False);

                definition.IncludeNpcAudio = true;
                Assert.That(definition.AudioMode, Is.EqualTo(NpcAudioMode.Profile));
                definition.AudioMode = NpcAudioMode.Silent;
                Assert.That(definition.IncludeNpcAudio, Is.False);
                definition.IncludeSecondaryMotion = true;
                Assert.That(definition.IncludeSecondaryMotion, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(audio);
            }
        }

        [Test]
        public void AvatarAudioImportReusesReferencesAndKeepsRightsExplicit()
        {
            Type avatarType = Type.GetType("SLZ.VRMK.Avatar, SLZ.Marrow");
            Type varianceType = Type.GetType("SLZ.Data.AudioVarianceData, SLZ.Marrow");
            Assert.That(avatarType, Is.Not.Null);
            Assert.That(varianceType, Is.Not.Null);

            var avatarObject = new GameObject("Avatar");
            Component avatar = avatarObject.AddComponent(avatarType);
            var profile = ScriptableObject.CreateInstance<NpcAudioProfile>();
            AudioClip smallPain = AudioClip.Create("small-pain", 32, 1, 16000, false);
            AudioClip dying = AudioClip.Create("dying", 32, 1, 16000, false);
            AudioClip dead = AudioClip.Create("dead", 32, 1, 16000, false);
            AudioClip walk = AudioClip.Create("walk", 32, 1, 16000, false);
            AudioClip run = AudioClip.Create("run", 32, 1, 16000, false);
            AudioClip highFall = AudioClip.Create("high-fall", 32, 1, 16000, false);
            AudioClip explicitImpact = AudioClip.Create(
                "explicit-impact", 32, 1, 16000, false);
            Object[] variances =
            {
                CreateVariance(varianceType, smallPain),
                CreateVariance(varianceType, dying),
                CreateVariance(varianceType, dead),
                CreateVariance(varianceType, walk),
                CreateVariance(varianceType, run),
                CreateVariance(varianceType, highFall),
            };
            try
            {
                var serialized = new SerializedObject(avatar);
                AssignObject(serialized, "smallPain", variances[0]);
                AssignObject(serialized, "dying", variances[1]);
                AssignObject(serialized, "dead", variances[2]);
                AssignObject(serialized, "footstepsWalk", variances[3]);
                AssignObject(serialized, "footstepsJog", variances[4]);
                AssignObject(serialized, "highFallOntoFeet", variances[5]);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                profile.SetProvenance(
                    "English",
                    "Creator-owned source",
                    "Creator",
                    "Redistribution permitted",
                    "Keep this note.");

                NpcAudioProfileImportService.CaptureAvatarReferences(
                    avatarObject, profile);

                Assert.That(profile.PainSmall, Is.EqualTo(new[] { smallPain }));
                Assert.That(profile.Death, Is.EqualTo(new[] { dying, dead }));
                Assert.That(profile.WalkConcrete, Is.EqualTo(new[] { walk }));
                Assert.That(profile.RunConcrete, Is.EqualTo(new[] { run }));
                Assert.That(profile.LargeEffort, Is.EqualTo(new[] { highFall }));
                Assert.That(profile.ImpactSpine, Is.Empty);
                Assert.That(profile.Source, Is.EqualTo("Creator-owned source"));
                Assert.That(profile.LicenseOrPermission,
                    Is.EqualTo("Redistribution permitted"));
                Assert.That(profile.Notes, Does.StartWith("Keep this note."));
                Assert.That(profile.Notes, Does.Contain("No audio asset was copied"));

                // Physical-impact clips are explicitly authored NPC audio. An
                // Avatar refresh must not replace a deliberate custom choice.
                profile.SetClips(
                    NpcAudioEvent.ImpactSpine, new[] { explicitImpact });
                NpcAudioProfileImportService.CaptureAvatarReferences(
                    avatarObject, profile);

                Assert.That(profile.LargeEffort, Is.EqualTo(new[] { highFall }));
                Assert.That(
                    profile.ImpactSpine, Is.EqualTo(new[] { explicitImpact }));
            }
            finally
            {
                foreach (Object variance in variances)
                    Object.DestroyImmediate(variance);
                Object.DestroyImmediate(smallPain);
                Object.DestroyImmediate(dying);
                Object.DestroyImmediate(dead);
                Object.DestroyImmediate(walk);
                Object.DestroyImmediate(run);
                Object.DestroyImmediate(highFall);
                Object.DestroyImmediate(explicitImpact);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(avatarObject);
            }
        }

        private static Object CreateVariance(Type varianceType, AudioClip clip)
        {
            Object variance = ScriptableObject.CreateInstance(varianceType);
            var serialized = new SerializedObject(variance);
            SerializedProperty clips = serialized.FindProperty("audioClips");
            Assert.That(clips, Is.Not.Null);
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return variance;
        }

        private static void AssignObject(
            SerializedObject serialized,
            string propertyName,
            Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, propertyName);
            property.objectReferenceValue = value;
        }

        [Test]
        public void AvatarSourceProfileKeepsStablePathBindings()
        {
            var profile = ScriptableObject.CreateInstance<NpcAvatarSourceProfile>();
            try
            {
                profile.SetBindings(
                    new[]
                    {
                        new NpcHumanoidBoneBinding(HumanBodyBones.Hips, "Rig/Hips"),
                        new NpcHumanoidBoneBinding(HumanBodyBones.Head, "Rig/Hips/Head"),
                    },
                    new[]
                    {
                        new NpcAvatarRendererBinding(
                            NpcAvatarRendererCategory.Body, "Visuals/Body"),
                    },
                    new[]
                    {
                        new NpcOptionalAvatarBinding(
                            NpcOptionalAvatarRole.LeftUpperArmTwist,
                            "Rig/Hips/ArmTwist.L"),
                    },
                    "Rig/Hips/Hand.L",
                    "Rig/Hips/Hand.R",
                    "EyeCenter",
                    "Rig/Hips/Head/Jaw");

                Assert.That(profile.HumanoidBones.Count, Is.EqualTo(2));
                Assert.That(profile.HumanoidBones[0].TransformPath, Is.EqualTo("Rig/Hips"));
                Assert.That(profile.Renderers.Single().Category,
                    Is.EqualTo(NpcAvatarRendererCategory.Body));
                Assert.That(profile.OptionalBones.Single().Role,
                    Is.EqualTo(NpcOptionalAvatarRole.LeftUpperArmTwist));
                Assert.That(profile.EyeCenterOverridePath, Is.EqualTo("EyeCenter"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void CanonicalGraphMatchesTheProvenSixteenBodyContract()
        {
            Assert.That(NpcHumanoidGraph.CanonicalRoles.Length, Is.EqualTo(16));
            Assert.That(NpcHumanoidGraph.TryGetParent(
                HumanBodyBones.LeftHand, out HumanBodyBones leftHandParent), Is.True);
            Assert.That(leftHandParent, Is.EqualTo(HumanBodyBones.LeftLowerArm));
            Assert.That(NpcHumanoidGraph.TryGetParent(
                HumanBodyBones.Hips, out _), Is.False);
            Assert.That(NpcHumanoidGraph.TryGetPrimaryChild(
                HumanBodyBones.RightUpperLeg, out HumanBodyBones rightLegChild), Is.True);
            Assert.That(rightLegChild, Is.EqualTo(HumanBodyBones.RightLowerLeg));
            Assert.That(NpcHumanoidGraph.TryGetOppositeSide(
                HumanBodyBones.LeftUpperLeg, out HumanBodyBones rightUpperLeg), Is.True);
            Assert.That(rightUpperLeg, Is.EqualTo(HumanBodyBones.RightUpperLeg));
            Assert.That(NpcHumanoidGraph.TryGetOppositeSide(
                HumanBodyBones.RightHand, out HumanBodyBones leftHand), Is.True);
            Assert.That(leftHand, Is.EqualTo(HumanBodyBones.LeftHand));
            Assert.That(NpcHumanoidGraph.TryGetOppositeSide(
                HumanBodyBones.Chest, out _), Is.False);
            Assert.That(NpcHumanoidGraph.CanonicalRoles.Count(role =>
                NpcHumanoidGraph.TryGetOppositeSide(role, out _)), Is.EqualTo(12));
            foreach (HumanBodyBones role in NpcHumanoidGraph.CanonicalRoles)
            {
                if (!NpcHumanoidGraph.TryGetOppositeSide(role, out HumanBodyBones opposite))
                    continue;
                Assert.That(NpcHumanoidGraph.TryGetOppositeSide(
                    opposite, out HumanBodyBones roundTrip), Is.True);
                Assert.That(roundTrip, Is.EqualTo(role));
            }
        }

        [Test]
        public void NativeMuscleOrderIsExplicitAndLegsFirst()
        {
            Assert.That(NpcHumanoidGraph.NativeMuscleOrder.Length, Is.EqualTo(16));
            Assert.That(NpcHumanoidGraph.NativeMuscleOrder.Distinct().Count(), Is.EqualTo(16));
            Assert.That(NpcHumanoidGraph.NativeMuscleOrder[0], Is.EqualTo(HumanBodyBones.Hips));
            Assert.That(NpcHumanoidGraph.NativeMuscleOrder[1],
                Is.EqualTo(HumanBodyBones.LeftUpperLeg));
            Assert.That(NpcHumanoidGraph.NativeMuscleOrder[7], Is.EqualTo(HumanBodyBones.Spine));
            Assert.That(NpcHumanoidGraph.NativeMuscleOrder[15],
                Is.EqualTo(HumanBodyBones.RightHand));
        }

        [Test]
        public void FittedReceiptRequiresPositiveGeometryAndAlignmentState()
        {
            var profile = ScriptableObject.CreateInstance<NpcAnatomyProfile>();
            try
            {
                profile.ResetToHumanoidDefaults();
                foreach (NpcBodyRoleProfile role in profile.BodyRoles)
                {
                    role.AlignmentState = NpcAlignmentState.AutoFit;
                    if (role.ColliderShape == NpcColliderShape.Box)
                        role.ColliderSize = Vector3.one * 0.1f;
                    else
                    {
                        role.CapsuleRadius = 0.05f;
                        role.CapsuleHeight = 0.2f;
                    }
                }
                profile.MarkBaselineFitted("source-hash");

                Assert.That(profile.HasFittedBaseline, Is.True);
                Assert.That(profile.BaselineMatches("source-hash"), Is.True);
                Assert.That(profile.BaselineMatches("other"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PhysicalJawFitUsesOnlyMajorityWeightedVerticesAndAcceptedHinge()
        {
            var avatarRoot = new GameObject("AvatarRoot");
            var jawObject = new GameObject("Jaw");
            jawObject.transform.SetParent(avatarRoot.transform, false);
            var rendererObject = new GameObject("Face");
            rendererObject.transform.SetParent(avatarRoot.transform, false);
            SkinnedMeshRenderer renderer = rendererObject
                .AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = "JawWeightFixture" };
            mesh.vertices = new[]
            {
                new Vector3(-0.06f, 0.01f, -0.02f),
                new Vector3(0.06f, 0.08f, 0.04f),
                new Vector3(0f, 0.04f, 0.01f),
                new Vector3(4f, 4f, 4f),
            };
            mesh.boneWeights = new[]
            {
                JawWeight(1f),
                JawWeight(0.75f),
                JawWeight(0.5f),
                JawWeight(0.49f),
            };
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            renderer.sharedMesh = mesh;
            renderer.bones = new[] { jawObject.transform, avatarRoot.transform };
            var jaw = new NpcBodyRoleProfile(HumanBodyBones.Jaw);
            try
            {
                bool fitted = NpcBaselineFitter.TryFitJawFromWeightedVertices(
                    avatarRoot.transform,
                    jawObject.transform,
                    new[] { renderer },
                    jaw,
                    out string error);

                Assert.That(fitted, Is.True, error);
                Assert.That(jaw.Enabled, Is.True);
                Assert.That(jaw.ColliderShape, Is.EqualTo(NpcColliderShape.Box));
                Assert.That(Vector3.Distance(
                    jaw.ColliderCenter,
                    new Vector3(0f, 0.045f, 0.01f)), Is.LessThan(0.00001f));
                Assert.That(Vector3.Distance(
                    jaw.ColliderSize,
                    new Vector3(0.12f, 0.07f, 0.06f)), Is.LessThan(0.00001f));
                Assert.That(Vector3.Distance(jaw.JointAxis, Vector3.right),
                    Is.LessThan(0.00001f));
                Assert.That(Vector3.Distance(jaw.JointSecondaryAxis, Vector3.up),
                    Is.LessThan(0.00001f));
                Assert.That(jaw.AngularLowLimits,
                    Is.EqualTo(new Vector3(-28f, -10f, 0f)));
                Assert.That(jaw.AngularHighLimits,
                    Is.EqualTo(new Vector3(0f, 10f, 0f)));
                Assert.That(jaw.AngularZMotion, Is.EqualTo(NpcJointMotion.Locked));
                Assert.That(jaw.MassKilograms, Is.EqualTo(1.1765f).Within(0.0001f));
                Assert.That(jaw.JointDriveMaxForce, Is.EqualTo(36f));
                Assert.That(jaw.MuscleSpring, Is.EqualTo(5000000f));
                Assert.That(jaw.MuscleWeight, Is.EqualTo(1f));
                Assert.That(jaw.MuscleDamper, Is.EqualTo(100000f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(avatarRoot);
            }
        }

        private static BoneWeight JawWeight(float value)
        {
            return new BoneWeight
            {
                boneIndex0 = 0,
                weight0 = value,
                boneIndex1 = 1,
                weight1 = 1f - value,
            };
        }
    }
}
