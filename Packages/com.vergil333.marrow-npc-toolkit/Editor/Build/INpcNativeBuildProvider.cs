using System;
using System.Collections.Generic;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;

namespace Vergil333.MarrowNpcToolkit.Editor.Build
{
    /// <summary>
    /// Optional patch-specific implementation of native NPC authoring. The
    /// public toolkit owns staging, deterministic verification, asset saving,
    /// and commit/rollback; a provider only configures the isolated prefab
    /// instance supplied in <see cref="NpcNativeBuildContext"/>.
    /// </summary>
    public interface INpcNativeBuildProvider : INpcCompatibilityProbe
    {
        /// <summary>
        /// Configures the isolated staged prefab instance. Implementations must
        /// mutate only context.OutputRoot and must not save, import, move, or
        /// delete assets. They must not modify any authoring input referenced by
        /// context.Definition.
        /// </summary>
        NpcNativeBuildProviderResult ConfigureStagedPrefab(
            NpcNativeBuildContext context);

        /// <summary>
        /// Validates the prefab after Unity has saved, imported, and reloaded
        /// the isolated staging asset. Implementations must treat the supplied
        /// prefab as read-only and return the same structural fingerprint that
        /// ConfigureStagedPrefab returned for the corresponding pass.
        /// </summary>
        NpcNativeBuildProviderResult ValidateSavedPrefab(
            NpcNativeBuildValidationContext context);
    }

    /// <summary>
    /// Read-only build inputs plus the one isolated GameObject a provider may
    /// mutate. OutputRoot is an unpacked clone of the validated Physics Preview,
    /// so provider changes cannot be applied back to that preview or the Avatar.
    /// </summary>
    public sealed class NpcNativeBuildContext
    {
        public NpcDefinition Definition { get; }
        public GameObject OutputRoot { get; }
        public Transform AnimationRoot { get; }
        public Transform PhysicsRoot { get; }
        public NpcCompatibilityCapabilities RequiredCapabilities { get; }
        public string InputFingerprint { get; }
        public int PassNumber { get; }

        internal NpcNativeBuildContext(
            NpcDefinition definition,
            GameObject outputRoot,
            NpcCompatibilityCapabilities requiredCapabilities,
            string inputFingerprint,
            int passNumber)
        {
            Definition = definition;
            OutputRoot = outputRoot;
            RequiredCapabilities = requiredCapabilities;
            InputFingerprint = inputFingerprint ?? string.Empty;
            PassNumber = passNumber;
            AnimationRoot = FindDirectChild(outputRoot, "AnimationRoot");
            PhysicsRoot = FindDirectChild(outputRoot, "Physics");
        }

        public Transform FindPhysicsBody(HumanBodyBones role)
        {
            if (PhysicsRoot == null)
                return null;
            Transform[] transforms = PhysicsRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i] != PhysicsRoot
                    && string.Equals(
                        transforms[i].name,
                        role.ToString(),
                        StringComparison.Ordinal))
                    return transforms[i];
            return null;
        }

        private static Transform FindDirectChild(GameObject root, string childName)
        {
            if (root == null)
                return null;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                    return child;
            }
            return null;
        }
    }

    /// <summary>
    /// Read-only view of one saved and reloaded staging prefab. This second
    /// provider boundary catches native references or ordered arrays that do
    /// not survive Unity serialization before an output can be committed.
    /// Providers must not mutate the prefab or save, import, move, or delete
    /// assets while validating it.
    /// </summary>
    public sealed class NpcNativeBuildValidationContext
    {
        public NpcDefinition Definition { get; }
        public GameObject OutputRoot { get; }
        public Transform AnimationRoot { get; }
        public Transform PhysicsRoot { get; }
        public NpcCompatibilityCapabilities RequiredCapabilities { get; }
        public string InputFingerprint { get; }
        public string OutputAssetPath { get; }

        internal NpcNativeBuildValidationContext(
            NpcDefinition definition,
            GameObject outputRoot,
            NpcCompatibilityCapabilities requiredCapabilities,
            string inputFingerprint,
            string outputAssetPath)
        {
            Definition = definition;
            OutputRoot = outputRoot;
            RequiredCapabilities = requiredCapabilities;
            InputFingerprint = inputFingerprint ?? string.Empty;
            OutputAssetPath = outputAssetPath ?? string.Empty;
            AnimationRoot = FindDirectChild(outputRoot, "AnimationRoot");
            PhysicsRoot = FindDirectChild(outputRoot, "Physics");
        }

        public Transform FindPhysicsBody(HumanBodyBones role)
        {
            if (PhysicsRoot == null)
                return null;
            Transform[] transforms = PhysicsRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
                if (transforms[i] != PhysicsRoot
                    && string.Equals(
                        transforms[i].name,
                        role.ToString(),
                        StringComparison.Ordinal))
                    return transforms[i];
            return null;
        }

        private static Transform FindDirectChild(GameObject root, string childName)
        {
            if (root == null)
                return null;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                    return child;
            }
            return null;
        }
    }

    public enum NpcNativeBuildMessageSeverity
    {
        Info,
        Warning,
        Error,
    }

    public sealed class NpcNativeBuildMessage
    {
        public NpcNativeBuildMessageSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        public NpcNativeBuildMessage(
            NpcNativeBuildMessageSeverity severity,
            string code,
            string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Provider-owned semantic receipt for one isolated generation pass. The
    /// structural fingerprint must cover every native component, serialized
    /// binding, ordered runtime array, and setting that the provider owns.
    /// </summary>
    public sealed class NpcNativeBuildProviderResult
    {
        private readonly NpcNativeBuildMessage[] messages;

        public bool Success { get; }
        public string StructuralFingerprint { get; }
        public IReadOnlyList<NpcNativeBuildMessage> Messages => messages;
        public int ErrorCount { get; }

        private NpcNativeBuildProviderResult(
            bool success,
            string structuralFingerprint,
            IEnumerable<NpcNativeBuildMessage> messages)
        {
            StructuralFingerprint = structuralFingerprint ?? string.Empty;
            this.messages = Copy(messages);
            ErrorCount = CountErrors(this.messages);
            Success = success
                      && ErrorCount == 0
                      && !string.IsNullOrWhiteSpace(StructuralFingerprint);
        }

        public static NpcNativeBuildProviderResult Succeeded(
            string structuralFingerprint,
            IEnumerable<NpcNativeBuildMessage> messages = null)
        {
            if (string.IsNullOrWhiteSpace(structuralFingerprint))
                return Failed(
                    "PROVIDER_FINGERPRINT_MISSING",
                    "The native provider did not return its required structural fingerprint.");
            return new NpcNativeBuildProviderResult(
                true,
                structuralFingerprint,
                messages);
        }

        public static NpcNativeBuildProviderResult Failed(
            string code,
            string message)
        {
            return new NpcNativeBuildProviderResult(
                false,
                string.Empty,
                new[]
                {
                    new NpcNativeBuildMessage(
                        NpcNativeBuildMessageSeverity.Error,
                        code,
                        message),
                });
        }

        public static NpcNativeBuildProviderResult Failed(
            IEnumerable<NpcNativeBuildMessage> messages)
        {
            return new NpcNativeBuildProviderResult(false, string.Empty, messages);
        }

        private static NpcNativeBuildMessage[] Copy(
            IEnumerable<NpcNativeBuildMessage> values)
        {
            if (values == null)
                return Array.Empty<NpcNativeBuildMessage>();
            var result = new List<NpcNativeBuildMessage>();
            foreach (NpcNativeBuildMessage value in values)
                if (value != null)
                    result.Add(value);
            return result.ToArray();
        }

        private static int CountErrors(IEnumerable<NpcNativeBuildMessage> values)
        {
            int count = 0;
            foreach (NpcNativeBuildMessage value in values)
                if (value.Severity == NpcNativeBuildMessageSeverity.Error)
                    count++;
            return count;
        }
    }
}
