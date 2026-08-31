using System;
using Vergil333.MarrowNpcToolkit.Authoring;

namespace Vergil333.MarrowNpcToolkit.Editor.Compatibility
{
    /// <summary>
    /// Native NPC features supplied by an optional compatibility provider.
    /// Avatar authoring is deliberately not represented here because it is
    /// supplied by the Marrow Avatar SDK, not by a native NPC provider.
    /// </summary>
    [Flags]
    public enum NpcCompatibilityCapabilities
    {
        None = 0,
        CoreAnatomy = 1 << 0,
        AI = 1 << 1,
        Pooling = 1 << 2,
        Grips = 1 << 3,
        Gaze = 1 << 4,
        Jaw = 1 << 5,
        Audio = 1 << 6,
        SecondaryMotion = 1 << 7,
        All = CoreAnatomy | AI | Pooling | Grips | Gaze | Jaw | Audio
              | SecondaryMotion,
    }

    /// <summary>
    /// One canonical translation from public NPC feature choices to the
    /// native capabilities that every build, receipt, and packaging gate must
    /// compare. Keeping this here prevents a stale receipt from remaining
    /// current merely because one caller forgot an optional feature bit.
    /// </summary>
    public static class NpcCompatibilityRequirements
    {
        public static NpcCompatibilityCapabilities ForDefinition(
            NpcDefinition definition)
        {
            NpcCompatibilityCapabilities required =
                NpcCompatibilityCapabilities.CoreAnatomy
                | NpcCompatibilityCapabilities.AI
                | NpcCompatibilityCapabilities.Pooling;
            if (definition == null) return required;
            if (definition.IncludeHandGrips)
                required |= NpcCompatibilityCapabilities.Grips;
            if (definition.IncludeGaze)
                required |= NpcCompatibilityCapabilities.Gaze;
            if (definition.IncludePhysicalJaw)
                required |= NpcCompatibilityCapabilities.Jaw;
            if (definition.IncludeNpcAudio)
                required |= NpcCompatibilityCapabilities.Audio;
            if (definition.IncludeSecondaryMotion)
                required |= NpcCompatibilityCapabilities.SecondaryMotion;
            return required;
        }
    }
}
