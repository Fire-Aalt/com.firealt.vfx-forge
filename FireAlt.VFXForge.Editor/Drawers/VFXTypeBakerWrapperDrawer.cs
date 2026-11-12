using System;
using System.Reflection;
using FireAlt.Core.Editor;
using FireAlt.VFXForge.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace FireAlt.VFXForge.Editor
{
    public abstract class VFXTypeBakerWrapperDrawerBase : PropertyDrawer
    {
        private const string ROOT_FIELD_INDENT_CLASS_NAME = "hybrid-vfx-editor-baker-root-indent";
        private const string WARNING_CLASS_NAME = "vfx-type-baker-wrapper-warning";
        private const string HIDDEN_FIELD_CLASS_NAME = "vfx-type-baker-wrapper-hidden-field";
        
        private HelpBox _warningBox;
        private PropertyField _hiddenDataField;
        private PropertyField _bakerTypePropertyField;
        private PropertyField _rootPropertyField;
        private string _lastBakerManagedReferenceType;
        private string _onChangedMethod;

        protected abstract string BakerBaseTypeName { get; }
        
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.TryAddStyleSheet(HybridVisualEffectStyleResources.HybridVisualEffectEditorStyleSheet);
            var bakerProperty = SerializationUtils.FindRelativeProperty(property, "_baker");
            var bakerTypeProperty = SerializationUtils.FindRelativeProperty(property, "_selectedBakerTypeName");

            if (bakerProperty == null || bakerTypeProperty == null)
            {
                return root;
            }

            _bakerTypePropertyField = new PropertyField(bakerTypeProperty, "Baker Type");
            root.Add(_bakerTypePropertyField);
            _rootPropertyField = new PropertyField(bakerProperty, property.displayName);
            _rootPropertyField.AddToClassList(ROOT_FIELD_INDENT_CLASS_NAME);
            root.Add(_rootPropertyField);
            _lastBakerManagedReferenceType = bakerProperty.managedReferenceFullTypename;
            _onChangedMethod = ResolveOnChangedMethod();
            
            var refreshQueued = false;

            void QueueWarningRefresh()
            {
                if (refreshQueued)
                {
                    return;
                }

                refreshQueued = true;
                root.schedule.Execute(() =>
                {
                    refreshQueued = false;
                    RefreshWarnings(root, property);
                });
            }

            _rootPropertyField.RegisterCallback<GeometryChangedEvent>(_ => QueueWarningRefresh());
            RegisterChangeCallbacks(property);
            root.TrackSerializedObjectValue(property.serializedObject, _ => QueueWarningRefresh());
            root.RegisterCallback<AttachToPanelEvent>(_ => QueueWarningRefresh());

            QueueWarningRefresh();
            return root;
        }

        private void RegisterChangeCallbacks(SerializedProperty property)
        {
            if (string.IsNullOrWhiteSpace(_onChangedMethod))
            {
                return;
            }

            RegisterRootChangeCallback(property);
            RegisterChangeCallback(_bakerTypePropertyField, property, _onChangedMethod);
        }

        private static void RegisterChangeCallback(PropertyField propertyField, SerializedProperty property, string methodName)
        {
            if (propertyField == null)
            {
                return;
            }

            propertyField.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                var targetObject = property.serializedObject.targetObject;
                if (targetObject == null)
                {
                    return;
                }

                var method = targetObject.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                method?.Invoke(targetObject, null);
            });
        }

        private string ResolveOnChangedMethod()
        {
            var drawerAttribute = fieldInfo?.GetCustomAttribute<VFXTypeBakerFieldAttribute>();
            if (drawerAttribute == null || string.IsNullOrWhiteSpace(drawerAttribute.OnChangedMethod))
            {
                return null;
            }

            return drawerAttribute.OnChangedMethod;
        }

        private void RegisterRootChangeCallback(SerializedProperty property)
        {
            if (_rootPropertyField == null)
            {
                return;
            }

            RegisterChangeCallback(_rootPropertyField, property, _onChangedMethod);
        }

        private void RefreshWarnings(VisualElement root, SerializedProperty property)
        {
            RebuildBakerPropertyFieldIfNeeded(root, property);
            
            var vfxType = GetVfxType(property);
            var baker = GetBaker(property);
            var bakerType = baker?.GetType();
            var vfxTypeName = vfxType?.FullName;

            _bakerTypePropertyField.style.display = vfxType == null ? DisplayStyle.None : DisplayStyle.Flex;
            _rootPropertyField.style.display = bakerType == null ? DisplayStyle.None : DisplayStyle.Flex;
            _rootPropertyField.label = vfxTypeName;

            _warningBox?.RemoveFromHierarchy();
            
            if (bakerType == null)
            {
                return;
            }

            RestoreHiddenDataField();
            
            if (!IsLabelOnlyPropertyField(_rootPropertyField))
            {
                return;
            }
            
            var propertyFieldIndex = root.IndexOf(_rootPropertyField);
            _warningBox ??= new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _warningBox.Q<Label>().style.fontSize = 12f;
            _warningBox.text = $"'{vfxTypeName}' VFX type could not be drawn using a default baker. " +
                               $"The VFX type should be marked [Serializable] or a dedicated '{BakerBaseTypeName}' should be created.";
            _warningBox.AddToClassList(WARNING_CLASS_NAME);
            root.Insert(propertyFieldIndex, _warningBox);

            _rootPropertyField.style.display = DisplayStyle.None;
            _rootPropertyField.AddToClassList(HIDDEN_FIELD_CLASS_NAME);
            _hiddenDataField = _rootPropertyField;
        }

        private void RebuildBakerPropertyFieldIfNeeded(VisualElement root, SerializedProperty property)
        {
            var bakerProperty = SerializationUtils.FindRelativeProperty(property, "_baker");
            if (bakerProperty == null)
            {
                return;
            }

            var currentManagedReferenceType = bakerProperty.managedReferenceFullTypename;
            if (_rootPropertyField != null && _lastBakerManagedReferenceType == currentManagedReferenceType)
            {
                return;
            }

            var insertIndex = _rootPropertyField == null ? root.childCount : root.IndexOf(_rootPropertyField);
            if (_rootPropertyField != null)
            {
                if (_hiddenDataField == _rootPropertyField)
                {
                    _hiddenDataField = null;
                }

                _rootPropertyField.RemoveFromHierarchy();
            }

            _rootPropertyField = new PropertyField(bakerProperty, property.displayName);
            _rootPropertyField.AddToClassList(ROOT_FIELD_INDENT_CLASS_NAME);
            _rootPropertyField.BindProperty(bakerProperty);
            _rootPropertyField.RegisterCallback<GeometryChangedEvent>(_ => root.schedule.Execute(() => RefreshWarnings(root, property)));
            if (insertIndex < 0 || insertIndex > root.childCount)
            {
                root.Add(_rootPropertyField);
            }
            else
            {
                root.Insert(insertIndex, _rootPropertyField);
            }

            _lastBakerManagedReferenceType = currentManagedReferenceType;

            if (!string.IsNullOrWhiteSpace(_onChangedMethod))
            {
                RegisterRootChangeCallback(property);
            }
        }

        private static bool IsLabelOnlyPropertyField(PropertyField propertyField)
        {
            if (propertyField.childCount != 1 || propertyField[0] is not VisualElement container)
            {
                return false;
            }
            return container.childCount == 1 && container[0] is Label;
        }
        
        private static Type GetVfxType(SerializedProperty property)
        {
            var vfxTypeProperty = SerializationUtils.FindRelativeProperty(property, "_vfxDataTypeName");
            if (vfxTypeProperty == null || string.IsNullOrEmpty(vfxTypeProperty.stringValue))
            {
                return null;
            }

            return VFXTypeNameResolver.ResolveType(vfxTypeProperty.stringValue);
        }

        private static object GetBaker(SerializedProperty property)
        {
            var bakerProperty = SerializationUtils.FindRelativeProperty(property, "_baker");
            return bakerProperty?.managedReferenceValue;
        }

        private void RestoreHiddenDataField()
        {
            if (_hiddenDataField == null)
            {
                return;
            }

            _hiddenDataField.style.display = DisplayStyle.Flex;
            _hiddenDataField.RemoveFromClassList(HIDDEN_FIELD_CLASS_NAME);
            _hiddenDataField = null;
        }
    }

    [CustomPropertyDrawer(typeof(VFXDataTypeBakerWrapper))]
    public class VFXDataTypeBakerWrapperDrawer : VFXTypeBakerWrapperDrawerBase
    {
        protected override string BakerBaseTypeName => "VFXDataTypeBaker<>";
    }

    [CustomPropertyDrawer(typeof(VFXArrayDataTypeBakerWrapper))]
    public class VFXArrayDataTypeBakerWrapperDrawer : VFXTypeBakerWrapperDrawerBase
    {
        protected override string BakerBaseTypeName => "VFXArrayDataTypeBaker<>";
    }
}
