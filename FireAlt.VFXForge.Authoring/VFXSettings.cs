using Unity.Scripting.LifecycleManagement;
using FireAlt.VFXForge.Data;
using UnityEditor;

namespace FireAlt.VFXForge.Authoring
{
    [NoAutoStaticsCleanup]
    public class VFXSettings : SettingsProvider
    {
        private const string PREFERENCES_PATH = "Preferences/FireAlt/VFX Forge";
        private const string DEFAULT_DECAL_VFX_GUID_KEY = "FireAlt.VFXForge.DefaultDecalVFXGuid";
        private const string PACKAGE_DEFAULT_DECAL_VFX_PATH = "Packages/com.firealt.vfx-forge/Shaders/Decals/DecalDefinition.asset";

        private VFXSettings(string path, SettingsScope scopes, string[] keywords = null)
            : base(path, scopes, keywords)
        {
        }

        public static VFXDecalDefinition DefaultDecalVFX
        {
            get
            {
                var guid = EditorPrefs.GetString(DEFAULT_DECAL_VFX_GUID_KEY, string.Empty);
                if (!string.IsNullOrEmpty(guid))
                {
                    var configuredPath = AssetDatabase.GUIDToAssetPath(guid);
                    var configuredDefinition = string.IsNullOrEmpty(configuredPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<VFXDecalDefinition>(configuredPath);
                    if (configuredDefinition != null)
                    {
                        return configuredDefinition;
                    }
                }

                var packageDefault = AssetDatabase.LoadAssetAtPath<VFXDecalDefinition>(PACKAGE_DEFAULT_DECAL_VFX_PATH);
                if (packageDefault != null)
                {
                    DefaultDecalVFX = packageDefault;
                }

                return packageDefault;
            }

            set
            {
                if (value == null)
                {
                    EditorPrefs.DeleteKey(DEFAULT_DECAL_VFX_GUID_KEY);
                    return;
                }

                var path = AssetDatabase.GetAssetPath(value);
                var guid = AssetDatabase.AssetPathToGUID(path);
                EditorPrefs.SetString(DEFAULT_DECAL_VFX_GUID_KEY, guid);
            }
        }

        public override void OnGUI(string searchContext)
        {
            base.OnGUI(searchContext);

            EditorGUI.BeginChangeCheck();
            var defaultDecalVFX = (VFXDecalDefinition)EditorGUILayout.ObjectField("Default Decal VFX", DefaultDecalVFX, typeof(VFXDecalDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                DefaultDecalVFX = defaultDecalVFX;
            }
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new VFXSettings(PREFERENCES_PATH, SettingsScope.User, new[] { "VFX", "Forge", "Decal", "Default" });
        }

        [MenuItem("FireAlt/VFX Forge Preferences")]
        private static void Open()
        {
            SettingsService.OpenUserPreferences(PREFERENCES_PATH);
        }
    }
}
