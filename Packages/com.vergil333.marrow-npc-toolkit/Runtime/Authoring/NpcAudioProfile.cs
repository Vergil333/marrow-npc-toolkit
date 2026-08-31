using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.Authoring
{
    /// <summary>
    /// Stable authoring names for the Patch 6 PowerLegs SFX arrays. Compatibility
    /// providers may map these values to their own supported runtime schema.
    /// </summary>
    public enum NpcAudioEvent
    {
        Agro = 0,
        UnAgro = 1,
        PainSmall = 2,
        PainBig = 3,
        Death = 4,
        JumpCharge = 5,
        Jump = 6,
        SmallEffort = 7,
        MediumEffort = 8,
        LargeEffort = 9,
        Attack1 = 10,
        AttackLand1 = 11,
        Attack2 = 12,
        ImpactHead = 13,
        ImpactSpine = 14,
        ImpactLimb = 15,
    }

    [CreateAssetMenu(
        fileName = "NpcAudioProfile",
        menuName = "Marrow NPC Toolkit/NPC Audio Profile",
        order = 13)]
    public sealed class NpcAudioProfile : ScriptableObject
    {
        [Header("Basic Reactions")]
        [Tooltip("Short hurt reactions. At least one saved clip is required in Profile mode.")]
        [SerializeField] private AudioClip[] painSmall = Array.Empty<AudioClip>();
        [Tooltip("Heavy hurt reactions. At least one saved clip is required in Profile mode.")]
        [SerializeField] private AudioClip[] painBig = Array.Empty<AudioClip>();
        [Tooltip("Death reactions. At least one saved clip is required in Profile mode.")]
        [SerializeField] private AudioClip[] death = Array.Empty<AudioClip>();

        [Header("Movement and Effort")]
        [SerializeField] private AudioClip[] jumpCharge = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] jump = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] smallEffort = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] mediumEffort = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] largeEffort = Array.Empty<AudioClip>();

        [Header("Awareness and Combat")]
        [SerializeField] private AudioClip[] agro = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] unAgro = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] attack1 = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] attackLand1 = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] attack2 = Array.Empty<AudioClip>();

        [Header("Physical Impacts")]
        [SerializeField] private AudioClip[] impactHead = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] impactSpine = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] impactLimb = Array.Empty<AudioClip>();

        [Header("Optional Loops")]
        [SerializeField] private AudioClip dotLoop1;
        [SerializeField] private AudioClip agroMovementLoop;
        [SerializeField] private AudioClip movementLoop;
        [SerializeField, Min(0.01f), Tooltip("Pitch applied by a compatible native NPC provider.")]
        private float pitchMultiplier = 1f;

        [Header("Footsteps")]
        [SerializeField] private AudioClip[] walkConcrete = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] runConcrete = Array.Empty<AudioClip>();
        [SerializeField, Min(0f), Tooltip("Footstep volume applied by a compatible native NPC provider.")]
        private float footstepVolumeMultiplier = 1f;

        [Header("Distribution Provenance")]
        [Tooltip("Spoken language, or blank for non-verbal sounds.")]
        [SerializeField] private string language;
        [Tooltip("Where the clips came from. A reference import does not grant distribution rights.")]
        [SerializeField] private string source;
        [Tooltip("Credit that must accompany a distributed NPC.")]
        [SerializeField] private string credit;
        [Tooltip("License or explicit permission that allows these clips to be distributed.")]
        [SerializeField] private string licenseOrPermission;
        [Tooltip("Additional provenance, editing, or usage notes.")]
        [SerializeField, TextArea] private string notes;

        public IReadOnlyList<AudioClip> Agro => Clips(agro);
        public IReadOnlyList<AudioClip> UnAgro => Clips(unAgro);
        public IReadOnlyList<AudioClip> PainSmall => Clips(painSmall);
        public IReadOnlyList<AudioClip> PainBig => Clips(painBig);
        public IReadOnlyList<AudioClip> Death => Clips(death);
        public IReadOnlyList<AudioClip> JumpCharge => Clips(jumpCharge);
        public IReadOnlyList<AudioClip> Jump => Clips(jump);
        public IReadOnlyList<AudioClip> SmallEffort => Clips(smallEffort);
        public IReadOnlyList<AudioClip> MediumEffort => Clips(mediumEffort);
        public IReadOnlyList<AudioClip> LargeEffort => Clips(largeEffort);
        public IReadOnlyList<AudioClip> Attack1 => Clips(attack1);
        public IReadOnlyList<AudioClip> AttackLand1 => Clips(attackLand1);
        public IReadOnlyList<AudioClip> Attack2 => Clips(attack2);
        public IReadOnlyList<AudioClip> ImpactHead => Clips(impactHead);
        public IReadOnlyList<AudioClip> ImpactSpine => Clips(impactSpine);
        public IReadOnlyList<AudioClip> ImpactLimb => Clips(impactLimb);
        public AudioClip DotLoop1 { get => dotLoop1; set => dotLoop1 = value; }
        public AudioClip AgroMovementLoop
        {
            get => agroMovementLoop;
            set => agroMovementLoop = value;
        }
        public AudioClip MovementLoop { get => movementLoop; set => movementLoop = value; }
        public float PitchMultiplier
        {
            get => pitchMultiplier;
            set => pitchMultiplier = value;
        }
        public IReadOnlyList<AudioClip> WalkConcrete => Clips(walkConcrete);
        public IReadOnlyList<AudioClip> RunConcrete => Clips(runConcrete);
        public float FootstepVolumeMultiplier
        {
            get => footstepVolumeMultiplier;
            set => footstepVolumeMultiplier = value;
        }
        public string Language => language;
        public string Source => source;
        public string Credit => credit;
        public string LicenseOrPermission => licenseOrPermission;
        public string Notes => notes;

        public bool HasBasicReactions => HasClip(painSmall)
                                         && HasClip(painBig)
                                         && HasClip(death);
        public bool HasFootsteps => HasClip(walkConcrete) && HasClip(runConcrete);

        public IReadOnlyList<AudioClip> GetClips(NpcAudioEvent audioEvent)
        {
            switch (audioEvent)
            {
                case NpcAudioEvent.Agro: return Agro;
                case NpcAudioEvent.UnAgro: return UnAgro;
                case NpcAudioEvent.PainSmall: return PainSmall;
                case NpcAudioEvent.PainBig: return PainBig;
                case NpcAudioEvent.Death: return Death;
                case NpcAudioEvent.JumpCharge: return JumpCharge;
                case NpcAudioEvent.Jump: return Jump;
                case NpcAudioEvent.SmallEffort: return SmallEffort;
                case NpcAudioEvent.MediumEffort: return MediumEffort;
                case NpcAudioEvent.LargeEffort: return LargeEffort;
                case NpcAudioEvent.Attack1: return Attack1;
                case NpcAudioEvent.AttackLand1: return AttackLand1;
                case NpcAudioEvent.Attack2: return Attack2;
                case NpcAudioEvent.ImpactHead: return ImpactHead;
                case NpcAudioEvent.ImpactSpine: return ImpactSpine;
                case NpcAudioEvent.ImpactLimb: return ImpactLimb;
                default: throw new ArgumentOutOfRangeException(nameof(audioEvent));
            }
        }

        public void SetClips(NpcAudioEvent audioEvent, IEnumerable<AudioClip> clips)
        {
            AudioClip[] values = Copy(clips);
            switch (audioEvent)
            {
                case NpcAudioEvent.Agro: agro = values; break;
                case NpcAudioEvent.UnAgro: unAgro = values; break;
                case NpcAudioEvent.PainSmall: painSmall = values; break;
                case NpcAudioEvent.PainBig: painBig = values; break;
                case NpcAudioEvent.Death: death = values; break;
                case NpcAudioEvent.JumpCharge: jumpCharge = values; break;
                case NpcAudioEvent.Jump: jump = values; break;
                case NpcAudioEvent.SmallEffort: smallEffort = values; break;
                case NpcAudioEvent.MediumEffort: mediumEffort = values; break;
                case NpcAudioEvent.LargeEffort: largeEffort = values; break;
                case NpcAudioEvent.Attack1: attack1 = values; break;
                case NpcAudioEvent.AttackLand1: attackLand1 = values; break;
                case NpcAudioEvent.Attack2: attack2 = values; break;
                case NpcAudioEvent.ImpactHead: impactHead = values; break;
                case NpcAudioEvent.ImpactSpine: impactSpine = values; break;
                case NpcAudioEvent.ImpactLimb: impactLimb = values; break;
                default: throw new ArgumentOutOfRangeException(nameof(audioEvent));
            }
        }

        public void SetFootsteps(
            IEnumerable<AudioClip> walking,
            IEnumerable<AudioClip> running,
            float volumeMultiplier = 1f)
        {
            walkConcrete = Copy(walking);
            runConcrete = Copy(running);
            footstepVolumeMultiplier = volumeMultiplier;
        }

        public void SetProvenance(
            string audioLanguage,
            string audioSource,
            string audioCredit,
            string permission,
            string provenanceNotes)
        {
            language = audioLanguage ?? string.Empty;
            source = audioSource ?? string.Empty;
            credit = audioCredit ?? string.Empty;
            licenseOrPermission = permission ?? string.Empty;
            notes = provenanceNotes ?? string.Empty;
        }

        private static IReadOnlyList<AudioClip> Clips(AudioClip[] values)
        {
            return values ?? Array.Empty<AudioClip>();
        }

        private static AudioClip[] Copy(IEnumerable<AudioClip> values)
        {
            if (values == null) return Array.Empty<AudioClip>();
            return values is AudioClip[] array
                ? (AudioClip[])array.Clone()
                : new List<AudioClip>(values).ToArray();
        }

        private static bool HasClip(AudioClip[] values)
        {
            if (values == null) return false;
            for (int index = 0; index < values.Length; index++)
                if (values[index] != null)
                    return true;
            return false;
        }
    }
}
