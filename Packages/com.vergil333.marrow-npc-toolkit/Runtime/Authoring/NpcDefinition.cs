using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    public enum NpcAvatarSourceKind
    {
        HumanoidPrefab,
        MarrowAvatarPrefab,
    }

    public enum NpcAudioMode
    {
        Silent = 0,
        Profile = 1,
    }

    [CreateAssetMenu(
        fileName = "NpcDefinition",
        menuName = "Marrow NPC Toolkit/NPC Definition",
        order = 10)]
    public sealed class NpcDefinition : ScriptableObject
    {
        [Header("Source")]
        [SerializeField] private GameObject sourceAvatar;
        [SerializeField] private NpcAvatarSourceKind sourceKind;
        [SerializeField, HideInInspector] private string sourceAssetGuid;
        [SerializeField, HideInInspector] private string sourceDependencyHash;

        [Header("Authoring Profiles")]
        [SerializeField] private NpcAvatarSourceProfile avatarSourceProfile;
        [SerializeField] private NpcAnatomyProfile anatomyProfile;
        [SerializeField] private NpcMovementProfile movementProfile;
        [SerializeField] private NpcBuildProfile buildProfile;
        [SerializeField] private NpcAudioProfile audioProfile;

        [Header("Optional Native Modules")]
        [SerializeField] private bool includePhysicalJaw = true;
        [SerializeField] private bool includeGaze = true;
        [SerializeField] private bool includeHandGrips = true;
        // Kept so older serialized definitions remain readable. Audio now defaults
        // explicitly to Silent until an author selects Profile mode.
        [SerializeField, HideInInspector] private bool includeNpcAudio;
        [SerializeField] private NpcAudioMode audioMode = NpcAudioMode.Silent;
        [SerializeField, Tooltip(
            "Generate spring-driven secondary bodies from the two Breast Soft "
            + "Body bones on the source Marrow Avatar. Disabled by "
            + "default because not every humanoid Avatar defines them.")]
        private bool includeSecondaryMotion;

        [SerializeField, HideInInspector] private string createdWithToolkitVersion;

        public GameObject SourceAvatar => sourceAvatar;
        public NpcAvatarSourceKind SourceKind => sourceKind;
        public string SourceAssetGuid => sourceAssetGuid;
        public string SourceDependencyHash => sourceDependencyHash;
        public NpcAvatarSourceProfile AvatarSourceProfile => avatarSourceProfile;
        public NpcAnatomyProfile AnatomyProfile => anatomyProfile;
        public NpcMovementProfile MovementProfile
        {
            get => movementProfile;
            set => movementProfile = value;
        }
        public NpcBuildProfile BuildProfile => buildProfile;
        public NpcAudioProfile AudioProfile
        {
            get => audioProfile;
            set => audioProfile = value;
        }
        public NpcAudioMode AudioMode
        {
            get => audioMode;
            set
            {
                audioMode = value;
                includeNpcAudio = value == NpcAudioMode.Profile;
            }
        }
        public string CreatedWithToolkitVersion => createdWithToolkitVersion;

        public bool IncludePhysicalJaw
        {
            get => includePhysicalJaw;
            set => includePhysicalJaw = value;
        }

        public bool IncludeGaze
        {
            get => includeGaze;
            set => includeGaze = value;
        }

        public bool IncludeHandGrips
        {
            get => includeHandGrips;
            set => includeHandGrips = value;
        }

        public bool IncludeNpcAudio
        {
            get => audioMode == NpcAudioMode.Profile;
            set => AudioMode = value ? NpcAudioMode.Profile : NpcAudioMode.Silent;
        }

        public bool IncludeSecondaryMotion
        {
            get => includeSecondaryMotion;
            set => includeSecondaryMotion = value;
        }

        public void Initialize(
            GameObject avatar,
            NpcAvatarSourceKind avatarSourceKind,
            NpcAvatarSourceProfile sourceProfile,
            NpcAnatomyProfile anatomy,
            NpcBuildProfile build,
            string assetGuid,
            string dependencyHash,
            NpcAudioProfile audio = null,
            NpcMovementProfile movement = null)
        {
            sourceAvatar = avatar;
            sourceKind = avatarSourceKind;
            avatarSourceProfile = sourceProfile;
            anatomyProfile = anatomy;
            movementProfile = movement;
            buildProfile = build;
            audioProfile = audio;
            audioMode = NpcAudioMode.Silent;
            includeNpcAudio = false;
            includeSecondaryMotion = false;
            sourceAssetGuid = assetGuid ?? string.Empty;
            sourceDependencyHash = dependencyHash ?? string.Empty;
            createdWithToolkitVersion = NpcToolkitVersion.Current;
        }
    }
}
