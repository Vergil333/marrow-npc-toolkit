using NUnit.Framework;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcAlignmentMirrorServiceTests
    {
        [Test]
        public void MirrorConvertsPoseThroughAvatarSpaceAndPreservesTargetTuning()
        {
            var prefab = new GameObject("Prefab Wrapper");
            var avatar = new GameObject("Nested Animator Root");
            var sourceObject = new GameObject("LeftUpperLeg");
            var targetObject = new GameObject("RightUpperLeg");
            try
            {
                prefab.transform.position = new Vector3(3f, -2f, 5f);
                prefab.transform.rotation = Quaternion.Euler(-3f, 17f, 8f);
                avatar.transform.SetParent(prefab.transform, false);
                avatar.transform.localPosition = new Vector3(0.4f, 0.1f, -0.3f);
                avatar.transform.localRotation = Quaternion.Euler(7f, 31f, -4f);
                sourceObject.transform.SetParent(avatar.transform, false);
                targetObject.transform.SetParent(avatar.transform, false);
                sourceObject.transform.localPosition = new Vector3(-0.22f, 0.9f, 0.03f);
                targetObject.transform.localPosition = new Vector3(0.26f, 0.9f, 0.03f);
                sourceObject.transform.localRotation = Quaternion.Euler(12f, 24f, -81f);
                targetObject.transform.localRotation = Quaternion.Euler(-9f, -18f, 79f);

                var source = new NpcBodyRoleProfile(HumanBodyBones.LeftUpperLeg)
                {
                    AlignmentState = NpcAlignmentState.Reviewed,
                    ColliderShape = NpcColliderShape.Box,
                    ColliderCenter = new Vector3(0.04f, 0.17f, -0.025f),
                    ColliderLocalRotation = Quaternion.Euler(16f, 28f, 9f),
                    ColliderSize = new Vector3(0.18f, 0.47f, 0.21f),
                    CapsuleRadius = 0.09f,
                    CapsuleHeight = 0.47f,
                    CapsuleDirection = 1,
                };
                var target = new NpcBodyRoleProfile(HumanBodyBones.RightUpperLeg)
                {
                    Enabled = false,
                    AutoFitCollider = false,
                    AlignmentState = NpcAlignmentState.AutoFit,
                    MassKilograms = 7.25f,
                    JointAxis = new Vector3(0.2f, 0.3f, 0.9f).normalized,
                    JointSecondaryAxis = new Vector3(-0.4f, 0.8f, 0.1f).normalized,
                    AngularXMotion = NpcJointMotion.Free,
                    AngularYMotion = NpcJointMotion.Locked,
                    AngularZMotion = NpcJointMotion.Limited,
                    AngularLowLimits = new Vector3(-17f, -23f, -31f),
                    AngularHighLimits = new Vector3(41f, 43f, 47f),
                    JointDriveMaxForce = 812f,
                    MuscleSpring = 1234f,
                    MuscleDamper = 567f,
                    MuscleWeight = 0.42f,
                };

                Vector3 sourceCenterRoot = avatar.transform.InverseTransformPoint(
                    sourceObject.transform.TransformPoint(source.ColliderCenter));
                float planeX = (sourceObject.transform.localPosition.x
                                + targetObject.transform.localPosition.x) * 0.5f;
                Vector3 expectedCenterRoot = sourceCenterRoot;
                expectedCenterRoot.x = planeX * 2f - expectedCenterRoot.x;

                Quaternion sourceRootRotation = Quaternion.Inverse(avatar.transform.rotation)
                                                  * sourceObject.transform.rotation
                                                  * source.ColliderLocalRotation;
                Vector3 expectedUp = ReflectRootX(sourceRootRotation * Vector3.up);
                Vector3 expectedForward = ReflectRootX(sourceRootRotation * Vector3.forward);
                Vector3 expectedRight = -ReflectRootX(sourceRootRotation * Vector3.right);

                Assert.That(NpcAlignmentMirrorService.TryMirrorCollider(
                    avatar.transform,
                    sourceObject.transform,
                    targetObject.transform,
                    source,
                    target,
                    out string error), Is.True, error);

                Vector3 actualCenterRoot = avatar.transform.InverseTransformPoint(
                    targetObject.transform.TransformPoint(target.ColliderCenter));
                AssertVector(actualCenterRoot, expectedCenterRoot);
                Quaternion actualRootRotation = Quaternion.Inverse(avatar.transform.rotation)
                                                  * targetObject.transform.rotation
                                                  * target.ColliderLocalRotation;
                AssertVector(actualRootRotation * Vector3.up, expectedUp);
                AssertVector(actualRootRotation * Vector3.forward, expectedForward);
                AssertVector(actualRootRotation * Vector3.right, expectedRight);

                Assert.That(target.ColliderShape, Is.EqualTo(source.ColliderShape));
                AssertVector(target.ColliderSize, source.ColliderSize);
                Assert.That(target.CapsuleRadius, Is.EqualTo(source.CapsuleRadius));
                Assert.That(target.CapsuleHeight, Is.EqualTo(source.CapsuleHeight));
                Assert.That(target.CapsuleDirection, Is.EqualTo(source.CapsuleDirection));
                Assert.That(target.AlignmentState, Is.EqualTo(NpcAlignmentState.Reviewed));

                Assert.That(target.Enabled, Is.False);
                Assert.That(target.AutoFitCollider, Is.False);
                Assert.That(target.MassKilograms, Is.EqualTo(7.25f));
                AssertVector(target.JointAxis, new Vector3(0.2f, 0.3f, 0.9f).normalized);
                AssertVector(
                    target.JointSecondaryAxis,
                    new Vector3(-0.4f, 0.8f, 0.1f).normalized);
                Assert.That(target.AngularXMotion, Is.EqualTo(NpcJointMotion.Free));
                Assert.That(target.AngularYMotion, Is.EqualTo(NpcJointMotion.Locked));
                Assert.That(target.AngularZMotion, Is.EqualTo(NpcJointMotion.Limited));
                AssertVector(target.AngularLowLimits, new Vector3(-17f, -23f, -31f));
                AssertVector(target.AngularHighLimits, new Vector3(41f, 43f, 47f));
                Assert.That(target.JointDriveMaxForce, Is.EqualTo(812f));
                Assert.That(target.MuscleSpring, Is.EqualTo(1234f));
                Assert.That(target.MuscleDamper, Is.EqualTo(567f));
                Assert.That(target.MuscleWeight, Is.EqualTo(0.42f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void MirrorRejectsCenterlineAndUnseededSourcesWithoutChangingTarget()
        {
            var avatar = new GameObject("Avatar");
            var sourceObject = new GameObject("Source");
            var targetObject = new GameObject("Target");
            try
            {
                sourceObject.transform.SetParent(avatar.transform, false);
                targetObject.transform.SetParent(avatar.transform, false);
                var centerSource = new NpcBodyRoleProfile(HumanBodyBones.Hips)
                {
                    AlignmentState = NpcAlignmentState.Reviewed,
                    ColliderShape = NpcColliderShape.Sphere,
                    CapsuleRadius = 0.1f,
                };
                var pairedTarget = new NpcBodyRoleProfile(HumanBodyBones.RightUpperLeg)
                {
                    ColliderCenter = new Vector3(1f, 2f, 3f),
                };
                Assert.That(NpcAlignmentMirrorService.TryMirrorCollider(
                    avatar.transform,
                    sourceObject.transform,
                    targetObject.transform,
                    centerSource,
                    pairedTarget,
                    out _), Is.False);
                AssertVector(pairedTarget.ColliderCenter, new Vector3(1f, 2f, 3f));

                var unseeded = new NpcBodyRoleProfile(HumanBodyBones.LeftFoot)
                {
                    AlignmentState = NpcAlignmentState.Unseeded,
                    ColliderShape = NpcColliderShape.Box,
                    ColliderSize = Vector3.one,
                };
                var rightFoot = new NpcBodyRoleProfile(HumanBodyBones.RightFoot)
                {
                    ColliderCenter = new Vector3(4f, 5f, 6f),
                };
                Assert.That(NpcAlignmentMirrorService.TryMirrorCollider(
                    avatar.transform,
                    sourceObject.transform,
                    targetObject.transform,
                    unseeded,
                    rightFoot,
                    out _), Is.False);
                AssertVector(rightFoot.ColliderCenter, new Vector3(4f, 5f, 6f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(avatar);
            }
        }

        private static Vector3 ReflectRootX(Vector3 value)
        {
            return new Vector3(-value.x, value.y, value.z);
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.00001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.00001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.00001f));
        }
    }
}
