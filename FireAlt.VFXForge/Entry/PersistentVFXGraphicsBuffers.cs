using FireAlt.VFXForge.Data;
using FireAlt.Core.Collections;
using FireAlt.Core.Extensions;
using Unity.Collections.LowLevel.Unsafe;
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
        
        private readonly GraphicsBuffer _spawnIndexBuffer;
        private readonly GraphicsBuffer _transformBuffer;
        
        private GraphicsBuffer _arraySpawnIndexBuffer;
        private readonly GraphicsBuffer _dataBuffer;
        private readonly GraphicsBuffer _arrayPtrBuffer;
        private GraphicsBuffer _arrayDataBuffer;
        
        public PersistentVFXGraphicsBuffers(VisualEffect target, VFXDefinition definition) 
            : base(target, definition)
        {
            var doubleCapacity = definition.capacity * 2;
            CreateGraphicsBuffer(ref _transformBuffer, VFXProperties.TransformBuffer, doubleCapacity, UnsafeUtility.SizeOf<VFXTransform>());
            CreateGraphicsBuffer(ref _spawnIndexBuffer, VFXProperties.SpawnIndexBuffer, doubleCapacity, UnsafeUtility.SizeOf<VFXSpawnIndex>());
            
            if (definition.DataGpuSize != 0)
            {
                CreateGraphicsBuffer(ref _dataBuffer, VFXProperties.DataBuffer, doubleCapacity, definition.DataGpuSize);
            }
            if (definition.ArrayDataGpuSize != 0)
            {
                CreateGraphicsBuffer(ref _arraySpawnIndexBuffer, VFXProperties.ArraySpawnIndexBuffer, doubleCapacity / 2, UnsafeUtility.SizeOf<VFXArraySpawnIndex>());
                CreateGraphicsBuffer(ref _arrayPtrBuffer, VFXProperties.ArrayPtrBuffer, doubleCapacity, UnsafeUtility.SizeOf<VFXArrayPtr>());
                ResizeArrayDataBuffer(4096);
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
            SetBuffer(_transformBuffer, data.AsNativeArray(), uploadRange, TransformMarker);
        }
        
        public void SetDataBuffer(UnsafeArray<byte> data, UploadRange uploadRange)
        {
            SetBuffer(_dataBuffer, data.AsNativeArray(), uploadRange.Expand(DataGpuSize), DataMarker);
        }
        
        public void SetIndexBuffers(UnsafeList<VFXSpawnIndex> spawnIndices, UnsafeList<VFXArraySpawnIndex> arraySpawnIndices)
        {
            if (spawnIndices.IsCreated)
            {
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
