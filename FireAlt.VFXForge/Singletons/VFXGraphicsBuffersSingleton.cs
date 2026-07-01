using System;
using System.Collections.Generic;
using FireAlt.VFXForge.Data;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.VFX;

namespace FireAlt.VFXForge
{
    internal struct VFXGraphicsBuffersSingleton : IComponentData, IDisposable
    {
        public UnityObjectRef<VFXGraphicsBuffersObject> Value;

        public VFXGraphicsBuffersSingleton(int capacity)
        {
            Value = ScriptableObject.CreateInstance<VFXGraphicsBuffersObject>();
            Value.Value.InstantVFXGraphEntries = new Dictionary<VFXKey, InstantVFXGraphicsBuffers>(capacity);
            Value.Value.PersistentVFXGraphEntries = new Dictionary<VFXKey, PersistentVFXGraphicsBuffers>(capacity);
        }
        
        [BurstDiscard]
        public void Dispose()
        {
            Value.Value.Dispose();
        }
    }

    public class VFXGraphicsBuffersObject : ScriptableObject, IDisposable
    {
        public Dictionary<VFXKey, InstantVFXGraphicsBuffers> InstantVFXGraphEntries;
        public Dictionary<VFXKey, PersistentVFXGraphicsBuffers> PersistentVFXGraphEntries;
        
        public void Dispose()
        {
            foreach (var pair in InstantVFXGraphEntries)
            {
                pair.Value.Dispose();
            }
            foreach (var pair in PersistentVFXGraphEntries)
            {
                pair.Value.Dispose();
            }
        }
    }

    public abstract class VFXGraphicsBuffers : IDisposable
    {
        protected readonly VisualEffect Target;
        protected readonly int DataGpuSize;
        protected readonly int ArrayDataGpuSize;

        protected VFXGraphicsBuffers(VisualEffect target, VFXDefinition definition)
        {
            Target = target;
            DataGpuSize = definition.DataGpuSize;
            ArrayDataGpuSize = definition.ArrayDataGpuSize;
        }
        
        protected void CreateGraphicsBuffer(ref GraphicsBuffer graphicsBuffer, ShaderProperty vfxProperty, int count, int stride)
        {
            graphicsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
            Common.SetGraphicsBuffer(Target, vfxProperty, graphicsBuffer);
        }
        
        protected static void SetBuffer<T>(GraphicsBuffer buffer, NativeArray<T> data, UploadRange uploadRange, ProfilerMarker marker)
            where T : unmanaged
        {
            Assert.IsTrue(buffer.IsValid());
            marker.Begin();
            buffer.SetData(data, uploadRange.StartIndex, uploadRange.StartIndex, uploadRange.Count);
            marker.End();
        }
        
        protected static void ResizeBuffer(VisualEffect target, ref GraphicsBuffer buffer, ShaderProperty bufferProperty, int gpuSize, int minCapacity)
        {
            var requiredCount = (minCapacity + gpuSize - 1) / gpuSize;
            if (buffer == null || buffer.count < requiredCount)
            {
                var bufferCount = buffer?.count * 2 ?? 0;
                var newCapacity = math.ceilpow2(math.max(requiredCount, bufferCount));
                
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, gpuSize);
                Common.SetGraphicsBuffer(target, bufferProperty, buffer);
            }
        }
        
        protected static void ResizeBuffer<T>(VisualEffect target, ref GraphicsBuffer buffer, ShaderProperty bufferProperty, int minCapacity) 
            where T : unmanaged
        {
            if (buffer == null || buffer.count < minCapacity)
            {
                var bufferCount = buffer?.count * 2 ?? 0;
                var newCapacity = math.ceilpow2(math.max(minCapacity, bufferCount));
                
                buffer?.Release();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newCapacity, UnsafeUtility.SizeOf<T>());
                Common.SetGraphicsBuffer(target, bufferProperty, buffer);
            }
        }

        public virtual void Dispose()
        {
            
        }
        
        public bool HasRequiredProperties()
        {
            try
            {
                Common.CheckHasSpawnRequestsInt(Target, DataGpuSize, ArrayDataGpuSize);
                CheckHasSharedBuffers();
                if (DataGpuSize != 0)
                {
                    CheckHasDataBuffers();
                }
                if (ArrayDataGpuSize != 0)
                {
                    CheckHasArrayDataBuffers();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e, Target);
                return false;
            }
            return true;
        }
        
        protected virtual void CheckHasSharedBuffers() {}
        protected abstract void CheckHasDataBuffers();
        protected abstract void CheckHasArrayDataBuffers();
        
        protected void CheckHasBuffer(ShaderProperty bufferProperty)
        {
            Common.CheckHasBuffer(Target, bufferProperty);
        }
    }
}
