using Unity.Scripting.LifecycleManagement;
using FireAlt.Core.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FireAlt.VFXForge.Editor
{
    
    [NoAutoStaticsCleanup]
    public static partial class HybridVisualEffectStyleResources
    {
        public static StyleSheet HybridVisualEffectEditorStyleSheet;

        [OnCodeInitializing]
        private static void Initialize()
        {
            HybridVisualEffectEditorStyleSheet = Load<StyleSheet>("Styles/HybridVisualEffectEditor.uss");
        }

        private static T Load<T>(string path) where T : Object
        {
            return AssetDatabaseUtils.LoadEditorResource<T>(path, "com.firealt.vfx-forge");
        }
    }
}
