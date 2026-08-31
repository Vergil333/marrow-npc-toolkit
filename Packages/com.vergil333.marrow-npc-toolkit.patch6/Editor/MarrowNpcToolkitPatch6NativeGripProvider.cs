using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Project-local Patch 6 player-on-NPC body grabs. Native components and
    /// HandPose assets stay outside the public package. The provider constructs
    /// every component and staged transform explicitly; no donor component or
    /// donor scene reference is copied.
    /// </summary>
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const float GripHugeValue = 3e38f;
        private const float GripEpsilon = 0.0001f;

        private static readonly HumanBodyBones[] GenericGripRoles =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.Chest,
            HumanBodyBones.Head,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
        };

        private static readonly HumanBodyBones[] CylinderGripRoles =
        {
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
        };

        private static readonly IReadOnlyDictionary<HumanBodyBones, HumanBodyBones>
            CylinderGripEnds = new Dictionary<HumanBodyBones, HumanBodyBones>
            {
                [HumanBodyBones.LeftUpperArm] = HumanBodyBones.LeftLowerArm,
                [HumanBodyBones.LeftLowerArm] = HumanBodyBones.LeftHand,
                [HumanBodyBones.RightUpperArm] = HumanBodyBones.RightLowerArm,
                [HumanBodyBones.RightLowerArm] = HumanBodyBones.RightHand,
                [HumanBodyBones.LeftUpperLeg] = HumanBodyBones.LeftLowerLeg,
                [HumanBodyBones.LeftLowerLeg] = HumanBodyBones.LeftFoot,
                [HumanBodyBones.RightUpperLeg] = HumanBodyBones.RightLowerLeg,
                [HumanBodyBones.RightLowerLeg] = HumanBodyBones.RightFoot,
            };

        private static bool RequiresGripShell(
            NpcCompatibilityCapabilities capabilities)
        {
            return (capabilities & NpcCompatibilityCapabilities.Grips) != 0;
        }

        private static bool TryPreflightGripBuild(out string detail)
        {
            try
            {
                GripTypes types = GripTypes.Resolve();
                MarrowNpcToolkitPatch6BehaviourSettings.GripResolved settings =
                    RequireGripSettings(types);
                ValidatePersistentAsset(
                    settings.GenericGripPose, types.HandPose,
                    "Generic Body-Grab Pose");
                ValidatePersistentAsset(
                    settings.CylinderGripPose, types.HandPose,
                    "Cylinder Limb-Grab Pose");
                detail = "The explicit project-local body-grab poses and Patch 6 "
                         + "grip declarations passed preflight.";
                return true;
            }
            catch (Exception exception)
            {
                detail = "Patch 6 body-grab preflight failed: " + exception.Message;
                return false;
            }
        }

        private static GripShell ConfigureGripShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            GripTypes types = GripTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.GripResolved settings =
                RequireGripSettings(types);
            if (outputRoot.GetComponentsInChildren(types.GenericGrip, true).Length != 0
                || outputRoot.GetComponentsInChildren(types.CylinderGrip, true).Length != 0)
                throw new InvalidOperationException(
                    "The staged preview already contains native grip components.");

            var generic = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in GenericGripRoles)
            {
                NativeRole nativeRole = roles[role];
                Transform target = nativeRole.Body;
                if (role == HumanBodyBones.LeftHand
                    || role == HumanBodyBones.RightHand)
                    target = CreateGripTarget(
                        nativeRole,
                        role + "GripCenter",
                        ColliderBoundsInFrame(
                            nativeRole.Collider, nativeRole.Body).center,
                        Quaternion.identity);

                Component grip = AddNative(
                    nativeRole.Body.gameObject,
                    types.GenericGrip,
                    role + " GenericGrip");
                ConfigureGripCommon(
                    grip,
                    settings.GenericGripPose,
                    target,
                    Vector3.zero,
                    Vector3.zero,
                    GenericRadius(nativeRole));
                generic.Add(role, grip);
            }

            var cylinder = new Dictionary<HumanBodyBones, Component>();
            var centers = new Dictionary<HumanBodyBones, Transform>();
            foreach (HumanBodyBones role in CylinderGripRoles)
            {
                NativeRole nativeRole = roles[role];
                NativeRole endRole = roles[CylinderGripEnds[role]];
                Vector3 endLocal = nativeRole.Body.InverseTransformPoint(
                    endRole.Body.position);
                float length = endLocal.magnitude;
                if (!IsFinite(endLocal) || !IsFinite(length) || length <= 0.02f)
                    throw new InvalidOperationException(
                        role + " cannot derive a finite limb-grip segment.");
                Vector3 along = endLocal / length;
                Vector3 up = Mathf.Abs(Vector3.Dot(along, Vector3.up)) > 0.95f
                    ? Vector3.right
                    : Vector3.up;
                Quaternion rotation = Quaternion.LookRotation(along, up);
                Transform center = CreateGripTarget(
                    nativeRole,
                    "GripCenter",
                    endLocal * 0.5f,
                    rotation);

                Component grip = AddNative(
                    nativeRole.Body.gameObject,
                    types.CylinderGrip,
                    role + " CylinderGrip");
                ConfigureGripCommon(
                    grip,
                    settings.CylinderGripPose,
                    center,
                    Vector3.forward,
                    Vector3.up,
                    ColliderRadius(nativeRole));
                var data = new SerializedObject(grip);
                SetFloat(data, "rotationLimit", 180f);
                SetFloat(data, "rotationPriorityBuffer", 20f);
                SetObject(data, "handPoseOnFlippedPrimaryAxis", null);
                SetInt(data, "targetFlipOnPrimaryAxis", 1);
                SetInt(data, "targetFlipOnTertiaryAxis", 0);
                SetFloat(data, "dynamicFriction", 0.7f);
                SetFloat(data, "staticFriction", 0.9f);
                SetFloat(data, "limit", length * 0.5f);
                SetInt(data, "hasCapA", 1);
                SetInt(data, "hasCapB", 1);
                SetInt(data, "ignoreFlipOnZ", 0);
                SetFloat(data, "rotationalFrictionMult", 1f);
                SetFloat(data, "aspectRatio", 1f);
                SetInt(data, "variableRadius", 0);
                Require(data, "RadiusCurve").animationCurveValue =
                    new AnimationCurve();
                data.ApplyModifiedPropertiesWithoutUndo();
                cylinder.Add(role, grip);
                centers.Add(role, center);
            }

            var shell = new GripShell(
                settings, types, generic, cylinder, centers);
            ValidateGripShell(outputRoot, roles, shell);
            return shell;
        }

        private static GripShell ResolveGripShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            GripTypes types = GripTypes.Resolve();
            MarrowNpcToolkitPatch6BehaviourSettings.GripResolved settings =
                RequireGripSettings(types);
            var generic = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in GenericGripRoles)
                generic.Add(
                    role,
                    RequireOnlyComponent(
                        roles[role].Body.gameObject,
                        types.GenericGrip,
                        role + " GenericGrip"));

            var cylinder = new Dictionary<HumanBodyBones, Component>();
            var centers = new Dictionary<HumanBodyBones, Transform>();
            foreach (HumanBodyBones role in CylinderGripRoles)
            {
                cylinder.Add(
                    role,
                    RequireOnlyComponent(
                        roles[role].Body.gameObject,
                        types.CylinderGrip,
                        role + " CylinderGrip"));
                centers.Add(
                    role,
                    RequireDirectChild(roles[role].Body, "GripCenter"));
            }

            var shell = new GripShell(
                settings, types, generic, cylinder, centers);
            ValidateGripShell(outputRoot, roles, shell);
            return shell;
        }

        private static void ConfigureGripCommon(
            Component grip,
            UnityEngine.Object handPose,
            Transform target,
            Vector3 primaryAxis,
            Vector3 secondaryAxis,
            float radius)
        {
            var data = new SerializedObject(grip);
            SetInt(data, "isThrowable", 1);
            SetInt(data, "ignoreGripTargetOnAttach", 0);
            SetObjectArray(
                data, "gripColliders", Array.Empty<UnityEngine.Object>());
            SetObjectArray(
                data, "additionalGripColliders", Array.Empty<UnityEngine.Object>());
            Require(data, "handleAmplifyCurve").animationCurveValue =
                new AnimationCurve();
            SetObject(data, "handPose", handPose);
            SetVector(data, "primaryMovementAxis", primaryAxis);
            SetVector(data, "secondaryMovementAxis", secondaryAxis);
            SetInt(data, "gripOptions", 1);
            SetFloat(data, "priority", 1f);
            SetFloat(data, "minBreakForce", GripHugeValue);
            SetFloat(data, "maxBreakForce", GripHugeValue);
            SetFloat(data, "defaultGripDistance", GripHugeValue);
            SetFloat(data, "gripDistanceSqr", GripHugeValue);
            SetFloat(data, "radius", radius);
            SetObject(data, "targetTransform", target);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform CreateGripTarget(
            NativeRole role,
            string name,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            if (Enumerable.Range(0, role.Body.childCount)
                .Select(role.Body.GetChild)
                .Any(value => string.Equals(
                    value.name, name, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    role.Role + " already contains a direct " + name + " child.");
            var targetObject = new GameObject(name)
            {
                layer = role.Body.gameObject.layer,
            };
            Transform target = targetObject.transform;
            target.SetParent(role.Body, false);
            target.localPosition = localPosition;
            target.localRotation = localRotation;
            target.localScale = Vector3.one;
            return target;
        }

        private static float GenericRadius(NativeRole role)
        {
            if (role.Role == HumanBodyBones.LeftHand
                || role.Role == HumanBodyBones.RightHand
                || role.Role == HumanBodyBones.LeftFoot
                || role.Role == HumanBodyBones.RightFoot)
                return 0f;
            return ColliderRadius(role);
        }

        private static float ColliderRadius(NativeRole role)
        {
            Bounds bounds = ColliderBoundsInFrame(role.Collider, role.Body);
            float radius = Mathf.Min(
                bounds.extents.x,
                Mathf.Min(bounds.extents.y, bounds.extents.z));
            if (!IsFinite(radius) || radius <= 0.001f)
                throw new InvalidOperationException(
                    role.Role + " produced an invalid grip radius.");
            return radius;
        }

        private static void ValidateGripShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            GripShell shell)
        {
            if (outputRoot == null || shell == null
                || shell.Generic.Count != 8 || shell.Cylinder.Count != 8)
                throw new InvalidOperationException(
                    "The Patch 6 body-grab shell must contain 8 generic and 8 "
                    + "cylinder role entries.");
            int genericCount = outputRoot.GetComponentsInChildren(
                shell.Types.GenericGrip, true).Length;
            int cylinderCount = outputRoot.GetComponentsInChildren(
                shell.Types.CylinderGrip, true).Length;
            int jawGenericCount = roles.ContainsKey(HumanBodyBones.Jaw)
                ? roles[HumanBodyBones.Jaw].Collider.gameObject
                    .GetComponents(shell.Types.GenericGrip).Length
                : 0;
            if (jawGenericCount > 1)
                throw new InvalidOperationException(
                    "The Physical Jaw collider owns duplicate GenericGrip components.");
            int expectedGenericCount = 8 + jawGenericCount;
            if (genericCount != expectedGenericCount || cylinderCount != 8)
                throw new InvalidOperationException(
                    "The staged NPC must contain exactly " + expectedGenericCount
                    + " GenericGrip and 8 CylinderGrip components; found "
                    + genericCount + " and " + cylinderCount + ".");

            foreach (HumanBodyBones role in GenericGripRoles)
            {
                NativeRole nativeRole = roles[role];
                Component grip = shell.Generic[role];
                if (grip == null || grip.transform != nativeRole.Body
                    || nativeRole.Body.GetComponents(shell.Types.GenericGrip).Length != 1
                    || nativeRole.Body.GetComponents(shell.Types.CylinderGrip).Length != 0)
                    throw new InvalidOperationException(
                        role + " does not own exactly one direct GenericGrip.");
                Transform expectedTarget = nativeRole.Body;
                if (role == HumanBodyBones.LeftHand
                    || role == HumanBodyBones.RightHand)
                {
                    expectedTarget = RequireDirectChild(
                        nativeRole.Body, role + "GripCenter");
                    Bounds bounds = ColliderBoundsInFrame(
                        nativeRole.Collider, nativeRole.Body);
                    ValidateTargetTransform(
                        expectedTarget,
                        bounds.center,
                        Quaternion.identity,
                        role + " grip center");
                }
                ValidateGripCommon(
                    grip,
                    shell.Settings.GenericGripPose,
                    expectedTarget,
                    Vector3.zero,
                    Vector3.zero,
                    GenericRadius(nativeRole));
            }

            foreach (HumanBodyBones role in CylinderGripRoles)
            {
                NativeRole nativeRole = roles[role];
                NativeRole endRole = roles[CylinderGripEnds[role]];
                Component grip = shell.Cylinder[role];
                if (grip == null || grip.transform != nativeRole.Body
                    || nativeRole.Body.GetComponents(shell.Types.CylinderGrip).Length != 1
                    || nativeRole.Body.GetComponents(shell.Types.GenericGrip).Length != 0)
                    throw new InvalidOperationException(
                        role + " does not own exactly one direct CylinderGrip.");

                Vector3 endLocal = nativeRole.Body.InverseTransformPoint(
                    endRole.Body.position);
                float length = endLocal.magnitude;
                Vector3 along = endLocal / length;
                Vector3 up = Mathf.Abs(Vector3.Dot(along, Vector3.up)) > 0.95f
                    ? Vector3.right
                    : Vector3.up;
                Quaternion rotation = Quaternion.LookRotation(along, up);
                Transform center = shell.Centers[role];
                ValidateTargetTransform(
                    center,
                    endLocal * 0.5f,
                    rotation,
                    role + " GripCenter");
                ValidateGripCommon(
                    grip,
                    shell.Settings.CylinderGripPose,
                    center,
                    Vector3.forward,
                    Vector3.up,
                    ColliderRadius(nativeRole));

                var data = new SerializedObject(grip);
                RequireGripFloat(data, "rotationLimit", 180f);
                RequireGripFloat(data, "rotationPriorityBuffer", 20f);
                RequireGripObject(data, "handPoseOnFlippedPrimaryAxis", null);
                RequireGripByte(data, "targetFlipOnPrimaryAxis", 1);
                RequireGripByte(data, "targetFlipOnTertiaryAxis", 0);
                RequireGripFloat(data, "dynamicFriction", 0.7f);
                RequireGripFloat(data, "staticFriction", 0.9f);
                RequireGripFloat(data, "limit", length * 0.5f);
                RequireGripByte(data, "hasCapA", 1);
                RequireGripByte(data, "hasCapB", 1);
                RequireGripByte(data, "ignoreFlipOnZ", 0);
                RequireGripFloat(data, "rotationalFrictionMult", 1f);
                RequireGripFloat(data, "aspectRatio", 1f);
                RequireGripByte(data, "variableRadius", 0);
                RequireEmptyCurve(data, "RadiusCurve");
            }
        }

        private static void ValidateGripCommon(
            Component grip,
            UnityEngine.Object handPose,
            Transform target,
            Vector3 primaryAxis,
            Vector3 secondaryAxis,
            float radius)
        {
            if (!(grip is Behaviour behaviour) || !behaviour.enabled)
                throw new InvalidOperationException(
                    grip.GetType().Name + " must be enabled.");
            var data = new SerializedObject(grip);
            RequireGripByte(data, "isThrowable", 1);
            RequireGripByte(data, "ignoreGripTargetOnAttach", 0);
            RequireEmptyObjectArray(data, "gripColliders");
            RequireEmptyObjectArray(data, "additionalGripColliders");
            RequireEmptyCurve(data, "handleAmplifyCurve");
            RequireGripObject(data, "handPose", handPose);
            RequireGripVector(data, "primaryMovementAxis", primaryAxis);
            RequireGripVector(data, "secondaryMovementAxis", secondaryAxis);
            if (Require(data, "gripOptions").intValue != 1)
                throw new InvalidOperationException(
                    grip.GetType().Name + ".gripOptions must be 1.");
            RequireGripFloat(data, "priority", 1f);
            RequireGripFloat(data, "minBreakForce", GripHugeValue);
            RequireGripFloat(data, "maxBreakForce", GripHugeValue);
            RequireGripFloat(data, "defaultGripDistance", GripHugeValue);
            RequireGripFloat(data, "gripDistanceSqr", GripHugeValue);
            RequireGripFloat(data, "radius", radius);
            RequireGripObject(data, "targetTransform", target);
            if (!EditorUtility.IsPersistent(handPose))
                throw new InvalidOperationException(
                    grip.GetType().Name + ".handPose is not a persistent asset.");
        }

        private static void ValidateTargetTransform(
            Transform target,
            Vector3 localPosition,
            Quaternion localRotation,
            string label)
        {
            if (target == null || target.parent == null
                || !NearGrip(target.localPosition, localPosition)
                || !NearGrip(target.localRotation, localRotation)
                || !NearGrip(target.localScale, Vector3.one))
                throw new InvalidOperationException(
                    label + " does not match its deterministic local frame.");
        }

        private static void RequireGripByte(
            SerializedObject data, string path, int expected)
        {
            int actual = Require(data, path).intValue;
            if (actual != expected)
                throw new InvalidOperationException(
                    data.targetObject.GetType().Name + "." + path + " differs: "
                    + actual + " vs " + expected + ".");
        }

        private static void RequireGripFloat(
            SerializedObject data, string path, float expected)
        {
            float actual = Require(data, path).floatValue;
            float tolerance = Mathf.Max(GripEpsilon, Mathf.Abs(expected) * 0.000001f);
            if (!IsFinite(actual) || Mathf.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException(
                    data.targetObject.GetType().Name + "." + path + " differs: "
                    + actual.ToString("R", CultureInfo.InvariantCulture) + " vs "
                    + expected.ToString("R", CultureInfo.InvariantCulture) + ".");
        }

        private static void RequireGripVector(
            SerializedObject data, string path, Vector3 expected)
        {
            if (!NearGrip(Require(data, path).vector3Value, expected))
                throw new InvalidOperationException(
                    data.targetObject.GetType().Name + "." + path + " differs.");
        }

        private static void RequireGripObject(
            SerializedObject data, string path, UnityEngine.Object expected)
        {
            if (Require(data, path).objectReferenceValue != expected)
                throw new InvalidOperationException(
                    data.targetObject.GetType().Name + "." + path + " differs.");
        }

        private static void RequireEmptyObjectArray(
            SerializedObject data, string path)
        {
            SerializedProperty property = Require(data, path);
            if (!property.isArray || property.arraySize != 0)
                throw new InvalidOperationException(
                    data.targetObject.GetType().Name + "." + path
                    + " must be an empty array.");
        }

        private static void RequireEmptyCurve(
            SerializedObject data, string path)
        {
            AnimationCurve curve = Require(data, path).animationCurveValue;
            if (curve == null || curve.length != 0)
                throw new InvalidOperationException(
                    data.targetObject.GetType().Name + "." + path
                    + " must be an empty curve.");
        }

        private static bool NearGrip(Vector3 left, Vector3 right)
        {
            return IsFinite(left) && IsFinite(right)
                                  && Vector3.Distance(left, right) <= GripEpsilon;
        }

        private static bool NearGrip(Quaternion left, Quaternion right)
        {
            return 1f - Mathf.Abs(Quaternion.Dot(left, right)) <= GripEpsilon;
        }

        private static string CreateGripFingerprint(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            GripShell shell)
        {
            ValidateGripShell(outputRoot, roles, shell);
            var text = new StringBuilder(8192);
            text.Append("patch6-body-grips-v1|poses=")
                .Append(GripAssetKey(shell.Settings.GenericGripPose)).Append(',')
                .Append(GripAssetKey(shell.Settings.CylinderGripPose)).Append('|');
            foreach (HumanBodyBones role in NativeEntityOrder)
            {
                Component grip = shell.Generic.TryGetValue(role, out Component generic)
                    ? generic
                    : shell.Cylinder[role];
                var data = new SerializedObject(grip);
                Transform target = Require(data, "targetTransform")
                    .objectReferenceValue as Transform;
                text.Append(role).Append('=')
                    .Append(grip.GetType().FullName).Append(',')
                    .Append(RelativePath(outputRoot.transform, grip.transform)).Append(',')
                    .Append(RelativePath(outputRoot.transform, target)).Append(',');
                AppendVector(text, target.localPosition);
                AppendQuaternion(text, target.localRotation);
                AppendGripCommonFingerprint(text, data, outputRoot);
                if (shell.Cylinder.ContainsKey(role))
                    AppendCylinderGripFingerprint(text, data);
                text.Append('|');
            }
            return text.ToString();
        }

        private static void AppendGripCommonFingerprint(
            StringBuilder text,
            SerializedObject data,
            GameObject outputRoot)
        {
            text.Append(Require(data, "isThrowable").intValue)
                .Append(',')
                .Append(Require(data, "ignoreGripTargetOnAttach").intValue)
                .Append(',')
                .Append(Require(data, "gripColliders").arraySize).Append(',')
                .Append(Require(data, "additionalGripColliders").arraySize).Append(',')
                .Append(GripAssetKey(Require(data, "handPose").objectReferenceValue))
                .Append(',');
            AppendVector(text, Require(data, "primaryMovementAxis").vector3Value);
            AppendVector(text, Require(data, "secondaryMovementAxis").vector3Value);
            text.Append(Require(data, "gripOptions").intValue).Append(',')
                .Append(GripFloat(data, "priority")).Append(',')
                .Append(GripFloat(data, "minBreakForce")).Append(',')
                .Append(GripFloat(data, "maxBreakForce")).Append(',')
                .Append(GripFloat(data, "defaultGripDistance")).Append(',')
                .Append(GripFloat(data, "gripDistanceSqr")).Append(',')
                .Append(GripFloat(data, "radius")).Append(',')
                .Append(Require(data, "handleAmplifyCurve")
                    .animationCurveValue.length).Append(',');
        }

        private static void AppendCylinderGripFingerprint(
            StringBuilder text,
            SerializedObject data)
        {
            text.Append(GripFloat(data, "rotationLimit")).Append(',')
                .Append(GripFloat(data, "rotationPriorityBuffer")).Append(',')
                .Append(Require(data, "handPoseOnFlippedPrimaryAxis")
                    .objectReferenceValue == null ? "null," : "unexpected,")
                .Append(Require(data, "targetFlipOnPrimaryAxis").intValue)
                .Append(',')
                .Append(Require(data, "targetFlipOnTertiaryAxis").intValue)
                .Append(',')
                .Append(GripFloat(data, "dynamicFriction")).Append(',')
                .Append(GripFloat(data, "staticFriction")).Append(',')
                .Append(GripFloat(data, "limit")).Append(',')
                .Append(Require(data, "hasCapA").intValue)
                .Append(',')
                .Append(Require(data, "hasCapB").intValue)
                .Append(',')
                .Append(Require(data, "ignoreFlipOnZ").intValue)
                .Append(',')
                .Append(GripFloat(data, "rotationalFrictionMult")).Append(',')
                .Append(GripFloat(data, "aspectRatio")).Append(',')
                .Append(Require(data, "variableRadius").intValue)
                .Append(',')
                .Append(Require(data, "RadiusCurve").animationCurveValue.length)
                .Append(',');
        }

        private static string GripFloat(SerializedObject data, string path)
        {
            return Require(data, path).floatValue.ToString(
                "R", CultureInfo.InvariantCulture);
        }

        private static string GripAssetKey(UnityEngine.Object value)
        {
            string path = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
            return string.IsNullOrWhiteSpace(path)
                ? "missing"
                : StableAssetId(value) + "@"
                  + AssetDatabase.GetAssetDependencyHash(path).ToString();
        }

        private static MarrowNpcToolkitPatch6BehaviourSettings.GripResolved
            RequireGripSettings(GripTypes types)
        {
            if (!MarrowNpcToolkitPatch6BehaviourSettings.TryResolveGrips(
                    types.HandPose,
                    out MarrowNpcToolkitPatch6BehaviourSettings.GripResolved settings,
                    out string detail))
                throw new InvalidOperationException(detail);
            ValidatePersistentAsset(
                settings.GenericGripPose, types.HandPose,
                "Generic Body-Grab Pose");
            ValidatePersistentAsset(
                settings.CylinderGripPose, types.HandPose,
                "Cylinder Limb-Grab Pose");
            return settings;
        }

        private sealed class GripShell
        {
            public MarrowNpcToolkitPatch6BehaviourSettings.GripResolved Settings
            {
                get;
            }
            public GripTypes Types { get; }
            public IReadOnlyDictionary<HumanBodyBones, Component> Generic { get; }
            public IReadOnlyDictionary<HumanBodyBones, Component> Cylinder { get; }
            public IReadOnlyDictionary<HumanBodyBones, Transform> Centers { get; }

            public GripShell(
                MarrowNpcToolkitPatch6BehaviourSettings.GripResolved settings,
                GripTypes types,
                IReadOnlyDictionary<HumanBodyBones, Component> generic,
                IReadOnlyDictionary<HumanBodyBones, Component> cylinder,
                IReadOnlyDictionary<HumanBodyBones, Transform> centers)
            {
                Settings = settings;
                Types = types;
                Generic = generic;
                Cylinder = cylinder;
                Centers = centers;
            }
        }

        private sealed class GripTypes
        {
            public Type GenericGrip { get; }
            public Type CylinderGrip { get; }
            public Type HandPose { get; }

            private GripTypes(
                Type genericGrip,
                Type cylinderGrip,
                Type handPose)
            {
                GenericGrip = genericGrip;
                CylinderGrip = cylinderGrip;
                HandPose = handPose;
            }

            public static GripTypes Resolve()
            {
                Type generic = ResolvePatch6ComponentType(
                    "SLZ.Marrow.GenericGrip", "SLZ.Marrow");
                Type cylinder = ResolvePatch6ComponentType(
                    "SLZ.Marrow.CylinderGrip", "SLZ.Marrow");
                Type handPose = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(value => string.Equals(
                        value.GetName().Name, "SLZ.Marrow", StringComparison.Ordinal))
                    .Select(value => value.GetType("SLZ.Marrow.HandPose", false))
                    .FirstOrDefault(value => value != null);
                if (handPose == null
                    || !typeof(ScriptableObject).IsAssignableFrom(handPose))
                    throw new TypeLoadException(
                        "SLZ.Marrow.HandPose is unavailable from SLZ.Marrow.");
                return new GripTypes(generic, cylinder, handPose);
            }
        }
    }
}
