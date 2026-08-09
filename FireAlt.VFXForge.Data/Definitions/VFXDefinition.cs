using System;
using FireAlt.Core.Inspectors;
using FireAlt.Core.ObjectManagement;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;

namespace FireAlt.VFXForge.Data
{
    public class VFXDefinition : ScriptableObject, IUID
    {
        [SerializeField, InspectorReadOnly]
        internal ushort key;
        
        int IUID.ID
        {
            get => key;
            set
            {
                if (value is < 0 or > ushort.MaxValue)
                {
                    Debug.LogError("Ran out of keys");
                    return;
                }

                key = (ushort)value;
            }
        }
        
        public static implicit operator VFXKey(VFXDefinition definition)
        {
            return definition == null ? 0 : definition.key;
        }
        
        public VisualEffectAsset visualEffectAsset;
        public int initialCapacity = 64;
        public bool useMaxCapacity;
        [ShowIf(nameof(useMaxCapacity))]
        public int maxCapacity = 128;
        public float timeoutDuration = 30f;
        
        [EnumToggleButtons]
        public VFXType vfxType;
        [VFXDataTypeDropdown(VFXDataTypeBakerKind.Data)]
        public ulong vfxDataType;
        [VFXDataTypeDropdown(VFXDataTypeBakerKind.ArrayData)]
        public ulong vfxArrayDataType;
        
        public bool IsPersistent => vfxType == VFXType.Persistent;
        public int InitialCapacity => math.max(initialCapacity, 0);
        public int MaxCapacity => math.max(maxCapacity, InitialCapacity);
        public int DataGpuSize => DataTypeInfo.GpuSize;
        public int ArrayDataGpuSize => ArrayDataTypeInfo.GpuSize;
        
        public VFXTypeRegistry.TypeInfo DataTypeInfo => VFXTypeRegistry.GetTypeInfo(vfxDataType);
        public VFXTypeRegistry.TypeInfo ArrayDataTypeInfo => VFXTypeRegistry.GetTypeInfo(vfxArrayDataType);
        
#if UNITY_EDITOR
        public static event Action OnVFXDefinitionChanged = delegate { };
        
        private void OnValidate()
        {
            timeoutDuration = math.max(timeoutDuration, 0);
            initialCapacity = math.max(initialCapacity, 0);
            maxCapacity = math.max(maxCapacity, initialCapacity);
            OnVFXDefinitionChanged.Invoke();
        }
#endif
    }
}
