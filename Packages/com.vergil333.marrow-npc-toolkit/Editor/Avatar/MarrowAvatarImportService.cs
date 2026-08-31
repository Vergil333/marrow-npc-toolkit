using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MarrowAvatar = SLZ.VRMK.Avatar;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Editor.AvatarIntake
{
    public static class MarrowAvatarImportService
    {
        public static GameObject ConfigureModelAsHumanoid(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string path = AssetDatabase.GetAssetPath(source);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    "The selected asset is not a model with a ModelImporter.");

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        public static GameObject CreateMarrowAvatarPrefab(GameObject source, string outputPath)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("An output prefab path is required.", nameof(outputPath));

            AvatarIntakeReport sourceReport = AvatarIntakeValidator.Validate(source);
            if (!sourceReport.IsHumanoid || sourceReport.HasErrors)
                throw new InvalidOperationException(
                    "The source must pass Humanoid intake validation before creating a Marrow Avatar prefab.");

            outputPath = AssetDatabase.GenerateUniqueAssetPath(outputPath);
            GameObject instance = null;
            try
            {
                instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (instance == null)
                    instance = Object.Instantiate(source);
                if (instance == null)
                    throw new InvalidOperationException("Unity could not instantiate the selected avatar source.");

                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        instance,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }

                instance.name = Path.GetFileNameWithoutExtension(outputPath);
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;

                Animator animator = instance.GetComponent<Animator>()
                                    ?? instance.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null
                                     || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException(
                        "The instantiated source lost its valid Humanoid Animator.");

                MarrowAvatar avatar = instance.GetComponent<MarrowAvatar>();
                bool createdAvatarComponent = avatar == null;
                if (createdAvatarComponent)
                    avatar = instance.AddComponent<MarrowAvatar>();
                avatar.animator = animator;
                avatar.wristLf ??= animator.GetBoneTransform(HumanBodyBones.LeftHand);
                avatar.wristRt ??= animator.GetBoneTransform(HumanBodyBones.RightHand);

                SkinnedMeshRenderer[] renderers =
                    instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (renderers.Length == 0)
                    throw new InvalidOperationException(
                        "The instantiated source has no skinned renderers.");

                if (createdAvatarComponent
                    || avatar.bodyMeshes == null || avatar.bodyMeshes.Length == 0)
                {
                    avatar.hairMeshes = renderers.Where(IsHairRenderer).ToArray();
                    avatar.headMeshes = renderers.Where(value =>
                        !IsHairRenderer(value) && IsHeadRenderer(value)).ToArray();
                    avatar.bodyMeshes = renderers.Where(value =>
                        !IsHairRenderer(value) && !IsHeadRenderer(value)).ToArray();
                    if (avatar.bodyMeshes.Length == 0)
                        avatar.bodyMeshes = renderers.Except(avatar.hairMeshes).ToArray();
                }

                EditorUtility.SetDirty(avatar);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
                if (saved == null)
                    throw new InvalidOperationException(
                        "Unity did not return the saved Marrow Avatar prefab.");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
            }
            finally
            {
                if (instance != null)
                    Object.DestroyImmediate(instance);
            }
        }

        public static void OpenForOfficialFineTuning(GameObject avatarPrefab)
        {
            if (avatarPrefab == null) return;
            Selection.activeObject = avatarPrefab;
            EditorGUIUtility.PingObject(avatarPrefab);
            AssetDatabase.OpenAsset(avatarPrefab);
        }

        private static bool IsHairRenderer(SkinnedMeshRenderer renderer)
        {
            return renderer != null
                   && renderer.name.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsHeadRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) return false;
            return renderer.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0
                   || renderer.name.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
