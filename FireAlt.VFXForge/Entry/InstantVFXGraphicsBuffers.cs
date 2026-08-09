using FireAlt.VFXForge.Data;
using FireAlt.Core.Extensions;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.VFX;

namespace FireAlt.VFXForge
{
    public class InstantVFXGraphicsBuffers : VFXGraphicsBuffers
    {
        private static readonly ProfilerMarker DataMarker = new("Set DataBuffer");
        private static readonly ProfilerMarker ArrayDataMarker = new("Set ArrayDataBuffer");
        private static readonly ProfilerMarker ArrayPtrMarker = new("Set ArrayPtrBuffer");
        private static readonly ProfilerMarker SpawnIndexMarker = new("Set SpawnIndexBuffer");
        
        private GraphicsBuffer _dataBuffer;
        private GraphicsBuffer _arrayDataBuffer;
        private GraphicsBuffer _arrayPtrBuffer;
        private GraphicsBuffer _arraySpawnIndexBuffer;
        
        public InstantVFXGraphicsBuffers(VisualEffect target, VFXDefinition definition) 
            : base(target, definition)
        {
            if (definition.DataGpuSize != 0)
            {
                ResizeDataBuffer(definition.InitialCapacity * definition.DataGpuSize);
            }
            if (definition.ArrayDataGpuSize != 0)
            {
                ResizeArrayDataBuffer(definition.InitialCapacity * definition.ArrayDataGpuSize,
                    definition.InitialCapacity, definition.InitialCapacity);
            }
        }
        
        protected override void CheckHasDataBuffers()
        {
            CheckHasBuffer(VFXProperties.DataBuffer);
        }

        protected override void CheckHasArrayDataBuffers()
        {
            CheckHasBuffer(VFXProperties.ArraySpawnIndexBuffer);
            CheckHasBuffer(VFXProperties.ArrayDataBuffer);
        }

        public void SetDataBuffer(UnsafeList<byte> data)
        {
            ResizeDataBuffer(data.Length);
            
            SetBuffer(_dataBuffer, data.AsNativeArray(), new UploadRange(0, data.Length), DataMarker);
        }
        
        public void SetArrayDataBuffer(UnsafeList<byte> data, UnsafeList<VFXArrayPtr> arrayPtrs, UnsafeList<VFXArraySpawnIndex> spawnIndices)
        {
            ResizeArrayDataBuffer(data.Length, arrayPtrs.Length, spawnIndices.Length);
            
            SetBuffer(_arrayDataBuffer, data.AsNativeArray(), new UploadRange(0, data.Length), ArrayDataMarker);
            SetBuffer(_arrayPtrBuffer, arrayPtrs.AsNativeArray(), new UploadRange(0, arrayPtrs.Length), ArrayPtrMarker);
            SetBuffer(_arraySpawnIndexBuffer, spawnIndices.AsNativeArray(), new UploadRange(0, spawnIndices.Length), SpawnIndexMarker);
        }
        
        private void ResizeDataBuffer(int minByteCapacity)
        {
            ResizeBuffer(Target, ref _dataBuffer, VFXProperties.DataBuffer, DataGpuSize, minByteCapacity);
        }
        
        private void ResizeArrayDataBuffer(int minDataCapacity, int minPtrsCapacity, int spawnCount)
        {
            ResizeBuffer(Target, ref _arrayDataBuffer, VFXProperties.ArrayDataBuffer, ArrayDataGpuSize, minDataCapacity);
            ResizeBuffer<VFXArrayPtr>(Target, ref _arrayPtrBuffer, VFXProperties.ArrayPtrBuffer, minPtrsCapacity);
            ResizeBuffer<VFXArraySpawnIndex>(Target, ref _arraySpawnIndexBuffer, VFXProperties.ArraySpawnIndexBuffer, spawnCount);
        }
        
        public override void Dispose()
        {
            _dataBuffer?.Dispose();
            _arrayDataBuffer?.Dispose();
            _arrayPtrBuffer?.Dispose();
            _arraySpawnIndexBuffer?.Dispose();
        }
    }
}
