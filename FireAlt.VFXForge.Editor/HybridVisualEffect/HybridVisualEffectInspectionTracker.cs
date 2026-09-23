using Unity.Scripting.LifecycleManagement;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace FireAlt.VFXForge.Editor
{
    [NoAutoStaticsCleanup]
    internal static partial class HybridVisualEffectInspectionTracker
    {
        private const string UNITY_VFX_OVERLAY_ID = "Scene View/Visual Effect";

        private static readonly Dictionary<SceneView, bool> HiddenUnityVfxOverlayStates = new();
        private static readonly List<HybridVisualEffectEditor> RegisteredEditors = new();

        private static List<HybridVisualEffect> _activeEffects = new();
        private static bool _isRebuildQueued;
        private static bool _isUnityVfxOverlayHidden;

        [OnCodeInitializing]
        private static void Initialize()
        {
            Selection.selectionChanged += RequestRebuild;
            EditorApplication.quitting += RestoreUnityVfxOverlayStateVoid;
            EditorApplication.delayCall += RequestRebuild;
        }

        [OnCodeUnloading]
        private static void Unload()
        {
            RestoreUnityVfxOverlayStateVoid();
            Selection.selectionChanged -= RequestRebuild;
            EditorApplication.quitting -= RestoreUnityVfxOverlayStateVoid;
            EditorApplication.delayCall -= RequestRebuild;
            EditorApplication.delayCall -= RebuildIfQueued;
        }

        internal static HybridVisualEffect PrimaryEffect => _activeEffects.Count > 0 ? _activeEffects[0] : null;

        internal static void RegisterEditor(HybridVisualEffectEditor editor)
        {
            if (editor == null || RegisteredEditors.Contains(editor))
            {
                return;
            }

            RegisteredEditors.Add(editor);
            RequestRebuild();
        }

        internal static void UnregisterEditor(HybridVisualEffectEditor editor)
        {
            if (editor == null)
            {
                return;
            }

            if (RegisteredEditors.Remove(editor))
            {
                RequestRebuild();
            }
        }

        internal static void RequestRebuild()
        {
            UpdateUnityVfxOverlayVisibility(HasImmediateInspectedEffects());

            if (_isRebuildQueued)
            {
                return;
            }

            _isRebuildQueued = true;
            EditorApplication.delayCall += RebuildIfQueued;
        }

        private static void RebuildIfQueued()
        {
            if (!_isRebuildQueued)
            {
                return;
            }

            _isRebuildQueued = false;
            RebuildNow();
        }

        private static void RebuildNow()
        {
            RemoveDeadEditors();

            var nextEffects = new List<HybridVisualEffect>();
            var seenEffects = new HashSet<HybridVisualEffect>();

            // Selection drives the primary target, while registered editor instances keep locked inspectors alive.
            AppendSelectedEffects(nextEffects, seenEffects);

            for (var i = 0; i < RegisteredEditors.Count; i++)
            {
                RegisteredEditors[i].AppendInspectedEffects(nextEffects, seenEffects);
            }

            if (_activeEffects.Count == nextEffects.Count && _activeEffects.SequenceEqual(nextEffects))
            {
                UpdateUnityVfxOverlayVisibility(_activeEffects.Count > 0);
                return;
            }

            var previousEffects = new HashSet<HybridVisualEffect>(_activeEffects);
            var nextEffectSet = new HashSet<HybridVisualEffect>(nextEffects);

            for (var i = 0; i < _activeEffects.Count; i++)
            {
                var effect = _activeEffects[i];
                if (effect == null || !nextEffectSet.Contains(effect))
                {
                    effect?.OnInspectorClosed();
                }
            }

            for (var i = 0; i < nextEffects.Count; i++)
            {
                var effect = nextEffects[i];
                if (!previousEffects.Contains(effect))
                {
                    effect.OnInspectorOpened();
                }
            }

            _activeEffects = nextEffects;
            UpdateUnityVfxOverlayVisibility(_activeEffects.Count > 0);
        }

        private static void AppendSelectedEffects(List<HybridVisualEffect> effects, HashSet<HybridVisualEffect> seenEffects)
        {
            var selectedGameObjects = Selection.gameObjects;
            for (var i = 0; i < selectedGameObjects.Length; i++)
            {
                var gameObject = selectedGameObjects[i];
                if (gameObject != null
                    && gameObject.TryGetComponent(out HybridVisualEffect effect)
                    && effect != null
                    && seenEffects.Add(effect))
                {
                    effects.Add(effect);
                }
            }
        }

        private static void RemoveDeadEditors()
        {
            for (var i = RegisteredEditors.Count - 1; i >= 0; i--)
            {
                if (RegisteredEditors[i] == null)
                {
                    RegisteredEditors.RemoveAt(i);
                }
            }
        }

        private static bool HasImmediateInspectedEffects()
        {
            var selectedGameObjects = Selection.gameObjects;
            for (var i = 0; i < selectedGameObjects.Length; i++)
            {
                if (selectedGameObjects[i] != null && selectedGameObjects[i].TryGetComponent<HybridVisualEffect>(out _))
                {
                    return true;
                }
            }

            for (var i = 0; i < RegisteredEditors.Count; i++)
            {
                var editor = RegisteredEditors[i];
                if (editor != null && editor.HasInspectedEffect())
                {
                    return true;
                }
            }

            return false;
        }

        private static void UpdateUnityVfxOverlayVisibility(bool shouldHide)
        {
            var didChange = false;
            if (shouldHide)
            {
                if (!_isUnityVfxOverlayHidden)
                {
                    _isUnityVfxOverlayHidden = true;
                    SceneView.duringSceneGui += OnSceneGui;
                    didChange = true;
                }

                foreach (var sceneView in SceneView.sceneViews.OfType<SceneView>())
                {
                    didChange |= HideUnityVfxOverlay(sceneView);
                }
            }
            else
            {
                didChange = RestoreUnityVfxOverlayState();
            }

            if (didChange)
            {
                SceneView.RepaintAll();
            }
        }

        private static bool HideUnityVfxOverlay(SceneView sceneView)
        {
            if (sceneView == null || !sceneView.TryGetOverlay(UNITY_VFX_OVERLAY_ID, out var overlay))
            {
                return false;
            }

            if (!HiddenUnityVfxOverlayStates.ContainsKey(sceneView))
            {
                HiddenUnityVfxOverlayStates.Add(sceneView, overlay.displayed);
            }

            if (overlay.displayed)
            {
                overlay.displayed = false;
                return true;
            }

            return false;
        }

        private static bool RestoreUnityVfxOverlayInOpenViews()
        {
            var didChange = false;
            foreach (var pair in HiddenUnityVfxOverlayStates)
            {
                if (pair.Key != null && pair.Key.TryGetOverlay(UNITY_VFX_OVERLAY_ID, out var overlay) && overlay != null)
                {
                    if (overlay.displayed != pair.Value)
                    {
                        didChange = true;
                    }

                    overlay.displayed = pair.Value;
                }
            }

            HiddenUnityVfxOverlayStates.Clear();
            return didChange;
        }

        private static void RestoreUnityVfxOverlayStateVoid()
        {
            RestoreUnityVfxOverlayState();
        }
        
        private static bool RestoreUnityVfxOverlayState()
        {
            var didChange = false;
            if (_isUnityVfxOverlayHidden)
            {
                SceneView.duringSceneGui -= OnSceneGui;
                _isUnityVfxOverlayHidden = false;
                didChange = true;
            }

            return RestoreUnityVfxOverlayInOpenViews() || didChange;
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            HideUnityVfxOverlay(sceneView);
        }
    }
}
