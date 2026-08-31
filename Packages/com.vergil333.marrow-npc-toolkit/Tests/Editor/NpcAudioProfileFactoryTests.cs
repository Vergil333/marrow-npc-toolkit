using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Vergil333.MarrowNpcToolkit.Authoring;
using Vergil333.MarrowNpcToolkit.Editor.Authoring;
using Object = UnityEngine.Object;

namespace Vergil333.MarrowNpcToolkit.Tests
{
    public sealed class NpcAudioProfileFactoryTests
    {
        private string folder;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__MarrowNpcToolkitAudioProfileFactoryTests_"
                     + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(folder))
                AssetDatabase.DeleteAsset(folder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void ExistingDefinitionGetsPersistentProfileAndStaysSilent()
        {
            string prefabPath = folder + "/Source.prefab";
            var sourceObject = new GameObject("Legacy Avatar");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(sourceObject, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
            }

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            definition.Initialize(
                source,
                NpcAvatarSourceKind.HumanoidPrefab,
                null,
                null,
                null,
                AssetDatabase.AssetPathToGUID(prefabPath),
                AssetDatabase.GetAssetDependencyHash(prefabPath).ToString());
            string definitionPath = folder + "/LegacyNpcDefinition.asset";
            AssetDatabase.CreateAsset(definition, definitionPath);
            AssetDatabase.SaveAssets();

            NpcAudioProfile profile = NpcAudioProfileFactory
                .CreateForDefinition(definition);

            Assert.That(profile, Is.Not.Null);
            Assert.That(definition.AudioProfile, Is.SameAs(profile));
            Assert.That(definition.AudioMode, Is.EqualTo(NpcAudioMode.Silent));
            Assert.That(EditorUtility.IsPersistent(profile), Is.True);
            Assert.That(
                Path.GetDirectoryName(AssetDatabase.GetAssetPath(profile))
                    ?.Replace('\\', '/'),
                Is.EqualTo(folder));
            Assert.That(profile.HasBasicReactions, Is.False);
        }
    }
}
