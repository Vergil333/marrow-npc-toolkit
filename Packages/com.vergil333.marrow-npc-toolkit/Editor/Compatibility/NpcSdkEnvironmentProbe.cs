using System;
using UnityEditor.PackageManager;
using MarrowAvatar = SLZ.VRMK.Avatar;

namespace Vergil333.MarrowNpcToolkit.Editor.Compatibility
{
    public enum NpcMarrowProviderKind
    {
        Official,
        Extended,
        Unknown,
    }

    public sealed class NpcSdkEnvironment
    {
        public NpcMarrowProviderKind ProviderKind { get; }
        public string PackageName { get; }
        public string PackageVersion { get; }
        public string DisplayName { get; }

        public NpcSdkEnvironment(
            NpcMarrowProviderKind providerKind,
            string packageName,
            string packageVersion,
            string displayName)
        {
            ProviderKind = providerKind;
            PackageName = packageName ?? string.Empty;
            PackageVersion = packageVersion ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }
    }

    public static class NpcSdkEnvironmentProbe
    {
        private const string OfficialPackage = "com.stresslevelzero.marrow.sdk";
        private const string ExtendedPackage = "com.stresslevelzero.marrow.sdk.extended";

        public static NpcSdkEnvironment Probe()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(MarrowAvatar).Assembly);
            string packageName = package?.name ?? string.Empty;
            string packageVersion = package?.version ?? string.Empty;
            if (string.Equals(packageName, OfficialPackage, StringComparison.Ordinal))
                return new NpcSdkEnvironment(
                    NpcMarrowProviderKind.Official,
                    packageName,
                    packageVersion,
                    "Official Marrow SDK");
            if (string.Equals(packageName, ExtendedPackage, StringComparison.Ordinal))
                return new NpcSdkEnvironment(
                    NpcMarrowProviderKind.Extended,
                    packageName,
                    packageVersion,
                    "Extended SDK");
            return new NpcSdkEnvironment(
                NpcMarrowProviderKind.Unknown,
                packageName,
                packageVersion,
                "Unknown Marrow provider");
        }
    }
}
