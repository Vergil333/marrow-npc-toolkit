using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using MarrowAvatar = SLZ.VRMK.Avatar;

namespace Vergil333.MarrowNpcToolkit.Editor.Authoring
{
    /// <summary>
    /// Reuses existing Marrow Avatar audio references as NPC authoring input.
    /// The service never copies, imports, or edits an AudioClip or
    /// AudioVarianceData asset.
    /// </summary>
    public static class NpcAudioProfileImportService
    {
        public static void CaptureAvatarReferences(
            GameObject avatarPrefab,
            NpcAudioProfile destination)
        {
            if (avatarPrefab == null) throw new ArgumentNullException(nameof(avatarPrefab));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            MarrowAvatar[] avatars = avatarPrefab.GetComponents<MarrowAvatar>();
            if (avatars.Length != 1)
                throw new InvalidOperationException(
                    "Expected exactly one Marrow Avatar component on the source prefab; found "
                    + avatars.Length + ".");

            var serialized = new SerializedObject(avatars[0]);
            AudioClip[] smallPain = ReadVariance(serialized, "smallPain");
            AudioClip[] bigPain = ReadVariance(serialized, "bigPain");
            AudioClip[] dying = ReadVariance(serialized, "dying");
            AudioClip[] dead = ReadVariance(serialized, "dead");
            AudioClip[] jump = ReadVariance(serialized, "bigEffort");
            AudioClip[] smallEffort = ReadVariance(serialized, "smallEffort");
            AudioClip[] recovery = ReadVariance(serialized, "recovery");
            AudioClip[] highFall = ReadVariance(serialized, "highFallOntoFeet");

            destination.SetClips(NpcAudioEvent.PainSmall, smallPain);
            destination.SetClips(NpcAudioEvent.PainBig, bigPain);
            destination.SetClips(NpcAudioEvent.Death, dying.Concat(dead));
            destination.SetClips(NpcAudioEvent.Jump, jump);
            destination.SetClips(
                NpcAudioEvent.SmallEffort,
                smallEffort.Length > 0 ? smallEffort : jump);
            destination.SetClips(NpcAudioEvent.MediumEffort, recovery);
            destination.SetClips(NpcAudioEvent.LargeEffort, highFall);
            // Avatar high-fall clips are living landing vocals. Physical-impact
            // channels can remain active on dead ragdolls and have no equivalent
            // Avatar category, so refreshing Avatar audio must not alter them.
            float footstepVolume = destination.FootstepVolumeMultiplier;
            destination.SetFootsteps(
                ReadVariance(serialized, "footstepsWalk"),
                ReadVariance(serialized, "footstepsJog"),
                footstepVolume);
            const string importNote =
                "Clip references were reused from the source Marrow Avatar. "
                + "No audio asset was copied or edited.";
            const string legacyImportNote =
                "Clip references were reused from the source Marrow Avatar. "
                + "No audio asset was copied. Verify source, credit, and permission before distribution.";
            string notes = destination.Notes ?? string.Empty;
            if (notes.IndexOf(legacyImportNote, StringComparison.Ordinal) >= 0)
                notes = notes.Replace(legacyImportNote, importNote);
            if (notes.IndexOf(importNote, StringComparison.Ordinal) < 0)
                notes = string.IsNullOrWhiteSpace(notes)
                    ? importNote
                    : notes.TrimEnd() + "\n\n" + importNote;
            destination.SetProvenance(
                destination.Language,
                destination.Source,
                destination.Credit,
                destination.LicenseOrPermission,
                notes);
            EditorUtility.SetDirty(destination);
        }

        private static AudioClip[] ReadVariance(
            SerializedObject avatar,
            string propertyName)
        {
            SerializedProperty slot = avatar.FindProperty(propertyName);
            UnityEngine.Object variance = slot?.objectReferenceValue;
            if (variance == null) return Array.Empty<AudioClip>();

            var serialized = new SerializedObject(variance);
            SerializedProperty clips = serialized.FindProperty("audioClips");
            if (clips == null || !clips.isArray)
                throw new InvalidOperationException(
                    "Avatar sound slot '" + propertyName
                    + "' does not expose an AudioVarianceData.audioClips array.");

            var values = new List<AudioClip>(clips.arraySize);
            for (int index = 0; index < clips.arraySize; index++)
            {
                UnityEngine.Object value = clips.GetArrayElementAtIndex(index)
                    .objectReferenceValue;
                if (value != null && !(value is AudioClip))
                    throw new InvalidOperationException(
                        "Avatar sound slot '" + propertyName + "' contains a non-AudioClip at "
                        + index + ".");
                values.Add(value as AudioClip);
            }
            return values.ToArray();
        }
    }
}
