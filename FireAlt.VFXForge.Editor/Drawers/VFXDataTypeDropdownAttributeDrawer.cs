using System.Collections.Generic;
using FireAlt.Core.Editor;
using FireAlt.Core.Editor.UI;
using FireAlt.VFXForge.Data;
using UnityEditor;
using UnityEngine.UIElements;

namespace FireAlt.VFXForge.Editor
{
    [CustomPropertyDrawer(typeof(VFXDataTypeDropdownAttribute))]
    public class VFXDataTypeDropdownAttributeDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            VFXTypeRegistry.RefreshIfPending();

            var root = new VisualElement();
            var dropdownAttribute = (VFXDataTypeDropdownAttribute)attribute;

            void Rebuild()
            {
                root.Clear();
                var items = GenerateItems(dropdownAttribute.BakerKind);
                var searchElement = new SearchElement(items, string.Empty, property.displayName);
                searchElement.OnSelection += item =>
                {
                    var stableTypeHash = (ulong)item.Data!;
                    property.longValue = (long)stableTypeHash;
                    property.serializedObject.ApplyModifiedProperties();
                };

                var searchButton = searchElement.Q<Button>();
                searchElement.SetText = item => HashToName((ulong)item.Data, searchButton.worldBound.width);

                searchElement.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    searchElement.Text = HashToName((ulong)property.longValue, searchButton.worldBound.width);
                });

                root.Add(searchElement);
            }

            void QueueRebuild()
            {
                root.schedule.Execute(Rebuild);
            }

            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                VFXTypeRegistry.Refreshed -= QueueRebuild;
                VFXTypeRegistry.Refreshed += QueueRebuild;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ => VFXTypeRegistry.Refreshed -= QueueRebuild);

            Rebuild();
            return root;
        }

        private static string HashToName(ulong stableTypeHash, float width)
        {
            var type = VFXTypeRegistry.GetType(stableTypeHash);
            var name = type == null ? "None" : type.ToString();
            return SerializationUtils.TrimNameToWidth(name, width);
        }
        
        protected List<SearchView.Item> GenerateItems(VFXDataTypeBakerKind bakerKind)
        {
            var componentTypes = new List<SearchView.Item> { new() { Path = "None", Data = 0UL } };
            var vfxTypes = bakerKind == VFXDataTypeBakerKind.Data
                ? VFXTypeCache.DataTypesList
                : VFXTypeCache.ArrayTypesList;

            foreach (var e in vfxTypes)
            {
                var stableTypeHash = VFXTypeRegistry.GetStableTypeHash(e);

                componentTypes.Add(new SearchView.Item { Path = VFXTypeCache.TypeNamesDictionary[e], Data = stableTypeHash });
            }

            return componentTypes;
        }
    }
}
