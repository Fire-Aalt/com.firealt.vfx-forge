using System.Reflection;
using FireAlt.Core.Inspectors;
using FireAlt.VFXForge.Authoring;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace FireAlt.VFXForge.Tests
{
    public class VFXSettingsTests
    {
        private const string DEFAULT_DECAL_VFX_GUID_KEY = "FireAlt.VFXForge.DefaultDecalVFXGuid";
        private const string PACKAGE_DEFAULT_DECAL_VFX_PATH = "Packages/com.firealt.vfx-forge/Shaders/Decals/DecalDefinition.asset";

        private bool hadStoredGuid;
        private string storedGuid;

        [SetUp]
        public void SetUp()
        {
            hadStoredGuid = EditorPrefs.HasKey(DEFAULT_DECAL_VFX_GUID_KEY);
            storedGuid = EditorPrefs.GetString(DEFAULT_DECAL_VFX_GUID_KEY, string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            if (hadStoredGuid)
            {
                EditorPrefs.SetString(DEFAULT_DECAL_VFX_GUID_KEY, storedGuid);
            }
            else
            {
                EditorPrefs.DeleteKey(DEFAULT_DECAL_VFX_GUID_KEY);
            }
        }

        [Test]
        public void DefaultDecalVFX_WhenPreferenceIsMissing_UsesAndStoresPackageDefault()
        {
            EditorPrefs.DeleteKey(DEFAULT_DECAL_VFX_GUID_KEY);

            var definition = VFXSettings.DefaultDecalVFX;

            Assert.IsNotNull(definition);
            Assert.AreEqual(PACKAGE_DEFAULT_DECAL_VFX_PATH, AssetDatabase.GetAssetPath(definition));
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(PACKAGE_DEFAULT_DECAL_VFX_PATH),
                EditorPrefs.GetString(DEFAULT_DECAL_VFX_GUID_KEY));
        }

        [Test]
        public void DefaultDecalVFX_WhenStoredGuidIsStale_UsesAndStoresPackageDefault()
        {
            EditorPrefs.SetString(DEFAULT_DECAL_VFX_GUID_KEY, "00000000000000000000000000000000");

            var definition = VFXSettings.DefaultDecalVFX;

            Assert.IsNotNull(definition);
            Assert.AreEqual(PACKAGE_DEFAULT_DECAL_VFX_PATH, AssetDatabase.GetAssetPath(definition));
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(PACKAGE_DEFAULT_DECAL_VFX_PATH),
                EditorPrefs.GetString(DEFAULT_DECAL_VFX_GUID_KEY));
        }
        
        [Test]
        public void CapacityFields_WhenInvalid_NormalizeInitialAndMax()
        {
            var definition = ScriptableObject.CreateInstance<VFXDefinition>();
            try
            {
                definition.initialCapacity = -5;
                definition.maxCapacity = -10;
                typeof(VFXDefinition).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(definition, null);

                Assert.That(definition.initialCapacity, Is.Zero);
                Assert.That(definition.maxCapacity, Is.Zero);

                definition.initialCapacity = 7;
                definition.maxCapacity = 3;
                typeof(VFXDefinition).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(definition, null);

                Assert.That(definition.maxCapacity, Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void TypeRegistryRefresh_WhenBakerCacheIsStale_RebuildsAvailableBakers()
        {
            Assert.That(
                VFXTypeCache.TryGetBakerTypes(typeof(VFXDecal), VFXDataTypeBakerKind.Data, out var initialBakers),
                Is.True);
            Assert.That(initialBakers, Is.Not.Empty);

            VFXTypeCache.DataBakerTypesMap.Clear();
            Assert.That(VFXTypeCache.TryGetBakerTypes(typeof(VFXDecal), VFXDataTypeBakerKind.Data, out _), Is.False);

            VFXTypeRegistry.Refresh();

            Assert.That(
                VFXTypeCache.TryGetBakerTypes(typeof(VFXDecal), VFXDataTypeBakerKind.Data, out var refreshedBakers),
                Is.True);
            Assert.That(refreshedBakers, Is.Not.Empty);
            Assert.That(refreshedBakers[0], Is.EqualTo(typeof(DefaultVFXDataTypeBaker<VFXDecal>)));
        }
    }
}
