using System;
using System.Linq;
using SLZ.Marrow.Warehouse;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Alignment;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.AvatarIntake;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Movement;
using Vergil333.MarrowNpcToolkit.Editor.Validation;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor
{
    public sealed class NpcToolkitWindow : EditorWindow
    {
        private const string DefaultAuthorKey = "Vergil333.MarrowNpcToolkit.DefaultAuthor";
        private const string Step4ControlName =
            "Vergil333.MarrowNpcToolkit.CheckReadiness";
        private static readonly string[] AudioModeLabels =
        {
            "Off",
            "Use Audio Profile",
        };

        [SerializeField] private Object avatarInput;
        [SerializeField, HideInInspector] private GameObject avatarSource;
        [SerializeField] private NpcDefinition definition;
        [SerializeField] private string author = "Author";
        [SerializeField] private string authoringFolder = "Assets/MarrowNpcToolkit";
        [SerializeField] private bool showWorkflowGuide = true;
        [SerializeField] private bool showNativeBuildDetails;
        [SerializeField] private Vector2 scroll;

        private AvatarIntakeReport intakeReport;
        private NpcRigMappingReport rigMappingReport;
        private NpcPhysicsPreviewReport physicsPreviewReport;
        private NpcMovementFitReport movementFitReport;
        private NpcBuildReadinessReport readinessReport;
        private NpcCompatibilityReport compatibilityReport;
        private NpcNativeBuildReport nativeBuildReport;
        private NpcNativeBuildReceiptInspection nativeReceiptInspection;
        private NpcSpawnableCratePreparationReport spawnableCrateReport;
        private NpcPalletPackReport palletPackReport;
        private string operationMessage;
        private MessageType operationMessageType = MessageType.Info;
        private bool focusStep4Requested;

        [MenuItem("Tools/Marrow NPC Toolkit", priority = 2100)]
        public static void Open()
        {
            var window = GetWindow<NpcToolkitWindow>();
            window.titleContent = new GUIContent("Marrow NPC Toolkit");
            window.minSize = new Vector2(520f, 560f);
            window.Show();
        }

        public static void OpenForReadiness(NpcDefinition selectedDefinition)
        {
            var window = GetWindow<NpcToolkitWindow>();
            window.titleContent = new GUIContent("Marrow NPC Toolkit");
            window.minSize = new Vector2(520f, 560f);
            if (selectedDefinition != null)
            {
                window.definition = selectedDefinition;
                window.avatarInput = selectedDefinition.SourceAvatar;
                window.avatarSource = selectedDefinition.SourceAvatar;
                window.physicsPreviewReport = null;
                window.ClearReadiness();
                window.RefreshIntake();
                window.RefreshRigMapping();
                window.SetOperation(
                    "Physics Preview generated. Step 4 is ready to check; this read-only check will not change your alignment.",
                    MessageType.Info);
            }
            window.focusStep4Requested = true;
            window.Show();
            window.Focus();
            window.Repaint();
        }

        private void OnEnable()
        {
            author = EditorPrefs.GetString(DefaultAuthorKey, author);
            if (avatarInput == null && Selection.activeObject != null
                                    && !string.IsNullOrEmpty(
                                        AssetDatabase.GetAssetPath(Selection.activeObject)))
                avatarInput = Selection.activeObject;
            RefreshIntake();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Layout
                || nativeReceiptInspection == null)
                nativeReceiptInspection =
                    NpcNativeBuildReceiptUtility.InspectCurrent(definition);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();
            DrawWorkflowGuide();
            DrawNextAction();
            EditorGUILayout.Space(8f);
            DrawImportAvatarStep();
            EditorGUILayout.Space(8f);
            DrawDefineNpcStep();
            EditorGUILayout.Space(8f);
            DrawAlignPhysicsStep();
            EditorGUILayout.Space(8f);
            DrawValidateStep();
            EditorGUILayout.Space(8f);
            DrawBuildStep();
            EditorGUILayout.Space(12f);

            if (!string.IsNullOrWhiteSpace(operationMessage))
                EditorGUILayout.HelpBox(operationMessage, operationMessageType);

            EditorGUILayout.EndScrollView();
        }

        private static void DrawHeader()
        {
            EditorGUILayout.LabelField("Marrow NPC Toolkit", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Unofficial guided authoring for native-style humanoid BONELAB NPCs",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox(
                "The toolkit preserves the selected character's own mesh and rig. Generated NPC prefabs will be separate assets; the source Avatar is never mutated by NPC generation.",
                MessageType.Info);

            NpcSdkEnvironment environment = NpcSdkEnvironmentProbe.Probe();
            MessageType providerMessage = environment.ProviderKind == NpcMarrowProviderKind.Unknown
                ? MessageType.Warning
                : MessageType.None;
            string version = string.IsNullOrWhiteSpace(environment.PackageVersion)
                ? string.Empty
                : " " + environment.PackageVersion;
            EditorGUILayout.HelpBox(
                $"Avatar provider: {environment.DisplayName}{version}. NPC generation will use a separately selected patch compatibility provider.",
                providerMessage);
        }

        private void DrawImportAvatarStep()
        {
            BeginStep("1", "Import Avatar", StepStateForAvatar());
            DrawStepExplanation(
                "Checks that the character is a supported Marrow Avatar with a usable Humanoid rig.",
                "Choose an AvatarCrate or Marrow Avatar prefab. Use the raw-model buttons only if you do not have one yet.",
                "The step says Ready and shows no red errors.");
            EditorGUI.BeginChangeCheck();
            avatarInput = EditorGUILayout.ObjectField(
                new GUIContent(
                    "Avatar Source",
                    "Select an AvatarCrate, an existing Marrow Avatar prefab, or a model asset."),
                avatarInput,
                typeof(Object),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                definition = null;
                ClearReadiness();
                operationMessage = null;
                RefreshIntake();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selected Project Asset"))
                    UseSelectedAsset();
                using (new EditorGUI.DisabledScope(avatarInput == null))
                {
                    if (GUILayout.Button("Ping Source"))
                    {
                        Selection.activeObject = avatarInput;
                        EditorGUIUtility.PingObject(avatarInput);
                    }
                }
            }

            DrawIntakeReport();

            if (intakeReport != null && intakeReport.CanConfigureAsHumanoid)
            {
                if (GUILayout.Button("Configure Model as Unity Humanoid"))
                    ConfigureAsHumanoid();
            }

            if (intakeReport != null && intakeReport.CanCreateMarrowAvatar)
            {
                if (GUILayout.Button("Create Marrow Avatar Prefab..."))
                    CreateMarrowAvatarPrefab();
            }

            if (intakeReport != null && intakeReport.IsMarrowAvatar)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Official Avatar Fine-Tuning"))
                        MarrowAvatarImportService.OpenForOfficialFineTuning(avatarSource);
                    if (GUILayout.Button("Recheck Avatar"))
                        RefreshIntake();
                }
                EditorGUILayout.LabelField(
                    "Unity will open the prefab with Marrow SDK's supported Avatar inspector and body/soft-body Scene handles.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EndStep();
        }

        private void DrawDefineNpcStep()
        {
            BeginStep("2", "Define NPC", definition != null ? "Created" : "Waiting");
            DrawStepExplanation(
                "Creates separate tuning files for this NPC; it does not change the source Avatar or copy its audio clips.",
                "Choose an authoring folder, then create the definition once Step 1 is Ready. Leave NPC Audio Off for a silent NPC, or use the Audio Profile to assign sounds by event.",
                "An NPC Definition and its source, anatomy, build, and audio profiles are shown below.");
            EditorGUI.BeginChangeCheck();
            author = EditorGUILayout.TextField(
                new GUIContent("Author", "Used for future pallet metadata."), author);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetString(DefaultAuthorKey, author);

            authoringFolder = EditorGUILayout.TextField(
                new GUIContent(
                    "Authoring Folder",
                    "Definition and tuning profiles are kept separate from generated prefabs."),
                authoringFolder);

            EditorGUI.BeginChangeCheck();
            definition = EditorGUILayout.ObjectField(
                "NPC Definition", definition, typeof(NpcDefinition), false) as NpcDefinition;
            if (EditorGUI.EndChangeCheck())
            {
                ClearReadiness();
                RefreshRigMapping();
            }

            bool canCreate = intakeReport != null && intakeReport.ReadyForNpcDefinition;
            using (new EditorGUI.DisabledScope(!canCreate))
            {
                if (GUILayout.Button("Create NPC Definition & Profiles"))
                    CreateDefinition();
            }

            if (!canCreate && definition == null)
            {
                EditorGUILayout.HelpBox(
                    "Finish Import Avatar first. A valid Marrow Avatar prefab gives the NPC workflow a supported Humanoid, renderer, wrist, and body-shape starting point.",
                    MessageType.None);
            }
            else if (definition != null)
            {
                EditorGUILayout.LabelField(
                    $"Source: {definition.SourceAvatar?.name ?? "Missing"}",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    $"Anatomy: {definition.AnatomyProfile?.BodyRoles.Count ?? 0} canonical roles"
                    + (definition.AnatomyProfile != null
                       && definition.AnatomyProfile.OptionalJaw.Enabled ? " + jaw" : string.Empty),
                    EditorStyles.wordWrappedLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Definition"))
                        Selection.activeObject = definition;
                    if (GUILayout.Button("Select Anatomy Profile"))
                        Selection.activeObject = definition.AnatomyProfile;
                    if (GUILayout.Button("Select Build Profile"))
                        Selection.activeObject = definition.BuildProfile;
                }
                if (GUILayout.Button("Refresh Snapshot from Tuned Avatar"))
                    RefreshAvatarSnapshot();
                EditorGUILayout.LabelField(
                    "Use this only after changing the source Avatar in the official Marrow Avatar editor.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(
                    "NPC Audio (optional)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                int selectedAudioMode = EditorGUILayout.Popup(
                    new GUIContent(
                        "NPC Audio",
                        "Off creates a silent NPC. Use Audio Profile installs the categorized clips referenced by the assigned profile."),
                    definition.AudioMode == NpcAudioMode.Profile ? 1 : 0,
                    AudioModeLabels);
                NpcAudioMode audioMode = selectedAudioMode == 1
                    ? NpcAudioMode.Profile
                    : NpcAudioMode.Silent;
                NpcAudioProfile audioProfile = EditorGUILayout.ObjectField(
                    new GUIContent(
                        "Audio Profile",
                        "A saved map from NPC events such as pain, death, effort, impacts, and footsteps to existing AudioClip assets."),
                    definition.AudioProfile,
                    typeof(NpcAudioProfile),
                    false) as NpcAudioProfile;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(definition, "Change NPC audio authoring");
                    definition.AudioMode = audioMode;
                    definition.AudioProfile = audioProfile;
                    EditorUtility.SetDirty(definition);
                    ClearReadiness();
                }

                if (definition.AudioMode == NpcAudioMode.Silent)
                    EditorGUILayout.HelpBox(
                        "Off: the generated NPC will not use reaction or movement audio. You can still prepare an Audio Profile without enabling it.",
                        MessageType.None);
                else
                    EditorGUILayout.HelpBox(
                        "Use Audio Profile: the generated NPC uses the assigned category map. Small Pain, Big Pain, and Death must contain at least one saved clip; other categories are optional.",
                        MessageType.Info);
                if (definition.AudioProfile == null)
                {
                    EditorGUILayout.HelpBox(
                        "No Audio Profile is assigned. Create a small settings asset beside this NPC Definition; supported sound references will be filled from the Avatar when available.",
                        MessageType.None);
                    if (GUILayout.Button("Create Missing Audio Profile"))
                        CreateAudioProfile();
                }
                else
                {
                    DrawAudioProfileSummary(definition.AudioProfile);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Review / Edit Audio Profile"))
                        {
                            Selection.activeObject = definition.AudioProfile;
                            EditorGUIUtility.PingObject(definition.AudioProfile);
                        }
                        bool canRefreshAudio = definition.SourceKind
                                               == NpcAvatarSourceKind.MarrowAvatarPrefab;
                        using (new EditorGUI.DisabledScope(!canRefreshAudio))
                        {
                            if (GUILayout.Button("Re-read Supported Audio from Avatar"))
                                RefreshAudioProfile();
                        }
                    }
                }
                EditorGUILayout.LabelField(
                    "The profile stores links to existing AudioClips; it does not duplicate or edit the audio files. Re-reading replaces only the supported Avatar-mapped categories; other custom categories stay unchanged.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField(
                    "Secondary Motion (optional)", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                bool includeSecondaryMotion = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Secondary Motion",
                        "Adds spring-driven bodies for the two Breast Soft Body bones assigned on the source Marrow Avatar."),
                    definition.IncludeSecondaryMotion);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(definition, "Change NPC secondary motion");
                    definition.IncludeSecondaryMotion = includeSecondaryMotion;
                    EditorUtility.SetDirty(definition);
                    ClearReadiness();
                }

                if (definition.IncludeSecondaryMotion)
                {
                    string secondaryMotionDetail = definition.SourceKind
                                                   == NpcAvatarSourceKind.MarrowAvatarPrefab
                        ? "Enabled: the native provider automatically uses the two Breast Soft Body bones already assigned on the source Marrow Avatar. It does not modify the Avatar. Abdomen and butt soft-body assignments are not included. The Unity Physics Preview still shows only the core NPC body set; Step 4 checks the breast bones and provider support."
                        : "This source is not a Marrow Avatar prefab, so it has no supported Breast Soft Body assignments to read. Convert and tune it with the official Marrow Avatar tools, or leave Secondary Motion off.";
                    EditorGUILayout.HelpBox(
                        secondaryMotionDetail,
                        definition.SourceKind == NpcAvatarSourceKind.MarrowAvatarPrefab
                            ? MessageType.Info
                            : MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Off by default. Enable this only when the source Marrow Avatar has both Breast Soft Body bones assigned. This module does not add abdomen or butt soft-body physics.",
                        MessageType.None);
                }
            }
            EndStep();
        }

        private void DrawAlignPhysicsStep()
        {
            int requestedBodyCount = definition != null
                                     && definition.IncludePhysicalJaw
                ? 17
                : 16;
            bool requestedJawReady = IsRequestedJawFitted(definition);
            if (definition != null && rigMappingReport == null) RefreshRigMapping();
            bool currentBaseline = definition != null
                                   && definition.AnatomyProfile != null
                                   && rigMappingReport != null
                                   && definition.AnatomyProfile.BaselineMatches(
                                       rigMappingReport.CurrentSourceDependencyHash)
                                   && requestedJawReady;
            string state = definition == null || definition.AnatomyProfile == null
                ? "Waiting"
                : currentBaseline
                    ? "Baseline ready"
                    : definition.AnatomyProfile.HasFittedBaseline
                        ? "Refresh auto-fit"
                        : "Needs auto-fit";
            BeginStep("3", "Align Physics", state);
            DrawStepExplanation(
                "Builds the NPC's invisible impact and ragdoll body, then records provider-neutral standing and navigation measurements.",
                "Create the automatic physics fit, review each shape, generate its preview, then create and review the Step 3D movement baseline.",
                $"The preview has {requestedBodyCount} physics bodies, and Step 3D has a current Movement Profile for the accepted Avatar.");
            if (definition == null || definition.AnatomyProfile == null)
            {
                EditorGUILayout.LabelField(
                    "Create an NPC Definition first.", EditorStyles.wordWrappedLabel);
                EndStep();
                return;
            }

            int fittedBodyCount = definition.AnatomyProfile.FittedRoleCount
                                  + (definition.IncludePhysicalJaw
                                     && requestedJawReady ? 1 : 0);
            EditorGUILayout.LabelField(
                $"Canonical rig: {rigMappingReport?.MatchingCanonicalCount ?? 0}/16 | "
                + $"Physics: {fittedBodyCount}/{requestedBodyCount}",
                EditorStyles.wordWrappedLabel);

            if (rigMappingReport != null)
            {
                foreach (NpcRigIssue issue in rigMappingReport.Issues.Where(value =>
                             value.Severity != NpcRigIssueSeverity.Info))
                {
                    EditorGUILayout.HelpBox(
                        issue.Message,
                        issue.Severity == NpcRigIssueSeverity.Error
                            ? MessageType.Error
                            : MessageType.Warning);
                }
            }

            bool ready = rigMappingReport != null
                         && rigMappingReport.ReadyForBaseline
                         && !rigMappingReport.SourceChanged;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Recheck Rig")) RefreshRigMapping();
                using (new EditorGUI.DisabledScope(!ready))
                {
                    if (GUILayout.Button("3A. Create / Refresh Automatic Fit"))
                        FitBaseline();
                }
            }
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("3B. Review Physics Alignment"))
                    NpcAlignmentWindow.Open(definition);
            }
            bool baselineReady = ready
                                 && definition.AnatomyProfile.BaselineMatches(
                                     rigMappingReport.CurrentSourceDependencyHash)
                                 && requestedJawReady;
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!baselineReady))
            {
                if (GUILayout.Button("3C. Generate / Refresh Physics Preview"))
                    GeneratePhysicsPreview();
                string previewPath = NpcPhysicsPreviewBuilder.GetOutputPath(definition);
                Object preview = AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
                using (new EditorGUI.DisabledScope(preview == null))
                {
                    if (GUILayout.Button("Open Preview")) AssetDatabase.OpenAsset(preview);
                }
            }
            if (physicsPreviewReport != null && !physicsPreviewReport.Success)
                foreach (string issue in physicsPreviewReport.Issues)
                    EditorGUILayout.HelpBox(issue, MessageType.Error);
            string currentPreviewPath = NpcPhysicsPreviewBuilder.GetOutputPath(definition);
            GameObject currentPreview = AssetDatabase.LoadAssetAtPath<GameObject>(
                currentPreviewPath);
            bool previewCurrent = false;
            if (currentPreview != null)
            {
                previewCurrent = NpcPhysicsPreviewBuilder.ReceiptMatches(
                    definition, currentPreviewPath, out string previewDetail);
                EditorGUILayout.HelpBox(
                    previewCurrent
                        ? "Physics Preview is current for this saved alignment."
                        : previewDetail,
                    previewCurrent ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "3D. Automatic Movement Adaptation",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Measures this Humanoid and proportionally adapts the native stock locomotion reference. No movement dials or manual review are required; BONELAB runtime testing remains the final proof.",
                EditorStyles.wordWrappedMiniLabel);
            NpcMovementProfile movement = definition.MovementProfile;
            string movementAuthoringFingerprint =
                NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(definition);
            bool movementFitCurrent = movement != null
                                      && movement.HasFittedMeasurements
                                      && rigMappingReport != null
                                      && movement.AutoFitMatches(
                                          rigMappingReport.CurrentSourceDependencyHash,
                                          movementAuthoringFingerprint);
            // Keep repaint cheap. Exact provider/donor currentness is verified
            // after the explicit Step 3D action and again by the explicit Step
            // 4 doctor; it must not rescan dependencies on every IMGUI event.
            bool movementRecipePrepared = movementFitCurrent
                                          && HasPreparedMovementRecipe(movement);
            using (new EditorGUI.DisabledScope(!baselineReady || !previewCurrent))
            {
                if (GUILayout.Button("3D. Recalculate Movement for This Avatar"))
                    FitMovementBaseline();
            }
            if (!previewCurrent)
                EditorGUILayout.HelpBox(
                    "Generate a current Physics Preview before creating the movement baseline.",
                    MessageType.None);
            else if (movement == null)
                EditorGUILayout.HelpBox(
                    "This existing Definition predates Movement Profiles. The Step 3D baseline action will create and link one beside the Definition automatically.",
                    MessageType.Info);
            else if (!movementFitCurrent)
                EditorGUILayout.HelpBox(
                    "The Movement Profile needs a current automatic baseline.",
                    MessageType.Warning);
            else
            {
                string providerState = movementRecipePrepared
                    ? "stock-reference movement prepared"
                    : "stock-reference movement needs recalculation";
                EditorGUILayout.HelpBox(
                    $"Automatic fit: {providerState}. Navigation height {movement.NavHeight:0.000} m, radius {movement.NavRadius:0.000} m, mean leg {movement.MeanLegLength:0.000} m.",
                    MessageType.Info);
                if (!movementRecipePrepared)
                    EditorGUILayout.HelpBox(
                        "Recalculate Step 3D to prepare the proportional standing pose and stock-reference movement settings. Step 4 performs the full currentness check.",
                        MessageType.Warning);
            }
            if (movement != null)
                DrawHostilityResponseControl(movement);
            if (movementFitReport != null && !movementFitReport.Success)
                foreach (string issue in movementFitReport.Issues)
                    EditorGUILayout.HelpBox(issue, MessageType.Error);
            EditorGUILayout.LabelField(
                "The fit is a practical approximation, not a skin-tight outline. Small overlap at joints is expected. A hand box should enclose the hand along wrist-to-fingers; a foot box should run heel-to-toe.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox(
                $"Preview hierarchy: AnimationRoot is the untouched visible/animated Avatar. Physics is its separate invisible {requestedBodyCount}-body partner. Alignment changes are stored in the Anatomy Profile, not in AnimationRoot or the source Avatar prefab.",
                MessageType.None);
            EndStep();
        }

        private void DrawValidateStep()
        {
            bool providerReady = IsRequestedProviderReady();
            string state = definition == null
                ? "Waiting"
                : readinessReport == null || compatibilityReport == null
                    ? "Not checked"
                    : !readinessReport.ReadyForBuild
                        ? "Authoring needs attention"
                        : providerReady
                            ? "Handoff ready"
                            : "Provider incomplete";
            BeginStep("4", "Check NPC Readiness", state);
            DrawStepExplanation(
                "Checks the source, requested 16- or 17-body anatomy, generated previews, movement recipe, and native-provider capabilities without changing any asset.",
                "Generate the Physics Preview and create the Step 3D movement baseline, then run this check. Fix red authoring or physics messages; provider capability gaps require toolkit/provider support, not collider changes.",
                "Physics says Ready and the selected native provider supports every requested feature.");

            using (new EditorGUI.DisabledScope(definition == null))
            {
                GUI.SetNextControlName(Step4ControlName);
                if (GUILayout.Button("4. Check NPC Readiness"))
                    RunReadinessCheck();
                if (focusStep4Requested && Event.current.type == EventType.Repaint)
                {
                    GUI.FocusControl(Step4ControlName);
                    GUI.ScrollTo(GUILayoutUtility.GetLastRect());
                    focusStep4Requested = false;
                    Repaint();
                }
            }

            if (readinessReport != null)
            {
                string physicsSummary = readinessReport.ReadyForBuild
                    ? $"Authoring ready: {readinessReport.RigidbodyCount} physics bodies, "
                      + $"{readinessReport.ColliderCount} colliders, "
                      + $"{readinessReport.JointCount} joints, and "
                      + $"{readinessReport.RendererCount} preserved renderers."
                    : $"Authoring needs attention: {readinessReport.ErrorCount} error(s), "
                      + $"{readinessReport.WarningCount} warning(s).";
                EditorGUILayout.HelpBox(
                    physicsSummary,
                    readinessReport.ReadyForBuild ? MessageType.Info : MessageType.Error);

                if (readinessReport.ReviewedRoleCount < readinessReport.ExpectedRoleCount)
                    EditorGUILayout.HelpBox(
                        $"Visual review recommended: {readinessReport.ReviewedRoleCount}/"
                        + $"{readinessReport.ExpectedRoleCount} body shapes are marked Reviewed.",
                        MessageType.Warning);

                foreach (NpcBuildReadinessIssue issue in readinessReport.Issues)
                {
                    MessageType type = issue.Severity == NpcBuildReadinessSeverity.Error
                        ? MessageType.Error
                        : issue.Severity == NpcBuildReadinessSeverity.Warning
                            ? MessageType.Warning
                            : MessageType.Info;
                    EditorGUILayout.HelpBox(issue.Message, type);
                }
                EditorGUILayout.LabelField(
                    "Readiness fingerprint: " + readinessReport.Fingerprint,
                    EditorStyles.wordWrappedMiniLabel);
            }

            if (compatibilityReport != null)
            {
                MessageType providerType = compatibilityReport.NativeNpcProviderAvailable
                    ? MessageType.Info
                    : MessageType.Error;
                string providerName = string.IsNullOrWhiteSpace(
                    compatibilityReport.ProviderDisplayName)
                    ? "Native provider"
                    : compatibilityReport.ProviderDisplayName;
                EditorGUILayout.HelpBox(
                    providerName + ": " + compatibilityReport.Detail,
                    providerType);

                DrawCapabilityRow(
                    "Core anatomy", true,
                    compatibilityReport.SupportsCoreAnatomy);
                DrawCapabilityRow("AI / movement", true, compatibilityReport.SupportsAI);
                DrawCapabilityRow("Pooling", true, compatibilityReport.SupportsPooling);
                DrawCapabilityRow(
                    "Player body grabs", definition != null && definition.IncludeHandGrips,
                    compatibilityReport.SupportsGrips);
                if (definition != null && definition.IncludeHandGrips)
                    EditorGUILayout.LabelField(
                        "Player body grabs let the player grab NPC body parts. NPC hand-animation poses belong to AI / movement.",
                        EditorStyles.wordWrappedMiniLabel);
                DrawCapabilityRow(
                    "Gaze", definition != null && definition.IncludeGaze,
                    compatibilityReport.SupportsGaze);
                DrawCapabilityRow(
                    "Physical jaw", definition != null && definition.IncludePhysicalJaw,
                    compatibilityReport.SupportsJaw);
                DrawCapabilityRow(
                    "NPC audio", definition != null && definition.IncludeNpcAudio,
                    compatibilityReport.SupportsAudio);
                DrawCapabilityRow(
                    "Secondary motion",
                    definition != null && definition.IncludeSecondaryMotion,
                    compatibilityReport.SupportsSecondaryMotion);
                if (definition != null && definition.IncludeSecondaryMotion)
                    EditorGUILayout.LabelField(
                        "Secondary motion uses the two Breast Soft Body bones assigned on the source Marrow Avatar. It does not include abdomen or butt soft-body assignments, and it does not add editable core colliders to Physics Alignment.",
                        EditorStyles.wordWrappedMiniLabel);

                if (!providerReady)
                {
                    string providerGuidance = compatibilityReport.NativeNpcProviderAvailable
                        ? "Physics and provider support are separate. Rows marked Missing from provider are toolkit/provider implementation gaps, not Avatar or collider-alignment errors. Disable an optional feature only if you genuinely do not want it; AI / movement and pooling remain required for a native NPC. Secondary Motion is provider-generated from the Avatar's two Breast Soft Body bones, not from collider alignment."
                        : "No matching native NPC provider is available. This is a toolkit/project-provider setup limitation, not an Avatar or collider-alignment error.";
                    EditorGUILayout.HelpBox(providerGuidance, MessageType.Warning);
                }
            }

            if (readinessReport != null && readinessReport.ReadyForBuild && providerReady)
                EditorGUILayout.HelpBox(
                    "Ready for the native-builder handoff. This does not mean generated, packed, spawn-tested, interaction-tested, or author-approved.",
                    MessageType.Info);
            EndStep();
        }

        private void DrawBuildStep()
        {
            bool physicsReady = readinessReport != null
                                && readinessReport.ReadyForBuild;
            bool providerReady = IsRequestedProviderReady();
            NpcNativeBuildReceipt storedReceipt =
                nativeReceiptInspection?.Receipt;
            NpcNativeBuildReceipt receipt =
                nativeReceiptInspection != null
                && nativeReceiptInspection.IsCurrent
                    ? storedReceipt
                    : null;
            bool currentReceiptInputsReady = nativeReceiptInspection?.Readiness
                                                 ?.ReadyForBuild == true;
            bool receiptStale = storedReceipt != null
                                && receipt == null
                                && currentReceiptInputsReady;
            bool receiptInputsNeedAttention = storedReceipt != null
                                              && nativeReceiptInspection?.Readiness != null
                                              && !currentReceiptInputsReady;
            bool buildInputsReady = physicsReady
                                    && !receiptInputsNeedAttention;
            bool nativeOutputOpen = IsNativeOutputPrefabOpen();
            NpcSpawnableCratePreparationReport boundAssets =
                GetSpawnableCrateDisplayReport();
            bool crateReady = receipt != null
                              && boundAssets != null
                              && boundAssets.Success;
            bool packReady = crateReady
                             && palletPackReport != null
                             && palletPackReport.Success
                             && string.Equals(
                                 palletPackReport.PackagingFingerprint,
                                 NpcPackagingFingerprintUtility.Compute(definition),
                                 StringComparison.Ordinal);
            bool nativeReady = receipt != null
                               || nativeBuildReport != null
                               && nativeBuildReport.Success;
            string state = packReady
                ? "Packed - runtime pending"
                : crateReady
                    ? "Spawnable Crate ready"
                    : definition == null
                        ? "Waiting"
                        : !buildInputsReady
                            ? "Run Step 4"
                            : !providerReady
                                ? "Provider incomplete"
                                : receiptStale
                                    ? "Rebuild 5A"
                                    : nativeReady
                                        ? "Prefab generated"
                                        : "Ready to build";
            BeginStep("5", "Build & Test", state);
            DrawStepExplanation(
                "Generates a separate native NPC prefab, prepares its official Marrow Pallet and Spawnable Crate, then packs the whole Pallet for Quest or Windows.",
                "Run 5A after Step 4, 5B to create or update the GUID-bound Pallet and Crate without changing their barcodes, then 5C on the selected platform.",
                "Done here means the native prefab passed saved-reload checks and the packed files are complete. BONELAB spawn and interaction proof still comes next.");

            if (nativeOutputOpen)
            {
                EditorGUILayout.HelpBox(
                    receiptStale
                        ? "The generated NPC is open and out of date. Return to the main scene, then run 5A again."
                        : "Return to the main scene before rebuilding the generated NPC.",
                    MessageType.Warning);
                if (GUILayout.Button("Return to Main Scene"))
                    ExitGeneratedPrefabMode();
                EditorGUILayout.LabelField(
                    "5A recreates only the generated NPC. It does not change the source Avatar or NPC tuning profiles; manual edits inside the generated prefab would be replaced.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                using (new EditorGUI.DisabledScope(
                           definition == null || !buildInputsReady
                                              || !providerReady))
                {
                    if (GUILayout.Button("5A. Generate Native NPC Prefab"))
                        BuildNativeNpcPrefab();
                }
            }

            if (definition != null && (!buildInputsReady || !providerReady))
                EditorGUILayout.HelpBox(
                    !buildInputsReady
                        ? "Run Check NPC Readiness in Step 4 and resolve any red authoring or physics messages first."
                        : "The selected provider does not yet cover every requested feature. Provider setup or implementation is required; changing colliders will not fix this.",
                    MessageType.None);

            if (receiptStale && !nativeOutputOpen)
                EditorGUILayout.HelpBox(
                    "The generated NPC no longer matches its saved 5A receipt. Run 5A again to update it; Physics Alignment does not need to be repeated.",
                    MessageType.Warning);
            else if (receiptInputsNeedAttention && !nativeOutputOpen)
                EditorGUILayout.HelpBox(
                    "The NPC authoring inputs changed after 5A and no longer pass the current readiness check. Run Step 4 again before rebuilding.",
                    MessageType.Warning);

            if (nativeBuildReport != null)
            {
                if (!nativeBuildReport.Success)
                {
                    if (!nativeOutputOpen)
                        DrawNativeBuildMessages(nativeBuildReport.Messages);
                }
                else if (receipt != null)
                {
                    EditorGUILayout.HelpBox(
                        "Native NPC generated and validated.",
                        MessageType.Info);
                    Object output = AssetDatabase.LoadAssetAtPath<GameObject>(
                        nativeBuildReport.OutputPrefabPath);
                    using (new EditorGUI.DisabledScope(output == null))
                    {
                        if (GUILayout.Button("Open & Focus Generated NPC"))
                            OpenAndFocusNativePrefab(
                                output,
                                nativeBuildReport.OutputPrefabPath);
                    }
                    EditorGUILayout.LabelField(
                        "A large green wire sphere on a gaze-enabled NPC is an editor-only player-notice range. It is not a collider and does not change the NPC's size.",
                        EditorStyles.wordWrappedMiniLabel);
                    DrawNativeBuildNonInfoMessages(nativeBuildReport.Messages);
                    EditorGUILayout.HelpBox(
                        "Editor generation, save/reload validation, and deterministic two-pass comparison passed. This is not yet proof that BONELAB spawned or exercised the NPC.",
                        MessageType.Warning);
                    showNativeBuildDetails = EditorGUILayout.Foldout(
                        showNativeBuildDetails,
                        "Technical build details",
                        true);
                    if (showNativeBuildDetails)
                    {
                        using (new EditorGUI.IndentLevelScope())
                        {
                            DrawNativeBuildMessages(nativeBuildReport.Messages);
                            EditorGUILayout.LabelField(
                                "Native prefab: " + nativeBuildReport.OutputPrefabPath,
                                EditorStyles.wordWrappedMiniLabel);
                            EditorGUILayout.LabelField(
                                "Build fingerprint: " + nativeBuildReport.OutputFingerprint,
                                EditorStyles.wordWrappedMiniLabel);
                        }
                    }
                }
            }

            string existingNativePath = definition == null
                ? string.Empty
                : NpcNativeBuildCoordinator.GetDefaultOutputPath(definition);
            Object existingNative = string.IsNullOrEmpty(existingNativePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(existingNativePath);
            if (!nativeOutputOpen && existingNative != null
                                  && (nativeBuildReport?.Success != true
                                      || receipt == null))
            {
                string label = receipt == null
                    ? "Open Existing Generated NPC (out of date)"
                    : "Open Existing Generated NPC";
                if (GUILayout.Button(label))
                    OpenAndFocusNativePrefab(existingNative, existingNativePath);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "5B. Prepare Spawnable Crate", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Creates the Pallet and Spawnable Crate once, then remembers their asset GUIDs. Future runs update those exact assets and preserve both barcodes.",
                EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(
                       definition == null || receipt == null
                                          || !physicsReady || !providerReady))
            {
                if (GUILayout.Button("5B. Prepare Spawnable Crate"))
                    PrepareSpawnableCrate();
            }
            if (definition != null && receipt == null)
                EditorGUILayout.HelpBox(
                    receiptStale
                        ? "Waiting for an updated 5A build."
                        : receiptInputsNeedAttention
                        ? "Waiting for a fresh Step 4 check and current 5A build."
                        : "Run 5A successfully first. Step 5B requires the current native prefab and its saved build receipt.",
                    MessageType.None);

            bool showSpawnableCrateReport = receipt != null
                                            && spawnableCrateReport != null;
            if (showSpawnableCrateReport)
            {
                foreach (NpcSpawnableCratePreparationMessage message
                         in spawnableCrateReport.Messages)
                {
                    MessageType type = message.Severity
                        == NpcSpawnableCratePreparationMessageSeverity.Error
                            ? MessageType.Error
                            : message.Severity
                                == NpcSpawnableCratePreparationMessageSeverity.Warning
                                ? MessageType.Warning
                                : MessageType.Info;
                    EditorGUILayout.HelpBox(message.Message, type);
                }
            }

            if (crateReady)
            {
                EditorGUILayout.LabelField(
                    "Pallet: " + boundAssets.PalletTitle,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Pallet barcode: " + boundAssets.PalletBarcode,
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "Spawnable Crate: " + boundAssets.CrateTitle,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Crate barcode: " + boundAssets.CrateBarcode,
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "Packaging fingerprint: " + boundAssets.PackagingFingerprint,
                    EditorStyles.wordWrappedMiniLabel);
                Object crate = AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                    boundAssets.CrateAssetPath);
                using (new EditorGUI.DisabledScope(crate == null))
                {
                    if (GUILayout.Button("Show Prepared Spawnable Crate"))
                    {
                        Selection.activeObject = crate;
                        EditorGUIUtility.PingObject(crate);
                    }
                }
                EditorGUILayout.HelpBox(
                    packReady
                        ? "The Pallet is packed. BONELAB runtime proof still comes next."
                        : "The Spawnable Crate is prepared but not packed. Continue with Step 5C below.",
                    packReady ? MessageType.Info : MessageType.Warning);

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "5C. Pack Pallet", EditorStyles.boldLabel);
                NpcTargetPlatform target = definition.BuildProfile.TargetPlatform;
                string platform = target == NpcTargetPlatform.Quest
                    ? "Quest / Android"
                    : "Windows PC";
                EditorGUILayout.LabelField(
                    "Selected platform: " + platform,
                    EditorStyles.wordWrappedLabel);
                bool needsSafeStart = NpcPalletPackCoordinator
                    .RequiresNonStandaloneStartForMacWindowsPack(
                        Application.platform,
                        NpcPalletPackCoordinator.RequiredBuildTarget(target),
                        PlayerSettings.GetScriptingBackend(
                            BuildTargetGroup.Standalone),
                        NpcPalletPackCoordinator.CurrentBuildTargetGroup());
                if (needsSafeStart)
                {
                    EditorGUILayout.HelpBox(
                        "Return Unity to Quest / Android once before packing Windows on macOS. Step 5C can then use the installed Windows build support temporarily and restore your project safely.",
                        MessageType.Warning);
                    if (GUILayout.Button("Return Unity to Quest / Android"))
                        SwitchPackTarget(NpcTargetPlatform.Quest);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Step 5C temporarily switches platform when needed, packs the Pallet, then returns Unity to "
                        + EditorUserBuildSettings.activeBuildTarget + ".",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("5C. Pack Pallet for " + platform))
                        PackPreparedPallet();
                }

                if (palletPackReport != null)
                {
                    foreach (NpcPalletPackMessage message
                             in palletPackReport.Messages)
                    {
                        MessageType type = message.Severity
                            == NpcPalletPackMessageSeverity.Error
                                ? MessageType.Error
                                : message.Severity
                                    == NpcPalletPackMessageSeverity.Warning
                                    ? MessageType.Warning
                                    : MessageType.Info;
                        EditorGUILayout.HelpBox(message.Message, type);
                    }

                    if (palletPackReport.Output != null)
                    {
                        EditorGUILayout.LabelField(
                            "Packed files: "
                            + palletPackReport.Output.OutputDirectory,
                            EditorStyles.wordWrappedMiniLabel);
                        using (new EditorGUI.DisabledScope(
                                   !System.IO.Directory.Exists(
                                       palletPackReport.Output.OutputDirectory)))
                        {
                            if (GUILayout.Button("Show Packed Files"))
                                EditorUtility.RevealInFinder(
                                    palletPackReport.Output.OutputDirectory);
                        }
                    }
                }
            }
            EndStep();
        }

        private static void DrawNativeBuildMessages(
            System.Collections.Generic.IReadOnlyList<NpcNativeBuildMessage> messages)
        {
            if (messages == null) return;
            foreach (NpcNativeBuildMessage message in messages)
            {
                MessageType type = message.Severity
                    == NpcNativeBuildMessageSeverity.Error
                        ? MessageType.Error
                        : message.Severity
                            == NpcNativeBuildMessageSeverity.Warning
                            ? MessageType.Warning
                            : MessageType.Info;
                EditorGUILayout.HelpBox(message.Message, type);
            }
        }

        private static void DrawNativeBuildNonInfoMessages(
            System.Collections.Generic.IReadOnlyList<NpcNativeBuildMessage> messages)
        {
            if (messages == null) return;
            foreach (NpcNativeBuildMessage message in messages)
            {
                if (message.Severity == NpcNativeBuildMessageSeverity.Info)
                    continue;
                MessageType type = message.Severity
                    == NpcNativeBuildMessageSeverity.Error
                        ? MessageType.Error
                        : MessageType.Warning;
                EditorGUILayout.HelpBox(message.Message, type);
            }
        }

        private static void OpenAndFocusNativePrefab(
            Object output,
            string outputPath)
        {
            AssetDatabase.OpenAsset(output);
            EditorApplication.delayCall += () =>
            {
                UnityEditor.SceneManagement.PrefabStage stage =
                    UnityEditor.SceneManagement.PrefabStageUtility
                        .GetCurrentPrefabStage();
                if (stage == null
                    || !string.Equals(
                        stage.assetPath,
                        outputPath,
                        StringComparison.OrdinalIgnoreCase))
                    return;

                GameObject root = stage.prefabContentsRoot;
                Selection.activeObject = root;
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                bool hasBounds = false;
                Bounds bounds = default;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null) continue;
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null) return;
                if (hasBounds) sceneView.Frame(bounds, false);
                else sceneView.FrameSelected();
            };
        }

        private static void DrawCapabilityRow(
            string label,
            bool requested,
            bool supported)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(145f));
                string status = !requested
                    ? "Not requested"
                    : supported ? "Ready" : "Missing from provider";
                GUIStyle style = requested && !supported
                    ? EditorStyles.boldLabel
                    : EditorStyles.miniLabel;
                GUILayout.Label(status, style);
            }
        }

        private static void DrawAudioProfileSummary(NpcAudioProfile profile)
        {
            if (profile == null) return;

            int smallPain = CountAudioClips(profile.PainSmall);
            int bigPain = CountAudioClips(profile.PainBig);
            int death = CountAudioClips(profile.Death);
            int jump = CountAudioClips(profile.Jump);
            int efforts = CountAudioClips(profile.SmallEffort)
                          + CountAudioClips(profile.MediumEffort)
                          + CountAudioClips(profile.LargeEffort);
            int impacts = CountAudioClips(profile.ImpactHead)
                          + CountAudioClips(profile.ImpactSpine)
                          + CountAudioClips(profile.ImpactLimb);
            int footsteps = CountAudioClips(profile.WalkConcrete)
                            + CountAudioClips(profile.RunConcrete);

            EditorGUILayout.HelpBox(
                (profile.HasBasicReactions
                    ? "Required reaction groups are filled; Step 4 performs the final validation."
                    : "The three required reaction groups are not all filled.")
                + $" Required: Small Pain {smallPain}, Big Pain {bigPain}, Death {death}."
                + $" Optional populated highlights: Jump {jump}, Effort assignments {efforts}, Impact assignments {impacts}, Footsteps {footsteps}."
                + " Empty optional categories stay silent. Use Review / Edit Audio Profile to inspect or replace individual clips in the Inspector.",
                profile.HasBasicReactions ? MessageType.None : MessageType.Warning);
        }

        private static int CountAudioClips(
            System.Collections.Generic.IReadOnlyList<AudioClip> clips)
        {
            if (clips == null) return 0;
            int count = 0;
            for (int index = 0; index < clips.Count; index++)
                if (clips[index] != null) count++;
            return count;
        }

        private void DrawHostilityResponseControl(NpcMovementProfile profile)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(
                "Response to damage", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "A typical hit means damage equal to 25% of maximum health. These values choose the NPC's reaction state; they do not change walking speed or guarantee that navigation can find a path.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUI.BeginChangeCheck();
            float starting = DrawHostilitySlider(
                new GUIContent(
                    "Starting hostility",
                    "Zero starts friendly. Values at or above 0.5 allow combat pursuit as soon as the NPC has a target."),
                profile.StartingHostility);
            float afterHit = DrawHostilitySlider(
                new GUIContent(
                    "Hostility after a typical hit",
                    "The hostility reached after losing 25% of maximum health."),
                Mathf.Max(starting, profile.HostilityAfterTypicalHit));
            afterHit = Mathf.Max(starting, afterHit);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Change NPC hostility response");
                profile.StartingHostility = starting;
                profile.HostilityAfterTypicalHit = afterHit;
                // Existing native movement settings still contain the old
                // aggression leaves. Removing the receipt makes Step 4/5A
                // reject that stale data until Step 3D prepares it again.
                profile.ClearProviderRecipe();
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
                ClearReadiness();
                SetOperation(
                    "Hostility response changed. Recalculate Step 3D to update the native movement settings before Step 4 or Step 5A.",
                    MessageType.Warning);
            }

            float selected = profile.HostilityAfterTypicalHit;
            bool allowsPursuit = selected
                                 >= NpcMovementProfile.CombatPursuitThreshold;
            string response = selected <= 0.0001f
                ? "No hostility gain. The NPC does not escalate from this typical hit."
                : allowsPursuit
                    ? "Agroed. Combat pursuit is allowed, but successful walking and pathfinding still require a valid runtime path."
                    : "Defensive / Engaged. The NPC may enter a fight stance, but combat pursuit remains disabled below 0.50.";
            EditorGUILayout.HelpBox(
                $"After a typical hit: {selected:0.00} — {response}",
                allowsPursuit ? MessageType.Info : MessageType.None);
        }

        private static float DrawHostilitySlider(
            GUIContent label,
            float value)
        {
            Rect row = EditorGUILayout.GetControlRect();
            float labelWidth = Mathf.Min(
                EditorGUIUtility.labelWidth,
                row.width * 0.42f);
            Rect labelRect = new Rect(
                row.x, row.y, labelWidth - 4f, row.height);
            Rect valueRect = new Rect(
                row.xMax - 48f, row.y, 48f, row.height);
            Rect sliderRect = new Rect(
                labelRect.xMax + 4f,
                row.y,
                Mathf.Max(20f, valueRect.x - labelRect.xMax - 10f),
                row.height);
            EditorGUI.LabelField(labelRect, label);
            value = GUI.HorizontalSlider(sliderRect, value, 0f, 1f);
            float marker = Mathf.Lerp(
                sliderRect.x + 5f,
                sliderRect.xMax - 5f,
                NpcMovementProfile.CombatPursuitThreshold);
            EditorGUI.DrawRect(
                new Rect(marker - 1f, sliderRect.y + 2f, 2f,
                    sliderRect.height - 4f),
                new Color(1f, 0.58f, 0.12f, 0.9f));
            value = Mathf.Clamp01(EditorGUI.FloatField(valueRect, value));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("0 friendly", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    "0.50 combat-pursuit boundary",
                    EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label("1 maximum", EditorStyles.miniLabel);
            }
            return value;
        }

        private void DrawWorkflowGuide()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            showWorkflowGuide = EditorGUILayout.Foldout(
                showWorkflowGuide, "How the five-step workflow works", true);
            if (showWorkflowGuide)
            {
                EditorGUILayout.LabelField(
                    "Work from top to bottom. Each step creates or checks the input needed by the next one.",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(2f);
                DrawWorkflowRow("1", "Import Avatar", "Prepare and check the character.");
                DrawWorkflowRow("2", "Define NPC", "Create safe, separate tuning profiles.");
                DrawWorkflowRow("3", "Physics & Motion", "Review the invisible body, then automatically adapt stock movement to this Humanoid.");
                DrawWorkflowRow("4", "Check Readiness", "Inspect physics and native-provider support without changing assets.");
                DrawWorkflowRow("5", "Build & Test", "Generate the native prefab, then prove it in BONELAB.");
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawNextAction()
        {
            string action;
            if (avatarInput == null)
                action = "Start here: select an AvatarCrate or Marrow Avatar prefab in Step 1.";
            else if (intakeReport == null || !intakeReport.ReadyForNpcDefinition)
                action = "Next: resolve the Step 1 messages until the Avatar state says Ready.";
            else if (definition == null)
                action = "Next: create the NPC Definition in Step 2.";
            else if (definition.AnatomyProfile == null
                     || !HasCurrentBaseline())
                action = "Next: create the automatic physics fit in Step 3A.";
            else
            {
                string previewPath = NpcPhysicsPreviewBuilder.GetOutputPath(definition);
                GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(previewPath);
                if (preview == null)
                    action = "Next: review the colored body shapes in Step 3B, then generate the preview in Step 3C.";
                else if (!NpcPhysicsPreviewBuilder.ReceiptMatches(
                             definition, previewPath, out string previewDetail))
                    action = "Next: " + previewDetail;
                else if (!HasCurrentMovementBaseline())
                    action = "Next: run the automatic movement adaptation in Step 3D.";
                else if (readinessReport == null || compatibilityReport == null)
                    action = "Next: run Check NPC Readiness in Step 4.";
                else if (!readinessReport.ReadyForBuild)
                    action = "Next: resolve the red authoring/readiness messages in Step 4, regenerate the preview if requested, then check again.";
                else if (!IsRequestedProviderReady())
                    action = "Physics is ready. Native generation is waiting on the missing toolkit/provider capabilities shown in Step 4; this is not a collider-alignment task.";
                else if (GetCurrentValidNativeReceipt() == null)
                    action = nativeReceiptInspection?.HasReceipt == true
                        ? "Next: update the generated NPC in Step 5A."
                        : "Next: generate the native NPC prefab in Step 5A.";
                else if (GetSpawnableCrateDisplayReport()?.Success != true)
                    action = "Next: prepare the GUID-bound Pallet and Spawnable Crate in Step 5B.";
                else if (palletPackReport?.Success != true
                         || !string.Equals(
                             palletPackReport.PackagingFingerprint,
                             NpcPackagingFingerprintUtility.Compute(definition),
                             StringComparison.Ordinal))
                    action = "Next: switch Unity to the selected platform if needed, then pack the Pallet in Step 5C.";
                else
                    action = "The Pallet is packed. Next: run a confirmed BONELAB spawn, movement, damage, recovery, pooling, and interaction test.";
            }

            EditorGUILayout.HelpBox(action, MessageType.Info);
        }

        private bool HasCurrentBaseline()
        {
            if (definition == null || definition.AnatomyProfile == null)
                return false;
            if (rigMappingReport == null) RefreshRigMapping();
            return rigMappingReport != null
                   && rigMappingReport.ReadyForBaseline
                   && !rigMappingReport.SourceChanged
                   && definition.AnatomyProfile.BaselineMatches(
                       rigMappingReport.CurrentSourceDependencyHash);
        }

        private bool HasCurrentMovementBaseline()
        {
            if (definition?.MovementProfile == null) return false;
            if (rigMappingReport == null) RefreshRigMapping();
            return rigMappingReport != null
                   && definition.MovementProfile.HasFittedMeasurements
                   && definition.MovementProfile.AutoFitMatches(
                       rigMappingReport.CurrentSourceDependencyHash,
                       NpcPhysicsPreviewBuilder.ComputeAuthoringFingerprint(
                           definition))
                   && HasPreparedMovementRecipe(definition.MovementProfile);
        }

        private static bool HasPreparedMovementRecipe(
            NpcMovementProfile profile)
        {
            return profile != null
                   && profile.ProviderStandingPose != null
                   && profile.ProviderMovementConfig != null
                   && !string.IsNullOrWhiteSpace(
                       profile.ProviderRecipeFingerprint);
        }

        private bool IsPreparedMovementRecipeCurrent(
            NpcMovementProfile profile,
            out string detail)
        {
            NpcMovementRecipeValidationReport validation =
                NpcMovementRecipeValidator.Validate(definition, profile);
            detail = validation.Detail;
            return validation.IsCurrent;
        }

        private bool IsRequestedProviderReady()
        {
            if (definition == null || compatibilityReport == null
                                   || !compatibilityReport.NativeNpcProviderAvailable)
                return false;
            return compatibilityReport.Supports(RequestedCapabilities());
        }

        private NpcCompatibilityCapabilities RequestedCapabilities()
        {
            return NpcCompatibilityRequirements.ForDefinition(definition);
        }

        private static void DrawWorkflowRow(string number, string title, string result)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(number + ".", EditorStyles.boldLabel, GUILayout.Width(22f));
                GUILayout.Label(title, GUILayout.Width(105f));
                GUILayout.Label(result, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawStepExplanation(
            string purpose,
            string action,
            string doneWhen)
        {
            EditorGUILayout.HelpBox(
                "What: " + purpose + "\n"
                + "Do: " + action + "\n"
                + "Done when: " + doneWhen,
                MessageType.None);
        }

        private static void DrawUpcomingStep(
            string number,
            string title,
            string explanation,
            bool foundationReady)
        {
            BeginStep(number, title, foundationReady ? "Profile ready" : "Upcoming");
            EditorGUILayout.LabelField(explanation, EditorStyles.wordWrappedLabel);
            EndStep();
        }

        private void DrawIntakeReport()
        {
            if (intakeReport == null) RefreshIntake();
            if (intakeReport == null) return;

            foreach (AvatarIntakeIssue issue in intakeReport.Issues)
            {
                MessageType type = issue.Severity == AvatarIntakeSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == AvatarIntakeSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, type);
            }

            if (avatarSource != null)
            {
                string type = intakeReport.IsAvatarCrate
                    ? "AvatarCrate -> Marrow Avatar prefab"
                    : intakeReport.IsMarrowAvatar ? "Marrow Avatar prefab"
                    : intakeReport.IsHumanoid ? "Unity Humanoid" : "Model asset";
                EditorGUILayout.LabelField(
                    $"Detected: {type} | Skinned renderers: {intakeReport.RendererCount}",
                    EditorStyles.miniLabel);
            }
        }

        private void UseSelectedAsset()
        {
            Object selected = Selection.activeObject;
            if (!(selected is GameObject) && !(selected is AvatarCrate))
            {
                SetOperation(
                    "Select an AvatarCrate, model, or prefab in the Project window first.",
                    MessageType.Warning);
                return;
            }

            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(selected)))
            {
                SetOperation("Select a Project asset, not a Scene object.", MessageType.Warning);
                return;
            }

            avatarInput = selected;
            definition = null;
            ClearReadiness();
            SetOperation("Selected " + selected.name + " for Avatar intake.", MessageType.Info);
            RefreshIntake();
        }

        private void ConfigureAsHumanoid()
        {
            try
            {
                avatarSource = MarrowAvatarImportService.ConfigureModelAsHumanoid(avatarSource);
                avatarInput = avatarSource;
                RefreshIntake();
                if (intakeReport.IsHumanoid)
                    SetOperation(
                        "The model reimported as a valid Unity Humanoid. Review any bone warnings, then create the Marrow Avatar prefab.",
                        MessageType.Info);
                else
                    SetOperation(
                        "Unity could not produce a valid Humanoid mapping automatically. Select the model, open Rig > Configure, and correct its bone map.",
                        MessageType.Error);
            }
            catch (Exception exception)
            {
                SetOperation(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void CreateMarrowAvatarPrefab()
        {
            string sourcePath = AssetDatabase.GetAssetPath(avatarSource);
            string folder = string.IsNullOrEmpty(sourcePath)
                ? "Assets"
                : System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "Assets";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Marrow Avatar Prefab",
                avatarSource.name + "Avatar",
                "prefab",
                "Create an original prefab that uses Marrow SDK's supported Avatar component.",
                folder);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                avatarSource = MarrowAvatarImportService.CreateMarrowAvatarPrefab(
                    avatarSource, path);
                avatarInput = avatarSource;
                RefreshIntake();
                SetOperation(
                    "Created the Marrow Avatar prefab. Open official fine-tuning to review body meshes, wrists, eyes, and body handles before defining the NPC.",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                SetOperation(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void CreateDefinition()
        {
            try
            {
                string folder = authoringFolder.TrimEnd('/');
                string characterFolder = folder + "/" + SafeAssetName(avatarSource.name);
                definition = NpcDefinitionFactory.Create(
                    avatarSource, author, characterFolder);
                ClearReadiness();
                RefreshRigMapping();
                SetOperation(
                    "Created the NPC Definition plus source, 16-role anatomy, movement, build, and audio profiles. NPC Audio starts Off; any Avatar clip references were reused without copying assets. The original Avatar remains untouched.",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                SetOperation(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void RefreshAvatarSnapshot()
        {
            try
            {
                if (definition == null || definition.SourceAvatar == null
                                       || definition.AvatarSourceProfile == null)
                    throw new InvalidOperationException(
                        "The NPC Definition is missing its Avatar source or source profile.");
                Undo.RegisterCompleteObjectUndo(
                    definition.AvatarSourceProfile, "Refresh NPC Avatar snapshot");
                MarrowAvatarSnapshotService.Capture(
                    definition.SourceAvatar, definition.AvatarSourceProfile);
                AssetDatabase.SaveAssets();
                ClearReadiness();
                RefreshRigMapping();
                SetOperation(
                    "Refreshed the source snapshot from the tuned Marrow Avatar. NPC-specific anatomy tuning was preserved.",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                SetOperation(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void RefreshAudioProfile()
        {
            try
            {
                if (definition == null || definition.SourceAvatar == null
                                       || definition.AudioProfile == null)
                    throw new InvalidOperationException(
                        "The NPC Definition is missing its Avatar source or Audio Profile.");
                Undo.RegisterCompleteObjectUndo(
                    definition.AudioProfile, "Refresh NPC audio references");
                NpcAudioProfileImportService.CaptureAvatarReferences(
                    definition.SourceAvatar, definition.AudioProfile);
                AssetDatabase.SaveAssets();
                ClearReadiness();
                SetOperation(
                    "Re-read Small Pain, Big Pain, Death, Jump, Small/Medium/Large Effort, Spine Impact, and Walk/Run from the source Avatar. Other custom categories were preserved, and no audio file was copied or edited.",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                SetOperation(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void CreateAudioProfile()
        {
            try
            {
                if (definition == null)
                    throw new InvalidOperationException(
                        "Select an NPC Definition before creating its Audio Profile.");
                NpcAudioProfile profile = NpcAudioProfileFactory
                    .CreateForDefinition(definition);
                Selection.activeObject = profile;
                EditorGUIUtility.PingObject(profile);
                ClearReadiness();
                SetOperation(
                    "Created and assigned an Audio Profile beside the NPC Definition. Supported Avatar sound references were filled when available. Review or edit its categories in the Inspector; NPC Audio is currently "
                    + (definition.AudioMode == NpcAudioMode.Profile
                        ? "enabled."
                        : "Off."),
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                SetOperation(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void RefreshIntake()
        {
            intakeReport = AvatarIntakeValidator.Validate(avatarInput);
            avatarSource = intakeReport.Source;
            Repaint();
        }

        private void RefreshRigMapping()
        {
            rigMappingReport = definition == null
                ? null
                : NpcRigMappingService.Validate(definition);
            Repaint();
        }

        private void FitBaseline()
        {
            NpcBaselineFitReport report = NpcBaselineFitter.Fit(
                definition, overwriteReviewed: false);
            ClearReadiness();
            if (report.Success)
            {
                AssetDatabase.SaveAssets();
                SetOperation(
                    $"Created the {(definition.IncludePhysicalJaw ? 17 : 16)}-body Avatar-fit physics baseline at {report.EyeHeightMeters:0.000} m eye height. Open the alignment workspace for visual review.",
                    MessageType.Info);
            }
            else
            {
                SetOperation(
                    report.Issues.Count == 0
                        ? "Physics baseline fitting failed."
                        : string.Join("\n", report.Issues),
                    MessageType.Error);
            }
            RefreshRigMapping();
        }

        private void GeneratePhysicsPreview()
        {
            physicsPreviewReport = NpcPhysicsPreviewBuilder.Build(definition);
            ClearReadiness();
            if (physicsPreviewReport.Success)
            {
                GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(
                    physicsPreviewReport.AssetPath);
                Selection.activeObject = preview;
                EditorGUIUtility.PingObject(preview);
                SetOperation(
                    $"Generated the Unity-only physics preview: {physicsPreviewReport.RigidbodyCount} bodies, {physicsPreviewReport.ColliderCount} primary colliders, {physicsPreviewReport.JointCount} joints, and {physicsPreviewReport.RendererCount} preserved renderer(s). This is an alignment artifact, not a spawnable NPC.",
                    MessageType.Info);
            }
            else
            {
                SetOperation(
                    physicsPreviewReport.Issues.Count == 0
                        ? "Physics preview generation failed."
                        : string.Join("\n", physicsPreviewReport.Issues),
                    MessageType.Error);
            }
        }

        private void FitMovementBaseline()
        {
            try
            {
                if (definition == null)
                    throw new InvalidOperationException(
                        "Select an NPC Definition before fitting movement.");

                bool createdProfile = definition.MovementProfile == null;
                NpcMovementProfile profile = createdProfile
                    ? NpcMovementProfileFactory.CreateForDefinition(definition)
                    : definition.MovementProfile;
                movementFitReport = NpcMovementProfileFitter.Fit(
                    definition,
                    resetReviewedTuning: true);
                if (!movementFitReport.Success)
                {
                    SetOperation(
                        movementFitReport.Issues.Count == 0
                            ? "Movement baseline fitting failed."
                            : string.Join("\n", movementFitReport.Issues),
                        MessageType.Error);
                    return;
                }

                string providerDetail;
                MessageType type = MessageType.Info;
                NpcMovementAuthoringProviderSelection selection =
                    NpcMovementAuthoringProviderRegistry.Default.Resolve(
                        definition.BuildProfile);
                if (!selection.CanPrepare)
                {
                    providerDetail = selection.Detail;
                    type = MessageType.Warning;
                }
                else
                {
                    Undo.RegisterCompleteObjectUndo(
                        profile, "Prepare NPC movement provider recipe");
                    NpcMovementAuthoringResult result =
                        selection.Provider.Prepare(definition, profile);
                    if (result == null || !result.Success)
                    {
                        string detail = result == null
                            ? "The movement provider returned no result."
                            : string.Join("\n", result.Messages);
                        providerDetail = string.IsNullOrWhiteSpace(detail)
                            ? "The movement provider could not prepare its persistent recipe."
                            : detail;
                        type = MessageType.Warning;
                    }
                    else if (profile.ProviderStandingPose == null
                             || profile.ProviderMovementConfig == null
                             || !string.Equals(
                                 profile.ProviderRecipeFingerprint,
                                 result.RecipeFingerprint,
                                 StringComparison.Ordinal))
                    {
                        providerDetail =
                            "Native movement preparation did not record both required assets and a matching receipt.";
                        type = MessageType.Warning;
                    }
                    else
                    {
                        bool current = IsPreparedMovementRecipeCurrent(
                            profile, out string validationDetail);
                        if (!current)
                        {
                            providerDetail = validationDetail;
                            type = MessageType.Warning;
                        }
                        else
                        {
                            providerDetail = result.Messages.Count == 0
                                ? "The compatible provider prepared and validated its persistent standing recipe."
                                : string.Join("\n", result.Messages);
                        }
                    }
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                ClearReadiness();
                string migration = createdProfile
                    ? " Created and linked the missing Movement Profile."
                    : string.Empty;
                SetOperation(
                    $"Calculated automatic movement from the Avatar: body {profile.BodyHeight:0.000} m, mean leg {profile.MeanLegLength:0.000} m, navigation radius {profile.NavRadius:0.000} m.{migration} Previous manual movement multipliers were removed; the compatible provider rebuilt from its native stock reference. {providerDetail} Runtime movement testing is still required.",
                    type);
            }
            catch (Exception exception)
            {
                movementFitReport = null;
                SetOperation(
                    "Movement baseline fitting could not complete: "
                    + exception.Message,
                    MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                RefreshRigMapping();
                Repaint();
            }
        }

        private void RunReadinessCheck()
        {
            try
            {
                nativeReceiptInspection = null;
                nativeBuildReport = null;
                readinessReport = NpcBuildReadinessDoctor.Validate(definition);
                compatibilityReport = NpcCompatibilityProbeRegistry.Default.Evaluate(
                    definition?.BuildProfile);
                bool handoffReady = readinessReport.ReadyForBuild
                                    && IsRequestedProviderReady();
                if (handoffReady)
                    SetOperation(
                        "Readiness check passed for the native-builder handoff. Runtime proof still comes later.",
                        MessageType.Info);
                else if (!readinessReport.ReadyForBuild)
                    SetOperation(
                        "Authoring/readiness needs attention. Fix the red Step 4 messages, regenerate the preview if requested, then check again.",
                        MessageType.Warning);
                else
                    SetOperation(
                        "Physics readiness passed. Native generation is blocked only by missing toolkit/provider capabilities; no collider-alignment changes are required.",
                        MessageType.Warning);
            }
            catch (Exception exception)
            {
                readinessReport = null;
                compatibilityReport = null;
                SetOperation(
                    "The read-only readiness check could not complete: "
                    + exception.Message,
                    MessageType.Error);
                Debug.LogException(exception);
            }
            Repaint();
        }

        private void BuildNativeNpcPrefab()
        {
            try
            {
                showNativeBuildDetails = false;
                operationMessage = null;
                nativeReceiptInspection = null;
                spawnableCrateReport = null;
                palletPackReport = null;
                nativeBuildReport = NpcNativeBuildCoordinator.Build(
                    new NpcNativeBuildRequest(
                        definition,
                        RequestedCapabilities()));
                if (nativeBuildReport.Success)
                {
                    Object output = AssetDatabase.LoadAssetAtPath<GameObject>(
                        nativeBuildReport.OutputPrefabPath);
                    Selection.activeObject = output;
                    if (output != null) EditorGUIUtility.PingObject(output);
                    operationMessage = null;
                }
            }
            catch (Exception exception)
            {
                nativeBuildReport = null;
                SetOperation(
                    "Native NPC generation could not complete: " + exception.Message,
                    MessageType.Error);
                Debug.LogException(exception);
            }
            nativeReceiptInspection = null;
            Repaint();
        }

        private void PrepareSpawnableCrate()
        {
            try
            {
                operationMessage = null;
                nativeReceiptInspection = null;
                palletPackReport = null;
                spawnableCrateReport =
                    NpcSpawnableCratePreparationCoordinator.Prepare(
                        new NpcSpawnableCratePreparationRequest(definition));
                if (spawnableCrateReport.Success)
                {
                    Object crate = AssetDatabase.LoadAssetAtPath<SpawnableCrate>(
                        spawnableCrateReport.CrateAssetPath);
                    Selection.activeObject = crate;
                    if (crate != null) EditorGUIUtility.PingObject(crate);
                }
            }
            catch (Exception exception)
            {
                spawnableCrateReport = null;
                SetOperation(
                    "Spawnable Crate preparation could not complete: "
                    + exception.Message,
                    MessageType.Error);
                Debug.LogException(exception);
            }
            nativeReceiptInspection = null;
            Repaint();
        }

        private void SwitchPackTarget(NpcTargetPlatform target)
        {
            try
            {
                bool switched = NpcPalletPackCoordinator.TrySwitchBuildTarget(
                    target, out string detail);
                SetOperation(
                    detail,
                    switched ? MessageType.Info : MessageType.Error);
            }
            catch (Exception exception)
            {
                SetOperation(
                    "Unity could not switch build target: " + exception.Message,
                    MessageType.Error);
                Debug.LogException(exception);
            }
            Repaint();
        }

        private void PackPreparedPallet()
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Marrow NPC Toolkit",
                    "Packing the complete Marrow Pallet...",
                    0.5f);
                palletPackReport = NpcPalletPackCoordinator.Pack(definition);
                if (palletPackReport.Preparation != null)
                    spawnableCrateReport = palletPackReport.Preparation;
                if (palletPackReport.Success)
                    SetOperation(
                        "The Pallet packed successfully and its output files passed completeness checks. Runtime spawn proof is still required.",
                        MessageType.Info);
                else
                {
                    string detail = palletPackReport.Messages
                        .LastOrDefault(value => value.Severity
                            == NpcPalletPackMessageSeverity.Error)?.Message;
                    SetOperation(
                        string.IsNullOrWhiteSpace(detail)
                            ? "Pallet packing did not complete. Review Step 5C."
                            : detail,
                        MessageType.Error);
                }
            }
            catch (Exception exception)
            {
                palletPackReport = null;
                SetOperation(
                    "Pallet packing could not complete: " + exception.Message,
                    MessageType.Error);
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Repaint();
        }

        private NpcNativeBuildReceipt GetStoredNativeReceipt()
        {
            return nativeReceiptInspection?.Receipt;
        }

        private NpcNativeBuildReceipt GetCurrentValidNativeReceipt()
        {
            return nativeReceiptInspection?.IsCurrent == true
                ? GetStoredNativeReceipt()
                : null;
        }

        private bool IsNativeOutputPrefabOpen()
        {
            if (definition == null) return false;
            UnityEditor.SceneManagement.PrefabStage stage =
                UnityEditor.SceneManagement.PrefabStageUtility
                    .GetCurrentPrefabStage();
            if (stage == null) return false;
            string outputPath = NpcNativeBuildCoordinator
                .GetDefaultOutputPath(definition);
            return string.Equals(
                stage.assetPath,
                outputPath,
                StringComparison.OrdinalIgnoreCase);
        }

        private void ExitGeneratedPrefabMode()
        {
            UnityEditor.SceneManagement.StageUtility.GoToMainStage();
            nativeBuildReport = null;
            nativeReceiptInspection = null;
            spawnableCrateReport = null;
            palletPackReport = null;
            operationMessage = null;
            Repaint();
        }

        private NpcSpawnableCratePreparationReport
            GetSpawnableCrateDisplayReport()
        {
            if (definition?.BuildProfile == null
                || !definition.BuildProfile.HasSpawnableAssetBindings)
                return null;
            return NpcSpawnableCratePreparationCoordinator.InspectBindings(
                definition);
        }

        private void ClearReadiness()
        {
            readinessReport = null;
            compatibilityReport = null;
            nativeBuildReport = null;
            nativeReceiptInspection = null;
            spawnableCrateReport = null;
            palletPackReport = null;
        }

        private string StepStateForAvatar()
        {
            if (intakeReport == null || avatarInput == null) return "Waiting";
            if (intakeReport.ReadyForNpcDefinition) return "Ready";
            if (intakeReport.HasErrors) return "Needs attention";
            if (intakeReport.IsHumanoid) return "Humanoid ready";
            return "Model selected";
        }

        private void SetOperation(string message, MessageType type)
        {
            operationMessage = message;
            operationMessageType = type;
        }

        private static string SafeAssetName(string value)
        {
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string clean = new string(value.Where(character => !invalid.Contains(character)).ToArray());
            return string.IsNullOrWhiteSpace(clean) ? "Character" : clean.Trim();
        }

        private static bool IsRequestedJawFitted(NpcDefinition definition)
        {
            if (definition == null || !definition.IncludePhysicalJaw) return true;

            NpcBodyRoleProfile jaw = definition.AnatomyProfile?.OptionalJaw;
            if (jaw == null
                || !jaw.Enabled
                || jaw.AlignmentState == NpcAlignmentState.Unseeded
                || jaw.ColliderShape != NpcColliderShape.Box)
                return false;

            Vector3 size = jaw.ColliderSize;
            return IsPositiveFinite(size.x)
                   && IsPositiveFinite(size.y)
                   && IsPositiveFinite(size.z);
        }

        private static bool IsPositiveFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        }

        private static void BeginStep(string number, string title, string state)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(number + ". " + title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(state, EditorStyles.miniLabel, GUILayout.Width(110f));
            }
            EditorGUILayout.Space(3f);
        }

        private static void EndStep()
        {
            EditorGUILayout.EndVertical();
        }
    }
}
