using FireAlt.Core.Inspectors;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.VFX;

namespace FireAlt.VFXForge.Data
{
    public class VFXDecalDefinition : ScriptableObject
    {
        public VisualEffectAsset visualEffectAsset;
        [FormerlySerializedAs("capacity")]
        public int initialCapacity = 100;
        public bool useMaxCapacity;
        [ShowIf(nameof(useMaxCapacity))]
        public int maxCapacity = 100;
        public float timeoutDuration = 30f;
        
        [VFXDataTypeDropdown(VFXDataTypeBakerKind.Data)]
        public ulong vfxDataType;
        [VFXDataTypeDropdown(VFXDataTypeBakerKind.ArrayData)]
        public ulong vfxArrayDataType;
        
#if UNITY_EDITOR
        private void OnValidate()
        {
            timeoutDuration = math.max(timeoutDuration, 0);
            initialCapacity = math.max(initialCapacity, 0);
            maxCapacity = math.max(maxCapacity, initialCapacity);
        }
#endif

        public VFXDefinition CreateDefinition(ushort newId)
        {
            var inst = CreateInstance<VFXDefinition>();
            inst.key = newId;
            inst.timeoutDuration = timeoutDuration;
            inst.initialCapacity = initialCapacity;
            inst.useMaxCapacity = useMaxCapacity;
            inst.maxCapacity = maxCapacity;
            inst.vfxDataType = vfxDataType;
            inst.vfxArrayDataType = vfxArrayDataType;
            inst.visualEffectAsset = visualEffectAsset;
            inst.vfxType = VFXType.Persistent;
            return inst;
        }
    }
}
