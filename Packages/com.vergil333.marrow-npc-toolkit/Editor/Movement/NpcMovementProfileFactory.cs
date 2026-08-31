using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Movement
{
    /// <summary>
    /// Adds the Movement Profile introduced after the original NPC Definition
    /// format. Existing definitions are upgraded beside their other authoring
    /// assets; the source Avatar is never copied or changed.
    /// </summary>
    public static class NpcMovementProfileFactory
    {
        public static NpcMovementProfile CreateForDefinition(
            NpcDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.MovementProfile != null)
                return definition.MovementProfile;

            string definitionPath = (AssetDatabase.GetAssetPath(definition)
                                     ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(definitionPath)
                || !definitionPath.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Save the NPC Definition under Assets before creating its Movement Profile.");

            string folder = Path.GetDirectoryName(definitionPath)
                ?.Replace('\\', '/') ?? "Assets";
            string sourceName = definition.SourceAvatar == null
                ? definition.name
                : definition.SourceAvatar.name;
            string safeName = SanitizeFileName(sourceName);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + safeName + "NpcMovement.asset");

            var profile = ScriptableObject.CreateInstance<NpcMovementProfile>();
            profile.name = safeName + " NPC Movement";
            profile.ResetToDefaults();
            AssetDatabase.CreateAsset(profile, assetPath);
            try
            {
                Undo.RegisterCreatedObjectUndo(
                    profile, "Create NPC Movement Profile");
                Undo.RecordObject(definition, "Assign NPC Movement Profile");
                var serialized = new SerializedObject(definition);
                SerializedProperty property = serialized.FindProperty(
                    "movementProfile");
                if (property == null)
                    throw new InvalidOperationException(
                        "The NPC Definition does not expose its Movement Profile field.");
                property.objectReferenceValue = profile;
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
            }
            catch
            {
                if (AssetDatabase.LoadAssetAtPath<NpcMovementProfile>(assetPath)
                    == profile)
                    AssetDatabase.DeleteAsset(assetPath);
                throw;
            }

            return profile;
        }

        private static string SanitizeFileName(string value)
        {
            value = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid.ToString(), string.Empty);
            return string.IsNullOrWhiteSpace(value)
                ? "Character"
                : value.Trim();
        }
    }
}
