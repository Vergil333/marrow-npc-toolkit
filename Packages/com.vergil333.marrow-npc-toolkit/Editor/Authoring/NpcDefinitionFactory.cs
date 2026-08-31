using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.AvatarIntake;
using MarrowAvatar = SLZ.VRMK.Avatar;

namespace Vergil333.MarrowNpcToolkit.Editor.Authoring
{
    public static class NpcDefinitionFactory
    {
        public static NpcDefinition Create(
            GameObject avatarPrefab,
            string author,
            string assetFolder)
        {
            if (avatarPrefab == null) throw new ArgumentNullException(nameof(avatarPrefab));
            string sourcePath = AssetDatabase.GetAssetPath(avatarPrefab);
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new InvalidOperationException("The avatar must be a persistent Project asset.");
            if (!assetFolder.StartsWith("Assets/", StringComparison.Ordinal)
                && assetFolder != "Assets")
                throw new InvalidOperationException("NPC authoring assets must be created under Assets/.");

            EnsureAssetFolder(assetFolder);
            string safeName = SanitizeFileName(avatarPrefab.name);
            string anatomyPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolder}/{safeName}NpcAnatomy.asset");
            string avatarProfilePath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolder}/{safeName}NpcAvatarSource.asset");
            string movementPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolder}/{safeName}NpcMovement.asset");
            string buildPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolder}/{safeName}NpcBuild.asset");
            string audioPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolder}/{safeName}NpcAudio.asset");
            string definitionPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolder}/{safeName}NpcDefinition.asset");

            var anatomy = ScriptableObject.CreateInstance<NpcAnatomyProfile>();
            anatomy.name = safeName + " NPC Anatomy";
            anatomy.ResetToHumanoidDefaults();

            var avatarProfile = ScriptableObject.CreateInstance<NpcAvatarSourceProfile>();
            avatarProfile.name = safeName + " NPC Avatar Source";
            MarrowAvatarSnapshotService.Capture(avatarPrefab, avatarProfile);

            var movement = ScriptableObject.CreateInstance<NpcMovementProfile>();
            movement.name = safeName + " NPC Movement";
            movement.ResetToDefaults();

            var build = ScriptableObject.CreateInstance<NpcBuildProfile>();
            build.name = safeName + " NPC Build";
            build.Initialize(author, avatarPrefab.name, assetFolder);

            var audio = ScriptableObject.CreateInstance<NpcAudioProfile>();
            audio.name = safeName + " NPC Audio";
            if (avatarPrefab.GetComponent<MarrowAvatar>() != null)
            {
                try
                {
                    NpcAudioProfileImportService.CaptureAvatarReferences(
                        avatarPrefab, audio);
                }
                catch (Exception exception)
                {
                    // Audio is optional and starts Silent. A provider variant
                    // with a different Avatar-audio schema must not block the
                    // otherwise valid NPC definition workflow.
                    Debug.LogWarning(
                        "Created an empty NPC Audio Profile because Avatar audio references "
                        + "could not be read: " + exception.GetType().Name + ": "
                        + exception.Message,
                        avatarPrefab);
                }
            }

            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            definition.name = safeName + " NPC Definition";
            var sourceKind = avatarPrefab.GetComponent<MarrowAvatar>() != null
                ? NpcAvatarSourceKind.MarrowAvatarPrefab
                : NpcAvatarSourceKind.HumanoidPrefab;
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            string dependencyHash = AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
            definition.Initialize(
                avatarPrefab,
                sourceKind,
                avatarProfile,
                anatomy,
                build,
                guid,
                dependencyHash,
                audio,
                movement);

            AssetDatabase.CreateAsset(avatarProfile, avatarProfilePath);
            AssetDatabase.CreateAsset(anatomy, anatomyPath);
            AssetDatabase.CreateAsset(movement, movementPath);
            AssetDatabase.CreateAsset(build, buildPath);
            AssetDatabase.CreateAsset(audio, audioPath);
            AssetDatabase.CreateAsset(definition, definitionPath);

            EditorUtility.SetDirty(avatarProfile);
            EditorUtility.SetDirty(anatomy);
            EditorUtility.SetDirty(movement);
            EditorUtility.SetDirty(build);
            EditorUtility.SetDirty(audio);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = definition;
            EditorGUIUtility.PingObject(definition);
            return definition;
        }

        private static void EnsureAssetFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
                throw new InvalidOperationException("Asset folder must begin with Assets.");

            string current = "Assets";
            for (int index = 1; index < parts.Length; index++)
            {
                string next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(value) ? "Character" : value.Trim();
        }
    }
}
