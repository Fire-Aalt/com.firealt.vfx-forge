using Unity.Scripting.LifecycleManagement;
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace FireAlt.VFXForge.Editor
{
    [Overlay(typeof(SceneView), "Scene View/Hybrid Visual Effect", "Hybrid Visual Effect", 
        defaultDockZone = DockZone.RightColumn, defaultDockPosition = DockPosition.Bottom, defaultDockIndex = 0,
        defaultLayout = Layout.Panel, defaultWidth = 226, defaultHeight = 85)]
    [NoAutoStaticsCleanup]
    public class HybridVisualEffectOverlay : IMGUIOverlay, ITransientOverlay
    {
        private delegate float PowerSliderContentDelegate(GUIContent label, float value, float leftValue, 
            float rightValue, float power, GUILayoutOption[] options);

        private const string VfxPackageName = "com.unity.visualeffectgraph";
        private const string DefaultIconPath = "Packages/com.unity.visualeffectgraph/Editor/SceneWindow/Textures/";
        private const float MinSlider = 1f;
        private const float MaxSlider = 4000f;
        private const float SliderPower = 10f;
        private const float PlayRateToValue = 100f;
        private const float ValueToPlayRate = 1f / PlayRateToValue;
        private static readonly int[] SetPlaybackValues = { 1, 10, 50, 100, 200, 500, 1000, 4000 };
        private static readonly string IconPath = ResolveIconPath();
        private static readonly GUIContent StopIcon = LoadIcon("Stop");
        private static readonly GUIContent PlayIcon = LoadIcon("Play");
        private static readonly GUIContent PauseIcon = LoadIcon("Pause");
        private static readonly GUIContent StepIcon = LoadIcon("Step");
        private static readonly GUIContent RestartIcon = LoadIcon("Restart");
        private static readonly GUIContent SetPlayRateLabel = new("Set");
        private static readonly GUIContent PlayRateLabel = new("Rate");
        private static readonly PowerSliderContentDelegate ReflectedPowerSliderContent = CreatePowerSliderContentDelegate();

        public bool visible => HybridVisualEffectEditor.ActiveEffect != null;

        public override void OnGUI()
        {
            var effect = HybridVisualEffectEditor.ActiveEffect;
            if (effect == null || !effect.HasEditorVisualEffect())
            {
                return;
            }

            GUILayout.BeginVertical();
            DrawToolbar(effect);
            DrawPlayRate(effect);
            DrawEvents(effect);
            GUILayout.EndVertical();
        }

        private static void DrawToolbar(HybridVisualEffect effect)
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(StopIcon, GUILayout.Width(52)))
            {
                InvokeAndRepaint(effect.EditorControlStop);
            }

            var playPauseIcon = effect.IsEditorPaused() ? PlayIcon : PauseIcon;
            if (GUILayout.Button(playPauseIcon, GUILayout.Width(52)))
            {
                InvokeAndRepaint(effect.EditorControlPlayPause);
            }

            if (GUILayout.Button(StepIcon, GUILayout.Width(52)))
            {
                InvokeAndRepaint(effect.EditorControlStep);
            }

            if (GUILayout.Button(RestartIcon, GUILayout.Width(52)))
            {
                InvokeAndRepaint(effect.EditorControlRestart);
            }

            GUILayout.EndHorizontal();
        }

        private static void DrawPlayRate(HybridVisualEffect effect)
        {
            var playRate = effect.GetEditorPlayRate();
            var playRateValue = Mathf.Max(MinSlider, playRate * PlayRateToValue);
            var eventType = Event.current.type;

            GUILayout.BeginHorizontal();
            GUILayout.Label(PlayRateLabel, GUILayout.Width(46));

            EditorGUI.BeginChangeCheck();
            var newPlayRateValue = DrawPowerSlider(Mathf.Round(playRateValue), GUILayout.Width(124));
            if (EditorGUI.EndChangeCheck())
            {
                var newPlayRate = newPlayRateValue * ValueToPlayRate;
                InvokeAndRepaint(() => effect.SetEditorPlayRate(newPlayRate));
            }

            if (EditorGUILayout.DropdownButton(SetPlayRateLabel, FocusType.Passive, GUILayout.Width(40)))
            {
                var menu = new GenericMenu();
                foreach (var value in SetPlaybackValues)
                {
                    menu.AddItem(new GUIContent($"{value}%"), false, () =>
                    {
                        InvokeAndRepaint(() => effect.SetEditorPlayRate(value * ValueToPlayRate));
                    });
                }

                var savedEventType = Event.current.type;
                Event.current.type = eventType;
                var buttonRect = GUILayoutUtility.GetLastRect();
                Event.current.type = savedEventType;
                menu.DropDown(buttonRect);
            }

            GUILayout.EndHorizontal();
        }

        private static void DrawEvents(HybridVisualEffect effect)
        {
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Play()"))
            {
                InvokeAndRepaint(effect.EditorPlay);
            }

            if (GUILayout.Button("Stop()"))
            {
                InvokeAndRepaint(effect.EditorStop);
            }

            GUILayout.EndHorizontal();
        }

        private static void RepaintSceneViews()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static void InvokeAndRepaint(Action action)
        {
            action.Invoke();
            RepaintSceneViews();
        }

        private static GUIContent LoadIcon(string iconName)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath + iconName + ".png");
            return texture != null ? new GUIContent(texture) : new GUIContent(iconName);
        }

        private static string ResolveIconPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(VfxPackageName);
            return packageInfo != null ? packageInfo.assetPath + "/Editor/SceneWindow/Textures/" : DefaultIconPath;
        }

        private static float DrawPowerSlider(float value, params GUILayoutOption[] options)
        {
            if (ReflectedPowerSliderContent != null)
            {
                return ReflectedPowerSliderContent(GUIContent.none, value, MinSlider, MaxSlider, SliderPower, options);
            }
            throw new Exception("Internals changed for PowerSlider");
        }

        private static PowerSliderContentDelegate CreatePowerSliderContentDelegate()
        {
            var method = typeof(EditorGUILayout).GetMethod(
                "PowerSlider",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(GUIContent),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(GUILayoutOption[]),
                },
                null);

            if (method == null)
            {
                return null;
            }
            return (PowerSliderContentDelegate)method.CreateDelegate(typeof(PowerSliderContentDelegate));
        }
    }
}
