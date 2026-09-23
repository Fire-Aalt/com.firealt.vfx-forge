using Unity.Scripting.LifecycleManagement;
using System;
using System.Collections.Generic;
using FireAlt.Core.Editor.Inspectors;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace FireAlt.VFXForge.Editor
{
    [CustomEditor(typeof(HybridVisualEffect))]
    [NoAutoStaticsCleanup]
    public class HybridVisualEffectEditor : UnityEditor.Editor
    {
        private const int VISIBILITY_REFRESH_INTERVAL_MS = 200;
        internal static HybridVisualEffect ActiveEffect => HybridVisualEffectInspectionTracker.PrimaryEffect;
        
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.styleSheets.Add(DrawerStyleResources.CommonStyleSheet);
            root.styleSheets.Add(HybridVisualEffectStyleResources.HybridVisualEffectEditorStyleSheet);

            var conditionBindings = new List<ConditionBinding>();
            var vfxDefinitionProperty = serializedObject.FindProperty(HybridVisualEffect.VFX_DEFINITION_PROPERTY_NAME);

            var vfxDefinitionField = CreatePropertyField(HybridVisualEffect.VFX_DEFINITION_PROPERTY_NAME);
            if (vfxDefinitionField != null)
            {
                root.Add(vfxDefinitionField);
            }

            var uploadDataField = CreatePropertyField(HybridVisualEffect.UPLOAD_DATA_PROPERTY_NAME);
            if (uploadDataField != null)
            {
                var uploadDataBox = CreateBoxGroup("VFX Data Type Baker", uploadDataField);
                root.Add(uploadDataBox);
                conditionBindings.Add(new ConditionBinding(uploadDataBox, effect => effect.IsDefinitionValid()));
            }

            var uploadArrayDataField = CreatePropertyField(HybridVisualEffect.UPLOAD_ARRAY_DATA_PROPERTY_NAME);
            if (uploadArrayDataField != null)
            {
                var uploadArrayDataBox = CreateBoxGroup("VFX Array Data Type Baker", uploadArrayDataField);
                root.Add(uploadArrayDataBox);
                conditionBindings.Add(new ConditionBinding(uploadArrayDataBox, effect => effect.IsDefinitionValid()));
            }

            var trackingDurationField = CreatePropertyField(HybridVisualEffect.TRACKING_DURATION_PROPERTY_NAME);
            if (trackingDurationField != null)
            {
                root.Add(trackingDurationField);
                conditionBindings.Add(new ConditionBinding(trackingDurationField, effect => effect.ShowTrackingDuration()));
            }

            var focusedBoundsField = CreatePropertyField(HybridVisualEffect.FOCUSED_BOUNDS_SIZE_PROPERTY_NAME);
            if (focusedBoundsField != null)
            {
                root.Add(focusedBoundsField);
                conditionBindings.Add(new ConditionBinding(focusedBoundsField, effect => effect.IsDefinitionValid()));
            }

            if (vfxDefinitionProperty != null)
            {
                root.TrackPropertyValue(vfxDefinitionProperty, _ => RefreshDataAndReinitTargets());
            }

            void RefreshVisibility()
            {
                for (var i = 0; i < conditionBindings.Count; i++)
                {
                    var binding = conditionBindings[i];
                    binding.Target.style.display = IsAnyTargetMatch(binding.IsVisiblePredicate)
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
            }

            root.TrackSerializedObjectValue(serializedObject, _ => RefreshVisibility());
            root.schedule.Execute(RefreshVisibility).Every(VISIBILITY_REFRESH_INTERVAL_MS);
            RefreshVisibility();
            return root;
        }

        private void RefreshDataAndReinitTargets()
        {
            ForEachTarget(effect => effect.RefreshDataAndReinit());
        }

        private bool IsAnyTargetMatch(Func<HybridVisualEffect, bool> predicate)
        {
            var targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] is HybridVisualEffect effect && predicate(effect))
                {
                    return true;
                }
            }

            return false;
        }

        private void ForEachTarget(Action<HybridVisualEffect> action)
        {
            var targets = serializedObject.targetObjects;
            if (targets == null || targets.Length == 0)
            {
                return;
            }

            for (var i = 0; i < targets.Length; i++)
            {
                if (targets[i] is HybridVisualEffect effect)
                {
                    action(effect);
                }
            }
        }

        private PropertyField CreatePropertyField(string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            return property == null ? null : new PropertyField(property);
        }

        private static VisualElement CreateBoxGroup(string title, VisualElement content)
        {
            var group = new VisualElement();
            group.AddToClassList("hybrid-vfx-editor-box-group");

            var header = new Label(title);
            header.AddToClassList("kras-drawer-box-header");
            header.AddToClassList("kras-drawer-box-title");

            var body = new VisualElement();
            body.AddToClassList("kras-drawer-box-body");
            body.Add(content);

            group.Add(header);
            group.Add(body);
            return group;
        }

        private readonly struct ConditionBinding
        {
            public ConditionBinding(VisualElement target, Func<HybridVisualEffect, bool> isVisiblePredicate)
            {
                Target = target;
                IsVisiblePredicate = isVisiblePredicate;
            }

            public VisualElement Target { get; }
            public Func<HybridVisualEffect, bool> IsVisiblePredicate { get; }
        }

        private void OnEnable()
        {
            HybridVisualEffectInspectionTracker.RegisterEditor(this);
        }

        private void OnDisable()
        {
            HybridVisualEffectInspectionTracker.UnregisterEditor(this);
        }

        internal void AppendInspectedEffects(List<HybridVisualEffect> effects, HashSet<HybridVisualEffect> seenEffects)
        {
            var inspectedTargets = targets;
            if (inspectedTargets == null || inspectedTargets.Length == 0)
            {
                return;
            }

            for (var i = 0; i < inspectedTargets.Length; i++)
            {
                if (inspectedTargets[i] is HybridVisualEffect effect && effect != null && seenEffects.Add(effect))
                {
                    effects.Add(effect);
                }
            }
        }

        internal bool HasInspectedEffect()
        {
            var inspectedTargets = targets;
            if (inspectedTargets == null || inspectedTargets.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < inspectedTargets.Length; i++)
            {
                if (inspectedTargets[i] is HybridVisualEffect)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
