using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.Alignment
{
    public sealed class NpcBaselineFitReport
    {
        private readonly List<string> issues = new List<string>();

        public bool Success { get; internal set; }
        public float EyeHeightMeters { get; internal set; }
        public int FittedRoleCount { get; internal set; }
        public int PreservedReviewedRoleCount { get; internal set; }
        public string Fingerprint { get; internal set; }
        public IReadOnlyList<string> Issues => issues;

        internal void Add(string message)
        {
            issues.Add(message);
        }
    }

    public static class NpcBaselineFitter
    {
        private const float MinimumDimension = 0.01f;
        // Marrow Avatar ellipses describe the visible skin envelope. A physical
        // torso body should sit inside that silhouette so arms/legs do not hit
        // an inflated pelvis or rib cage. Eighty percent of the smaller ellipse
        // radius produces an internal collision core while retaining the
        // source Avatar's proportions.
        private const float TorsoCoreFraction = 0.8f;

        public static NpcBaselineFitReport Fit(
            NpcDefinition definition,
            bool overwriteReviewed,
            bool registerUndo = true)
        {
            var result = new NpcBaselineFitReport();
            if (definition == null || definition.SourceAvatar == null
                                   || definition.AvatarSourceProfile == null
                                   || definition.AnatomyProfile == null)
            {
                result.Add("The NPC Definition is missing its Avatar or authoring profiles.");
                return result;
            }

            NpcRigMappingReport mapping = NpcRigMappingService.Validate(definition);
            if (mapping.SourceChanged)
            {
                result.Add("The Avatar changed after its snapshot was captured. Refresh the Avatar snapshot before fitting physics.");
                return result;
            }
            if (!mapping.ReadyForBaseline)
            {
                foreach (NpcRigIssue issue in mapping.Issues.Where(value =>
                             value.Severity == NpcRigIssueSeverity.Error))
                    result.Add(issue.Message);
                if (result.Issues.Count == 0)
                    result.Add("The 16-role Humanoid mapping is not ready for baseline fitting.");
                return result;
            }

            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(definition.SourceAvatar);
                instance.name = definition.SourceAvatar.name + " [NPC Alignment Read Only]";
                instance.hideFlags = HideFlags.HideAndDontSave;
                Transform root = instance.transform;
                Animator animator = FindAnimator(root, definition.AvatarSourceProfile.AnimatorPath);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    result.Add("The accepted Avatar snapshot no longer resolves a valid Humanoid Animator.");
                    return result;
                }

                Vector3 rootScale = root.lossyScale;
                if (!IsUniform(rootScale))
                {
                    result.Add($"Physics fitting requires uniform source scale; the Avatar resolves to {rootScale}.");
                    return result;
                }

                Vector3 eyeWorld = ResolveEyeCenter(
                    root, animator, definition.AvatarSourceProfile);
                float eyeHeight = (Quaternion.Inverse(animator.transform.rotation)
                                   * (eyeWorld - animator.transform.position)).y;
                if (!IsFinitePositive(eyeHeight))
                {
                    result.Add("Could not derive a positive eye height from the Marrow Avatar eyes or Eye Center Override.");
                    return result;
                }

                NpcAnatomyProfile anatomy = definition.AnatomyProfile;
                if (registerUndo)
                    Undo.RegisterCompleteObjectUndo(anatomy, "Fit NPC physics baseline");
                anatomy.SeedMissingHumanoidDefaults();

                var bones = NpcHumanoidGraph.CanonicalRoles.ToDictionary(
                    role => role,
                    role => animator.GetBoneTransform(role));
                if (bones.Any(value => value.Value == null))
                {
                    result.Add("A canonical Humanoid bone disappeared while resolving the fitting instance.");
                    return result;
                }

                foreach (HumanBodyBones role in NpcHumanoidGraph.CanonicalRoles)
                {
                    NpcBodyRoleProfile profile = anatomy.FindRole(role);
                    if (profile == null) continue;
                    if (!overwriteReviewed
                        && (profile.AlignmentState == NpcAlignmentState.Reviewed
                            || !profile.AutoFitCollider))
                    {
                        result.PreservedReviewedRoleCount++;
                        continue;
                    }

                    Transform bone = bones[role];
                    Vector3 endWorld = ResolveRoleEnd(animator, role, bone, eyeWorld);
                    FitRole(
                        profile,
                        role,
                        bone,
                        endWorld,
                        animator,
                        animator.transform,
                        eyeHeight,
                        definition.AvatarSourceProfile);
                    profile.AlignmentState = NpcAlignmentState.AutoFit;
                    result.FittedRoleCount++;
                }

                bool requestedJawReady = true;
                if (definition.IncludePhysicalJaw)
                {
                    NpcBodyRoleProfile jawProfile = anatomy.OptionalJaw;
                    Transform jaw = ResolveMappedJaw(
                        root, animator, definition.AvatarSourceProfile);
                    if (jaw == null)
                    {
                        requestedJawReady = false;
                        result.Add(
                            "Physical Jaw is requested, but the accepted Avatar snapshot has no mapped Jaw. Map a Humanoid Jaw or turn off Physical Jaw in Define NPC.");
                    }
                    else
                    {
                        anatomy.JawClosedReferenceLocal = root.InverseTransformPoint(
                            jaw.position);
                        anatomy.JawClosedLocalRotation = jaw.localRotation;
                        if (!overwriteReviewed
                            && jawProfile != null
                            && (jawProfile.AlignmentState == NpcAlignmentState.Reviewed
                                || !jawProfile.AutoFitCollider))
                        {
                            result.PreservedReviewedRoleCount++;
                            requestedJawReady = IsFittedJaw(jawProfile);
                            if (!requestedJawReady)
                                result.Add(
                                    "The protected Physical Jaw alignment is incomplete. Enable Allow Auto-Fit or use Reset Selected to Auto-Fit, then fit again.");
                        }
                        else
                        {
                            string jawError = null;
                            if (jawProfile == null
                                 || !TryFitJawFromWeightedVertices(
                                     animator.transform,
                                     jaw,
                                     instance.GetComponentsInChildren<SkinnedMeshRenderer>(true),
                                     jawProfile,
                                     out jawError))
                            {
                                requestedJawReady = false;
                                result.Add(jawError ??
                                    "The Physical Jaw profile is missing from the Anatomy Profile.");
                            }
                            else
                            {
                                jawProfile.AlignmentState = NpcAlignmentState.AutoFit;
                                result.FittedRoleCount++;
                            }
                        }

                        // The Physical Jaw hinge is an authored humanoid
                        // invariant, not part of the collider adjustment. Old
                        // reviewed profiles could preserve the opposite axis,
                        // which turns the asymmetric -28..0 range into a
                        // closing/overbite range. Keep the reviewed box, but
                        // always refresh the hinge frame from the accepted
                        // Avatar lateral/up axes.
                        if (jawProfile != null)
                            ConfigureJawHingeFrame(
                                animator.transform,
                                jaw,
                                jawProfile);
                    }
                }

                anatomy.EyeCenterLocal = root.InverseTransformPoint(eyeWorld);
                anatomy.LeftSoleLocal = ResolveSole(
                    root, bones[HumanBodyBones.LeftFoot], anatomy.FindRole(HumanBodyBones.LeftFoot));
                anatomy.RightSoleLocal = ResolveSole(
                    root, bones[HumanBodyBones.RightFoot], anatomy.FindRole(HumanBodyBones.RightFoot));
                anatomy.MarkBaselineFitted(mapping.CurrentSourceDependencyHash);
                EditorUtility.SetDirty(anatomy);

                result.EyeHeightMeters = eyeHeight;
                result.Fingerprint = Fingerprint(anatomy, eyeHeight);
                result.Success = anatomy.HasFittedBaseline && requestedJawReady;
                if (!result.Success)
                {
                    if (!anatomy.HasFittedBaseline)
                        result.Add("The fitted profile did not produce 16 finite, positive collider contracts.");
                }
                return result;
            }
            catch (Exception exception)
            {
                result.Add(exception.Message);
                Debug.LogException(exception);
                return result;
            }
            finally
            {
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        public static string Fingerprint(NpcAnatomyProfile anatomy, float eyeHeight)
        {
            if (anatomy == null) return string.Empty;
            var builder = new StringBuilder();
            Append(builder, eyeHeight);
            foreach (HumanBodyBones role in NpcHumanoidGraph.CanonicalRoles)
            {
                NpcBodyRoleProfile value = anatomy.FindRole(role);
                if (value == null)
                {
                    builder.Append(role).Append("|missing;");
                    continue;
                }
                builder.Append(role).Append('|')
                    .Append((int)value.ColliderShape).Append('|')
                    .Append((int)value.AlignmentState).Append('|');
                Append(builder, value.ColliderCenter);
                Append(builder, value.ColliderLocalRotation);
                Append(builder, value.ColliderSize);
                Append(builder, value.CapsuleRadius);
                Append(builder, value.CapsuleHeight);
                Append(builder, value.MassKilograms);
                Append(builder, value.JointAxis);
                Append(builder, value.JointSecondaryAxis);
                builder.Append(';');
            }
            NpcBodyRoleProfile jaw = anatomy.OptionalJaw;
            if (jaw != null
                && (jaw.Enabled || jaw.AlignmentState != NpcAlignmentState.Unseeded))
            {
                builder.Append("Jaw|")
                    .Append((int)jaw.ColliderShape).Append('|')
                    .Append((int)jaw.AlignmentState).Append('|');
                Append(builder, jaw.ColliderCenter);
                Append(builder, jaw.ColliderLocalRotation);
                Append(builder, jaw.ColliderSize);
                Append(builder, jaw.MassKilograms);
                Append(builder, jaw.JointAxis);
                Append(builder, jaw.JointSecondaryAxis);
                Append(builder, anatomy.JawClosedReferenceLocal);
                Append(builder, anatomy.JawClosedLocalRotation);
                builder.Append(';');
            }
            return Hash128.Compute(builder.ToString()).ToString();
        }

        internal static bool TryFitJawFromWeightedVertices(
            Transform avatarRoot,
            Transform jaw,
            IEnumerable<SkinnedMeshRenderer> renderers,
            NpcBodyRoleProfile profile,
            out string error)
        {
            error = string.Empty;
            if (avatarRoot == null || jaw == null || profile == null)
            {
                error = "The Physical Jaw fit is missing its Avatar root, Jaw transform, or Anatomy profile.";
                return false;
            }

            bool hasVertex = false;
            Bounds bounds = default;
            foreach (SkinnedMeshRenderer renderer in renderers
                         ?? Enumerable.Empty<SkinnedMeshRenderer>())
            {
                Mesh mesh = renderer == null ? null : renderer.sharedMesh;
                if (mesh == null || renderer.bones == null) continue;
                var jawIndexes = renderer.bones
                    .Select((bone, index) => new { bone, index })
                    .Where(value => value.bone == jaw)
                    .Select(value => value.index)
                    .ToArray();
                if (jawIndexes.Length == 0) continue;

                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                if (vertices == null || weights == null
                                     || vertices.Length != weights.Length)
                    continue;
                Matrix4x4 toJaw = jaw.worldToLocalMatrix
                                  * renderer.localToWorldMatrix;
                for (int index = 0; index < vertices.Length; index++)
                {
                    BoneWeight weight = weights[index];
                    float jawWeight = 0f;
                    foreach (int jawIndex in jawIndexes)
                    {
                        if (weight.boneIndex0 == jawIndex) jawWeight += weight.weight0;
                        if (weight.boneIndex1 == jawIndex) jawWeight += weight.weight1;
                        if (weight.boneIndex2 == jawIndex) jawWeight += weight.weight2;
                        if (weight.boneIndex3 == jawIndex) jawWeight += weight.weight3;
                    }
                    if (jawWeight < 0.5f) continue;

                    Vector3 point = toJaw.MultiplyPoint3x4(vertices[index]);
                    if (!IsFinite(point)) continue;
                    if (!hasVertex)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        hasVertex = true;
                    }
                    else bounds.Encapsulate(point);
                }
            }

            if (!hasVertex || !IsFinite(bounds.center)
                           || !IsFinite(bounds.size)
                           || bounds.size.sqrMagnitude < 0.000001f)
            {
                error =
                    "Physical Jaw could not find a usable lower-face envelope. At least one source SkinnedMeshRenderer must bind vertices to the mapped Jaw with a summed Jaw weight of 0.5 or more.";
                return false;
            }

            profile.Enabled = true;
            profile.ColliderShape = NpcColliderShape.Box;
            profile.ColliderCenter = bounds.center;
            profile.ColliderLocalRotation = Quaternion.identity;
            profile.ColliderSize = Positive(bounds.size);
            profile.CapsuleRadius = 0f;
            profile.CapsuleHeight = 0f;
            profile.MassKilograms = 1.1765f;
            ConfigureJawHingeFrame(avatarRoot, jaw, profile);
            profile.AngularXMotion = NpcJointMotion.Limited;
            profile.AngularYMotion = NpcJointMotion.Limited;
            profile.AngularZMotion = NpcJointMotion.Locked;
            profile.AngularLowLimits = new Vector3(-28f, -10f, 0f);
            profile.AngularHighLimits = new Vector3(0f, 10f, 0f);
            profile.JointDriveMaxForce = 36f;
            profile.MuscleSpring = 5000000f;
            // This authoring value is divided by the provider's global damper
            // when it becomes PuppetMaster Muscle.Props. Match the 100,000
            // global scale so the generated per-muscle multiplier is 1.0.
            profile.MuscleDamper = 100000f;
            profile.MuscleWeight = 1f;
            return true;
        }

        internal static void ConfigureJawHingeFrame(
            Transform avatarRoot,
            Transform jaw,
            NpcBodyRoleProfile profile)
        {
            if (avatarRoot == null || jaw == null || profile == null)
                throw new ArgumentNullException(
                    "Physical Jaw hinge fitting requires its Avatar frame, Jaw, and profile.");
            // PhysX uses the sign of the primary axis to decide which side of
            // an asymmetric low..high range is opening. The accepted Patch 6
            // Jaw contract uses the Avatar right direction here; on the
            // validated imported Jaw frame this correctly resolves to local -X.
            Vector3 worldAxis = avatarRoot.right;
            Vector3 worldSecondary = Vector3.ProjectOnPlane(
                avatarRoot.up, worldAxis).normalized;
            if (worldSecondary.sqrMagnitude < 0.000001f)
                worldSecondary = avatarRoot.forward;
            profile.JointAxis = jaw.InverseTransformDirection(worldAxis).normalized;
            profile.JointSecondaryAxis = jaw
                .InverseTransformDirection(worldSecondary).normalized;
        }

        private static bool IsFittedJaw(NpcBodyRoleProfile jaw)
        {
            return jaw != null
                   && jaw.Enabled
                   && jaw.AlignmentState != NpcAlignmentState.Unseeded
                   && jaw.ColliderShape == NpcColliderShape.Box
                   && IsFinitePositive(jaw.ColliderSize.x)
                   && IsFinitePositive(jaw.ColliderSize.y)
                   && IsFinitePositive(jaw.ColliderSize.z);
        }

        private static Transform ResolveMappedJaw(
            Transform root,
            Animator animator,
            NpcAvatarSourceProfile source)
        {
            if (root == null || animator == null || source == null
                             || string.IsNullOrWhiteSpace(source.JawPath))
                return null;
            Transform pathJaw = root.Find(source.JawPath);
            Transform humanoidJaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            return pathJaw != null && pathJaw == humanoidJaw ? pathJaw : null;
        }

        private static void FitRole(
            NpcBodyRoleProfile profile,
            HumanBodyBones role,
            Transform bone,
            Vector3 endWorld,
            Animator animator,
            Transform animatorRoot,
            float eyeHeight,
            NpcAvatarSourceProfile source)
        {
            if (role == HumanBodyBones.Head)
            {
                FitHead(profile, bone, animatorRoot, eyeHeight, source.BodyFit);
            }
            else if (IsHand(role))
            {
                FitHand(
                    profile,
                    role,
                    bone,
                    endWorld,
                    animator,
                    animatorRoot,
                    eyeHeight,
                    source.Wrist);
            }
            else if (IsFoot(role))
            {
                FitFoot(
                    profile,
                    bone,
                    endWorld,
                    animatorRoot,
                    eyeHeight,
                    source.Ankle);
            }
            else if (IsTorso(role))
            {
                FitTorsoCapsule(
                    profile,
                    role,
                    bone,
                    ResolveTorsoEnd(
                        animator,
                        role,
                        bone,
                        endWorld,
                        eyeHeight),
                    animatorRoot,
                    eyeHeight,
                    source);
            }
            else
            {
                FitCapsule(profile, role, bone, endWorld, animatorRoot, eyeHeight, source);
            }

            Vector3 direction = endWorld - bone.position;
            if (direction.sqrMagnitude < 0.000001f) direction = animatorRoot.up;
            direction.Normalize();
            Vector3 hinge = Vector3.Cross(direction, animatorRoot.forward);
            if (hinge.sqrMagnitude < 0.0001f)
                hinge = Vector3.Cross(direction, animatorRoot.up);
            if (hinge.sqrMagnitude < 0.0001f) hinge = animatorRoot.right;
            float sign = IsLimb(role) ? -1f : 1f;
            profile.JointAxis = bone.InverseTransformDirection(hinge.normalized * sign).normalized;
            profile.JointSecondaryAxis = bone.InverseTransformDirection(direction).normalized;
        }

        private static void FitCapsule(
            NpcBodyRoleProfile profile,
            HumanBodyBones role,
            Transform bone,
            Vector3 endWorld,
            Transform animatorRoot,
            float eyeHeight,
            NpcAvatarSourceProfile source)
        {
            Vector3 segmentLocal = bone.InverseTransformPoint(endWorld);
            float length = Mathf.Max(segmentLocal.magnitude, eyeHeight * 0.025f);
            float radius = RadiusFor(role, source, eyeHeight, length);
            radius = Mathf.Clamp(radius, eyeHeight * 0.012f, Mathf.Max(eyeHeight * 0.15f, length * 0.45f));
            float height = Mathf.Max(length, radius * 2f);

            profile.ColliderShape = NpcColliderShape.Capsule;
            profile.CapsuleDirection = 1;
            profile.CapsuleRadius = radius;
            profile.CapsuleHeight = height;
            profile.ColliderSize = new Vector3(radius * 2f, height, radius * 2f);
            profile.ColliderCenter = segmentLocal * 0.5f;
            profile.ColliderLocalRotation = RotationForDirection(
                bone, endWorld - bone.position, animatorRoot);
        }

        private static void FitTorsoCapsule(
            NpcBodyRoleProfile profile,
            HumanBodyBones role,
            Transform bone,
            Vector3 endWorld,
            Transform animatorRoot,
            float eyeHeight,
            NpcAvatarSourceProfile source)
        {
            Vector3 direction = endWorld - bone.position;
            if (direction.sqrMagnitude < 0.000001f)
                direction = animatorRoot.up * (eyeHeight * 0.08f);
            float segmentLength = Mathf.Max(direction.magnitude, eyeHeight * 0.025f);
            float radius = TorsoRadiusFor(role, source, eyeHeight);
            radius = Mathf.Clamp(radius, eyeHeight * 0.025f, eyeHeight * 0.11f);
            float height = Mathf.Max(radius * 2f, segmentLength);
            Vector3 directionWorld = direction.normalized;

            profile.ColliderShape = NpcColliderShape.Capsule;
            profile.CapsuleDirection = 1;
            profile.CapsuleRadius = radius;
            profile.CapsuleHeight = height;
            profile.ColliderSize = new Vector3(radius * 2f, height, radius * 2f);
            // Torso cores begin at their authored Humanoid joint and extend
            // upward. Centering on half the short bone segment made a large
            // radius spill below the pelvis into both upper legs.
            profile.ColliderCenter = bone.InverseTransformDirection(directionWorld)
                                     * (height * 0.5f);
            profile.ColliderLocalRotation = RotationForDirection(
                bone, directionWorld, animatorRoot);
        }

        private static float TorsoRadiusFor(
            HumanBodyBones role,
            NpcAvatarSourceProfile source,
            float eyeHeight)
        {
            NpcAvatarBodyFit body = source.BodyFit;
            float width;
            float depth;
            if (role == HumanBodyBones.Hips)
            {
                width = body.HipsWidth;
                depth = MaxDepth(body.HipsDepth);
            }
            else if (role == HumanBodyBones.Spine)
            {
                width = body.WaistWidth;
                depth = MaxDepth(body.WaistDepth);
            }
            else
            {
                width = body.ChestWidth;
                depth = MaxDepth(body.ChestDepth);
            }
            float smallerRadius = Mathf.Min(width, depth);
            if (!IsFinitePositive(smallerRadius))
                smallerRadius = Mathf.Max(width, depth);
            return eyeHeight * smallerRadius * TorsoCoreFraction;
        }

        private static Vector3 ResolveTorsoEnd(
            Animator animator,
            HumanBodyBones role,
            Transform bone,
            Vector3 fallback,
            float eyeHeight)
        {
            if (role != HumanBodyBones.Chest) return fallback;
            Vector3 direction = fallback - bone.position;
            if (direction.sqrMagnitude < 0.000001f)
                direction = animator.transform.up;
            Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck != null)
            {
                // Neck determines where the chest core ends, but the canonical
                // Chest-to-Head chain determines its authored axis. Pointing
                // directly at an offset Neck can introduce a depth tilt.
                return bone.position + direction.normalized
                       * Vector3.Distance(bone.position, neck.position);
            }

            // A 16-role graph connects Chest directly to Head, but the physical
            // chest must not consume the neck. Cap that fallback direction.
            return bone.position + direction.normalized
                   * Mathf.Min(direction.magnitude, eyeHeight * 0.18f);
        }

        private static void FitHead(
            NpcBodyRoleProfile profile,
            Transform bone,
            Transform animatorRoot,
            float eyeHeight,
            NpcAvatarBodyFit fit)
        {
            float bottomY = eyeHeight * (1f - Mathf.Max(fit.ChinY, 0.02f));
            float topY = eyeHeight * (1f + Mathf.Max(fit.HeadTop, 0.02f));
            float height = Mathf.Max(topY - bottomY, eyeHeight * 0.12f);
            float radius = eyeHeight * Mathf.Max(
                Mathf.Max(fit.ForeheadWidth, fit.JawWidth),
                MaxDepth(fit.ForeheadDepth, fit.JawDepth));
            radius = Mathf.Clamp(radius, eyeHeight * 0.035f, eyeHeight * 0.12f);
            height = Mathf.Max(height, radius * 2f);

            Vector3 centerAnimator = animatorRoot.InverseTransformPoint(bone.position);
            centerAnimator.y = (bottomY + topY) * 0.5f;
            Vector3 centerWorld = animatorRoot.TransformPoint(centerAnimator);
            profile.ColliderShape = NpcColliderShape.Capsule;
            profile.CapsuleDirection = 1;
            profile.CapsuleRadius = radius;
            profile.CapsuleHeight = height;
            profile.ColliderSize = new Vector3(radius * 2f, height, radius * 2f);
            profile.ColliderCenter = bone.InverseTransformPoint(centerWorld);
            profile.ColliderLocalRotation = RotationForDirection(
                bone, animatorRoot.up, animatorRoot);
        }

        private static void FitHand(
            NpcBodyRoleProfile profile,
            HumanBodyBones role,
            Transform bone,
            Vector3 endWorld,
            Animator animator,
            Transform animatorRoot,
            float eyeHeight,
            NpcAvatarLimbEllipse wrist)
        {
            float skeletonLength = Mathf.Max(
                Vector3.Distance(bone.position, endWorld), eyeHeight * 0.055f);
            float wristRadius = EllipseRadius(wrist) * eyeHeight;
            Vector3 direction = (endWorld - bone.position).normalized;
            if (direction.sqrMagnitude < 0.0001f)
                direction = role == HumanBodyBones.LeftHand ? -animatorRoot.right : animatorRoot.right;

            float length = skeletonLength;
            float width = Mathf.Max(skeletonLength * 0.55f, wristRadius * 2.2f);
            float centerAcross = 0f;
            float centerAlong = skeletonLength * 0.45f;
            if (TryResolveHandFrame(
                    animator, role, direction, animatorRoot,
                    out Vector3 worldX, out Vector3 worldY, out _))
            {
                Transform[] samples = HandEnvelopeRoles(role)
                    .Select(animator.GetBoneTransform)
                    .Where(value => value != null)
                    .ToArray();
                if (samples.Length > 0)
                {
                    float minimumX = samples.Min(value =>
                        Vector3.Dot(value.position - bone.position, worldX));
                    float maximumX = samples.Max(value =>
                        Vector3.Dot(value.position - bone.position, worldX));
                    float maximumY = Mathf.Max(skeletonLength, samples.Max(value =>
                        Vector3.Dot(value.position - bone.position, worldY)));
                    float sidePadding = Mathf.Max(wristRadius * 0.35f, eyeHeight * 0.003f);
                    float wristPadding = wristRadius * 0.2f;
                    float fingertipPadding = wristRadius * 0.6f;
                    width = Mathf.Max(
                        maximumX - minimumX + sidePadding * 2f,
                        wristRadius * 2.2f);
                    float startY = -wristPadding;
                    float endY = maximumY + fingertipPadding;
                    length = endY - startY;
                    centerAcross = (minimumX + maximumX) * 0.5f;
                    centerAlong = (startY + endY) * 0.5f;
                    direction = worldY;
                }
            }
            float depth = Mathf.Max(width * 0.36f, wristRadius * 1.4f);

            profile.ColliderShape = NpcColliderShape.Box;
            profile.ColliderSize = Positive(new Vector3(width, length, depth));
            Vector3 centerWorld = bone.position
                                  + WorldXOrFallback(
                                      animator, role, direction, animatorRoot) * centerAcross
                                  + direction * centerAlong;
            profile.ColliderCenter = bone.InverseTransformPoint(centerWorld);
            profile.ColliderLocalRotation = RotationForHand(
                animator, role, bone, direction, animatorRoot);
            profile.CapsuleRadius = 0f;
            profile.CapsuleHeight = 0f;
        }

        private static void FitFoot(
            NpcBodyRoleProfile profile,
            Transform bone,
            Vector3 endWorld,
            Transform animatorRoot,
            float eyeHeight,
            NpcAvatarLimbEllipse ankle)
        {
            Vector3 up = animatorRoot.up.normalized;
            Vector3 avatarForward = Vector3.ProjectOnPlane(animatorRoot.forward, up);
            if (avatarForward.sqrMagnitude < 0.000001f)
                avatarForward = Vector3.Cross(up, animatorRoot.right);
            avatarForward.Normalize();

            // A Humanoid Foot -> Toes segment is not necessarily a sole axis.
            // High-heel rigs can point it almost straight down, which previously
            // produced an upright box along the shin. Preserve its toe-out angle
            // but project it onto the Avatar ground plane for heel-to-toe length.
            Vector3 toeOffset = endWorld - bone.position;
            Vector3 forwardOffset = Vector3.ProjectOnPlane(toeOffset, up);
            float forwardDistance = forwardOffset.magnitude;
            Vector3 forward = forwardDistance > eyeHeight * 0.005f
                ? forwardOffset / forwardDistance
                : avatarForward;
            // A malformed/reversed toe mapping must not turn a foot backwards.
            if (Vector3.Dot(forward, avatarForward) < 0.25f)
                forward = avatarForward;

            float length = Mathf.Max(forwardDistance * 1.45f, eyeHeight * 0.12f);
            float ankleRadius = EllipseRadius(ankle) * eyeHeight;
            float width = Mathf.Max(ankleRadius * 2.2f, eyeHeight * 0.055f);
            float height = Mathf.Max(ankleRadius * 1.35f, eyeHeight * 0.035f);
            float ankleHeight = Vector3.Dot(bone.position - animatorRoot.position, up);
            // Official Avatar prefabs stand on the Animator root plane. Keep
            // this body around the solid foot/sole instead of inflating it to
            // encompass the full sloped ankle-to-toe segment of a high heel.
            float centerHeight = height * 0.5f;

            // Leave about 30% of the generated length behind the ankle for the
            // heel and 70% in front for the toe box.
            const float FootCenterForwardFraction = 0.2f;
            Vector3 centerWorld = bone.position
                                  + forward * (length * FootCenterForwardFraction)
                                  + up * (centerHeight - ankleHeight);

            profile.ColliderShape = NpcColliderShape.Box;
            profile.ColliderSize = Positive(new Vector3(width, length, height));
            profile.ColliderCenter = bone.InverseTransformPoint(centerWorld);
            profile.ColliderLocalRotation = RotationForFoot(bone, forward, up);
            profile.CapsuleRadius = 0f;
            profile.CapsuleHeight = 0f;
        }

        private static float RadiusFor(
            HumanBodyBones role,
            NpcAvatarSourceProfile source,
            float eyeHeight,
            float length)
        {
            NpcAvatarBodyFit body = source.BodyFit;
            switch (role)
            {
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                    return eyeHeight * AverageRadius(source.UpperArm, source.Elbow);
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                    return eyeHeight * AverageRadius(source.Forearm, source.Wrist);
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                    return eyeHeight * AverageRadius(source.ThighUpper, source.Knee);
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                    return eyeHeight * AverageRadius(source.Calf, source.Ankle);
                default:
                    return length * 0.15f;
            }
        }

        private static bool IsTorso(HumanBodyBones role)
        {
            return role == HumanBodyBones.Hips
                   || role == HumanBodyBones.Spine
                   || role == HumanBodyBones.Chest;
        }

        private static Vector3 ResolveRoleEnd(
            Animator animator,
            HumanBodyBones role,
            Transform bone,
            Vector3 eyeWorld)
        {
            if (role == HumanBodyBones.Head) return eyeWorld + animator.transform.up * 0.1f;
            if (NpcHumanoidGraph.TryGetPrimaryChild(role, out HumanBodyBones child))
            {
                Transform childBone = animator.GetBoneTransform(child);
                if (childBone != null) return childBone.position;
            }
            if (role == HumanBodyBones.LeftHand || role == HumanBodyBones.RightHand)
                return ResolveHandTip(animator, role, bone);
            if (role == HumanBodyBones.LeftFoot)
            {
                Transform toes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
                if (toes != null) return toes.position;
            }
            if (role == HumanBodyBones.RightFoot)
            {
                Transform toes = animator.GetBoneTransform(HumanBodyBones.RightToes);
                if (toes != null) return toes.position;
            }
            return bone.position + animator.transform.forward * 0.1f;
        }

        private static Vector3 ResolveHandTip(
            Animator animator,
            HumanBodyBones role,
            Transform hand)
        {
            HumanBodyBones[] proximalCandidates = role == HumanBodyBones.LeftHand
                ? new[]
                {
                    HumanBodyBones.LeftIndexProximal,
                    HumanBodyBones.LeftMiddleProximal,
                    HumanBodyBones.LeftRingProximal,
                    HumanBodyBones.LeftLittleProximal,
                }
                : new[]
                {
                    HumanBodyBones.RightIndexProximal,
                    HumanBodyBones.RightMiddleProximal,
                    HumanBodyBones.RightRingProximal,
                    HumanBodyBones.RightLittleProximal,
                };
            HumanBodyBones[] distalCandidates = role == HumanBodyBones.LeftHand
                ? new[]
                {
                    HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleDistal,
                }
                : new[]
                {
                    HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleDistal,
                };

            Transform[] proximal = proximalCandidates
                .Select(animator.GetBoneTransform)
                .Where(value => value != null)
                .ToArray();
            Transform[] distal = distalCandidates
                .Select(animator.GetBoneTransform)
                .Where(value => value != null)
                .ToArray();

            // Aim through the centre of the four main knuckles. Picking one
            // farthest fingertip makes a splayed or asymmetric hand box lean
            // toward that finger, while including the thumb pulls it sideways.
            Transform[] directionSamples = proximal.Length > 0 ? proximal : distal;
            if (directionSamples.Length > 0)
            {
                Vector3 center = directionSamples
                    .Aggregate(Vector3.zero, (sum, value) => sum + value.position)
                    / directionSamples.Length;
                Vector3 direction = center - hand.position;
                if (direction.sqrMagnitude > 0.000001f)
                {
                    direction.Normalize();
                    Transform[] lengthSamples = distal.Length > 0 ? distal : directionSamples;
                    float length = lengthSamples.Max(value =>
                        Vector3.Dot(value.position - hand.position, direction));
                    if (length > 0.001f) return hand.position + direction * length;
                }
            }

            Vector3 away = (hand.position - animator.GetBoneTransform(
                role == HumanBodyBones.LeftHand
                    ? HumanBodyBones.LeftLowerArm
                    : HumanBodyBones.RightLowerArm).position).normalized;
            return hand.position + away * 0.1f;
        }

        private static Quaternion RotationForHand(
            Animator animator,
            HumanBodyBones role,
            Transform bone,
            Vector3 worldLengthDirection,
            Transform animatorRoot)
        {
            if (!TryResolveHandFrame(
                    animator, role, worldLengthDirection, animatorRoot,
                    out _, out Vector3 worldY, out Vector3 worldZ))
                return RotationForDirection(bone, worldLengthDirection, animatorRoot);
            Vector3 localY = bone.InverseTransformDirection(worldY).normalized;
            Vector3 localZ = bone.InverseTransformDirection(worldZ).normalized;
            return Quaternion.LookRotation(localZ, localY);
        }

        private static Vector3 WorldXOrFallback(
            Animator animator,
            HumanBodyBones role,
            Vector3 worldLengthDirection,
            Transform animatorRoot)
        {
            return TryResolveHandFrame(
                animator, role, worldLengthDirection, animatorRoot,
                out Vector3 worldX, out _, out _)
                ? worldX
                : Vector3.Cross(worldLengthDirection, animatorRoot.forward).normalized;
        }

        private static bool TryResolveHandFrame(
            Animator animator,
            HumanBodyBones role,
            Vector3 worldLengthDirection,
            Transform animatorRoot,
            out Vector3 worldX,
            out Vector3 worldY,
            out Vector3 worldZ)
        {
            HumanBodyBones indexRole = role == HumanBodyBones.LeftHand
                ? HumanBodyBones.LeftIndexProximal
                : HumanBodyBones.RightIndexProximal;
            HumanBodyBones littleRole = role == HumanBodyBones.LeftHand
                ? HumanBodyBones.LeftLittleProximal
                : HumanBodyBones.RightLittleProximal;
            Transform index = animator.GetBoneTransform(indexRole);
            Transform little = animator.GetBoneTransform(littleRole);
            worldY = worldLengthDirection.sqrMagnitude < 0.000001f
                ? animatorRoot.up
                : worldLengthDirection.normalized;
            worldX = index == null || little == null
                ? Vector3.zero
                : Vector3.ProjectOnPlane(index.position - little.position, worldY).normalized;
            worldZ = Vector3.zero;
            if (worldX.sqrMagnitude < 0.0001f) return false;

            // X spans the knuckles, Y runs wrist-to-fingers, and Z is palm
            // thickness. This keeps a flat hand box flat regardless of whether
            // the source Avatar uses arms-down, A-pose, or T-pose hands.
            worldZ = Vector3.Cross(worldX, worldY).normalized;
            if (Vector3.Dot(worldZ, animatorRoot.forward) < 0f)
            {
                worldX = -worldX;
                worldZ = -worldZ;
            }
            return true;
        }

        private static HumanBodyBones[] HandEnvelopeRoles(HumanBodyBones role)
        {
            return role == HumanBodyBones.LeftHand
                ? new[]
                {
                    HumanBodyBones.LeftThumbProximal,
                    HumanBodyBones.LeftThumbIntermediate,
                    HumanBodyBones.LeftThumbDistal,
                    HumanBodyBones.LeftIndexProximal,
                    HumanBodyBones.LeftIndexIntermediate,
                    HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleProximal,
                    HumanBodyBones.LeftMiddleIntermediate,
                    HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingProximal,
                    HumanBodyBones.LeftRingIntermediate,
                    HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleProximal,
                    HumanBodyBones.LeftLittleIntermediate,
                    HumanBodyBones.LeftLittleDistal,
                }
                : new[]
                {
                    HumanBodyBones.RightThumbProximal,
                    HumanBodyBones.RightThumbIntermediate,
                    HumanBodyBones.RightThumbDistal,
                    HumanBodyBones.RightIndexProximal,
                    HumanBodyBones.RightIndexIntermediate,
                    HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleProximal,
                    HumanBodyBones.RightMiddleIntermediate,
                    HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingProximal,
                    HumanBodyBones.RightRingIntermediate,
                    HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleProximal,
                    HumanBodyBones.RightLittleIntermediate,
                    HumanBodyBones.RightLittleDistal,
                };
        }

        private static Vector3 ResolveEyeCenter(
            Transform root,
            Animator animator,
            NpcAvatarSourceProfile source)
        {
            if (!string.IsNullOrWhiteSpace(source.EyeCenterOverridePath))
            {
                Transform eyeOverride = root.Find(source.EyeCenterOverridePath);
                if (eyeOverride != null) return eyeOverride.position;
            }
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (left != null && right != null) return Vector3.Lerp(left.position, right.position, 0.5f);
            if (left != null) return left.position;
            if (right != null) return right.position;
            return animator.transform.position;
        }

        private static Animator FindAnimator(Transform root, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                Transform animatorTransform = root.Find(path);
                if (animatorTransform != null)
                {
                    Animator animator = animatorTransform.GetComponent<Animator>();
                    if (animator != null) return animator;
                }
            }
            return root.GetComponent<Animator>() ?? root.GetComponentInChildren<Animator>(true);
        }

        private static Quaternion RotationForDirection(
            Transform bone,
            Vector3 worldDirection,
            Transform animatorRoot)
        {
            Vector3 worldY = worldDirection.sqrMagnitude < 0.000001f
                ? animatorRoot.up
                : worldDirection.normalized;
            Vector3 worldZ = Vector3.ProjectOnPlane(animatorRoot.forward, worldY).normalized;
            if (worldZ.sqrMagnitude < 0.0001f)
                worldZ = Vector3.ProjectOnPlane(animatorRoot.up, worldY).normalized;
            if (worldZ.sqrMagnitude < 0.0001f) worldZ = animatorRoot.forward;
            Vector3 localY = bone.InverseTransformDirection(worldY).normalized;
            Vector3 localZ = bone.InverseTransformDirection(worldZ).normalized;
            return Quaternion.LookRotation(localZ, localY);
        }

        private static Quaternion RotationForFoot(
            Transform bone,
            Vector3 worldForward,
            Vector3 worldUp)
        {
            Vector3 localY = bone.InverseTransformDirection(worldForward).normalized;
            Vector3 localZ = bone.InverseTransformDirection(worldUp).normalized;
            return Quaternion.LookRotation(localZ, localY);
        }

        private static Vector3 ResolveSole(
            Transform root,
            Transform foot,
            NpcBodyRoleProfile profile)
        {
            if (foot == null || profile == null) return Vector3.zero;
            Vector3 centerWorld = foot.TransformPoint(profile.ColliderCenter);
            float halfHeight = profile.ColliderShape == NpcColliderShape.Box
                ? profile.ColliderSize.z * 0.5f
                : profile.CapsuleRadius;
            return root.InverseTransformPoint(centerWorld - root.up * halfHeight);
        }

        private static bool IsUniform(Vector3 scale)
        {
            return IsFinitePositive(scale.x)
                   && IsFinitePositive(scale.y)
                   && IsFinitePositive(scale.z)
                   && Mathf.Abs(scale.x - scale.y) < 0.0001f
                   && Mathf.Abs(scale.x - scale.z) < 0.0001f;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsLimb(HumanBodyBones role)
        {
            return role == HumanBodyBones.LeftUpperArm
                   || role == HumanBodyBones.RightUpperArm
                   || role == HumanBodyBones.LeftLowerArm
                   || role == HumanBodyBones.RightLowerArm
                   || role == HumanBodyBones.LeftHand
                   || role == HumanBodyBones.RightHand
                   || role == HumanBodyBones.LeftUpperLeg
                   || role == HumanBodyBones.RightUpperLeg
                   || role == HumanBodyBones.LeftLowerLeg
                   || role == HumanBodyBones.RightLowerLeg
                   || role == HumanBodyBones.LeftFoot
                   || role == HumanBodyBones.RightFoot;
        }

        private static bool IsHand(HumanBodyBones role)
        {
            return role == HumanBodyBones.LeftHand || role == HumanBodyBones.RightHand;
        }

        private static bool IsFoot(HumanBodyBones role)
        {
            return role == HumanBodyBones.LeftFoot || role == HumanBodyBones.RightFoot;
        }

        private static float AverageRadius(
            NpcAvatarLimbEllipse first,
            NpcAvatarLimbEllipse second)
        {
            float value = (EllipseRadius(first) + EllipseRadius(second)) * 0.5f;
            return value > 0.001f ? value : 0.03f;
        }

        private static float EllipseRadius(NpcAvatarLimbEllipse value)
        {
            return Mathf.Max(value.Radii.x, value.Radii.y);
        }

        private static float MaxDepth(params Vector2[] values)
        {
            float maximum = 0f;
            foreach (Vector2 value in values)
                maximum = Mathf.Max(maximum, value.x, value.y);
            return maximum;
        }

        private static Vector3 Positive(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(MinimumDimension, value.x),
                Mathf.Max(MinimumDimension, value.y),
                Mathf.Max(MinimumDimension, value.z));
        }

        private static void Append(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void Append(StringBuilder builder, Vector3 value)
        {
            Append(builder, value.x);
            Append(builder, value.y);
            Append(builder, value.z);
        }

        private static void Append(StringBuilder builder, Quaternion value)
        {
            Append(builder, value.x);
            Append(builder, value.y);
            Append(builder, value.z);
            Append(builder, value.w);
        }
    }
}
