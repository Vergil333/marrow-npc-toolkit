using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Installs the declaration-only types that BONELAB serializes from the
    /// predefined Assembly-CSharp assembly. Unity packages cannot add scripts
    /// to that predefined assembly directly, so this is an explicit, reversible
    /// project setup action.
    /// </summary>
    internal static class MarrowNpcToolkitPatch6DeclarationBootstrap
    {
        internal const string OutputAssetPath =
            "Assets/MarrowNpcToolkit/Patch6Declarations/AssemblyCSharp";
        internal const string StatePath =
            "ProjectSettings/MarrowNpcToolkitPatch6Declarations.json";

        private const int SchemaVersion = 1;
        private const string TemplateRelativePath =
            "Editor/DeclarationTemplates~/AssemblyCSharp";

        private static readonly string[] DeclarationFiles =
        {
            "AgentLinkControl.cs",
            "BehaviourPowerLegs.cs",
            "EyeAndHeadAnimator.cs",
            "GenericSpawnDelayEvent.cs",
            "LimbIKSlz.cs",
            "LookTargetController.cs",
            "PuppetMastaRefs.cs",
            "VisualDamageReceiver.cs",
        };

        [Serializable]
        private sealed class State
        {
            public int schemaVersion = SchemaVersion;
            public string packageVersion = string.Empty;
            public string installedUtc = string.Empty;
            public List<Entry> files = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string path = string.Empty;
            public string sha256 = string.Empty;
        }

        internal readonly struct Status
        {
            internal bool IsReady { get; }
            internal bool HasInstalledFiles { get; }
            internal string Detail { get; }

            internal Status(bool isReady, bool hasInstalledFiles, string detail)
            {
                IsReady = isReady;
                HasInstalledFiles = hasInstalledFiles;
                Detail = detail ?? string.Empty;
            }
        }

        [MenuItem(
            "Tools/Marrow NPC Toolkit/Install or Update Patch 6 Declarations",
            false,
            120)]
        private static void InstallFromMenu()
        {
            InstallOrUpdate(true);
        }

        internal static Status GetStatus()
        {
            string templateRoot;
            string templateError;
            if (!TryGetTemplateRoot(out templateRoot, out templateError))
                return new Status(false, false, templateError);

            int present = 0;
            var missing = new List<string>();
            var changed = new List<string>();
            foreach (string fileName in DeclarationFiles)
            {
                string source = Path.Combine(templateRoot, fileName + ".txt");
                string sourceMeta = Path.Combine(
                    templateRoot, fileName + ".meta.txt");
                string destination = ToAbsoluteAssetPath(
                    OutputAssetPath + "/" + fileName);
                string destinationMeta = destination + ".meta";
                if (!File.Exists(destination) || !File.Exists(destinationMeta))
                {
                    missing.Add(fileName);
                    continue;
                }

                present++;
                if (!File.Exists(source) || !File.Exists(sourceMeta)
                    || !string.Equals(HashFile(source), HashFile(destination),
                        StringComparison.Ordinal)
                    || !string.Equals(
                        HashFile(sourceMeta), HashFile(destinationMeta),
                        StringComparison.Ordinal))
                    changed.Add(fileName);
            }

            if (missing.Count == 0 && changed.Count == 0)
            {
                return new Status(
                    true,
                    true,
                    "Patch 6 project declarations are installed and current.");
            }

            if (present == 0)
            {
                return new Status(
                    false,
                    false,
                    "Patch 6 project declarations are not installed.");
            }

            var parts = new List<string>();
            if (missing.Count != 0)
                parts.Add("missing: " + string.Join(", ", missing));
            if (changed.Count != 0)
                parts.Add("different from this package: "
                          + string.Join(", ", changed));
            return new Status(
                false,
                true,
                "Patch 6 project declarations need review ("
                + string.Join("; ", parts) + ").");
        }

        internal static bool InstallOrUpdate(bool showSuccessDialog)
        {
            string environmentError;
            if (!ValidateSdkEnvironment(out environmentError))
            {
                EditorUtility.DisplayDialog(
                    "Patch 6 declarations were not installed",
                    environmentError,
                    "OK");
                return false;
            }

            string templateRoot;
            string templateError;
            if (!TryGetTemplateRoot(out templateRoot, out templateError))
            {
                EditorUtility.DisplayDialog(
                    "Patch 6 declarations were not installed",
                    templateError,
                    "OK");
                return false;
            }

            string outputRoot = ToAbsoluteAssetPath(OutputAssetPath);
            string[] externalDeclarations = FindExternalDeclarations(outputRoot);
            if (externalDeclarations.Length != 0)
            {
                EditorUtility.DisplayDialog(
                    "Existing Patch 6 declarations found",
                    "Declaration files already exist elsewhere under Assets. "
                    + "Installing another copy would create duplicate classes. "
                    + "Move or remove these old declarations first:\n\n"
                    + string.Join("\n", externalDeclarations),
                    "OK");
                return false;
            }

            string[] modified = DeclarationFiles
                .Where(fileName => IsExistingDestinationDifferent(
                    templateRoot, outputRoot, fileName))
                .ToArray();
            string backupRoot = string.Empty;
            if (modified.Length != 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Existing Patch 6 declarations differ",
                    "These installed declaration files differ from the package: "
                    + string.Join(", ", modified)
                    + ".\n\nUpdating will first copy the complete installed "
                    + "declaration folder to a dated backup under Assets/"
                    + "MarrowNpcToolkit/Backups.",
                    "Back Up & Update",
                    "Cancel",
                    string.Empty);
                if (choice != 0)
                    return false;
                backupRoot = CreateBackup(outputRoot);
            }

            Directory.CreateDirectory(outputRoot);
            var state = new State
            {
                packageVersion = GetPackageVersion(),
                installedUtc = DateTime.UtcNow.ToString("O"),
            };

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string fileName in DeclarationFiles)
                {
                    string source = Path.Combine(templateRoot, fileName + ".txt");
                    string sourceMeta = Path.Combine(
                        templateRoot, fileName + ".meta.txt");
                    string destination = Path.Combine(outputRoot, fileName);
                    string destinationMeta = destination + ".meta";
                    WriteNormalizedText(source, destination);
                    if (!File.Exists(sourceMeta))
                        throw new FileNotFoundException(
                            "Declaration meta template is missing.", sourceMeta);
                    WriteNormalizedText(sourceMeta, destinationMeta);
                    state.files.Add(new Entry
                    {
                        path = OutputAssetPath + "/" + fileName,
                        sha256 = HashFile(destination),
                    });
                }
                WriteState(state);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Patch 6 declaration installation failed",
                    exception.Message
                    + (string.IsNullOrWhiteSpace(backupRoot)
                        ? string.Empty
                        : "\n\nThe previous files were backed up at "
                          + ToAssetPath(backupRoot) + "."),
                    "OK");
                return false;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            if (showSuccessDialog)
            {
                EditorUtility.DisplayDialog(
                    "Patch 6 declarations installed",
                    "Installed eight declaration-only scripts at:\n"
                    + OutputAssetPath
                    + "\n\nUnity will compile them into Assembly-CSharp. "
                    + "After compilation, return to Patch 6 Behaviour settings "
                    + "and assign your project-local reference inputs."
                    + (string.IsNullOrWhiteSpace(backupRoot)
                        ? string.Empty
                        : "\n\nPrevious files were backed up at:\n"
                          + ToAssetPath(backupRoot)),
                    "OK");
            }
            return true;
        }

        private static bool ValidateSdkEnvironment(out string detail)
        {
            PackageManagerInfo[] packages =
                PackageManagerInfo.GetAllRegisteredPackages();
            bool extendedPackage = packages.Any(value =>
                value != null
                && ((value.name ?? string.Empty).IndexOf(
                        "extended", StringComparison.OrdinalIgnoreCase) >= 0
                    || (value.displayName ?? string.Empty).IndexOf(
                        "extended", StringComparison.OrdinalIgnoreCase) >= 0));
            bool extendedAssets = Directory.Exists(
                                      "Assets/Marrow-ExtendedSDK-MAINTAINED-main")
                                  || Directory.Exists(
                                      "Assets/Marrow-ExtendedSDK-MAINTAINED")
                                  || Directory.Exists("Assets/MarrowExtendedSDK");
            if (extendedPackage || extendedAssets)
            {
                detail = "The maintained Extended SDK is not compatible with "
                         + "this exact Patch 6 provider. Use a clean project "
                         + "with the official Marrow SDK 1.2.0.";
                return false;
            }

            PackageManagerInfo official = packages.FirstOrDefault(value =>
                value != null
                && string.Equals(value.name, "com.stresslevelzero.marrow.sdk",
                    StringComparison.Ordinal));
            if (official == null || !string.Equals(
                    official.version, "1.2.0", StringComparison.Ordinal))
            {
                detail = "Install the official Marrow SDK 1.2.0 before "
                         + "installing Patch 6 declarations.";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private static bool TryGetTemplateRoot(
            out string templateRoot,
            out string detail)
        {
            PackageManagerInfo package = PackageManagerInfo.FindForAssembly(
                typeof(MarrowNpcToolkitPatch6DeclarationBootstrap).Assembly);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                templateRoot = string.Empty;
                detail = "Unity could not resolve the Patch 6 provider package path.";
                return false;
            }

            string resolvedTemplateRoot = Path.Combine(
                package.resolvedPath, TemplateRelativePath);
            string missing = DeclarationFiles.FirstOrDefault(fileName =>
                !File.Exists(Path.Combine(
                    resolvedTemplateRoot, fileName + ".txt")));
            if (!string.IsNullOrWhiteSpace(missing))
            {
                templateRoot = string.Empty;
                detail = "The provider package is missing declaration template "
                         + missing + ".";
                return false;
            }

            templateRoot = resolvedTemplateRoot;
            detail = string.Empty;
            return true;
        }

        private static bool IsExistingDestinationDifferent(
            string templateRoot,
            string outputRoot,
            string fileName)
        {
            string source = Path.Combine(templateRoot, fileName + ".txt");
            string sourceMeta = Path.Combine(
                templateRoot, fileName + ".meta.txt");
            string destination = Path.Combine(outputRoot, fileName);
            string destinationMeta = destination + ".meta";
            if (!File.Exists(destination))
                return false;
            return !File.Exists(destinationMeta)
                   || !string.Equals(
                       HashFile(source), HashFile(destination),
                       StringComparison.Ordinal)
                   || !string.Equals(
                       HashFile(sourceMeta), HashFile(destinationMeta),
                       StringComparison.Ordinal);
        }

        private static string[] FindExternalDeclarations(string outputRoot)
        {
            string normalizedOutput = Path.GetFullPath(outputRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var declarationNames = new HashSet<string>(
                DeclarationFiles, StringComparer.OrdinalIgnoreCase);
            return Directory.GetFiles(
                    Application.dataPath, "*.cs", SearchOption.AllDirectories)
                .Where(path => declarationNames.Contains(Path.GetFileName(path)))
                .Where(path => !Path.GetFullPath(path).StartsWith(
                    normalizedOutput, StringComparison.Ordinal))
                .Select(ToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static string CreateBackup(string outputRoot)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string backupRoot = ToAbsoluteAssetPath(
                "Assets/MarrowNpcToolkit/Backups/Patch6Declarations-" + stamp);
            Directory.CreateDirectory(backupRoot);
            if (!Directory.Exists(outputRoot))
                return backupRoot;

            foreach (string source in Directory.GetFiles(outputRoot))
            {
                // Keep backups visible and recoverable without compiling a
                // second copy of every declaration into Assembly-CSharp.
                string backupName = Path.GetFileName(source) + ".txt";
                File.Copy(source, Path.Combine(backupRoot, backupName));
            }
            return backupRoot;
        }

        private static void WriteNormalizedText(string source, string destination)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException("Template is missing.", source);
            string text = File.ReadAllText(source).Replace("\r\n", "\n");
            File.WriteAllText(destination, text, new UTF8Encoding(false));
        }

        private static void WriteState(State state)
        {
            string json = JsonUtility.ToJson(state, true) + "\n";
            File.WriteAllText(StatePath, json, new UTF8Encoding(false));
        }

        private static string GetPackageVersion()
        {
            PackageManagerInfo package = PackageManagerInfo.FindForAssembly(
                typeof(MarrowNpcToolkitPatch6DeclarationBootstrap).Assembly);
            return package == null ? string.Empty : package.version;
        }

        private static string HashFile(string path)
        {
            using (var algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            string relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                relative));
        }

        private static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath, ".."));
            string relative = absolutePath.Substring(projectRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
