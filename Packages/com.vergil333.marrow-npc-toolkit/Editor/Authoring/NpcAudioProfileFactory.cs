using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using MarrowAvatar = SLZ.VRMK.Avatar;

namespace Vergil333.MarrowNpcToolkit.Editor.Authoring
{
    /// <summary>
    /// Adds the Audio Profile introduced after the first NPC Definition format
    /// to an existing authoring set. The operation creates only a small
    /// ScriptableObject and reuses persistent clip references; it never copies
    /// or imports audio files.
    /// </summary>
    public static class NpcAudioProfileFactory
    {
        public static NpcAudioProfile CreateForDefinition(
            NpcDefinition definition,
            bool reuseAvatarReferences = true)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.AudioProfile != null)
                return definition.AudioProfile;

            string definitionPath = (AssetDatabase.GetAssetPath(definition)
                                     ?? string.Empty).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(definitionPath)
                || (!definitionPath.StartsWith("Assets/", StringComparison.Ordinal)
                    && !string.Equals(definitionPath, "Assets", StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    "Save the NPC Definition under Assets before creating its Audio Profile.");

            string folder = Path.GetDirectoryName(definitionPath)
                ?.Replace('\\', '/') ?? "Assets";
            string sourceName = definition.SourceAvatar == null
                ? definition.name
                : definition.SourceAvatar.name;
            string safeName = SanitizeFileName(sourceName);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + safeName + "NpcAudio.asset");

            var profile = ScriptableObject.CreateInstance<NpcAudioProfile>();
            profile.name = safeName + " NPC Audio";
            if (reuseAvatarReferences
                && definition.SourceKind == NpcAvatarSourceKind.MarrowAvatarPrefab
                && definition.SourceAvatar != null
                && definition.SourceAvatar.GetComponent<MarrowAvatar>() != null)
            {
                try
                {
                    NpcAudioProfileImportService.CaptureAvatarReferences(
                        definition.SourceAvatar, profile);
                }
                catch (Exception exception)
                {
                    // Audio remains optional. A provider-specific Avatar schema
                    // must not prevent creation of an empty, editable profile.
                    Debug.LogWarning(
                        "Created an empty NPC Audio Profile because Avatar audio "
                        + "references could not be read: "
                        + exception.GetType().Name + ": " + exception.Message,
                        definition.SourceAvatar);
                }
            }

            AssetDatabase.CreateAsset(profile, assetPath);
            try
            {
                Undo.RegisterCreatedObjectUndo(profile, "Create NPC Audio Profile");
                Undo.RecordObject(definition, "Assign NPC Audio Profile");
                definition.AudioProfile = profile;
                // Deliberately keep AudioMode unchanged. Existing definitions
                // remain Silent until their author reviews the clips and opts in.
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
            }
            catch
            {
                if (AssetDatabase.LoadAssetAtPath<NpcAudioProfile>(assetPath) == profile)
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
            return string.IsNullOrWhiteSpace(value) ? "Character" : value.Trim();
        }
    }
}
