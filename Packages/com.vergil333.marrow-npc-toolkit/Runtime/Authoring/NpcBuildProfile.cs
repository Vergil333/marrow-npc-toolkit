using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    public enum NpcTargetPlatform
    {
        Quest,
        Windows,
    }

    [CreateAssetMenu(
        fileName = "NpcBuildProfile",
        menuName = "Marrow NPC Toolkit/NPC Build Profile",
        order = 12)]
    public sealed class NpcBuildProfile : ScriptableObject
    {
        [Header("Public Metadata")]
        [SerializeField] private string author;
        [SerializeField] private string palletTitle;
        [SerializeField] private string crateTitle;
        [SerializeField, TextArea] private string description;
        [SerializeField] private string version = "0.1.0";

        [Header("Build")]
        [SerializeField] private NpcTargetPlatform targetPlatform = NpcTargetPlatform.Quest;
        [SerializeField] private string generatedAssetFolder;
        [SerializeField] private string compatibilityProfileId =
            NpcToolkitVersion.InitialCompatibilityProfile;
        [SerializeField, HideInInspector] private string palletAssetGuid;
        [SerializeField, HideInInspector] private string spawnableCrateAssetGuid;

        public string Author => author;
        public string PalletTitle => palletTitle;
        public string CrateTitle => crateTitle;
        public string Description => description;
        public string Version => version;
        public NpcTargetPlatform TargetPlatform => targetPlatform;
        public string GeneratedAssetFolder => generatedAssetFolder;
        public string CompatibilityProfileId => compatibilityProfileId;
        public string PalletAssetGuid => palletAssetGuid ?? string.Empty;
        public string SpawnableCrateAssetGuid =>
            spawnableCrateAssetGuid ?? string.Empty;
        public bool HasSpawnableAssetBindings =>
            !string.IsNullOrWhiteSpace(PalletAssetGuid)
            && !string.IsNullOrWhiteSpace(SpawnableCrateAssetGuid);

        public void Initialize(string authorName, string characterName, string assetFolder)
        {
            author = string.IsNullOrWhiteSpace(authorName) ? "Author" : authorName.Trim();
            palletTitle = characterName + " NPC";
            crateTitle = characterName + " NPC";
            description = characterName + " native-style humanoid NPC.";
            version = "0.1.0";
            targetPlatform = NpcTargetPlatform.Quest;
            generatedAssetFolder = assetFolder.TrimEnd('/') + "/Generated";
            compatibilityProfileId = NpcToolkitVersion.InitialCompatibilityProfile;
            palletAssetGuid = string.Empty;
            spawnableCrateAssetGuid = string.Empty;
        }

        /// <summary>
        /// Stores Unity asset GUIDs only. Runtime authoring data deliberately
        /// has no dependency on Marrow Pallet or Crate types.
        /// </summary>
        public void SetSpawnableAssetBindings(
            string palletGuid,
            string spawnableCrateGuid)
        {
            palletAssetGuid = (palletGuid ?? string.Empty).Trim();
            spawnableCrateAssetGuid = (spawnableCrateGuid ?? string.Empty).Trim();
        }
    }
}
