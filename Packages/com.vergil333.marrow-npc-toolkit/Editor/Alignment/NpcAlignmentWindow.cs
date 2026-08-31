using System;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Alignment
{
    public sealed class NpcAlignmentWindow : EditorWindow
    {
        private static readonly int SceneGuideControlHint =
            "Vergil333.MarrowNpcToolkit.AlignmentSceneGuide".GetHashCode();
        private static readonly Color SkeletonColor = new Color(0.15f, 0.85f, 1f, 0.9f);
        private static readonly Color AutoFitColor = new Color(0.2f, 0.55f, 1f, 0.85f);
        private static readonly Color ReviewedColor = new Color(0.2f, 1f, 0.45f, 0.9f);
        private static readonly Color SelectedColor = new Color(1f, 0.65f, 0.1f, 1f);

        [SerializeField] private NpcDefinition definition;
        [SerializeField] private HumanBodyBones selectedRole = HumanBodyBones.Hips;
        [SerializeField] private bool showAllColliders = true;
        [SerializeField] private bool showSkeleton = true;
        [SerializeField] private bool showSceneGuide = true;
        [SerializeField] private bool showQuickGuide = true;
        [SerializeField] private bool showFitChecklist = true;
        [SerializeField] private bool showAdvanced;
        [SerializeField] private Vector2 windowScroll;
        [SerializeField] private Vector2 scroll;

        private NpcRigMappingReport mapping;
        private NpcBaselineFitReport lastFit;
        private NpcPhysicsPreviewReport lastPreview;
        private string saveMessage;
        private string reviewMessage;

        public static void Open(NpcDefinition value)
        {
            NpcAlignmentWindow window = GetWindow<NpcAlignmentWindow>();
            window.titleContent = new GUIContent("NPC Physics Alignment");
            window.minSize = new Vector2(430f, 620f);
            window.definition = value;
            window.reviewMessage = null;
            window.RefreshMapping();
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
            if (definition != null) RefreshMapping();
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
                "NPC Definition", definition, typeof(NpcDefinition), false) as NpcDefinition;
            if (EditorGUI.EndChangeCheck())
            {
                lastFit = null;
                reviewMessage = null;
                RefreshMapping();
                SceneView.RepaintAll();
            }

            // Keep the definition picker visible while the rest of this long
            // workflow scrolls as one page. The role table retains its own
            // fixed-height scroller so choosing among all 16/17 roles does not
            // push the selected-role editor and finish controls farther down.
            windowScroll = EditorGUILayout.BeginScrollView(
                windowScroll,
                false,
                true,
                GUILayout.ExpandHeight(true));
            if (definition == null || definition.AnatomyProfile == null
                                   || definition.AvatarSourceProfile == null)
            {
                EditorGUILayout.HelpBox(
                    "Select an NPC Definition created by Import Avatar and Define NPC.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (selectedRole == HumanBodyBones.Jaw
                && !definition.IncludePhysicalJaw)
                selectedRole = HumanBodyBones.Hips;

            DrawEditingContext();
            DrawPurposeGuide();
            DrawStatus();
            DrawToolbar();
            EditorGUILayout.Space(4f);
            DrawRoleTable();
            EditorGUILayout.Space(6f);
            DrawSelectedRole();
            EditorGUILayout.Space(8f);
            DrawReviewAndFinish();
            EditorGUILayout.Space(6f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawPurposeGuide()
        {
            EditorGUILayout.HelpBox(
                "You are using the visible Avatar as a measuring reference. The colored shapes are the NPC's future invisible impact and ragdoll body. This workspace writes only to the Anatomy Profile; it does not edit AnimationRoot, the mesh, bones, or source prefab.",
                MessageType.Info);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showQuickGuide = EditorGUILayout.Foldout(
                showQuickGuide, "Quick guide: review one body part at a time", true);
            if (showQuickGuide)
            {
                EditorGUILayout.LabelField(
                    "1. Select a role below and click Focus Selected.\n"
                    + "2. Orbit the Scene view and compare the orange shape with that body part.\n"
                    + "3. If it looks sensible, click Looks Good - Review & Next.\n"
                    + "4. If it is clearly wrong, drag the orange move, rotate, or size handles first.\n"
                    + "5. Save the Anatomy Profile, then generate the Physics Preview.",
                    EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showFitChecklist = EditorGUILayout.Foldout(
                showFitChecklist, "What counts as a good fit?", true);
            if (showFitChecklist)
            {
                EditorGUILayout.LabelField(
                    "Good: centered on the solid body part, follows it in 3D, and ends near its joints. Small overlap between neighboring shapes is expected.\n"
                    + "Fix it: if it misses most of the body part, crosses into an unrelated limb, or sticks far outside the character.\n"
                    + "Do not chase the skin exactly. Ignore hair, loose clothing, individual finger/toe outlines, and other small details.",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Colors: cyan = Humanoid rig, blue = automatic fit, green = reviewed, orange = selected.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "Shape versus controls: the orange rounded capsule/sphere (or orange hand/foot/jaw box) is the physical body. The pale rectangular cage and white squares resize its bounds; large rings rotate it and arrows move it. Those controls are not collision volume.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawEditingContext()
        {
            NpcAnatomyProfile anatomy = definition.AnatomyProfile;
            string anatomyPath = AssetDatabase.GetAssetPath(anatomy);
            string sourcePath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool viewingSource = stage != null
                                 && string.Equals(
                                     stage.assetPath, sourcePath, StringComparison.Ordinal);
            bool dirty = EditorUtility.IsDirty(anatomy);

            EditorGUILayout.HelpBox(
                viewingSource
                    ? "You are viewing the source Avatar as a reference. The colored shapes are a non-destructive alignment overlay; adjustments are stored in the NPC Anatomy asset, not in the Avatar prefab."
                    : "The alignment overlay belongs on the source Avatar prefab. Click Open Avatar Prefab to see it. Open Generated Preview is a separate prefab containing AnimationRoot and Physics.",
                viewingSource ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.LabelField(
                "Colors: orange = selected, blue = auto-fit, green = manually reviewed, cyan = target rig.",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    dirty ? "Alignment changes: NOT SAVED" : "Alignment changes: saved",
                    dirty ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button("Save Alignment Changes", GUILayout.Width(190f)))
                    {
                        AssetDatabase.SaveAssetIfDirty(anatomy);
                        Repaint();
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(anatomyPath))
                EditorGUILayout.LabelField(
                    $"Saved in: {anatomyPath}", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        private void DrawStatus()
        {
            NpcAnatomyProfile anatomy = definition.AnatomyProfile;
            int expected = ActiveRoles().Length;
            int fitted = FittedActiveRoleCount();
            bool fittedBaseline = anatomy.HasFittedBaseline
                                  && (!definition.IncludePhysicalJaw
                                      || IsFittedJaw(anatomy.OptionalJaw));
            string status = fittedBaseline
                ? $"Physics baseline: {fitted}/{expected} fitted"
                : $"Physics baseline: {fitted}/{expected}";
            EditorGUILayout.HelpBox(status, fittedBaseline
                ? MessageType.Info
                : MessageType.Warning);

            if (anatomy.HasFittedBaseline
                && !string.Equals(
                    anatomy.BaselineToolkitVersion,
                    NpcToolkitVersion.Current,
                    StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox(
                    $"This automatic fit was made by toolkit {anatomy.BaselineToolkitVersion}. Click Create / Refresh Auto-Fit Baseline to apply the current {NpcToolkitVersion.Current} fitting rules. Reviewed manual roles will be kept.",
                    MessageType.Warning);
            }

            if (mapping == null) RefreshMapping();
            if (mapping != null)
            {
                foreach (NpcRigIssue issue in mapping.Issues.Where(value =>
                             value.Severity != NpcRigIssueSeverity.Info))
                {
                    EditorGUILayout.HelpBox(
                        issue.Message,
                        issue.Severity == NpcRigIssueSeverity.Error
                            ? MessageType.Error
                            : MessageType.Warning);
                }
            }

            if (lastFit != null)
            {
                if (lastFit.Success)
                    EditorGUILayout.HelpBox(
                        $"Auto-fit complete at {lastFit.EyeHeightMeters:0.000} m eye height. "
                        + $"Changed {lastFit.FittedRoleCount}; preserved {lastFit.PreservedReviewedRoleCount} reviewed role(s). "
                        + $"Fingerprint {lastFit.Fingerprint}.",
                        MessageType.Info);
                else
                    foreach (string issue in lastFit.Issues)
                        EditorGUILayout.HelpBox(issue, MessageType.Error);
            }
            if (lastPreview != null)
            {
                if (lastPreview.Success)
                    EditorGUILayout.HelpBox(
                        $"Unity preview generated: {lastPreview.RigidbodyCount} bodies, "
                        + $"{lastPreview.ColliderCount} primary colliders, {lastPreview.JointCount} joints. "
                        + $"Fingerprint {lastPreview.Fingerprint}.",
                        MessageType.Info);
                else
                    foreach (string issue in lastPreview.Issues)
                        EditorGUILayout.HelpBox(issue, MessageType.Error);
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Recheck Rig")) RefreshMapping();
                if (GUILayout.Button("Open Avatar Prefab")) OpenSourcePrefab();
                if (GUILayout.Button("Focus Selected")) FocusSelected();
            }

            bool ready = mapping != null && mapping.ReadyForBaseline && !mapping.SourceChanged;
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Create / Refresh Auto-Fit Baseline"))
                    RunFit(false);
                if (GUILayout.Button("Refit Everything (including reviewed roles)"))
                {
                    if (EditorUtility.DisplayDialog(
                            "Overwrite reviewed NPC alignment?",
                            "This replaces every manual collider alignment with a fresh Avatar-based fit. Undo remains available.",
                            "Refit Everything",
                            "Cancel"))
                        RunFit(true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                showSkeleton = EditorGUILayout.ToggleLeft("Target rig", showSkeleton);
                showAllColliders = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Show other bodies",
                        "Show the non-selected body colliders as depth-tested context. The selected orange body is always shown."),
                    showAllColliders);
                showSceneGuide = EditorGUILayout.ToggleLeft("Scene guide", showSceneGuide);
            }
        }

        private void DrawRoleTable()
        {
            HumanBodyBones[] roles = ActiveRoles();
            int expected = roles.Length;
            EditorGUILayout.LabelField(
                definition.IncludePhysicalJaw
                    ? "17-body alignment (Physical Jaw enabled)"
                    : "16-body alignment",
                EditorStyles.boldLabel);
            int reviewed = ReviewedActiveRoleCount();
            EditorGUILayout.LabelField(
                $"Reviewed {reviewed}/{expected}. Blue automatic fits are valid draft shapes; review all {expected} before a final native build.",
                EditorStyles.wordWrappedMiniLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(225f));
            foreach (HumanBodyBones role in roles)
            {
                NpcBodyRoleProfile value = ProfileForRole(role);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = role == selectedRole;
                    string roleLabel = ObjectNames.NicifyVariableName(role.ToString());
                    if (GUILayout.Toggle(selected, roleLabel, "Button", GUILayout.Width(180f))
                        && !selected)
                    {
                        selectedRole = role;
                        reviewMessage = null;
                        Repaint();
                        SceneView.RepaintAll();
                    }
                    GUILayout.Label(value == null ? "Missing" : StateLabel(value.AlignmentState),
                        EditorStyles.miniLabel, GUILayout.Width(75f));
                    string path = role == HumanBodyBones.Jaw
                        ? definition.AvatarSourceProfile.JawPath ?? ""
                        : definition.AvatarSourceProfile.HumanoidBones
                            .Where(binding => binding.Role == role)
                            .Select(binding => binding.TransformPath)
                            .FirstOrDefault() ?? "";
                    GUILayout.Label(path, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSelectedRole()
        {
            NpcBodyRoleProfile value = ProfileForRole(selectedRole);
            if (value == null) return;

            EditorGUILayout.LabelField(
                ObjectNames.NicifyVariableName(selectedRole.ToString()),
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Only the selected role is editable. Manual changes mark it Reviewed and are preserved by normal refits.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox(FitTip(selectedRole), MessageType.None);

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle("Enabled", value.Enabled);
            bool autoFit = EditorGUILayout.Toggle("Allow Auto-Fit", value.AutoFitCollider);
            NpcColliderShape shape;
            using (new EditorGUI.DisabledScope(selectedRole == HumanBodyBones.Jaw))
                shape = (NpcColliderShape)EditorGUILayout.EnumPopup(
                    new GUIContent(
                        "Collider Shape",
                        selectedRole == HumanBodyBones.Jaw
                            ? "A Physical Jaw uses one box fitted to the vertices primarily weighted to the Jaw bone."
                            : "Capsule and Sphere are round. Box is the only shape with independently adjustable X, Y, and Z dimensions."),
                    selectedRole == HumanBodyBones.Jaw
                        ? NpcColliderShape.Box
                        : value.ColliderShape);
            Vector3 center = EditorGUILayout.Vector3Field("Local Center", value.ColliderCenter);
            Vector3 euler = EditorGUILayout.Vector3Field(
                "Local Rotation", value.ColliderLocalRotation.eulerAngles);
            Vector3 size = value.ColliderSize;
            float radius = value.CapsuleRadius;
            float height = value.CapsuleHeight;
            if (shape == NpcColliderShape.Box)
                size = EditorGUILayout.Vector3Field("Size (X / Y / Z)", size);
            else if (shape == NpcColliderShape.Sphere)
                radius = EditorGUILayout.FloatField("Radius (all directions)", radius);
            else
            {
                radius = EditorGUILayout.FloatField("Radius (width + depth)", radius);
                height = EditorGUILayout.FloatField("Height (long axis)", height);
            }
            EditorGUILayout.HelpBox(
                ColliderShapeTip(selectedRole, shape, size, radius, height),
                MessageType.None);

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Body and joint tuning", true);
            float mass = value.MassKilograms;
            Vector3 jointAxis = value.JointAxis;
            Vector3 secondary = value.JointSecondaryAxis;
            float maxForce = value.JointDriveMaxForce;
            if (showAdvanced)
            {
                mass = EditorGUILayout.FloatField("Mass (kg)", mass);
                jointAxis = EditorGUILayout.Vector3Field("Joint Axis", jointAxis);
                secondary = EditorGUILayout.Vector3Field("Secondary Axis", secondary);
                maxForce = EditorGUILayout.FloatField("Drive Max Force", maxForce);
                EditorGUILayout.LabelField(
                    $"Motion: {value.AngularXMotion} / {value.AngularYMotion} / {value.AngularZMotion}",
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"Limits: {value.AngularLowLimits} to {value.AngularHighLimits}",
                    EditorStyles.miniLabel);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RegisterCompleteObjectUndo(
                    definition.AnatomyProfile, "Edit NPC anatomy role");
                value.Enabled = enabled;
                value.AutoFitCollider = autoFit;
                value.ColliderShape = shape;
                value.ColliderCenter = center;
                value.ColliderLocalRotation = Quaternion.Euler(euler);
                value.ColliderSize = Positive(size);
                value.CapsuleRadius = Mathf.Max(0.005f, radius);
                value.CapsuleHeight = Mathf.Max(value.CapsuleRadius * 2f, height);
                value.MassKilograms = Mathf.Max(0.001f, mass);
                value.JointAxis = SafeDirection(jointAxis, Vector3.right);
                value.JointSecondaryAxis = SafeDirection(secondary, Vector3.up);
                value.JointDriveMaxForce = Mathf.Max(0f, maxForce);
                value.AlignmentState = NpcAlignmentState.Reviewed;
                EditorUtility.SetDirty(definition.AnatomyProfile);
                saveMessage = null;
                SceneView.RepaintAll();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string reviewButtonLabel = value.AlignmentState == NpcAlignmentState.Reviewed
                    ? "Go to Next Unreviewed"
                    : "Looks Good - Review & Next";
                if (GUILayout.Button(reviewButtonLabel))
                    ReviewSelectedAndAdvance(value);
                if (GUILayout.Button(new GUIContent(
                        "Reset Selected to Auto-Fit",
                        "Restore only this body part to the toolkit's Avatar-based fit.")))
                {
                    Undo.RegisterCompleteObjectUndo(
                        definition.AnatomyProfile, "Return NPC role to auto-fit");
                    value.AlignmentState = NpcAlignmentState.AutoFit;
                    value.AutoFitCollider = true;
                    EditorUtility.SetDirty(definition.AnatomyProfile);
                    RunFit(false);
                }
            }
            DrawMirrorSelectedRole(value);
            if (!string.IsNullOrWhiteSpace(reviewMessage))
                EditorGUILayout.HelpBox(reviewMessage, MessageType.Info);
        }

        private void DrawMirrorSelectedRole(NpcBodyRoleProfile source)
        {
            bool hasOpposite = NpcHumanoidGraph.TryGetOppositeSide(
                selectedRole, out HumanBodyBones targetRole);
            string sourceLabel = ObjectNames.NicifyVariableName(selectedRole.ToString());
            string targetLabel = hasOpposite
                ? ObjectNames.NicifyVariableName(targetRole.ToString())
                : "Opposite Side";
            string unavailableReason = string.Empty;
            Transform avatarRoot = null;
            Transform sourceBone = null;
            Transform targetBone = null;
            NpcBodyRoleProfile target = null;

            if (!hasOpposite)
            {
                unavailableReason =
                    selectedRole == HumanBodyBones.Jaw
                        ? "Jaw is one centerline body and cannot be mirrored. Fit and review it directly."
                        : "Centerline roles do not have an opposite side to mirror to.";
            }
            else
            {
                Transform sourcePrefabRoot = ResolveSourcePrefabRoot();
                avatarRoot = ResolveAvatarSpaceRoot(sourcePrefabRoot);
                sourceBone = sourcePrefabRoot == null
                    ? null
                    : ResolveRoleTransform(sourcePrefabRoot, selectedRole);
                targetBone = sourcePrefabRoot == null
                    ? null
                    : ResolveRoleTransform(sourcePrefabRoot, targetRole);
                target = ProfileForRole(targetRole);
                if (avatarRoot == null || sourceBone == null || targetBone == null
                                       || target == null)
                    unavailableReason =
                        "Open the source Avatar prefab and recheck its paired Humanoid bones.";
                else if (source.AlignmentState == NpcAlignmentState.Unseeded)
                    unavailableReason =
                        "Create the automatic fit or finish this side before mirroring it.";
            }

            string buttonText = hasOpposite
                ? $"Mirror {sourceLabel}  ->  {targetLabel}"
                : "Mirror Selected to Opposite Side";
            string tooltip = hasOpposite
                ? $"Replace only {targetLabel}'s collider shape, size, position, and rotation with a root-space mirror of {sourceLabel}. Target body and joint tuning stays unchanged."
                : unavailableReason;
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(unavailableReason)))
            {
                if (GUILayout.Button(new GUIContent(buttonText, tooltip)))
                    ConfirmAndMirrorSelected(
                        avatarRoot,
                        sourceBone,
                        targetBone,
                        source,
                        target,
                        targetRole,
                        sourceLabel,
                        targetLabel);
            }
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(unavailableReason)
                    ? $"Copies collider alignment only and marks {targetLabel} Reviewed. Its mass, joint, drive, muscle, Enabled, and Allow Auto-Fit settings stay unchanged."
                    : unavailableReason,
                EditorStyles.wordWrappedMiniLabel);
        }

        private void ConfirmAndMirrorSelected(
            Transform avatarRoot,
            Transform sourceBone,
            Transform targetBone,
            NpcBodyRoleProfile source,
            NpcBodyRoleProfile target,
            HumanBodyBones targetRole,
            string sourceLabel,
            string targetLabel)
        {
            string reviewedWarning = target.AlignmentState == NpcAlignmentState.Reviewed
                ? $"\n\n{targetLabel} is already reviewed; its current collider alignment will be replaced."
                : string.Empty;
            if (!EditorUtility.DisplayDialog(
                    $"Mirror {sourceLabel} to {targetLabel}?",
                    $"This mirrors {sourceLabel}'s collider through the Avatar's center plane and replaces only {targetLabel}'s collider alignment. {targetLabel} will be marked Reviewed.{reviewedWarning}\n\n{targetLabel}'s body, joint, drive, muscle, Enabled, and Allow Auto-Fit settings remain unchanged. Undo is available.",
                    $"Mirror to {targetLabel}",
                    "Cancel"))
                return;

            Undo.RegisterCompleteObjectUndo(
                definition.AnatomyProfile,
                $"Mirror {sourceLabel} collider to {targetLabel}");
            if (!NpcAlignmentMirrorService.TryMirrorCollider(
                    avatarRoot,
                    sourceBone,
                    targetBone,
                    source,
                    target,
                    out string error))
            {
                reviewMessage = "Mirror was not applied: " + error;
                Repaint();
                return;
            }

            EditorUtility.SetDirty(definition.AnatomyProfile);
            saveMessage = null;
            selectedRole = targetRole;
            reviewMessage =
                $"Mirrored {sourceLabel} -> {targetLabel} in Avatar space. "
                + $"Now reviewing {targetLabel}; its body and joint tuning was preserved.";
            Repaint();
            SceneView.RepaintAll();
            EditorApplication.delayCall += FocusSelectedAfterGui;
        }

        private void DrawReviewAndFinish()
        {
            NpcAnatomyProfile anatomy = definition.AnatomyProfile;
            int expected = ActiveRoles().Length;
            int reviewed = ReviewedActiveRoleCount();
            bool dirty = EditorUtility.IsDirty(anatomy);
            bool baselineReady = mapping != null
                                 && mapping.ReadyForBaseline
                                 && !mapping.SourceChanged
                                 && anatomy.BaselineMatches(
                                     mapping.CurrentSourceDependencyHash)
                                 && (!definition.IncludePhysicalJaw
                                     || (ResolveRoleTransform(
                                             ResolveSourcePrefabRoot(),
                                             HumanBodyBones.Jaw) != null
                                         && IsFittedJaw(anatomy.OptionalJaw)));

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Finish Step 3", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                reviewed == expected
                    ? $"All {expected} shapes have been visually accepted."
                    : $"{reviewed}/{expected} shapes reviewed. You may generate a draft preview now, but review all {expected} before a final native build.",
                EditorStyles.wordWrappedLabel);

            if (definition.IncludePhysicalJaw
                && !IsFittedJaw(anatomy.OptionalJaw))
                EditorGUILayout.HelpBox(
                    "Physical Jaw is requested but its box is not fitted and enabled. Run the automatic fit, select Jaw, and review it before generating this 17-body preview.",
                    MessageType.Error);

            if (dirty)
                EditorGUILayout.HelpBox(
                    "The Anatomy Profile has unsaved changes. You do not need to save the source Avatar prefab.",
                    MessageType.Warning);
            else if (!string.IsNullOrWhiteSpace(saveMessage))
                EditorGUILayout.HelpBox(saveMessage, MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!dirty))
                {
                    if (GUILayout.Button("Save Alignment Profile"))
                        SaveAlignment();
                }
                using (new EditorGUI.DisabledScope(!baselineReady))
                {
                    if (GUILayout.Button("Save & Generate Physics Preview"))
                    {
                        SaveAlignment();
                        GeneratePreview();
                    }
                }
            }

            string previewPath = NpcPhysicsPreviewBuilder.GetOutputPath(definition);
            GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
            using (new EditorGUI.DisabledScope(preview == null))
            {
                if (GUILayout.Button("Open Generated Preview"))
                    AssetDatabase.OpenAsset(preview);
            }
            string previewDetail = null;
            bool previewCurrent = preview != null
                                  && NpcPhysicsPreviewBuilder.ReceiptMatches(
                                      definition, previewPath, out previewDetail);
            if (previewCurrent)
            {
                EditorGUILayout.HelpBox(
                    "Physics Preview is current. Next, run the read-only Step 4 readiness check.",
                    MessageType.Info);
                if (GUILayout.Button("Continue to Step 4 - Check NPC Readiness"))
                    NpcToolkitWindow.OpenForReadiness(definition);
            }
            else if (preview != null && !string.IsNullOrWhiteSpace(previewDetail))
            {
                EditorGUILayout.HelpBox(previewDetail, MessageType.Warning);
            }
            EditorGUILayout.LabelField(
                $"In that preview, AnimationRoot is the visible/animated Avatar. Physics is the separate {expected}-body hierarchy that will later be driven as a ragdoll.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private static string FitTip(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                    return "Hand check: the box represents the whole hand as one solid collision body. Its longest direction should run from wrist toward the fingers, its width across the knuckles, and its thinnest direction through the palm. It may enclose the fingers as one simple envelope; do not fit each finger separately. Orbit the view: screen-horizontal or screen-vertical does not matter; the 3D direction does.";

                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                    return "Foot check: the box should run from heel toward the toes and stay centered around the solid foot/sole. It should not stand upright along the shin or try to include every toe.";

                case HumanBodyBones.Head:
                    return "Head check: center the shape on the solid skull and face core. Ignore hair, ribbons, ears, and other decorative parts; the neck shape may overlap slightly at the base.";

                case HumanBodyBones.Jaw:
                    return "Jaw check: this box represents the movable Jaw bone's weighted lower-face envelope, not a literal jawbone outline or the whole head. It is fitted from mesh vertices whose summed Jaw weight is at least 50%, so it may include the chin, mouth, lower cheeks, and reach near the nose. Slight overlap with Head is normal; do not shrink it to the chin alone. Leave the eyes, forehead, skull, hair, and neck to Head. The hinge follows the Avatar's left-right axis; use Reset Selected to Auto-Fit if the box or hinge frame becomes confusing.";

                case HumanBodyBones.Hips:
                    return "Hips check: cover the solid inner pelvis around the hip joints and sacrum; it does not need to touch every edge of the buttocks, clothing, or outer silhouette. The automatic Hips shape is normally a short Capsule. When Height equals its diameter it correctly looks spherical. Radius changes width and depth together; Height makes the capsule longer along its axis. End around the hip joints, not down the thighs. Small silhouette underfill and overlap with spine or upper-leg shapes are normal.";

                case HumanBodyBones.Chest:
                    return "Chest check: the orange body is one complete capsule, not a half-sphere. Cover the solid upper ribcage/sternum core from above the Spine toward the base of the neck. Ignore breasts, shoulders, arms, hair, loose clothing, and accessories. Slight overlap with the Spine and Head shapes is normal.";

                case HumanBodyBones.Spine:
                    return "Torso check: cover the solid trunk and follow the centerline. Do not widen it to include arms, hair, loose clothing, or accessories. Neighboring torso shapes may overlap slightly.";

                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                    return "Limb check: the capsule should follow the bone from one joint toward the next, stay centered inside the limb, and end close to the joints. Modest overlap at elbows, knees, shoulders, or hips is expected.";

                default:
                    return "Good fit: centered on the solid body part, follows it in 3D, and ends near neighboring joints without reaching far outside the character.";
            }
        }

        private static string ColliderShapeTip(
            HumanBodyBones role,
            NpcColliderShape shape,
            Vector3 size,
            float radius,
            float height)
        {
            if (shape == NpcColliderShape.Box)
            {
                string boxTip =
                    $"Box: {Mathf.Abs(size.x) * 100f:0.0} x {Mathf.Abs(size.y) * 100f:0.0} x {Mathf.Abs(size.z) * 100f:0.0} cm. X, Y, and Z can be resized independently with the Size fields or the white cage handles.";
                return role == HumanBodyBones.Hips
                    ? boxTip + " This is an advanced custom Hips shape; Reset Selected to Auto-Fit restores the normal round Hips core."
                    : boxTip;
            }

            float diameter = Mathf.Max(0.01f, radius * 2f);
            if (shape == NpcColliderShape.Sphere)
            {
                return $"Sphere: {diameter * 100f:0.0} cm across in every direction. Radius resizes all axes together; a Sphere cannot become pill-shaped. Choose Capsule only when a longer round body is intentional.";
            }

            float safeHeight = Mathf.Max(diameter, height);
            bool sphereLike = safeHeight <= diameter + 0.0005f;
            string capsuleState = sphereLike
                ? $"Short Capsule: {diameter * 100f:0.0} cm diameter x {safeHeight * 100f:0.0} cm height. Height equals diameter, so it correctly looks like a sphere."
                : $"Capsule: {diameter * 100f:0.0} cm diameter x {safeHeight * 100f:0.0} cm height.";
            string controls =
                " Radius changes width and depth together; Height lengthens only the round body's long axis. A Capsule cannot be oval from front to side.";
            if (role != HumanBodyBones.Hips)
                return capsuleState + controls;

            return capsuleState + controls
                   + " For Hips, judge the solid inner pelvis in both views, not the full buttock or clothing silhouette. Do not switch shape merely to touch every visible edge.";
        }

        private void ReviewSelectedAndAdvance(NpcBodyRoleProfile value)
        {
            if (value == null || definition == null || definition.AnatomyProfile == null)
                return;

            HumanBodyBones acceptedRole = selectedRole;
            if (value.AlignmentState != NpcAlignmentState.Reviewed)
            {
                Undo.RegisterCompleteObjectUndo(
                    definition.AnatomyProfile, "Review NPC anatomy role");
                value.AlignmentState = NpcAlignmentState.Reviewed;
                EditorUtility.SetDirty(definition.AnatomyProfile);
                saveMessage = null;
            }

            HumanBodyBones[] roles = ActiveRoles();
            int currentIndex = Array.IndexOf(roles, selectedRole);
            bool advanced = false;
            for (int offset = 1; offset < roles.Length; offset++)
            {
                HumanBodyBones candidate = roles[(currentIndex + offset) % roles.Length];
                NpcBodyRoleProfile candidateProfile = ProfileForRole(candidate);
                if (candidateProfile == null
                    || candidateProfile.AlignmentState == NpcAlignmentState.Reviewed)
                    continue;
                selectedRole = candidate;
                advanced = true;
                break;
            }

            int reviewed = ReviewedActiveRoleCount();
            int expected = roles.Length;
            string acceptedLabel = ObjectNames.NicifyVariableName(acceptedRole.ToString());
            reviewMessage = advanced
                ? $"Accepted {acceptedLabel}. Now reviewing "
                  + $"{ObjectNames.NicifyVariableName(selectedRole.ToString())}. "
                  + $"Reviewed {reviewed}/{expected}."
                : $"Accepted {acceptedLabel}. All {reviewed}/{expected} shapes are reviewed.";

            Repaint();
            SceneView.RepaintAll();
            if (advanced)
                EditorApplication.delayCall += FocusSelectedAfterGui;
        }

        private void FocusSelectedAfterGui()
        {
            if (this == null || definition == null) return;
            FocusSelected();
            Repaint();
            SceneView.RepaintAll();
        }

        private void SaveAlignment()
        {
            if (definition == null || definition.AnatomyProfile == null) return;
            AssetDatabase.SaveAssetIfDirty(definition.AnatomyProfile);
            saveMessage =
                "Alignment saved in the Anatomy Profile. The source Avatar prefab was not modified.";
            Repaint();
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            if (definition == null || definition.AnatomyProfile == null
                                   || definition.AvatarSourceProfile == null)
                return;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || stage.prefabContentsRoot == null) return;
            string expectedPath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            if (!string.Equals(stage.assetPath, expectedPath, StringComparison.Ordinal)) return;

            bool pointerOverGuide = IsPointerOverSceneGuide(sceneView);
            ReserveSceneGuideInput(sceneView, pointerOverGuide);
            // Process the screen-space guide before any world-space handles.
            // Its buttons must receive and consume MouseDown first.
            DrawSceneGuide(sceneView);
            Transform root = stage.prefabContentsRoot.transform;
            Color oldColor = Handles.color;
            Matrix4x4 oldMatrix = Handles.matrix;
            CompareFunction oldZTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.LessEqual;
                if (showSkeleton) DrawSkeleton(root);

                // Draw contextual bodies normally, allowing the Avatar mesh to
                // hide their rear surfaces and keep the view readable.
                if (showAllColliders)
                {
                    foreach (HumanBodyBones role in ActiveRoles())
                    {
                        if (role == selectedRole) continue;
                        Transform bone = ResolveRoleTransform(root, role);
                        NpcBodyRoleProfile profile = ProfileForRole(role);
                        if (bone == null || profile == null || !profile.Enabled) continue;
                        DrawCollider(bone, profile, false, false);
                    }
                }

                // Draw the selected body last and through the Avatar. A full
                // capsule otherwise looks like a half-sphere when the mesh
                // depth buffer hides its far arcs.
                Transform selectedBone = ResolveRoleTransform(root, selectedRole);
                NpcBodyRoleProfile selectedProfile =
                    ProfileForRole(selectedRole);
                if (selectedBone != null && selectedProfile != null && selectedProfile.Enabled)
                {
                    Handles.zTest = CompareFunction.Always;
                    DrawCollider(
                        selectedBone,
                        selectedProfile,
                        true,
                        !pointerOverGuide);
                }
            }
            finally
            {
                Handles.color = oldColor;
                Handles.matrix = oldMatrix;
                Handles.zTest = oldZTest;
            }

        }

        private void DrawSceneGuide(SceneView sceneView)
        {
            if (!TryGetSceneGuideRect(sceneView, out Rect area)) return;

            NpcBodyRoleProfile profile = ProfileForRole(selectedRole);
            Handles.BeginGUI();
            try
            {
                GUILayout.BeginArea(area, GUIContent.none, EditorStyles.helpBox);
                GUILayout.Label(
                    "Reviewing the Source Avatar",
                    EditorStyles.boldLabel);
                GUILayout.Label(
                    "Editing " + ObjectNames.NicifyVariableName(selectedRole.ToString()) + ": "
                    + SceneFitTip(selectedRole),
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(
                    "The selected collider is shown through the Avatar. Use the Scene handles only if its fit needs correction.",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(
                        profile == null ? "Missing" : StateLabel(profile.AlignmentState),
                        EditorStyles.miniLabel,
                        GUILayout.Width(65f));
                    using (new EditorGUI.DisabledScope(profile == null))
                    {
                        string reviewButtonLabel = profile != null
                                                   && profile.AlignmentState
                                                   == NpcAlignmentState.Reviewed
                            ? "Next Unreviewed"
                            : "Looks Good - Next";
                        if (GUILayout.Button(reviewButtonLabel))
                            ReviewSelectedAndAdvance(profile);
                    }
                    bool dirty = EditorUtility.IsDirty(definition.AnatomyProfile);
                    using (new EditorGUI.DisabledScope(!dirty))
                    {
                        if (GUILayout.Button("Save", GUILayout.Width(70f)))
                            SaveAlignment();
                    }
                }
                if (!string.IsNullOrWhiteSpace(reviewMessage))
                    GUILayout.Label(reviewMessage, EditorStyles.wordWrappedMiniLabel);
                GUILayout.EndArea();
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        private void ReserveSceneGuideInput(SceneView sceneView, bool pointerOverGuide)
        {
            if (!TryGetSceneGuideRect(sceneView, out _)) return;

            // Keep Scene selection and other default Scene controls out of the
            // guide rectangle. Interactive anatomy handles are also skipped
            // there, and the guide itself is processed before them.
            int controlId = GUIUtility.GetControlID(
                SceneGuideControlHint, FocusType.Passive);
            if (Event.current != null
                && Event.current.type == EventType.Layout
                && pointerOverGuide)
                HandleUtility.AddDefaultControl(controlId);
        }

        private bool IsPointerOverSceneGuide(SceneView sceneView)
        {
            return Event.current != null
                   && TryGetSceneGuideRect(sceneView, out Rect area)
                   && area.Contains(Event.current.mousePosition);
        }

        private bool TryGetSceneGuideRect(SceneView sceneView, out Rect area)
        {
            area = default;
            if (!showSceneGuide || sceneView == null || sceneView.position.width < 300f)
                return false;

            float width = Mathf.Min(430f, sceneView.position.width - 24f);
            float height = string.IsNullOrWhiteSpace(reviewMessage) ? 154f : 180f;
            area = new Rect(12f, 12f, width, height);
            return true;
        }

        private static string SceneFitTip(HumanBodyBones role)
        {
            switch (role)
            {
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                    return "Long axis wrist-to-fingers; wide across knuckles; thin through the palm. Screen vertical/horizontal does not matter.";
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                    return "Run heel-to-toe and stay centered on the solid foot, not upright along the shin.";
                case HumanBodyBones.Head:
                    return "Cover the skull and face core; ignore hair and accessories.";
                case HumanBodyBones.Jaw:
                    return "Cover the Jaw-weighted chin, mouth, and lower cheeks. Reaching near the nose and slight Head overlap can be normal; do not shrink it to the chin alone.";
                case HumanBodyBones.Hips:
                    return "Fit the solid inner pelvis, not the full buttock/clothing silhouette. This is normally a short Capsule: Radius changes width and depth together; Height makes a vertical pill. Sphere-like is valid when Height equals diameter.";
                case HumanBodyBones.Chest:
                    return "This is one complete capsule, shown through the Avatar. Cover the upper ribcage/sternum core toward the base of the neck; ignore breasts and shoulders.";
                case HumanBodyBones.Spine:
                    return "Cover the solid body core; ignore loose clothing and nearby limbs.";
                default:
                    return "Follow the limb joint-to-joint; modest overlap with neighboring shapes is expected.";
            }
        }

        private void DrawSkeleton(Transform root)
        {
            Handles.matrix = Matrix4x4.identity;
            Handles.color = SkeletonColor;
            foreach (HumanBodyBones role in ActiveRoles())
            {
                Transform bone = ResolveRoleTransform(root, role);
                if (bone == null) continue;
                float size = HandleUtility.GetHandleSize(bone.position) * 0.025f;
                Handles.SphereHandleCap(0, bone.position, Quaternion.identity, size, EventType.Repaint);
                if (TryGetParent(role, out HumanBodyBones parent))
                {
                    Transform parentBone = ResolveRoleTransform(root, parent);
                    if (parentBone != null) Handles.DrawAAPolyLine(3f, parentBone.position, bone.position);
                }
            }
        }

        private void DrawCollider(
            Transform bone,
            NpcBodyRoleProfile profile,
            bool selected,
            bool interactive)
        {
            Handles.color = selected
                ? SelectedColor
                : profile.AlignmentState == NpcAlignmentState.Reviewed
                    ? ReviewedColor
                    : AutoFitColor;
            Handles.matrix = bone.localToWorldMatrix
                             * Matrix4x4.TRS(
                                 profile.ColliderCenter,
                                 profile.ColliderLocalRotation,
                                 Vector3.one);
            DrawWireShape(profile);
            if (!selected || !interactive) return;

            Handles.matrix = bone.localToWorldMatrix;
            EditorGUI.BeginChangeCheck();
            Vector3 center = Handles.PositionHandle(
                profile.ColliderCenter, profile.ColliderLocalRotation);
            Quaternion rotation = Handles.RotationHandle(
                profile.ColliderLocalRotation, center);
            if (EditorGUI.EndChangeCheck())
            {
                RecordSceneEdit(profile);
                profile.ColliderCenter = center;
                profile.ColliderLocalRotation = rotation;
            }

            Vector3 boundsSize = BoundsSize(profile);
            var bounds = new BoxBoundsHandle
            {
                center = Vector3.zero,
                size = boundsSize,
                wireframeColor = new Color(1f, 1f, 1f, 0.45f),
                handleColor = Color.white,
            };
            Handles.matrix = bone.localToWorldMatrix
                             * Matrix4x4.TRS(
                                 profile.ColliderCenter,
                                 profile.ColliderLocalRotation,
                                 Vector3.one);
            EditorGUI.BeginChangeCheck();
            bounds.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                RecordSceneEdit(profile);
                profile.ColliderCenter += profile.ColliderLocalRotation * bounds.center;
                ApplyBoundsSize(profile, bounds.size);
            }
        }

        private static void DrawWireShape(NpcBodyRoleProfile profile)
        {
            if (profile.ColliderShape == NpcColliderShape.Box)
            {
                Handles.DrawWireCube(Vector3.zero, profile.ColliderSize);
                return;
            }
            if (profile.ColliderShape == NpcColliderShape.Sphere)
            {
                DrawWireSphere(profile.CapsuleRadius);
                return;
            }

            float radius = profile.CapsuleRadius;
            float halfCylinder = Mathf.Max(0f, profile.CapsuleHeight * 0.5f - radius);
            Vector3 top = Vector3.up * halfCylinder;
            Vector3 bottom = Vector3.down * halfCylinder;
            Handles.DrawWireDisc(top, Vector3.up, radius);
            Handles.DrawWireDisc(bottom, Vector3.up, radius);
            // Positive arcs must bow away from the cylinder. Reversing these
            // start vectors folds both caps inward and makes a full capsule
            // look like a half-sphere or lens in side/front views.
            Handles.DrawWireArc(top, Vector3.forward, Vector3.right, 180f, radius);
            Handles.DrawWireArc(bottom, Vector3.forward, Vector3.left, 180f, radius);
            Handles.DrawWireArc(top, Vector3.right, Vector3.back, 180f, radius);
            Handles.DrawWireArc(bottom, Vector3.right, Vector3.forward, 180f, radius);
            Handles.DrawLine(top + Vector3.right * radius, bottom + Vector3.right * radius);
            Handles.DrawLine(top - Vector3.right * radius, bottom - Vector3.right * radius);
            Handles.DrawLine(top + Vector3.forward * radius, bottom + Vector3.forward * radius);
            Handles.DrawLine(top - Vector3.forward * radius, bottom - Vector3.forward * radius);
        }

        private static void DrawWireSphere(float radius)
        {
            Handles.DrawWireDisc(Vector3.zero, Vector3.right, radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.up, radius);
            Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);
        }

        private void RecordSceneEdit(NpcBodyRoleProfile profile)
        {
            Undo.RegisterCompleteObjectUndo(
                definition.AnatomyProfile, "Align NPC collider");
            profile.AlignmentState = NpcAlignmentState.Reviewed;
            EditorUtility.SetDirty(definition.AnatomyProfile);
            Repaint();
        }

        private static Vector3 BoundsSize(NpcBodyRoleProfile profile)
        {
            if (profile.ColliderShape == NpcColliderShape.Box)
                return Positive(profile.ColliderSize);
            if (profile.ColliderShape == NpcColliderShape.Sphere)
            {
                float diameter = Mathf.Max(0.01f, profile.CapsuleRadius * 2f);
                return Vector3.one * diameter;
            }
            float capsuleDiameter = Mathf.Max(0.01f, profile.CapsuleRadius * 2f);
            return new Vector3(
                capsuleDiameter,
                Mathf.Max(capsuleDiameter, profile.CapsuleHeight),
                capsuleDiameter);
        }

        private static void ApplyBoundsSize(NpcBodyRoleProfile profile, Vector3 value)
        {
            value = Positive(new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z)));
            if (profile.ColliderShape == NpcColliderShape.Box)
            {
                profile.ColliderSize = value;
                return;
            }
            if (profile.ColliderShape == NpcColliderShape.Sphere)
            {
                profile.CapsuleRadius = Mathf.Max(value.x, value.y, value.z) * 0.5f;
                return;
            }
            profile.CapsuleRadius = Mathf.Max(value.x, value.z) * 0.5f;
            profile.CapsuleHeight = Mathf.Max(value.y, profile.CapsuleRadius * 2f);
            profile.ColliderSize = new Vector3(
                profile.CapsuleRadius * 2f,
                profile.CapsuleHeight,
                profile.CapsuleRadius * 2f);
        }

        private Transform ResolveRoleTransform(Transform root, HumanBodyBones role)
        {
            if (root == null || definition?.AvatarSourceProfile == null) return null;
            string path = role == HumanBodyBones.Jaw
                ? definition.AvatarSourceProfile.JawPath
                : definition.AvatarSourceProfile.HumanoidBones
                    .Where(value => value.Role == role)
                    .Select(value => value.TransformPath)
                    .FirstOrDefault();
            return string.IsNullOrWhiteSpace(path) ? null : root.Find(path);
        }

        private Transform ResolveSourcePrefabRoot()
        {
            if (definition == null || definition.SourceAvatar == null) return null;
            string sourcePath = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null
                && stage.prefabContentsRoot != null
                && string.Equals(stage.assetPath, sourcePath, StringComparison.Ordinal))
                return stage.prefabContentsRoot.transform;

            // The prefab asset is a read-only transform reference here; mirror
            // results are written only to the separate Anatomy Profile.
            return definition.SourceAvatar.transform;
        }

        private Transform ResolveAvatarSpaceRoot(Transform sourcePrefabRoot)
        {
            if (sourcePrefabRoot == null || definition?.AvatarSourceProfile == null)
                return null;
            string path = definition.AvatarSourceProfile.AnimatorPath;
            if (string.IsNullOrWhiteSpace(path)) return sourcePrefabRoot;
            return sourcePrefabRoot.Find(path);
        }

        private void OpenSourcePrefab()
        {
            if (definition == null || definition.SourceAvatar == null) return;
            string path = AssetDatabase.GetAssetPath(definition.SourceAvatar);
            if (!string.IsNullOrWhiteSpace(path)) PrefabStageUtility.OpenPrefab(path);
        }

        private void FocusSelected()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null || definition == null) return;
            Transform bone = ResolveRoleTransform(stage.prefabContentsRoot.transform, selectedRole);
            if (bone == null) return;
            NpcBodyRoleProfile profile = ProfileForRole(selectedRole);
            Vector3 size = profile == null ? Vector3.one * 0.2f : BoundsSize(profile);
            SceneView.lastActiveSceneView?.Frame(
                new Bounds(bone.TransformPoint(profile?.ColliderCenter ?? Vector3.zero), size * 2f),
                false);
        }

        private void RunFit(bool overwriteReviewed)
        {
            lastFit = NpcBaselineFitter.Fit(definition, overwriteReviewed);
            if (lastFit.Success) AssetDatabase.SaveAssets();
            RefreshMapping();
            Repaint();
            SceneView.RepaintAll();
        }

        private void GeneratePreview()
        {
            lastPreview = NpcPhysicsPreviewBuilder.Build(definition);
            if (lastPreview.Success)
            {
                GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(
                    lastPreview.AssetPath);
                Selection.activeObject = preview;
                EditorGUIUtility.PingObject(preview);
            }
            Repaint();
        }

        private void RefreshMapping()
        {
            mapping = definition == null ? null : NpcRigMappingService.Validate(definition);
            Repaint();
        }

        private void UndoRedoPerformed()
        {
            reviewMessage = null;
            saveMessage = null;
            Repaint();
            SceneView.RepaintAll();
        }

        private HumanBodyBones[] ActiveRoles()
        {
            return definition != null && definition.IncludePhysicalJaw
                ? NpcHumanoidGraph.CanonicalRoles
                    .Concat(new[] { HumanBodyBones.Jaw })
                    .ToArray()
                : NpcHumanoidGraph.CanonicalRoles;
        }

        private NpcBodyRoleProfile ProfileForRole(HumanBodyBones role)
        {
            if (definition?.AnatomyProfile == null) return null;
            return role == HumanBodyBones.Jaw
                ? definition.AnatomyProfile.OptionalJaw
                : definition.AnatomyProfile.FindRole(role);
        }

        private int ReviewedActiveRoleCount()
        {
            return ActiveRoles().Count(role =>
                ProfileForRole(role)?.AlignmentState == NpcAlignmentState.Reviewed);
        }

        private int FittedActiveRoleCount()
        {
            if (definition?.AnatomyProfile == null) return 0;
            int count = definition.AnatomyProfile.FittedRoleCount;
            if (definition.IncludePhysicalJaw
                && IsFittedJaw(definition.AnatomyProfile.OptionalJaw))
                count++;
            return count;
        }

        private static bool IsFittedJaw(NpcBodyRoleProfile jaw)
        {
            return jaw != null
                   && jaw.Enabled
                   && jaw.AlignmentState != NpcAlignmentState.Unseeded
                   && jaw.ColliderShape == NpcColliderShape.Box
                   && jaw.ColliderSize.x > 0f
                   && jaw.ColliderSize.y > 0f
                   && jaw.ColliderSize.z > 0f
                   && !float.IsNaN(jaw.ColliderSize.x)
                   && !float.IsNaN(jaw.ColliderSize.y)
                   && !float.IsNaN(jaw.ColliderSize.z)
                   && !float.IsInfinity(jaw.ColliderSize.x)
                   && !float.IsInfinity(jaw.ColliderSize.y)
                   && !float.IsInfinity(jaw.ColliderSize.z);
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

        private static string StateLabel(NpcAlignmentState state)
        {
            switch (state)
            {
                case NpcAlignmentState.AutoFit: return "Auto-fit";
                case NpcAlignmentState.Reviewed: return "Reviewed";
                default: return "Unseeded";
            }
        }

        private static Vector3 Positive(Vector3 value)
        {
            return new Vector3(
                Mathf.Max(0.01f, value.x),
                Mathf.Max(0.01f, value.y),
                Mathf.Max(0.01f, value.z));
        }

        private static Vector3 SafeDirection(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude < 0.000001f ? fallback : value.normalized;
        }
    }
}
