using Unity.Scripting.LifecycleManagement;
using UnityEngine;

namespace FireAlt.VFXForge.Data
{
    [NoAutoStaticsCleanup]
    public static class VFXProperties
    {
        // Persistent & Instant
        public static readonly ShaderProperty SpawnRequestsCount = new("SpawnRequestsCount");
        public static readonly ShaderProperty SpawnIndexBuffer = new("SpawnIndexBuffer");
        public static readonly ShaderProperty DataBuffer = new("DataBuffer");
        
        public static readonly ShaderProperty SpawnArrayRequestsCount = new("SpawnArrayRequestsCount");
        public static readonly ShaderProperty ArraySpawnIndexBuffer = new("ArraySpawnIndexBuffer");
        public static readonly ShaderProperty ArrayPtrBuffer = new("ArrayPtrBuffer");
        public static readonly ShaderProperty ArrayDataBuffer = new("ArrayDataBuffer");
        
        // Persistent
        public static readonly ShaderProperty TransformBuffer = new("TransformBuffer");
    }

    public struct ShaderProperty
    {
        public int Id;
        public string Name;

        public ShaderProperty(string name)
        {
            Id = Shader.PropertyToID(name);
            Name = name;
        }
    }
}