using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Project-local Patch 6 binding for the public, patch-neutral NPC Audio
    /// Profile. The profile and every AudioClip remain project assets; this
    /// provider only writes their existing references into the generated NPC.
    /// </summary>
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private static readonly NpcAudioEvent[] NativeAudioEventOrder =
        {
            NpcAudioEvent.Agro,
            NpcAudioEvent.UnAgro,
            NpcAudioEvent.PainSmall,
            NpcAudioEvent.PainBig,
            NpcAudioEvent.Death,
            NpcAudioEvent.JumpCharge,
            NpcAudioEvent.Jump,
            NpcAudioEvent.SmallEffort,
            NpcAudioEvent.MediumEffort,
            NpcAudioEvent.LargeEffort,
            NpcAudioEvent.Attack1,
            NpcAudioEvent.AttackLand1,
            NpcAudioEvent.Attack2,
            NpcAudioEvent.ImpactHead,
            NpcAudioEvent.ImpactSpine,
            NpcAudioEvent.ImpactLimb,
        };

        private static readonly string[] NativeAudioEventFields =
        {
            "agro",
            "unAgro",
            "painSmall",
            "painBig",
            "death",
            "jumpCharge",
            "jump",
            "smallEffort",
            "mediumEffort",
            "largeEffort",
            "attack1",
            "attackLand1",
            "attack2",
            "impactHead",
            "impactSpine",
            "impactLimb",
        };

        private static bool RequiresAudioShell(
            Vergil333.MarrowNpcToolkit.Editor.Compatibility
                .NpcCompatibilityCapabilities capabilities)
        {
            return (capabilities
                    & Vergil333.MarrowNpcToolkit.Editor.Compatibility
                        .NpcCompatibilityCapabilities.Audio) != 0;
        }

        private static bool TryPreflightAudioBuild(out string detail)
        {
            // Definition-specific profile completeness is deliberately checked
            // by readiness and again at build time. A compatibility probe has no
            // selected definition, so its only honest claim is structural.
            detail = "The Patch 6 provider can bind persistent project-owned "
                     + "Audio Profile clips to PowerLegs and FootstepSFX. "
                     + "The selected NPC Definition is still responsible for "
                     + "supplying a ready profile.";
            return true;
        }

        private static NativeAudioShell ConfigureAudioShell(
            NpcDefinition definition,
            NativeBehaviourShell behaviourShell)
        {
            NpcAudioProfile profile = RequireNativeAudioProfile(definition);
            ConfigureNativePowerAudio(
                behaviourShell.PowerLegs,
                behaviourShell.ImpactSource,
                profile);
            ConfigureNativeFootstepAudio(behaviourShell.FootstepSfx, profile);
            ValidateNativePowerAudioState(
                definition, behaviourShell.PowerLegs, behaviourShell.ImpactSource);
            ValidateNativeFootstepAudioState(
                definition, behaviourShell.FootstepSfx);
            return new NativeAudioShell(profile);
        }

        private static NativeAudioShell ResolveAudioShell(
            NpcDefinition definition,
            NativeBehaviourShell behaviourShell)
        {
            NpcAudioProfile profile = RequireNativeAudioProfile(definition);
            ValidateNativePowerAudioState(
                definition, behaviourShell.PowerLegs, behaviourShell.ImpactSource);
            ValidateNativeFootstepAudioState(
                definition, behaviourShell.FootstepSfx);
            return new NativeAudioShell(profile);
        }

        private static void ConfigureNativePowerAudio(
            Component powerLegs,
            AudioSource impactSource,
            NpcAudioProfile profile)
        {
            var serialized = new SerializedObject(powerLegs);
            for (int index = 0; index < NativeAudioEventOrder.Length; index++)
                SetNativeAudioArray(
                    serialized,
                    "sfx." + NativeAudioEventFields[index],
                    profile.GetClips(NativeAudioEventOrder[index]));
            SetObject(serialized, "sfx.dotLoop1", profile.DotLoop1);
            SetObject(
                serialized,
                "sfx.agroMovementLoop",
                profile.AgroMovementLoop);
            SetObject(serialized, "sfx.movementLoop", profile.MovementLoop);
            SetObject(serialized, "sfx.impactSource", impactSource);
            SetFloat(serialized, "sfx.pitchMultiplier", profile.PitchMultiplier);

            // Voice playback does not enable the separate FaceAnim subsystem.
            SetIntIfPresent(serialized, "faceAnim.faceAnimEnabled", 0);
            foreach (string field in new[]
                     {
                         "greetings", "agros", "unAgros", "deaths",
                         "painSmalls", "painBigs", "attack1s", "efforts",
                         "eventLines",
                     })
                SetArraySizeIfPresent(serialized, "faceAnim." + field, 0);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureNativeFootstepAudio(
            Component footstepSfx,
            NpcAudioProfile profile)
        {
            var serialized = new SerializedObject(footstepSfx);
            SetNativeAudioArray(
                serialized, "walkConcrete", profile.WalkConcrete);
            SetNativeAudioArray(
                serialized, "runConcrete", profile.RunConcrete);
            SetFloat(
                serialized,
                "volumeMult",
                profile.FootstepVolumeMultiplier);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void ValidateNativePowerAudioState(
            NpcDefinition definition,
            Component powerLegs,
            AudioSource impactSource)
        {
            if (definition == null)
                throw new InvalidOperationException(
                    "Native audio validation requires an NPC Definition.");
            if (powerLegs == null || impactSource == null)
                throw new InvalidOperationException(
                    "Native audio validation requires PowerLegs and ImpactSrc.");

            var serialized = new SerializedObject(powerLegs);
            if (Require(serialized, "sfx.impactSource").objectReferenceValue
                != impactSource)
                throw new InvalidOperationException(
                    "PowerLegs.sfx.impactSource is not its direct ImpactSrc.");
            ValidateNativeImpactSource(impactSource);

            if (definition.AudioMode == NpcAudioMode.Silent)
            {
                for (int index = 0; index < NativeAudioEventFields.Length; index++)
                    ValidateNativeAudioArray(
                        serialized,
                        "sfx." + NativeAudioEventFields[index],
                        Array.Empty<AudioClip>(),
                        "silent " + NativeAudioEventFields[index]);
                foreach (string field in new[]
                         {
                             "dotLoop1", "agroMovementLoop", "movementLoop",
                         })
                    if (Require(serialized, "sfx." + field).objectReferenceValue
                        != null)
                        throw new InvalidOperationException(
                            "Silent native audio requires sfx." + field + " to be null.");
                if (!NearNativeAudio(
                        Require(serialized, "sfx.pitchMultiplier").floatValue,
                        1f))
                    throw new InvalidOperationException(
                        "Silent native audio requires pitchMultiplier 1.");
                ValidateNativeFaceAnimationDisabled(serialized);
                return;
            }

            NpcAudioProfile profile = RequireNativeAudioProfile(definition);
            for (int index = 0; index < NativeAudioEventOrder.Length; index++)
                ValidateNativeAudioArray(
                    serialized,
                    "sfx." + NativeAudioEventFields[index],
                    profile.GetClips(NativeAudioEventOrder[index]),
                    NativeAudioEventFields[index]);
            ValidateNativeAudioObject(
                serialized, "sfx.dotLoop1", profile.DotLoop1);
            ValidateNativeAudioObject(
                serialized,
                "sfx.agroMovementLoop",
                profile.AgroMovementLoop);
            ValidateNativeAudioObject(
                serialized,
                "sfx.movementLoop",
                profile.MovementLoop);
            if (!NearNativeAudio(
                    Require(serialized, "sfx.pitchMultiplier").floatValue,
                    profile.PitchMultiplier))
                throw new InvalidOperationException(
                    "PowerLegs audio pitch differs from the NPC Audio Profile.");
            ValidateNativeFaceAnimationDisabled(serialized);
        }

        internal static void ValidateNativeFootstepAudioState(
            NpcDefinition definition,
            Component footstepSfx)
        {
            if (definition == null || footstepSfx == null)
                throw new InvalidOperationException(
                    "Native footstep validation requires a definition and FootstepSFX.");
            var serialized = new SerializedObject(footstepSfx);
            if (definition.AudioMode == NpcAudioMode.Silent)
            {
                if (!NearNativeAudio(
                        Require(serialized, "volumeMult").floatValue,
                        1f))
                    throw new InvalidOperationException(
                        "Silent FootstepSFX volume multiplier must be 1.");
                foreach (string path in new[] { "walkConcrete", "runConcrete" })
                {
                    SerializedProperty array = Require(serialized, path);
                    if (!array.isArray || array.arraySize != 6)
                        throw new InvalidOperationException(
                            "Silent FootstepSFX." + path
                            + " must retain the six-slot Patch 6 baseline.");
                    for (int index = 0; index < array.arraySize; index++)
                        if (array.GetArrayElementAtIndex(index).objectReferenceValue
                            != null)
                            throw new InvalidOperationException(
                                "Silent FootstepSFX." + path
                                + " entries must be null.");
                }
                return;
            }

            NpcAudioProfile profile = RequireNativeAudioProfile(definition);
            ValidateNativeAudioArray(
                serialized,
                "walkConcrete",
                profile.WalkConcrete,
                "walking footsteps");
            ValidateNativeAudioArray(
                serialized,
                "runConcrete",
                profile.RunConcrete,
                "running footsteps");
            if (!NearNativeAudio(
                    Require(serialized, "volumeMult").floatValue,
                    profile.FootstepVolumeMultiplier))
                throw new InvalidOperationException(
                    "FootstepSFX volume differs from the NPC Audio Profile.");
        }

        internal static IEnumerable<UnityEngine.Object> NativeAudioAssets(
            NpcDefinition definition)
        {
            if (definition == null || definition.AudioMode != NpcAudioMode.Profile)
                yield break;
            NpcAudioProfile profile = RequireNativeAudioProfile(definition);
            yield return profile;
            foreach (NpcAudioEvent audioEvent in NativeAudioEventOrder)
                foreach (AudioClip clip in profile.GetClips(audioEvent))
                    yield return clip;
            foreach (AudioClip clip in profile.WalkConcrete)
                yield return clip;
            foreach (AudioClip clip in profile.RunConcrete)
                yield return clip;
            if (profile.DotLoop1 != null) yield return profile.DotLoop1;
            if (profile.AgroMovementLoop != null)
                yield return profile.AgroMovementLoop;
            if (profile.MovementLoop != null) yield return profile.MovementLoop;
        }

        private static void AppendAudioFingerprint(
            StringBuilder text,
            NativeAudioShell shell)
        {
            if (shell == null || shell.Profile == null)
                throw new InvalidOperationException(
                    "The requested native Audio shell was not resolved.");
            NpcAudioProfile profile = shell.Profile;
            text.Append("audio=profile|")
                .Append("profile=").Append(StableNativeAudioAssetId(profile)).Append('|')
                .Append("pitch=").Append(NativeAudioFloat(profile.PitchMultiplier))
                .Append('|')
                .Append("footstepVolume=")
                .Append(NativeAudioFloat(profile.FootstepVolumeMultiplier))
                .Append('|');
            for (int index = 0; index < NativeAudioEventOrder.Length; index++)
                AppendNativeAudioList(
                    text,
                    NativeAudioEventFields[index],
                    profile.GetClips(NativeAudioEventOrder[index]));
            AppendNativeAudioList(text, "walkConcrete", profile.WalkConcrete);
            AppendNativeAudioList(text, "runConcrete", profile.RunConcrete);
            text.Append("dotLoop1=")
                .Append(StableNativeAudioAssetId(profile.DotLoop1)).Append('|')
                .Append("agroMovementLoop=")
                .Append(StableNativeAudioAssetId(profile.AgroMovementLoop)).Append('|')
                .Append("movementLoop=")
                .Append(StableNativeAudioAssetId(profile.MovementLoop)).Append('|');
        }

        private static NpcAudioProfile RequireNativeAudioProfile(
            NpcDefinition definition)
        {
            if (definition == null || definition.AudioMode != NpcAudioMode.Profile)
                throw new InvalidOperationException(
                    "The Audio capability requires NPC Audio mode Profile.");
            NpcAudioProfile profile = definition.AudioProfile;
            RequireNativeAudioAsset(profile, "NPC Audio Profile");
            if (profile.PainSmall.Count == 0
                || profile.PainBig.Count == 0
                || profile.Death.Count == 0)
                throw new InvalidOperationException(
                    "NPC Audio Profile requires non-empty Pain Small, Pain Big, "
                    + "and Death groups.");

            foreach (NpcAudioEvent audioEvent in NativeAudioEventOrder)
                ValidateNativeAudioClips(
                    profile.GetClips(audioEvent), audioEvent.ToString());
            ValidateNativeAudioClips(profile.WalkConcrete, "Walk Concrete");
            ValidateNativeAudioClips(profile.RunConcrete, "Run Concrete");
            if ((profile.WalkConcrete.Count == 0)
                != (profile.RunConcrete.Count == 0))
                throw new InvalidOperationException(
                    "NPC Audio Profile footsteps require both walking and running "
                    + "groups, or neither group.");
            RequireOptionalNativeAudioAsset(profile.DotLoop1, "DOT Loop");
            RequireOptionalNativeAudioAsset(
                profile.AgroMovementLoop, "Agro Movement Loop");
            RequireOptionalNativeAudioAsset(profile.MovementLoop, "Movement Loop");
            if (!FiniteNativeAudio(profile.PitchMultiplier)
                || profile.PitchMultiplier <= 0f)
                throw new InvalidOperationException(
                    "NPC Audio Profile pitch must be finite and greater than zero.");
            if (!FiniteNativeAudio(profile.FootstepVolumeMultiplier)
                || profile.FootstepVolumeMultiplier < 0f)
                throw new InvalidOperationException(
                    "NPC Audio Profile footstep volume must be finite and non-negative.");
            return profile;
        }

        private static void SetNativeAudioArray(
            SerializedObject serialized,
            string path,
            IReadOnlyList<AudioClip> clips)
        {
            SerializedProperty array = Require(serialized, path);
            if (!array.isArray)
                throw new InvalidOperationException(path + " is not an AudioClip array.");
            array.arraySize = clips.Count;
            for (int index = 0; index < clips.Count; index++)
                array.GetArrayElementAtIndex(index).objectReferenceValue = clips[index];
        }

        private static void ValidateNativeAudioArray(
            SerializedObject serialized,
            string path,
            IReadOnlyList<AudioClip> expected,
            string label)
        {
            SerializedProperty array = Require(serialized, path);
            if (!array.isArray || array.arraySize != expected.Count)
                throw new InvalidOperationException(
                    label + " clip count differs from the NPC Audio Profile.");
            for (int index = 0; index < expected.Count; index++)
                if (array.GetArrayElementAtIndex(index).objectReferenceValue
                    != expected[index])
                    throw new InvalidOperationException(
                        label + " clip order differs at index " + index + ".");
        }

        private static void ValidateNativeAudioObject(
            SerializedObject serialized,
            string path,
            AudioClip expected)
        {
            if (Require(serialized, path).objectReferenceValue != expected)
                throw new InvalidOperationException(
                    path + " differs from the NPC Audio Profile.");
        }

        private static void ValidateNativeImpactSource(AudioSource source)
        {
            if (!source.enabled || source.playOnAwake || source.clip != null
                || source.loop || source.mute || source.spatialize
                || !NearNativeAudio(source.volume, 1f)
                || !NearNativeAudio(source.pitch, 1f)
                || source.priority != 128
                || !NearNativeAudio(source.minDistance, 1f)
                || !NearNativeAudio(source.maxDistance, 500f))
                throw new InvalidOperationException(
                    "ImpactSrc AudioSource differs from the Patch 6 playback baseline.");
        }

        private static void ValidateNativeFaceAnimationDisabled(
            SerializedObject serialized)
        {
            SerializedProperty enabled = serialized.FindProperty(
                "faceAnim.faceAnimEnabled");
            if (enabled != null && enabled.intValue != 0)
                throw new InvalidOperationException(
                    "NPC Audio must not enable the separate FaceAnim subsystem.");
            foreach (string field in new[]
                     {
                         "greetings", "agros", "unAgros", "deaths",
                         "painSmalls", "painBigs", "attack1s", "efforts",
                         "eventLines",
                     })
            {
                SerializedProperty events = serialized.FindProperty(
                    "faceAnim." + field);
                if (events != null && (!events.isArray || events.arraySize != 0))
                    throw new InvalidOperationException(
                        "NPC Audio must leave faceAnim." + field + " empty.");
            }
        }

        private static void ValidateNativeAudioClips(
            IReadOnlyList<AudioClip> clips,
            string label)
        {
            for (int index = 0; index < clips.Count; index++)
                RequireNativeAudioAsset(
                    clips[index], label + " clip " + index);
        }

        private static void RequireOptionalNativeAudioAsset(
            AudioClip clip,
            string label)
        {
            if (clip != null) RequireNativeAudioAsset(clip, label);
        }

        private static void RequireNativeAudioAsset(
            UnityEngine.Object value,
            string label)
        {
            if (value == null || !EditorUtility.IsPersistent(value)
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(value)))
                throw new InvalidOperationException(
                    label + " must be a saved persistent Project asset.");
        }

        private static void AppendNativeAudioList(
            StringBuilder text,
            string field,
            IReadOnlyList<AudioClip> clips)
        {
            text.Append(field).Append('[').Append(clips.Count).Append("]=");
            for (int index = 0; index < clips.Count; index++)
            {
                if (index > 0) text.Append(',');
                text.Append(StableNativeAudioAssetId(clips[index]));
            }
            text.Append('|');
        }

        private static string StableNativeAudioAssetId(UnityEngine.Object value)
        {
            if (value == null) return "null";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    value, out string guid, out long localId)
                || string.IsNullOrWhiteSpace(guid))
                throw new InvalidOperationException(
                    value.name + " is not a stable persistent Project asset.");
            string path = AssetDatabase.GetAssetPath(value);
            string dependency = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.GetAssetDependencyHash(path).ToString();
            return guid + ":" + localId.ToString(CultureInfo.InvariantCulture)
                   + ":" + dependency;
        }

        private static string NativeAudioFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static bool FiniteNativeAudio(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool NearNativeAudio(float left, float right)
        {
            return Math.Abs(left - right) <= 0.0001f;
        }

        private sealed class NativeAudioShell
        {
            public NpcAudioProfile Profile { get; }

            public NativeAudioShell(NpcAudioProfile profile)
            {
                Profile = profile;
            }
        }
    }
}
