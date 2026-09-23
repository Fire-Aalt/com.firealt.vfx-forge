using Unity.Scripting.LifecycleManagement;
using System;

namespace FireAlt.VFXForge.Data
{
    [NoAutoStaticsCleanup]
    public struct VFXKey : IEquatable<VFXKey>
    {
        public ushort Value;
        
        public static readonly VFXKey Null = new();
        
        public static implicit operator VFXKey(ushort id)
        {
            return new VFXKey { Value = id };
        }
        
        public static implicit operator VFXKey(int id)
        {
            return (ushort)id;
        }

        public bool Equals(VFXKey other)
        {
            return Value == other.Value;
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return $"VFXKey({Value})";
        }
    }
}