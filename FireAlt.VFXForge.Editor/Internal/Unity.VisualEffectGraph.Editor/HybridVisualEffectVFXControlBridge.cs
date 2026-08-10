using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.VFX;

namespace UnityEditor.VFX.UI
{
    [InitializeOnLoad]
    internal static class HybridVisualEffectVFXControlBridge
    {
        private const string ADVANCED_VISUAL_EFFECT_EDITOR_TYPE_NAME = "UnityEditor.VFX.AdvancedVisualEffectEditor";
        private const string HYBRID_VISUAL_EFFECT_FULL_NAME = "FireAlt.VFXForge.HybridVisualEffect";
        private const string HybridVisualEffectTypeName = HYBRID_VISUAL_EFFECT_FULL_NAME + ", FireAlt.VFXForge";
        private const string INSPECTION_TRACKER_FULL_NAME = "FireAlt.VFXForge.Editor.HybridVisualEffectInspectionTracker";
        private const string InspectionTrackerTypeName = INSPECTION_TRACKER_FULL_NAME + ", FireAlt.VFXForge.Editor";
        private const string ON_HIERARCHY_SELECTION_CHANGED_METHOD_NAME = "OnHierarchySelectionChanged";
        private const string BOUNDS_RECORDER_FIELD_NAME = "m_BoundsRecorder";
        private const int POST_RELOAD_PREPARATION_UPDATES = 10;
        private static readonly object PatchMarker = new();
        private static readonly FieldInfo DebugUIField = typeof(VFXComponentBoard).GetField("m_DebugUI", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BoundsRecorderField = typeof(VFXComponentBoard).GetField(BOUNDS_RECORDER_FIELD_NAME, BindingFlags.Instance | BindingFlags.NonPublic);

        private static Type s_HybridVisualEffectType;
        private static Type s_InspectionTrackerType;
        private static MethodInfo s_EditorControlStopMethod;
        private static MethodInfo s_EditorControlPlayPauseMethod;
        private static MethodInfo s_EditorControlStepMethod;
        private static MethodInfo s_EditorControlRestartMethod;
        private static MethodInfo s_EditorPlayMethod;
        private static MethodInfo s_EditorStopMethod;
        private static PropertyInfo s_HybridVisualEffectProperty;
        private static PropertyInfo s_PrimaryEffectProperty;
        private static int s_OpenWindowPreparationUpdates;

        static HybridVisualEffectVFXControlBridge()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.delayCall += Update;
            s_OpenWindowPreparationUpdates = POST_RELOAD_PREPARATION_UPDATES;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                s_OpenWindowPreparationUpdates = POST_RELOAD_PREPARATION_UPDATES;
            }
        }

        private static void OnSelectionChanged()
        {
            PatchUnsafeUnitySelectionHandler();

            if (GetInspectedHybridVisualEffect() == null)
            {
                AttachSelectedVisualEffectToOpenWindows();
            }

            Update();
        }

        private static void Update()
        {
            if (s_OpenWindowPreparationUpdates > 0)
            {
                PrepareOpenWindowsAfterReload();
                --s_OpenWindowPreparationUpdates;
            }

            PatchUnsafeUnitySelectionHandler();
            AttachInspectedHybridToMatchingWindows();

            foreach (var window in VFXViewWindow.GetAllWindows())
            {
                PatchWindow(window);
            }
        }

        private static void PatchWindow(VFXViewWindow window)
        {
            var graphView = window?.graphView;
            if (graphView == null)
            {
                return;
            }

            var board = graphView.Q<VFXComponentBoard>();
            if (board == null)
            {
                return;
            }

            var stopButton = board.Q<Button>("stop");
            if (stopButton == null || ReferenceEquals(stopButton.userData, PatchMarker))
            {
                return;
            }

            PatchButton(stopButton, () => OnStop(board));
            PatchButton(board.Q<Button>("play"), () => OnPlayPause(board));
            PatchButton(board.Q<Button>("step"), () => OnStep(board));
            PatchButton(board.Q<Button>("restart"), () => OnRestart(board));
            PatchButton(board.Q<Button>("on-play-button"), () => OnPlayEvent(board));
            PatchButton(board.Q<Button>("on-stop-button"), () => OnStopEvent(board));
        }

        private static void PatchButton(Button button, Action handler)
        {
            if (button == null)
            {
                return;
            }

            button.clickable = new Clickable(handler);
            button.userData = PatchMarker;
        }

        private static void AttachInspectedHybridToMatchingWindows()
        {
            var visualEffect = GetInspectedHybridVisualEffect();
            if (!TryGetVisualEffectAsset(visualEffect, out var visualEffectAsset))
            {
                return;
            }

            foreach (var window in VFXViewWindow.GetAllWindows())
            {
                var graphView = window?.graphView;
                if (graphView?.controller?.graph?.visualEffectResource?.asset != visualEffectAsset)
                {
                    continue;
                }

                if (graphView.attachedComponent == visualEffect)
                {
                    var board = graphView.Q<VFXComponentBoard>();
                    if (board == null || BoundsRecorderField == null || BoundsRecorderField.GetValue(board) != null)
                    {
                        continue;
                    }

                    // Domain reload can retain the attachment while losing the board's non-serialized recorder.
                    graphView.attachedComponent = null;
                }

                if (!CanCreateBoundsRecorder(graphView))
                {
                    continue;
                }

                graphView.attachedComponent = visualEffect;
                window.Repaint();
            }
        }

        private static void AttachSelectedVisualEffectToOpenWindows()
        {
            var visualEffect = GetSelectedVisualEffect();
            if (!TryGetVisualEffectAsset(visualEffect, out var visualEffectAsset))
            {
                return;
            }

            foreach (var window in VFXViewWindow.GetAllWindows())
            {
                var graphView = window?.graphView;
                if (graphView != null && graphView.controller?.graph?.visualEffectResource?.asset != visualEffectAsset)
                {
                    continue;
                }

                if (graphView != null && !CanCreateBoundsRecorder(graphView))
                {
                    continue;
                }

                try
                {
                    window.AttachTo(visualEffect);
                }
                catch (MissingComponentException)
                {
                    continue;
                }
            }
        }

        private static bool CanCreateBoundsRecorder(VFXView graphView)
        {
            if (TryValidateParticleData(graphView))
            {
                return true;
            }

            try
            {
                foreach (var block in graphView.controller.graph.children
                             .OfType<VFXContext>()
                             .SelectMany(context => context.children)
                             .OfType<VFXSubgraphBlock>())
                {
                    block.RecreateCopy();
                }
            }
            catch (Exception exception) when (exception is NullReferenceException or InvalidOperationException)
            {
                return false;
            }

            return TryValidateParticleData(graphView);
        }

        private static bool TryValidateParticleData(VFXView graphView)
        {
            try
            {
                foreach (var data in graphView.GetAllContexts()
                             .Select(context => context.controller.model.GetData())
                             .OfType<VFXDataParticle>()
                             .Distinct())
                {
                    _ = data.CanBeCompiled();
                }

                return true;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        private static void PrepareOpenWindowsAfterReload()
        {
            foreach (var window in VFXViewWindow.GetAllWindows())
            {
                var graphView = window?.graphView;
                if (graphView != null)
                {
                    _ = CanCreateBoundsRecorder(graphView);
                }
            }
        }

        private static void PatchUnsafeUnitySelectionHandler()
        {
            var callbacks = Selection.selectionChanged;
            if (callbacks == null)
            {
                return;
            }

            foreach (var callback in callbacks.GetInvocationList())
            {
                if (callback is not Action action)
                {
                    continue;
                }

                if (!string.Equals(action.Method.Name, ON_HIERARCHY_SELECTION_CHANGED_METHOD_NAME, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(action.Method.DeclaringType?.FullName, ADVANCED_VISUAL_EFFECT_EDITOR_TYPE_NAME, StringComparison.Ordinal))
                {
                    continue;
                }

                Selection.selectionChanged -= action;
            }
        }

        private static VisualEffect GetSelectedVisualEffect()
        {
            var selectedGameObject = Selection.activeGameObject;
            if (selectedGameObject == null)
            {
                return null;
            }

            if (!selectedGameObject.TryGetComponent(out VisualEffect visualEffect) || visualEffect == null)
            {
                return null;
            }

            return visualEffect;
        }

        private static VisualEffect GetInspectedHybridVisualEffect()
        {
            var effect = GetPrimaryInspectedEffect();
            if (effect == null)
            {
                return null;
            }

            var visualEffect = GetHybridVisualEffectProperty()?.GetValue(effect) as VisualEffect;
            return GetHybridComponent(visualEffect) != null ? visualEffect : null;
        }

        private static void OnStop(VFXComponentBoard board)
        {
            var effect = board.GetAttachedComponent();
            if (effect == null)
            {
                return;
            }

            if (!InvokeHybrid(effect, ref s_EditorControlStopMethod, "EditorControlStop"))
            {
                effect.ControlStop();
            }

            NotifyDebug(board, VFXUIDebug.Events.VFXStop);
            RepaintViews();
        }

        private static void OnPlayPause(VFXComponentBoard board)
        {
            var effect = board.GetAttachedComponent();
            if (effect == null)
            {
                return;
            }

            if (!InvokeHybrid(effect, ref s_EditorControlPlayPauseMethod, "EditorControlPlayPause"))
            {
                effect.ControlPlayPause();
            }

            NotifyDebug(board, VFXUIDebug.Events.VFXPlayPause);
            RepaintViews();
        }

        private static void OnStep(VFXComponentBoard board)
        {
            var effect = board.GetAttachedComponent();
            if (effect == null)
            {
                return;
            }

            if (!InvokeHybrid(effect, ref s_EditorControlStepMethod, "EditorControlStep"))
            {
                effect.ControlStep();
            }

            NotifyDebug(board, VFXUIDebug.Events.VFXStep);
            RepaintViews();
        }

        private static void OnRestart(VFXComponentBoard board)
        {
            var effect = board.GetAttachedComponent();
            if (effect == null)
            {
                return;
            }

            if (!InvokeHybrid(effect, ref s_EditorControlRestartMethod, "EditorControlRestart"))
            {
                effect.ControlRestart();
            }

            NotifyDebug(board, VFXUIDebug.Events.VFXReset);
            RepaintViews();
        }

        private static void OnPlayEvent(VFXComponentBoard board)
        {
            var effect = board.GetAttachedComponent();
            if (effect == null)
            {
                return;
            }

            if (!InvokeHybrid(effect, ref s_EditorPlayMethod, "EditorPlay"))
            {
                board.SendEvent(VisualEffectAsset.PlayEventName);
            }

            RepaintViews();
        }

        private static void OnStopEvent(VFXComponentBoard board)
        {
            var effect = board.GetAttachedComponent();
            if (effect == null)
            {
                return;
            }

            if (!InvokeHybrid(effect, ref s_EditorStopMethod, "EditorStop"))
            {
                board.SendEvent(VisualEffectAsset.StopEventName);
            }

            RepaintViews();
        }

        private static bool InvokeHybrid(VisualEffect effect, ref MethodInfo method, string methodName)
        {
            var hybridComponent = GetHybridComponent(effect);
            if (hybridComponent == null)
            {
                return false;
            }

            method ??= GetHybridMethod(methodName);
            if (method == null)
            {
                return false;
            }

            method.Invoke(hybridComponent, null);
            return true;
        }

        private static object GetHybridComponent(VisualEffect effect)
        {
            if (effect == null)
            {
                return null;
            }

            var hybridType = GetHybridVisualEffectType();
            return hybridType != null ? effect.GetComponent(hybridType) : null;
        }

        private static Type GetHybridVisualEffectType()
        {
            if (s_HybridVisualEffectType == null)
            {
                s_HybridVisualEffectType = Type.GetType(HybridVisualEffectTypeName);
                if (s_HybridVisualEffectType == null)
                {
                    foreach (var assembly in AllAssemblies)
                    {
                        s_HybridVisualEffectType = assembly.GetType(HYBRID_VISUAL_EFFECT_FULL_NAME);
                        if (s_HybridVisualEffectType != null)
                        {
                            break;
                        }
                    }
                }
            }

            return s_HybridVisualEffectType;
        }

        private static MethodInfo GetHybridMethod(string methodName)
        {
            var hybridType = GetHybridVisualEffectType();
            return hybridType?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static object GetPrimaryInspectedEffect()
        {
            var property = GetPrimaryEffectProperty();
            return property?.GetValue(null);
        }

        private static PropertyInfo GetPrimaryEffectProperty()
        {
            if (s_PrimaryEffectProperty == null)
            {
                var trackerType = GetInspectionTrackerType();
                s_PrimaryEffectProperty = trackerType?.GetProperty("PrimaryEffect", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return s_PrimaryEffectProperty;
        }

        private static PropertyInfo GetHybridVisualEffectProperty()
        {
            if (s_HybridVisualEffectProperty == null)
            {
                var hybridType = GetHybridVisualEffectType();
                s_HybridVisualEffectProperty = hybridType?.GetProperty("VisualEffect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            return s_HybridVisualEffectProperty;
        }

        private static Type GetInspectionTrackerType()
        {
            if (s_InspectionTrackerType == null)
            {
                s_InspectionTrackerType = Type.GetType(InspectionTrackerTypeName);
                if (s_InspectionTrackerType == null)
                {
                    foreach (var assembly in AllAssemblies)
                    {
                        s_InspectionTrackerType = assembly.GetType(INSPECTION_TRACKER_FULL_NAME);
                        if (s_InspectionTrackerType != null)
                        {
                            break;
                        }
                    }
                }
            }

            return s_InspectionTrackerType;
        }

        private static bool TryGetVisualEffectAsset(VisualEffect visualEffect, out VisualEffectAsset visualEffectAsset)
        {
            visualEffectAsset = null;
            if (visualEffect == null)
            {
                return false;
            }

            try
            {
                visualEffectAsset = visualEffect.visualEffectAsset;
            }
            catch (MissingComponentException)
            {
                return false;
            }

            return visualEffectAsset != null;
        }

        private static void NotifyDebug(VFXComponentBoard board, VFXUIDebug.Events debugEvent)
        {
            if (DebugUIField?.GetValue(board) is VFXUIDebug debugUI)
            {
                debugUI.Notify(debugEvent);
            }
        }

        private static void RepaintViews()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();

            foreach (var window in VFXViewWindow.GetAllWindows())
            {
                window.Repaint();
            }
        }
        
        private static Assembly[] _allAssemblies;
#if UNITY_6000_6_OR_NEWER
        public static Assembly[] AllAssemblies => _allAssemblies ??= UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies().ToArray();
#else
        public static Assembly[] AllAssemblies => _allAssemblies ??= AppDomain.CurrentDomain.GetAssemblies();
#endif

    }
}
