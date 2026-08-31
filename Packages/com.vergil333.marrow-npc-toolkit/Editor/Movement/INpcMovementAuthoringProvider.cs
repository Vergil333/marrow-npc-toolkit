using System;
using System.Collections.Generic;
using System.Linq;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.Editor.Movement
{
    /// <summary>
    /// Optional project-local bridge that turns the public, provider-neutral
    /// movement measurements into persistent assets required by one exact game
    /// compatibility contract. Preparation is an explicit authoring action; it
    /// is deliberately separate from the native-build transaction.
    /// </summary>
    public interface INpcMovementAuthoringProvider : INpcCompatibilityProbe
    {
        /// <summary>
        /// Prepares deterministic provider-owned movement assets and records
        /// them through NpcMovementProfile.SetProviderRecipe. Implementations may
        /// create or update only their own generated assets. The source Avatar,
        /// Anatomy Profile, Build Profile, and other authoring inputs are
        /// read-only.
        /// </summary>
        NpcMovementAuthoringResult Prepare(
            NpcDefinition definition,
            NpcMovementProfile profile);

        /// <summary>
        /// Recomputes the provider recipe receipt from current inputs without
        /// creating, changing, or saving assets. A recipe is current only when
        /// the returned fingerprint also equals the fingerprint stored in the
        /// Movement Profile.
        /// </summary>
        NpcMovementAuthoringValidationResult Validate(
            NpcDefinition definition,
            NpcMovementProfile profile);
    }

    public sealed class NpcMovementAuthoringResult
    {
        private readonly string[] messages;

        public bool Success { get; }
        public string RecipeFingerprint { get; }
        public IReadOnlyList<string> Messages => messages;

        private NpcMovementAuthoringResult(
            bool success,
            string recipeFingerprint,
            IEnumerable<string> resultMessages)
        {
            RecipeFingerprint = (recipeFingerprint ?? string.Empty).Trim();
            messages = (resultMessages ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
            Success = success && !string.IsNullOrWhiteSpace(RecipeFingerprint);
            if (success && !Success && messages.Length == 0)
                messages = new[]
                {
                    "The movement authoring provider returned no deterministic recipe fingerprint.",
                };
        }

        public static NpcMovementAuthoringResult Succeeded(
            string recipeFingerprint,
            params string[] messages)
        {
            return new NpcMovementAuthoringResult(
                true, recipeFingerprint, messages);
        }

        public static NpcMovementAuthoringResult Failed(params string[] messages)
        {
            return new NpcMovementAuthoringResult(
                false, string.Empty, messages);
        }
    }

    public sealed class NpcMovementAuthoringValidationResult
    {
        private readonly string[] messages;

        public bool IsCurrent { get; }
        public string RecipeFingerprint { get; }
        public IReadOnlyList<string> Messages => messages;

        private NpcMovementAuthoringValidationResult(
            bool isCurrent,
            string recipeFingerprint,
            IEnumerable<string> resultMessages)
        {
            RecipeFingerprint = (recipeFingerprint ?? string.Empty).Trim();
            messages = (resultMessages ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
            IsCurrent = isCurrent
                        && !string.IsNullOrWhiteSpace(RecipeFingerprint);
            if (isCurrent && !IsCurrent && messages.Length == 0)
                messages = new[]
                {
                    "The movement authoring provider could not recompute its recipe fingerprint.",
                };
        }

        public static NpcMovementAuthoringValidationResult Current(
            string recipeFingerprint,
            params string[] messages)
        {
            return new NpcMovementAuthoringValidationResult(
                true, recipeFingerprint, messages);
        }

        public static NpcMovementAuthoringValidationResult Stale(
            params string[] messages)
        {
            return new NpcMovementAuthoringValidationResult(
                false, string.Empty, messages);
        }
    }
}
