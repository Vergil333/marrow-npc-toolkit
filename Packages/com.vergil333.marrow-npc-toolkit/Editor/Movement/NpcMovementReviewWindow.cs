using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Movement
{
    public sealed class NpcMovementReviewWindow : EditorWindow
    {
        [SerializeField] private NpcDefinition definition;
        [SerializeField] private Vector2 scroll;
        private string statusMessage;

        public static void Open(NpcDefinition value)
        {
            NpcMovementReviewWindow window =
                GetWindow<NpcMovementReviewWindow>();
            window.titleContent = new GUIContent("Automatic Movement Details");
            window.minSize = new Vector2(460f, 620f);
            window.definition = value;
            window.statusMessage = null;
            window.OpenSourcePrefab();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
            Undo.undoRedoPerformed -= UndoRedoPerformed;
            Undo.undoRedoPerformed += UndoRedoPerformed;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            Undo.undoRedoPerformed -= UndoRedoPerformed;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            definition = EditorGUILayout.ObjectField(
                "NPC Definition",
                definition,
                typeof(NpcDefinition),
                false) as NpcDefinition;
            if (EditorGUI.EndChangeCheck())
            {
                statusMessage = null;
                OpenSourcePrefab();
                SceneView.RepaintAll();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox(
                "Read-only automatic-fit details. This view checks measured proportions, foot support, and an estimated center of mass. It does not run BONELAB AI, LiteLoco, PuppetMaster, NavMesh movement, collisions, falling, or recovery.",
                MessageType.Info);

            if (definition == null)
            {
                EditorGUILayout.LabelField(
                    "Select an NPC Definition.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
                return;
            }
            NpcMovementProfile profile = definition.MovementProfile;
            if (profile == null)
            {
                EditorGUILayout.HelpBox(
                    "This older NPC Definition has no Movement Profile. Return to Step 3D and choose Recalculate Movement for This Avatar; the toolkit will create and link it beside the Definition.",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (!profile.HasFittedMeasurements)
            {
                EditorGUILayout.HelpBox(
                    "Run Step 3D Recalculate Movement for This Avatar before viewing automatic movement details.",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawMeasurements(profile);
            EditorGUILayout.Space(8f);
            DrawChecks(profile);
            EditorGUILayout.Space(8f);
            DrawAutomaticContract(profile);
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Source in Scene View"))
                    OpenSourcePrefab();
                if (GUILayout.Button("Frame Character")) FrameCharacter();
            }
            if (!string.IsNullOrWhiteSpace(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawMeasurements(NpcMovementProfile profile)
        {
            EditorGUILayout.LabelField(
                "Automatic standing measurements", EditorStyles.boldLabel);
            DrawMeters("Eye height", profile.EyeHeight);
            DrawMeters("Body / navigation height", profile.NavHeight);
            DrawMeters("Left leg", profile.LeftLegLength);
            DrawMeters("Right leg", profile.RightLegLength);
            DrawMeters("Mean leg", profile.MeanLegLength);
            DrawMeters("Hip width", profile.HipWidth);
            DrawMeters("Standing foot separation", profile.StanceWidth);
            DrawMeters("Navigation radius", profile.NavRadius);
            DrawMeters("Navigation base offset", profile.NavBaseOffset);
            EditorGUILayout.LabelField(
                "Native movement setup",
                profile.ProviderStandingPose != null
                && profile.ProviderMovementConfig != null
                && !string.IsNullOrWhiteSpace(
                    profile.ProviderRecipeFingerprint)
                    ? "Recorded; Step 4 verifies currentness"
                    : "Refresh Step 3D");
        }

        private void DrawChecks(NpcMovementProfile profile)
        {
            EditorGUILayout.LabelField("Standing checks", EditorStyles.boldLabel);
            float legDifference = Mathf.Abs(
                profile.LeftLegLength - profile.RightLegLength);
            float legRatio = profile.MeanLegLength <= 0f
                ? float.PositiveInfinity
                : legDifference / profile.MeanLegLength;
            DrawCheck(
                "Leg-length difference",
                legRatio <= 0.05f,
                legRatio <= 0.12f,
                $"{legDifference:0.000} m ({legRatio * 100f:0.0}%)");

            if (TryBuildSceneData(out SceneData data))
            {
                DrawCheck(
                    "Left/right sole mismatch",
                    data.SoleMismatch <= 0.02f,
                    data.SoleMismatch <= 0.05f,
                    $"{data.SoleMismatch:0.000} m");
                DrawCheck(
                    "Estimated COM over support area",
                    data.CenterInsideSupport,
                    data.CenterInsideSupport,
                    data.CenterInsideSupport ? "Inside" : "Outside");
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Open the accepted source prefab to calculate live sole and support checks.",
                    MessageType.None);
            }

            bool radiusSensible = profile.NavHeight > 0f
                                  && profile.NavRadius >= profile.NavHeight * 0.1f
                                  && profile.NavRadius < profile.NavHeight * 0.45f;
            DrawCheck(
                "Navigation clearance",
                radiusSensible,
                radiusSensible,
                $"radius {profile.NavRadius:0.000} m / height {profile.NavHeight:0.000} m");
            EditorGUILayout.LabelField(
                "These checks identify obvious standing-proportion mistakes; they are not pass/fail certification for unusual humanoids.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawAutomaticContract(NpcMovementProfile profile)
        {
            EditorGUILayout.LabelField(
                "Automatic native adaptation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The provider scales its stock-reference stride, step clearance, and cadence from the measured mean leg length, places foot targets from the measured soles and headings, and derives navigation clearance from the fitted body. These values are intentionally not editable without a native runtime preview.",
                EditorStyles.wordWrappedMiniLabel);
            DrawMeters("Mean leg reference", profile.MeanLegLength);
            DrawMeters("Measured stance", profile.StanceWidth);
            DrawMeters("Measured sole plane", profile.SoleHeight);
            EditorGUILayout.LabelField(
                "Runtime walk speed",
                profile.WalkSpeed.ToString("0.00") + " m/s");
            EditorGUILayout.LabelField(
                "Runtime test",
                "Required after packing");
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (!TryBuildSceneData(out SceneData data)) return;
            Color oldColor = Handles.color;
            CompareFunction oldZTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                float planeSize = Mathf.Max(
                    0.8f,
                    definition.MovementProfile.NavRadius * 3f
                    + definition.MovementProfile.StanceWidth);
                DrawGround(data, planeSize);

                Handles.color = new Color(0.2f, 0.9f, 1f, 1f);
                float markerSize = HandleUtility.GetHandleSize(
                    data.LeftSole) * 0.035f;
                Handles.SphereHandleCap(
                    0, data.LeftSole, Quaternion.identity, markerSize,
                    EventType.Repaint);
                Handles.SphereHandleCap(
                    0, data.RightSole, Quaternion.identity, markerSize,
                    EventType.Repaint);
                Handles.DrawAAPolyLine(
                    3f,
                    data.LeftSole,
                    data.LeftSole + data.LeftForward * planeSize * 0.22f);
                Handles.DrawAAPolyLine(
                    3f,
                    data.RightSole,
                    data.RightSole + data.RightForward * planeSize * 0.22f);

                Handles.color = data.CenterInsideSupport
                    ? new Color(0.25f, 0.9f, 0.35f, 1f)
                    : new Color(1f, 0.55f, 0.15f, 1f);
                if (data.SupportHull.Length >= 2)
                {
                    Vector3[] closed = data.SupportHull
                        .Concat(new[] { data.SupportHull[0] }).ToArray();
                    Handles.DrawAAPolyLine(5f, closed);
                }
                Handles.DrawDottedLine(
                    data.CenterOfMass, data.CenterProjection, 4f);
                Handles.SphereHandleCap(
                    0,
                    data.CenterProjection,
                    Quaternion.identity,
                    markerSize * 1.2f,
                    EventType.Repaint);
                Handles.Label(
                    data.CenterProjection + data.Up * markerSize,
                    "Estimated COM");

                if (Vector3.Distance(data.Pelvis, data.TunedPelvis) > 0.0001f)
                {
                    Handles.color = new Color(0.95f, 0.45f, 1f, 1f);
                    Handles.DrawDottedLine(data.Pelvis, data.TunedPelvis, 4f);
                    Handles.SphereHandleCap(
                        0,
                        data.TunedPelvis,
                        Quaternion.identity,
                        markerSize,
                        EventType.Repaint);
                    Handles.Label(
                        data.TunedPelvis + data.Up * markerSize,
                        "Tuned pelvis target");
                }
            }
            finally
            {
                Handles.color = oldColor;
                Handles.zTest = oldZTest;
            }
        }

        private static void DrawGround(SceneData data, float size)
        {
            Handles.color = new Color(0.7f, 0.75f, 0.8f, 0.45f);
            const int Divisions = 4;
            for (int index = -Divisions; index <= Divisions; index++)
            {
                float offset = size * index / (Divisions * 2f);
                Handles.DrawLine(
                    data.GroundOrigin + data.Right * offset
                                      - data.Forward * size * 0.5f,
                    data.GroundOrigin + data.Right * offset
                                      + data.Forward * size * 0.5f);
                Handles.DrawLine(
                    data.GroundOrigin + data.Forward * offset
                                      - data.Right * size * 0.5f,
                    data.GroundOrigin + data.Forward * offset
                                      + data.Right * size * 0.5f);
            }
        }

        private bool TryBuildSceneData(out SceneData data)
        {
            data = default;
            NpcMovementProfile movement = definition?.MovementProfile;
            NpcAnatomyProfile anatomy = definition?.AnatomyProfile;
            NpcAvatarSourceProfile source = definition?.AvatarSourceProfile;
            if (movement == null || anatomy == null || source == null
                                 || !movement.HasFittedMeasurements)
                return false;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            string expectedPath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            if (stage == null || stage.prefabContentsRoot == null
                              || !string.Equals(
                                  stage.assetPath,
                                  expectedPath,
                                  StringComparison.Ordinal))
                return false;

            Transform root = stage.prefabContentsRoot.transform;
            // Movement measurements are defined in the accepted source root
            // frame, not an arbitrarily nested Animator frame. This keeps the
            // review identical to the coordinate frame consumed at native
            // build time for rotated or scaled Humanoid armatures.
            Transform frame = root;
            Vector3 up = frame.up.normalized;
            Vector3 right = frame.right.normalized;
            Vector3 forward = frame.forward.normalized;
            Vector3 ground = frame.position
                             + up * movement.SoleHeight;
            Vector3 originalLeft = root.TransformPoint(anatomy.LeftSoleLocal);
            Vector3 originalRight = root.TransformPoint(anatomy.RightSoleLocal);
            float leftHeight = Vector3.Dot(originalLeft - frame.position, up);
            float rightHeight = Vector3.Dot(originalRight - frame.position, up);

            Vector3 midpoint = Vector3.Lerp(originalLeft, originalRight, 0.5f);
            midpoint -= up * Vector3.Dot(midpoint - ground, up);
            Vector3 lateral = Vector3.ProjectOnPlane(
                originalRight - originalLeft, up);
            if (lateral.sqrMagnitude < 0.000001f)
                lateral = right * movement.StanceWidth;
            Vector3 tunedHalf = lateral * (movement.StanceWidthScale * 0.5f);
            Vector3 leftSole = midpoint - tunedHalf;
            Vector3 rightSole = midpoint + tunedHalf;
            Vector3 leftForward = CorrectedForward(
                frame,
                movement.LeftFootForwardLocal,
                movement.LeftFootYawCorrectionDegrees,
                up,
                forward);
            Vector3 rightForward = CorrectedForward(
                frame,
                movement.RightFootForwardLocal,
                movement.RightFootYawCorrectionDegrees,
                up,
                forward);

            var bones = new Dictionary<HumanBodyBones, Transform>();
            foreach (NpcHumanoidBoneBinding binding in source.HumanoidBones)
            {
                Transform bone = root.Find(binding.TransformPath);
                if (bone != null) bones[binding.Role] = bone;
            }
            if (definition.IncludePhysicalJaw
                && anatomy.OptionalJaw != null
                && anatomy.OptionalJaw.Enabled
                && !string.IsNullOrWhiteSpace(source.JawPath))
            {
                Transform jaw = root.Find(source.JawPath);
                if (jaw != null) bones[HumanBodyBones.Jaw] = jaw;
            }
            if (!bones.TryGetValue(HumanBodyBones.LeftFoot, out _)
                || !bones.TryGetValue(HumanBodyBones.RightFoot, out _))
                return false;

            FootprintSize leftSize = Footprint(
                anatomy.FindRole(HumanBodyBones.LeftFoot), movement.NavRadius);
            FootprintSize rightSize = Footprint(
                anatomy.FindRole(HumanBodyBones.RightFoot), movement.NavRadius);
            List<Vector3> support = FootCorners(
                    leftSole, leftForward, up, leftSize)
                .Concat(FootCorners(
                    rightSole, rightForward, up, rightSize))
                .ToList();
            Vector3[] hull = ConvexHull(support, frame);

            float totalMass = 0f;
            Vector3 weighted = Vector3.zero;
            IEnumerable<NpcBodyRoleProfile> massRoles = anatomy.BodyRoles;
            if (definition.IncludePhysicalJaw
                && anatomy.OptionalJaw != null
                && anatomy.OptionalJaw.Enabled)
                massRoles = massRoles.Concat(new[] { anatomy.OptionalJaw });
            foreach (NpcBodyRoleProfile role in massRoles)
            {
                if (role == null || !role.Enabled || role.MassKilograms <= 0f
                                 || !bones.TryGetValue(
                                     role.Role, out Transform bone))
                    continue;
                weighted += bone.TransformPoint(role.ColliderCenter)
                            * role.MassKilograms;
                totalMass += role.MassKilograms;
            }
            Vector3 center = totalMass <= 0f
                ? bones[HumanBodyBones.Hips].position
                : weighted / totalMass;
            Vector3 projection = center
                                 - up * Vector3.Dot(center - ground, up);
            bool inside = hull.Length >= 3
                          && PointInsideConvexHull(projection, hull, frame);

            data = new SceneData
            {
                GroundOrigin = ground,
                Up = up,
                Right = right,
                Forward = forward,
                LeftSole = leftSole,
                RightSole = rightSole,
                LeftForward = leftForward,
                RightForward = rightForward,
                CenterOfMass = center,
                CenterProjection = projection,
                Pelvis = bones[HumanBodyBones.Hips].position,
                TunedPelvis = bones[HumanBodyBones.Hips].position
                              + up * movement.PelvisHeightOffset,
                SupportHull = hull,
                CenterInsideSupport = inside,
                SoleMismatch = Mathf.Abs(leftHeight - rightHeight),
            };
            return true;
        }

        private static FootprintSize Footprint(
            NpcBodyRoleProfile role,
            float navRadius)
        {
            float fallbackWidth = Mathf.Max(navRadius * 0.22f, 0.035f);
            float fallbackLength = Mathf.Max(navRadius * 0.55f, 0.09f);
            if (role == null) return new FootprintSize(
                fallbackWidth, fallbackLength);
            if (role.ColliderShape == NpcColliderShape.Box)
                return new FootprintSize(
                    Mathf.Max(role.ColliderSize.x * 0.5f, 0.015f),
                    Mathf.Max(role.ColliderSize.y * 0.5f, 0.03f));
            float radius = Mathf.Max(role.CapsuleRadius, 0.015f);
            return new FootprintSize(radius, Mathf.Max(
                role.CapsuleHeight * 0.5f, radius));
        }

        private static IEnumerable<Vector3> FootCorners(
            Vector3 center,
            Vector3 forward,
            Vector3 up,
            FootprintSize size)
        {
            Vector3 footRight = Vector3.Cross(up, forward).normalized;
            yield return center + footRight * size.HalfWidth
                                + forward * size.HalfLength;
            yield return center - footRight * size.HalfWidth
                                + forward * size.HalfLength;
            yield return center - footRight * size.HalfWidth
                                - forward * size.HalfLength;
            yield return center + footRight * size.HalfWidth
                                - forward * size.HalfLength;
        }

        private static Vector3 CorrectedForward(
            Transform frame,
            Vector3 local,
            float yaw,
            Vector3 up,
            Vector3 fallback)
        {
            Vector3 value = Vector3.ProjectOnPlane(
                frame.TransformDirection(local), up);
            if (value.sqrMagnitude < 0.000001f) value = fallback;
            return (Quaternion.AngleAxis(yaw, up) * value.normalized).normalized;
        }

        private static Vector3[] ConvexHull(
            IEnumerable<Vector3> points,
            Transform frame)
        {
            List<HullPoint> values = points.Select(point => new HullPoint(
                    point,
                    new Vector2(
                        Vector3.Dot(point - frame.position, frame.right),
                        Vector3.Dot(point - frame.position, frame.forward))))
                .OrderBy(value => value.Plane.x)
                .ThenBy(value => value.Plane.y)
                .ToList();
            if (values.Count <= 2) return values.Select(value => value.World).ToArray();
            var hull = new List<HullPoint>();
            foreach (HullPoint point in values)
            {
                while (hull.Count >= 2 && Cross(
                           hull[hull.Count - 2].Plane,
                           hull[hull.Count - 1].Plane,
                           point.Plane) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lowerCount = hull.Count;
            for (int index = values.Count - 2; index >= 0; index--)
            {
                HullPoint point = values[index];
                while (hull.Count > lowerCount && Cross(
                           hull[hull.Count - 2].Plane,
                           hull[hull.Count - 1].Plane,
                           point.Plane) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull.Select(value => value.World).ToArray();
        }

        private static bool PointInsideConvexHull(
            Vector3 point,
            IReadOnlyList<Vector3> hull,
            Transform frame)
        {
            Vector2 p = new Vector2(
                Vector3.Dot(point - frame.position, frame.right),
                Vector3.Dot(point - frame.position, frame.forward));
            float sign = 0f;
            for (int index = 0; index < hull.Count; index++)
            {
                Vector3 firstWorld = hull[index];
                Vector3 secondWorld = hull[(index + 1) % hull.Count];
                Vector2 first = new Vector2(
                    Vector3.Dot(firstWorld - frame.position, frame.right),
                    Vector3.Dot(firstWorld - frame.position, frame.forward));
                Vector2 second = new Vector2(
                    Vector3.Dot(secondWorld - frame.position, frame.right),
                    Vector3.Dot(secondWorld - frame.position, frame.forward));
                float cross = Cross(first, second, p);
                if (Mathf.Abs(cross) <= 0.000001f) continue;
                if (Mathf.Abs(sign) <= 0.000001f) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != Mathf.Sign(sign)) return false;
            }
            return true;
        }

        private static float Cross(Vector2 origin, Vector2 first, Vector2 second)
        {
            Vector2 a = first - origin;
            Vector2 b = second - origin;
            return a.x * b.y - a.y * b.x;
        }

        private void OpenSourcePrefab()
        {
            if (definition == null || definition.SourceAvatar == null) return;
            string path = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            if (!string.IsNullOrWhiteSpace(path))
                PrefabStageUtility.OpenPrefab(path);
        }

        private void FrameCharacter()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage?.prefabContentsRoot == null) return;
            Renderer[] renderers = stage.prefabContentsRoot
                .GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            SceneView.lastActiveSceneView?.Frame(bounds, false);
        }

        private void UndoRedoPerformed()
        {
            statusMessage = null;
            Repaint();
            SceneView.RepaintAll();
        }

        private static void DrawMeters(string label, float value)
        {
            EditorGUILayout.LabelField(label, value.ToString("0.000") + " m");
        }

        private static void DrawCheck(
            string label,
            bool good,
            bool acceptable,
            string display)
        {
            GUIContent icon = EditorGUIUtility.IconContent(
                good ? "TestPassed" : acceptable ? "console.warnicon" : "console.erroricon");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(icon, GUILayout.Width(22f), GUILayout.Height(18f));
                EditorGUILayout.LabelField(label, display);
            }
        }

        private readonly struct FootprintSize
        {
            public float HalfWidth { get; }
            public float HalfLength { get; }

            public FootprintSize(float halfWidth, float halfLength)
            {
                HalfWidth = halfWidth;
                HalfLength = halfLength;
            }
        }

        private readonly struct HullPoint
        {
            public Vector3 World { get; }
            public Vector2 Plane { get; }

            public HullPoint(Vector3 world, Vector2 plane)
            {
                World = world;
                Plane = plane;
            }
        }

        private struct SceneData
        {
            public Vector3 GroundOrigin;
            public Vector3 Up;
            public Vector3 Right;
            public Vector3 Forward;
            public Vector3 LeftSole;
            public Vector3 RightSole;
            public Vector3 LeftForward;
            public Vector3 RightForward;
            public Vector3 CenterOfMass;
            public Vector3 CenterProjection;
            public Vector3 Pelvis;
            public Vector3 TunedPelvis;
            public Vector3[] SupportHull;
            public bool CenterInsideSupport;
            public float SoleMismatch;
        }
    }
}
