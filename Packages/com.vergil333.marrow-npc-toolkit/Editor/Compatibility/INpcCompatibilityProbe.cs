namespace Vergil333.MarrowNpcToolkit.Editor.Compatibility
{
    /// <summary>
    /// Implemented by an optional, separately distributed native NPC provider.
    /// The toolkit core never references provider-specific game declarations.
    /// </summary>
    public interface INpcCompatibilityProbe
    {
        /// <summary>A stable identifier for this provider implementation.</summary>
        string ProviderId { get; }

        /// <summary>A short user-facing provider name.</summary>
        string DisplayName { get; }

        /// <summary>
        /// The exact compatibility contract supported by this provider. This is
        /// matched ordinally against NpcBuildProfile.CompatibilityProfileId.
        /// </summary>
        string CompatibilityProfileId { get; }

        /// <summary>
        /// Checks whether the provider can currently be used and which native
        /// NPC capabilities it supplies. Implementations must not modify assets.
        /// </summary>
        NpcCompatibilityProbeResult Probe();
    }

    public sealed class NpcCompatibilityProbeResult
    {
        public bool IsAvailable { get; }
        public NpcCompatibilityCapabilities Capabilities { get; }
        public string Detail { get; }

        private NpcCompatibilityProbeResult(
            bool isAvailable,
            NpcCompatibilityCapabilities capabilities,
            string detail)
        {
            IsAvailable = isAvailable;
            Capabilities = isAvailable
                ? capabilities & NpcCompatibilityCapabilities.All
                : NpcCompatibilityCapabilities.None;
            Detail = detail ?? string.Empty;
        }

        public static NpcCompatibilityProbeResult Available(
            NpcCompatibilityCapabilities capabilities,
            string detail = "")
        {
            return new NpcCompatibilityProbeResult(true, capabilities, detail);
        }

        public static NpcCompatibilityProbeResult Unavailable(string detail)
        {
            return new NpcCompatibilityProbeResult(
                false,
                NpcCompatibilityCapabilities.None,
                detail);
        }
    }
}
