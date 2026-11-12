using Unity.Scripting.LifecycleManagement;
using FireAlt.Core.Editor;
using UnityEngine.UIElements;

namespace FireAlt.VFXForge.Editor
{
    [NoAutoStaticsCleanup]
    public static class HybridVisualEffectStyleResources
    {
        private static readonly PackageEditorResources PackageResources = new("com.firealt.vfx-forge");

        public static StyleSheet HybridVisualEffectEditorStyleSheet => PackageResources.Load<StyleSheet>("Styles/HybridVisualEffectEditor.uss");
    }
}
