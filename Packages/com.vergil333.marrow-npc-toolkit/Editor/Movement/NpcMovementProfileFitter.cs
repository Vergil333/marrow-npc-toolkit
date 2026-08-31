using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Movement
{
    public sealed class NpcMovementFitReport
    {
        private readonly List<string> issues = new List<string>();

        public bool Success { get; internal set; }
        public bool PreservedReviewedTuning { get; internal set; }
        public bool MeasurementsChanged { get; internal set; }
        public string Fingerprint { get; internal set; }
        public IReadOnlyList<string> Issues => issues;

        internal void Add(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !issues.Contains(message))
                issues.Add(message);
        }
    }

    internal struct NpcMovementMeasurements
    {
        public float EyeHeight;
        public float BodyHeight;
        public float NavHeight;
        public float LeftLegLength;
        public float RightLegLength;
        public float HipWidth;
        public float StanceWidth;
        public float SoleHeight;
        public float NavRadius;
        public float NavBaseOffset;
        public Vector3 LeftFootForwardLocal;
        public Vector3 RightFootForwardLocal;
    }

    /// <summary>
    /// Measures a clean instance of the accepted Humanoid and stores only public,
    /// provider-neutral standing/navigation inputs. The source Avatar and Anatomy
    /// Profile are treated as immutable measurement references.
    /// </summary>
    public static class NpcMovementProfileFitter
    {
        // The accepted Patch 6 humanoid baseline uses a 0.41 m agent radius at
        // 1.76 m height. Physics torso colliders are intentionally smaller inner
        // cores, so they cannot by themselves describe navigation clearance.
        private const float NavigationRadiusHeightFraction = 0.41f / 1.76f;

        public static NpcMovementFitReport Fit(
            NpcDefinition definition,
            bool resetReviewedTuning,
            bool registerUndo = true)
        {
            var report = new NpcMovementFitReport();
            if (definition == null || definition.SourceAvatar == null
                                   || definition.AvatarSourceProfile == null
                                   || definition.AnatomyProfile == null
                                   || definition.MovementProfile == null)
            {
                report.Add(
                    "The NPC Definition is missing its Avatar, Anatomy, or Movement Profile.");
                return report;
            }

            NpcRigMappingReport mapping = NpcRigMappingService.Validate(definition);
            if (!mapping.ReadyForBaseline || mapping.SourceChanged)
            {
                foreach (NpcRigIssue issue in mapping.Issues.Where(value =>
                             value.Severity == NpcRigIssueSeverity.Error
                             || value.Severity == NpcRigIssueSeverity.Warning))
                    report.Add(issue.Message);
                if (report.Issues.Count == 0)
                    report.Add(
                        "The accepted 16-role Humanoid mapping is not ready for movement fitting.");
                return report;
            }
            if (!definition.AnatomyProfile.BaselineMatches(
                    mapping.CurrentSourceDependencyHash))
            {
                report.Add(
                    "Create or refresh the Physics Alignment baseline before measuring movement.");
                return report;
            }
            string authoringFingerprint =
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition);
            if (string.IsNullOrWhiteSpace(authoringFingerprint))
            {
                report.Add(
                    "The Avatar and Physics Alignment did not produce a stable movement authoring fingerprint.");
                return report;
            }

            string sourcePath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            string sourceHashBefore = AssetDatabase.GetAssetDependencyHash(
                sourcePath).ToString();
            string sourceProfileBefore = EditorJsonUtility.ToJson(
                definition.AvatarSourceProfile, false);
            string anatomyBefore = EditorJsonUtility.ToJson(
                definition.AnatomyProfile, false);
            NpcMovementProfile profile = definition.MovementProfile;
            string profileBefore = EditorJsonUtility.ToJson(profile, false);
            bool profileWasDirty = EditorUtility.IsDirty(profile);
            Scene previewScene = default;
            GameObject instance = null;
            int undoGroup = -1;
            try
            {
                instance = InstantiateMeasurementSource(
                    definition.SourceAvatar, out previewScene);
                instance.name = definition.SourceAvatar.name
                                + " [Movement Measurement Read Only]";
                instance.hideFlags = HideFlags.HideAndDontSave;
                Transform root = instance.transform;
                Animator animator = FindAnimator(
                    root, definition.AvatarSourceProfile.AnimatorPath);
                if (animator == null || animator.avatar == null
                                     || !animator.avatar.isHuman)
                {
                    report.Add(
                        "The accepted Avatar no longer resolves a valid Humanoid Animator.");
                    return report;
                }

                var bones = new Dictionary<HumanBodyBones, Transform>();
                foreach (NpcHumanoidBoneBinding binding in
                         definition.AvatarSourceProfile.HumanoidBones)
                {
                    if (!NpcHumanoidGraph.CanonicalRoles.Contains(binding.Role))
                        continue;
                    Transform resolved = Resolve(root, binding.TransformPath);
                    if (resolved == null
                        || animator.GetBoneTransform(binding.Role) != resolved)
                    {
                        report.Add(
                            $"The stored {binding.Role} Humanoid binding no longer resolves on a clean Avatar instance.");
                        return report;
                    }
                    bones[binding.Role] = resolved;
                }
                if (NpcHumanoidGraph.CanonicalRoles.Any(role =>
                        !bones.ContainsKey(role)))
                {
                    report.Add(
                        "A canonical Humanoid binding disappeared while measuring movement.");
                    return report;
                }

                if (!TryMeasure(
                        root,
                        animator,
                        bones,
                        definition.AvatarSourceProfile,
                        definition.AnatomyProfile,
                        out NpcMovementMeasurements measurements,
                        out string measurementError))
                {
                    report.Add(measurementError);
                    return report;
                }

                bool sourceChanged = !string.Equals(
                    profile.AutoFitSourceDependencyHash,
                    mapping.CurrentSourceDependencyHash,
                    StringComparison.Ordinal);
                bool authoringChanged = !string.Equals(
                    profile.AutoFitAuthoringFingerprint,
                    authoringFingerprint,
                    StringComparison.Ordinal);
                bool measurementsChanged = !MeasurementsMatch(
                    profile, measurements);
                if (!MovementProfileMatchesSnapshot(
                        profile, profileBefore, profileWasDirty))
                {
                    report.Add(
                        "The Movement Profile changed while Unity measured the Avatar. Nothing was committed.");
                    return report;
                }
                if (!InputsUnchanged(
                        sourcePath,
                        sourceHashBefore,
                        definition.AvatarSourceProfile,
                        sourceProfileBefore,
                        definition.AnatomyProfile,
                        anatomyBefore))
                {
                    report.Add(
                        "Movement fitting changed a read-only Avatar or Anatomy input.");
                    return report;
                }
                if (registerUndo)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Fit NPC movement baseline");
                    Undo.RegisterCompleteObjectUndo(
                        profile, "Fit NPC movement baseline");
                }
                // Movement is an automatic stock-reference adaptation. Earlier
                // toolkit versions exposed provider multipliers without a
                // truthful native preview, so preserving those values could
                // silently carry guesses (for example an oversized step height)
                // into every rebuild. Every fit now starts from the documented
                // automatic contract and derives only measurable Avatar data.
                _ = resetReviewedTuning; // retained for source compatibility
                // Damage response is an explicit gameplay choice rather than
                // an unpreviewed locomotion multiplier. Preserve it when the
                // automatic Avatar measurements are refreshed.
                float startingHostility = profile.StartingHostility;
                float hostilityAfterTypicalHit =
                    profile.HostilityAfterTypicalHit;
                profile.ResetToDefaults();
                profile.StartingHostility = startingHostility;
                profile.HostilityAfterTypicalHit =
                    hostilityAfterTypicalHit;
                profile.SetAutoFitMeasurements(
                    measurements.EyeHeight,
                    measurements.BodyHeight,
                    measurements.NavHeight,
                    measurements.LeftLegLength,
                    measurements.RightLegLength,
                    measurements.HipWidth,
                    measurements.StanceWidth,
                    measurements.SoleHeight,
                    measurements.NavRadius,
                    measurements.NavBaseOffset,
                    measurements.LeftFootForwardLocal,
                    measurements.RightFootForwardLocal,
                    mapping.CurrentSourceDependencyHash,
                    authoringFingerprint);
                EditorUtility.SetDirty(profile);
                report.MeasurementsChanged = measurementsChanged || sourceChanged
                                             || authoringChanged;
                report.Fingerprint = Hash128.Compute(
                    EditorJsonUtility.ToJson(profile, false)).ToString();
                report.Success = true;
                return report;
            }
            catch (Exception exception)
            {
                report.Add(exception.Message);
                Debug.LogException(exception);
                return report;
            }
            finally
            {
                try
                {
                    if (instance != null) Object.DestroyImmediate(instance);
                }
                catch (Exception exception)
                {
                    report.Success = false;
                    report.Add(
                        "Unity could not clean up the movement measurement instance: "
                        + exception.Message);
                }
                try
                {
                    if (previewScene.IsValid())
                        EditorSceneManager.ClosePreviewScene(previewScene);
                }
                catch (Exception exception)
                {
                    report.Success = false;
                    report.Add(
                        "Unity could not close the movement measurement scene: "
                        + exception.Message);
                }

                bool inputsUnchanged = false;
                bool inputsChecked = false;
                try
                {
                    inputsUnchanged = InputsUnchanged(
                        sourcePath,
                        sourceHashBefore,
                        definition.AvatarSourceProfile,
                        sourceProfileBefore,
                        definition.AnatomyProfile,
                        anatomyBefore);
                    inputsChecked = true;
                }
                catch (Exception exception)
                {
                    report.Success = false;
                    report.Add(
                        "Unity could not verify the read-only movement inputs: "
                        + exception.Message);
                }
                if (inputsChecked && !inputsUnchanged)
                {
                    report.Success = false;
                    report.Add(
                        "Movement fitting changed a read-only Avatar or Anatomy input.");
                }

                if (report.Success && undoGroup >= 0)
                {
                    try
                    {
                        Undo.CollapseUndoOperations(undoGroup);
                    }
                    catch (Exception exception)
                    {
                        report.Success = false;
                        report.Add(
                            "Unity could not finalize the movement-fit Undo operation: "
                            + exception.Message);
                    }
                }

                if (!report.Success)
                {
                    if (undoGroup >= 0)
                    {
                        try
                        {
                            Undo.RevertAllDownToGroup(undoGroup);
                        }
                        catch (Exception exception)
                        {
                            report.Add(
                                "Unity could not revert the movement-fit Undo operation: "
                                + exception.Message);
                        }
                    }
                    try
                    {
                        RestoreMovementProfileSnapshot(
                            profile, profileBefore, profileWasDirty);
                    }
                    catch (Exception exception)
                    {
                        report.Add(
                            "Unity could not restore the Movement Profile after the failed fit: "
                            + exception.Message);
                    }
                    report.PreservedReviewedTuning = false;
                    report.MeasurementsChanged = false;
                    report.Fingerprint = string.Empty;
                }
            }
        }

        internal static GameObject InstantiateMeasurementSource(
            GameObject source,
            out Scene previewScene)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            previewScene = default;
            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                GameObject instance = PrefabUtility.InstantiatePrefab(
                    source, previewScene) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException(
                        "Unity could not instantiate the accepted Avatar prefab in the movement measurement scene.");
                return instance;
            }
            catch
            {
                try
                {
                    if (previewScene.IsValid())
                        EditorSceneManager.ClosePreviewScene(previewScene);
                }
                catch
                {
                    // Preserve the original instantiation failure.
                }
                previewScene = default;
                throw;
            }
        }

        internal static void RestoreMovementProfileSnapshot(
            NpcMovementProfile profile,
            string serializedProfile,
            bool wasDirty)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (string.IsNullOrWhiteSpace(serializedProfile))
                throw new ArgumentException(
                    "The Movement Profile snapshot is empty.",
                    nameof(serializedProfile));
            EditorJsonUtility.FromJsonOverwrite(serializedProfile, profile);
            if (wasDirty)
                EditorUtility.SetDirty(profile);
            else
                EditorUtility.ClearDirty(profile);
        }

        private static bool MovementProfileMatchesSnapshot(
            NpcMovementProfile profile,
            string serializedProfile,
            bool wasDirty)
        {
            return profile != null
                   && string.Equals(
                       serializedProfile,
                       EditorJsonUtility.ToJson(profile, false),
                       StringComparison.Ordinal)
                   && wasDirty == EditorUtility.IsDirty(profile);
        }

        private static bool InputsUnchanged(
            string sourcePath,
            string sourceHashBefore,
            NpcAvatarSourceProfile sourceProfile,
            string sourceProfileBefore,
            NpcAnatomyProfile anatomy,
            string anatomyBefore)
        {
            string sourceHashAfter = AssetDatabase.GetAssetDependencyHash(
                sourcePath).ToString();
            return string.Equals(
                       sourceHashBefore,
                       sourceHashAfter,
                       StringComparison.Ordinal)
                   && sourceProfile != null
                   && string.Equals(
                       sourceProfileBefore,
                       EditorJsonUtility.ToJson(sourceProfile, false),
                       StringComparison.Ordinal)
                   && anatomy != null
                   && string.Equals(
                       anatomyBefore,
                       EditorJsonUtility.ToJson(anatomy, false),
                       StringComparison.Ordinal);
        }

        internal static bool TryMeasure(
            Transform root,
            Animator animator,
            IReadOnlyDictionary<HumanBodyBones, Transform> bones,
            NpcAvatarSourceProfile source,
            NpcAnatomyProfile anatomy,
            out NpcMovementMeasurements measurements,
            out string error)
        {
            measurements = default;
            error = string.Empty;
            if (root == null || animator == null || bones == null
                             || source == null || anatomy == null)
            {
                error = "Movement measurement inputs are incomplete.";
                return false;
            }

            // Store every public movement measurement in the accepted source
            // prefab's root frame. The Animator may legally live beneath a
            // rotated/scaled child; the native output, navigation agent, and
            // provider helpers are all authored in the NPC/source-root frame.
            Transform frame = root;
            Vector3 up = frame.up.normalized;
            Vector3 right = frame.right.normalized;
            Vector3 forward = frame.forward.normalized;
            Vector3 eyeWorld = root.TransformPoint(anatomy.EyeCenterLocal);
            Vector3 leftSoleWorld = root.TransformPoint(anatomy.LeftSoleLocal);
            Vector3 rightSoleWorld = root.TransformPoint(anatomy.RightSoleLocal);

            float eyeHeight = Height(frame, eyeWorld, up);
            float leftSoleHeight = Height(frame, leftSoleWorld, up);
            float rightSoleHeight = Height(frame, rightSoleWorld, up);
            float soleHeight = Mathf.Min(leftSoleHeight, rightSoleHeight);
            float headTop = eyeHeight * (1f + Mathf.Max(
                source.BodyFit.HeadTop, 0.02f));
            float bodyHeight = headTop - soleHeight;
            float leftLeg = SegmentSum(
                bones[HumanBodyBones.LeftUpperLeg],
                bones[HumanBodyBones.LeftLowerLeg],
                bones[HumanBodyBones.LeftFoot]);
            float rightLeg = SegmentSum(
                bones[HumanBodyBones.RightUpperLeg],
                bones[HumanBodyBones.RightLowerLeg],
                bones[HumanBodyBones.RightFoot]);
            float hipWidth = HorizontalDistance(
                bones[HumanBodyBones.LeftUpperLeg].position,
                bones[HumanBodyBones.RightUpperLeg].position,
                up);
            float stanceWidth = HorizontalDistance(
                leftSoleWorld, rightSoleWorld, up);
            float navHeight = bodyHeight;
            float navRadius = NavigationRadius(
                navHeight,
                TorsoRadius(frame, bones, anatomy, right, forward));
            Vector3 leftFootForward = FootForwardLocal(
                frame,
                animator,
                HumanBodyBones.LeftFoot,
                HumanBodyBones.LeftToes,
                up,
                forward);
            Vector3 rightFootForward = FootForwardLocal(
                frame,
                animator,
                HumanBodyBones.RightFoot,
                HumanBodyBones.RightToes,
                up,
                forward);

            float[] positive =
            {
                eyeHeight, bodyHeight, navHeight, leftLeg, rightLeg,
                hipWidth, stanceWidth, navRadius,
            };
            if (positive.Any(value => !IsFinitePositive(value))
                || !IsFinite(soleHeight)
                || !IsFinite(leftFootForward)
                || !IsFinite(rightFootForward)
                || leftFootForward.sqrMagnitude < 0.5f
                || rightFootForward.sqrMagnitude < 0.5f)
            {
                error =
                    "The accepted Humanoid did not produce finite positive movement measurements.";
                return false;
            }

            measurements = new NpcMovementMeasurements
            {
                EyeHeight = eyeHeight,
                BodyHeight = bodyHeight,
                NavHeight = navHeight,
                LeftLegLength = leftLeg,
                RightLegLength = rightLeg,
                HipWidth = hipWidth,
                StanceWidth = stanceWidth,
                SoleHeight = soleHeight,
                NavRadius = navRadius,
                NavBaseOffset = soleHeight,
                LeftFootForwardLocal = leftFootForward,
                RightFootForwardLocal = rightFootForward,
            };
            return true;
        }

        private static float TorsoRadius(
            Transform frame,
            IReadOnlyDictionary<HumanBodyBones, Transform> bones,
            NpcAnatomyProfile anatomy,
            Vector3 right,
            Vector3 forward)
        {
            float radius = 0f;
            foreach (HumanBodyBones role in new[]
                     {
                         HumanBodyBones.Hips,
                         HumanBodyBones.Spine,
                         HumanBodyBones.Chest,
                     })
            {
                NpcBodyRoleProfile profile = anatomy.FindRole(role);
                if (profile == null || !bones.TryGetValue(role, out Transform bone))
                    continue;
                Vector3 center = bone.TransformPoint(profile.ColliderCenter);
                Quaternion rotation = bone.rotation
                                      * profile.ColliderLocalRotation;
                Vector3 fromFrame = center - frame.position;
                float rightExtent = ProjectedExtent(profile, rotation, right);
                float forwardExtent = ProjectedExtent(
                    profile, rotation, forward);
                radius = Mathf.Max(
                    radius,
                    Mathf.Abs(Vector3.Dot(fromFrame, right)) + rightExtent,
                    Mathf.Abs(Vector3.Dot(fromFrame, forward)) + forwardExtent);
            }
            return radius;
        }

        private static bool MeasurementsMatch(
            NpcMovementProfile profile,
            NpcMovementMeasurements value)
        {
            if (profile == null || !profile.HasFittedMeasurements) return false;
            const float ScalarTolerance = 0.00001f;
            const float DirectionToleranceDegrees = 0.01f;
            return Mathf.Abs(profile.EyeHeight - value.EyeHeight)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.BodyHeight - value.BodyHeight)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.NavHeight - value.NavHeight)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.LeftLegLength - value.LeftLegLength)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.RightLegLength - value.RightLegLength)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.HipWidth - value.HipWidth)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.StanceWidth - value.StanceWidth)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.SoleHeight - value.SoleHeight)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.NavRadius - value.NavRadius)
                       <= ScalarTolerance
                   && Mathf.Abs(profile.NavBaseOffset - value.NavBaseOffset)
                       <= ScalarTolerance
                   && Vector3.Angle(
                       profile.LeftFootForwardLocal,
                       value.LeftFootForwardLocal) <= DirectionToleranceDegrees
                   && Vector3.Angle(
                       profile.RightFootForwardLocal,
                       value.RightFootForwardLocal) <= DirectionToleranceDegrees;
        }

        private static float ProjectedExtent(
            NpcBodyRoleProfile profile,
            Quaternion rotation,
            Vector3 worldDirection)
        {
            Vector3 direction = worldDirection.normalized;
            if (profile.ColliderShape == NpcColliderShape.Sphere)
                return profile.CapsuleRadius;
            if (profile.ColliderShape == NpcColliderShape.Box)
            {
                Vector3 half = profile.ColliderSize * 0.5f;
                return Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.right))
                           * half.x
                       + Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.up))
                           * half.y
                       + Mathf.Abs(Vector3.Dot(direction, rotation * Vector3.forward))
                           * half.z;
            }

            Vector3 localAxis = profile.CapsuleDirection == 0
                ? Vector3.right
                : profile.CapsuleDirection == 2
                    ? Vector3.forward
                    : Vector3.up;
            Vector3 axis = rotation * localAxis;
            float axial = Mathf.Abs(Vector3.Dot(direction, axis));
            float segmentHalf = Mathf.Max(
                0f, profile.CapsuleHeight * 0.5f - profile.CapsuleRadius);
            return segmentHalf * axial + profile.CapsuleRadius;
        }

        private static Vector3 FootForwardLocal(
            Transform frame,
            Animator animator,
            HumanBodyBones footRole,
            HumanBodyBones toeRole,
            Vector3 up,
            Vector3 fallbackForward)
        {
            Transform foot = animator.GetBoneTransform(footRole);
            Transform toe = animator.GetBoneTransform(toeRole);
            return CalculateFootForwardLocal(
                frame, foot, toe, up, fallbackForward);
        }

        internal static Vector3 CalculateFootForwardLocal(
            Transform frame,
            Transform foot,
            Transform toe,
            Vector3 up,
            Vector3 fallbackForward)
        {
            Vector3 world = foot != null && toe != null
                ? toe.position - foot.position
                : fallbackForward;
            world = Vector3.ProjectOnPlane(world, up);
            if (world.sqrMagnitude < 0.000001f) world = fallbackForward;
            Vector3 local = frame.InverseTransformDirection(world.normalized);
            local.y = 0f;
            return local.sqrMagnitude < 0.000001f
                ? Vector3.forward
                : local.normalized;
        }

        private static float SegmentSum(
            Transform upper,
            Transform lower,
            Transform foot)
        {
            return CalculateLegLength(upper, lower, foot);
        }

        internal static float CalculateLegLength(
            Transform upper,
            Transform lower,
            Transform foot)
        {
            if (upper == null || lower == null || foot == null) return 0f;
            return Vector3.Distance(upper.position, lower.position)
                   + Vector3.Distance(lower.position, foot.position);
        }

        internal static float NavigationRadius(
            float navHeight,
            float torsoEnvelopeRadius)
        {
            if (!IsFinitePositive(navHeight)) return 0f;
            float rawRadius = Mathf.Max(
                torsoEnvelopeRadius,
                navHeight * NavigationRadiusHeightFraction);
            float minimumRadius = Mathf.Max(navHeight * 0.04f, 0.01f);
            return Mathf.Clamp(rawRadius, minimumRadius, navHeight * 0.45f);
        }

        private static float HorizontalDistance(
            Vector3 first,
            Vector3 second,
            Vector3 up)
        {
            return Vector3.ProjectOnPlane(second - first, up).magnitude;
        }

        private static float Height(
            Transform frame,
            Vector3 point,
            Vector3 up)
        {
            return Vector3.Dot(point - frame.position, up);
        }

        private static Animator FindAnimator(Transform root, string path)
        {
            Transform holder = Resolve(root, path);
            return holder == null
                ? root.GetComponent<Animator>()
                  ?? root.GetComponentInChildren<Animator>(true)
                : holder.GetComponent<Animator>();
        }

        private static Transform Resolve(Transform root, string path)
        {
            if (root == null) return null;
            return string.IsNullOrWhiteSpace(path) ? root : root.Find(path);
        }

        private static bool IsFinitePositive(float value)
        {
            return IsFinite(value) && value > 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
