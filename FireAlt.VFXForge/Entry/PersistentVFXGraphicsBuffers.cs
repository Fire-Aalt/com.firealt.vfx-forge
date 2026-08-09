using FireAlt.VFXForge.Data;
using FireAlt.Core.Collections;
using FireAlt.Core.Extensions;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.VFX;

namespace FireAlt.VFXForge
{
    public class PersistentVFXGraphicsBuffers : VFXGraphicsBuffers
    {
        private static readonly ProfilerMarker TransformMarker = new("Set TransformBuffer");
        private static readonly ProfilerMarker SpawnIndexMarker = new("Set SpawnIndexBuffer");
        private static readonly ProfilerMarker DataMarker = new("Set DataBuffer");
        private static readonly ProfilerMarker ArrayDataMarker = new("Set ArrayDataBuffer");
        private static readonly ProfilerMarker ArrayPtrMarker = new("Set ArrayPtrBuffer");
        
        private GraphicsBuffer _spawnIndexBuffer;
        private GraphicsBuffer _transformBuffer;
        
        private GraphicsBuffer _arraySpawnIndexBuffer;
        private GraphicsBuffer _dataBuffer;
        private GraphicsBuffer _arrayPtrBuffer;
        private GraphicsBuffer _arrayDataBuffer;
        
        public PersistentVFXGraphicsBuffers(VisualEffect target, VFXDefinition definition) 
            : base(target, definition)
        {
            var doubleCapacity = definition.InitialCapacity * 2;
            CreateGraphicsBuffer(ref _transformBuffer, VFXProperties.TransformBuffer, math.max(1, doubleCapacity),
                UnsafeUtility.SizeOf<VFXTransform>());

            if (definition.DataGpuSize != 0 || definition.ArrayDataGpuSize == 0)
            {
                CreateGraphicsBuffer(ref _spawnIndexBuffer, VFXProperties.SpawnIndexBuffer, math.max(1, doubleCapacity),
                    UnsafeUtility.SizeOf<VFXSpawnIndex>());
            }
            
            if (definition.DataGpuSize != 0)
            {
                CreateGraphicsBuffer(ref _dataBuffer, VFXProperties.DataBuffer, math.max(1, doubleCapacity), definition.DataGpuSize);
            }
            if (definition.ArrayDataGpuSize != 0)
            {
                CreateGraphicsBuffer(ref _arraySpawnIndexBuffer, VFXProperties.ArraySpawnIndexBuffer,
                    math.max(1, definition.InitialCapacity), UnsafeUtility.SizeOf<VFXArraySpawnIndex>());
                CreateGraphicsBuffer(ref _arrayPtrBuffer, VFXProperties.ArrayPtrBuffer, math.max(1, doubleCapacity),
                    UnsafeUtility.SizeOf<VFXArrayPtr>());
                ResizeArrayDataBuffer(definition.InitialCapacity * definition.ArrayDataGpuSize);
            }
        }

        protected override void CheckHasSharedBuffers()
        {
            CheckHasBuffer(VFXProperties.TransformBuffer);
            if (ArrayDataGpuSize == 0)
            {
                CheckHasBuffer(VFXProperties.SpawnIndexBuffer);
            }
        }
        
        protected override void CheckHasDataBuffers()
        {
            CheckHasBuffer(VFXProperties.DataBuffer);
        }

        protected override void CheckHasArrayDataBuffers()
        {
            if (DataGpuSize == 0)
            {
                CheckHasBuffer(VFXProperties.ArraySpawnIndexBuffer);
            }
            CheckHasBuffer(VFXProperties.ArrayDataBuffer);
            CheckHasBuffer(VFXProperties.ArrayPtrBuffer);
        }

        public void SetTransformBuffer(UnsafeArray<VFXTransform> data, UploadRange uploadRange)
        {
            ResizeBuffer<VFXTransform>(Target, ref _transformBuffer, VFXProperties.TransformBuffer, data.Length);
            SetBuffer(_transformBuffer, data.AsNativeArray(), uploadRange, TransformMarker);
        }
        
        public void SetDataBuffer(UnsafeArray<byte> data, UploadRange uploadRange)
        {
            ResizeBuffer(Target, ref _dataBuffer, VFXProperties.DataBuffer, DataGpuSize, data.Length);
            SetBuffer(_dataBuffer, data.AsNativeArray(), uploadRange.Expand(DataGpuSize), DataMarker);
        }
        
        public void SetIndexBuffers(UnsafeList<VFXSpawnIndex> spawnIndices, UnsafeList<VFXArraySpawnIndex> arraySpawnIndices)
        {
            if (spawnIndices.IsCreated)
            {
                ResizeBuffer<VFXSpawnIndex>(Target, ref _spawnIndexBuffer, VFXProperties.SpawnIndexBuffer,
                    spawnIndices.Length);
                SetBuffer(_spawnIndexBuffer, spawnIndices.AsNativeArray(), new UploadRange(0, spawnIndices.Length), SpawnIndexMarker);
            }

            if (arraySpawnIndices.IsCreated)
            {
                ResizeBuffer<VFXArraySpawnIndex>(Target, ref _arraySpawnIndexBuffer, VFXProperties.ArraySpawnIndexBuffer, arraySpawnIndices.Length);
                SetBuffer(_arraySpawnIndexBuffer, arraySpawnIndices.AsNativeArray(), new UploadRange(0, arraySpawnIndices.Length), SpawnIndexMarker);
            }
        }
        
        public void SetArrayDataBuffer(in UnsafeHeapMemory data, UnsafeArray<VFXArrayPtr> arrayPtrs, 
            UploadRange arrayDataRange, UploadRange ptrUploadRange)
        {
            var dataList = data.DataList;
            var arrayByteRange = arrayDataRange.Expand(ArrayDataGpuSize);
            
            ResizeArrayDataBuffer(arrayByteRange.EndIndex);
            SetBuffer(_arrayDataBuffer, dataList.AsNativeArray(), arrayByteRange, ArrayDataMarker);
            ResizeBuffer<VFXArrayPtr>(Target, ref _arrayPtrBuffer, VFXProperties.ArrayPtrBuffer, arrayPtrs.Length);
            SetBuffer(_arrayPtrBuffer, arrayPtrs.AsNativeArray(), ptrUploadRange, ArrayPtrMarker);
        }
        
        private void ResizeArrayDataBuffer(int minDataCapacity)
        {
            ResizeBuffer(Target, ref _arrayDataBuffer, VFXProperties.ArrayDataBuffer, ArrayDataGpuSize, minDataCapacity);
        }
        
        public override void Dispose()
        {
            _transformBuffer.Dispose();
            
            _spawnIndexBuffer?.Dispose();
            _dataBuffer?.Dispose();
            
            _arraySpawnIndexBuffer?.Dispose();
            _arrayDataBuffer?.Dispose();
            _arrayPtrBuffer?.Dispose();
        }
    }
}
