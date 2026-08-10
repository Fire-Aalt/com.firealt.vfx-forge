using System;
using System.Collections.Generic;
using FireAlt.Core.Editor;
using FireAlt.Core.Editor.UI;
using FireAlt.VFXForge.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace FireAlt.VFXForge.Editor
{
    [CustomPropertyDrawer(typeof(VFXBakerTypeDropdownAttribute))]
    public class VFXBakerTypeDropdownAttributeDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            VFXTypeRegistry.RefreshIfPending();

            var root = new VisualElement();
            var dropdownAttribute = (VFXBakerTypeDropdownAttribute)attribute;

            void Rebuild()
            {
                root.Clear();
                var items = GenerateItems(property, dropdownAttribute.BakerKind);
                var searchElement = new SearchElement(items, string.Empty, "Baker Type");
                var searchButton = searchElement.Q<Button>();

                string ResolveSelectedTypeName()
                {
                    var selectedTypeName = property.stringValue;
                    if (string.IsNullOrEmpty(selectedTypeName) && items.Count > 0)
                    {
                        selectedTypeName = items[0].Data as string;
                    }

                    return selectedTypeName;
                }

                searchElement.OnSelection += item =>
                {
                    var selectedTypeName = item.Data as string;
                    property.stringValue = selectedTypeName;
                    UpdateBakerInstance(property, selectedTypeName);
                    property.serializedObject.ApplyModifiedProperties();
                    searchElement.Text = TypeNameToDisplayName(selectedTypeName, searchButton.worldBound.width);
                };

                searchElement.SetText = item => TypeNameToDisplayName(item.Data as string, searchButton.worldBound.width);
                searchElement.Text = TypeNameToDisplayName(ResolveSelectedTypeName(), searchButton.worldBound.width);

                searchElement.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    searchElement.Text = TypeNameToDisplayName(ResolveSelectedTypeName(), searchButton.worldBound.width);
                });

                root.Add(searchElement);
            }

            void QueueRebuild()
            {
                root.schedule.Execute(Rebuild);
            }

            Rebuild();
            root.TrackSerializedObjectValue(property.serializedObject, _ => Rebuild());
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                VFXTypeRegistry.Refreshed -= QueueRebuild;
                VFXTypeRegistry.Refreshed += QueueRebuild;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ => VFXTypeRegistry.Refreshed -= QueueRebuild);
            return root;
        }

        private static void UpdateBakerInstance(SerializedProperty selectedTypeProperty, string selectedTypeName)
        {
            var bakerProperty = SerializationUtils.FindSiblingProperty(selectedTypeProperty, "_baker");
            if (bakerProperty == null)
            {
                return;
            }

            var selectedType = VFXTypeNameResolver.ResolveType(selectedTypeName);
            var currentBaker = bakerProperty.managedReferenceValue;
            bakerProperty.managedReferenceValue = VFXBakerInstanceFactory.EnsureBakerInstance(currentBaker, selectedType, selectedTypeName);
        }

        private static List<SearchView.Item> GenerateItems(SerializedProperty property, VFXDataTypeBakerKind bakerKind)
        {
            var bakerTypes = new List<SearchView.Item>();
            var vfxType = GetVfxType(property);

            if (vfxType == null || !VFXTypeCache.TryGetBakerTypes(vfxType, bakerKind, out var availableBakers))
            {
                return bakerTypes;
            }

            foreach (var bakerType in availableBakers)
            {
                bakerTypes.Add(new SearchView.Item
                {
                    Path = FormatTypeName(bakerType),
                    Data = VFXTypeNameResolver.ToStoredTypeName(bakerType),
                });
            }

            return bakerTypes;
        }

        private static string TypeNameToDisplayName(string assemblyQualifiedTypeName, float width)
        {
            var type = VFXTypeNameResolver.ResolveType(assemblyQualifiedTypeName);
            var name = type == null ? "None" : FormatTypeName(type);
            return SerializationUtils.TrimNameToWidth(name, width);
        }

        private static string FormatTypeName(Type type)
        {
            if (type == null)
            {
                return "None";
            }

            if (IsDefaultBakerType(type))
            {
                return "Default";
            }

            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }

            var genericTypeDefinition = type.GetGenericTypeDefinition();
            var genericTypeName = genericTypeDefinition.FullName ?? genericTypeDefinition.Name;
            var backtickIndex = genericTypeName.IndexOf('`');
            if (backtickIndex >= 0)
            {
                genericTypeName = genericTypeName.Substring(0, backtickIndex);
            }

            var genericArguments = type.GetGenericArguments();
            var argumentNames = new string[genericArguments.Length];
            for (var i = 0; i < genericArguments.Length; i++)
            {
                argumentNames[i] = FormatTypeName(genericArguments[i]);
            }

            return $"{genericTypeName}<{string.Join(", ", argumentNames)}>";
        }

        private static bool IsDefaultBakerType(Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            var genericTypeDefinition = type.GetGenericTypeDefinition();
            return genericTypeDefinition == typeof(DefaultVFXDataTypeBaker<>)
                || genericTypeDefinition == typeof(DefaultVFXArrayDataTypeBaker<>);
        }

        private static Type GetVfxType(SerializedProperty property)
        {
            var vfxTypeProperty = SerializationUtils.FindSiblingProperty(property, "_vfxDataTypeName");
            if (vfxTypeProperty == null || string.IsNullOrEmpty(vfxTypeProperty.stringValue))
            {
                return null;
            }

            return VFXTypeNameResolver.ResolveType(vfxTypeProperty.stringValue);
        }
    }
}
