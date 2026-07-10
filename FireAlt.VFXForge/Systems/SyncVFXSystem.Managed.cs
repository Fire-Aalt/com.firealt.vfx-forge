using System.Collections.Generic;
using FireAlt.Core.Extensions;
using FireAlt.Core.Utility;
using FireAlt.VFXForge.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;

namespace FireAlt.VFXForge
{
    public partial struct SyncVFXSystem
    {
        private struct ManagedArgs
        {
            public UnityObjectRef<VFXGraphicsBuffersObject> GraphicsBuffersObject;
            public float WorldDeltaTime;
            public JobHandle ResolvePersistentHandle;
            public NativeList<VFXStateChange> StateChanges;
        }
        
        private static unsafe void GetVFXActivityStatusPacked(void* argumentsPtr, int argumentsSize)
        {
            ref var managedArgs = ref BurstInterop.ArgumentsFromPtr<BurstManagedPair<UnityObjectRef<HybridVisualEffect>, bool>>(argumentsPtr, argumentsSize);
            ref var visualEffect = ref managedArgs.First;
            ref var isActive = ref managedArgs.Second;
            
            isActive = visualEffect.Value.VisualEffect.aliveParticleCount > 0;
        }
        
        private static unsafe void UploadDataPacked(void* argumentsPtr, int argumentsSize)
        {
            ref var managedArgs = ref BurstInterop.ArgumentsFromPtr<BurstManagedPair<VFXSingleton, ManagedArgs>>(argumentsPtr, argumentsSize);
            ref var vfxSingleton = ref managedArgs.First;
            ref var args = ref managedArgs.Second;
            
            var graphicsBuffersObject = args.GraphicsBuffersObject.Value;
            if (graphicsBuffersObject == null)
            {
                args.StateChanges.Clear();
                return;
            }

            graphicsBuffersObject.EnsureInitialized();
            
            foreach (var stateChange in args.StateChanges)
            {
                var hybridVisualEffect = stateChange.HybridVisualEffect.Value;
                hybridVisualEffect.SetVFXActive(stateChange.Enabled);
                var definition = hybridVisualEffect.VFXDefinition;
                VFXKey key = definition;

                if (stateChange.Enabled)
                {
                    var timeoutDuration = definition.timeoutDuration;
                    if (definition.IsPersistent)
                    {
                        graphicsBuffersObject.PersistentVFXGraphEntries[key] = new PersistentVFXGraphicsBuffers(
                                hybridVisualEffect.VisualEffect, definition);
                        vfxSingleton.PersistentAliveVFX.GetValueAsRef(key).SetTimeoutDuration(timeoutDuration);
                    }
                    else
                    {
                        graphicsBuffersObject.InstantVFXGraphEntries[key] = new InstantVFXGraphicsBuffers(
                            hybridVisualEffect.VisualEffect, definition);
                        vfxSingleton.InstantAliveVFX.GetValueAsRef(key).SetTimeoutDuration(timeoutDuration);
                    }
                }
                else
                {
                    if (definition.IsPersistent)
                    {
                        graphicsBuffersObject.PersistentVFXGraphEntries[key].Dispose();
                    }
                    else
                    {
                        graphicsBuffersObject.InstantVFXGraphEntries[key].Dispose();
                    }
                }
            }
            args.StateChanges.Clear();
            
            var isPlaying = Application.isPlaying;
            var unityDeltaTime = Time.deltaTime;
            var deltaTimeMultiplier = args.WorldDeltaTime / unityDeltaTime;
            if (unityDeltaTime == 0)
            {
                deltaTimeMultiplier = 1f;
            }
            
            Profiler.BeginSample("InstantVFXGraphEntries");
            foreach (var pair in vfxSingleton.InstantAliveVFX)
            {
                ref var entry = ref vfxSingleton.GetInstant(pair.Key);
                var vfxGraph = entry.HybridVisualEffect.Value.VisualEffect;
                var graphicsBuffers = graphicsBuffersObject.InstantVFXGraphEntries[pair.Key];
                if (!graphicsBuffers.HasRequiredProperties()) continue;

                var spawnRequestCount = entry.RequestsCount;
                var spawnArrayRequestCount = entry.ArrayRequestsCount;
                
                if (isPlaying)
                {
                    vfxGraph.playRate = deltaTimeMultiplier;
                }
                Common.TrySetInt(vfxGraph, VFXProperties.SpawnRequestsCount, spawnRequestCount);
                Common.TrySetInt(vfxGraph, VFXProperties.SpawnArrayRequestsCount, spawnArrayRequestCount);
                
                if (spawnRequestCount == 0 && spawnArrayRequestCount == 0) continue;

                if (entry.DataSizeInBytes > 0)
                {
                    graphicsBuffers.SetDataBuffer(entry.DataBuffer.List);
                    entry.DataBuffer.Clear();
                }
                if (entry.ArrayDataSizeInBytes > 0)
                {
                    graphicsBuffers.SetArrayDataBuffer(entry.ArrayDataBuffer.List, entry.ArrayPtrBuffer.List, entry.ArraySpawnIndexBuffer.List);
                    entry.ArrayDataBuffer.Clear();
                    entry.ArrayPtrBuffer.Clear();
                    entry.ArraySpawnIndexBuffer.Clear();
                }
                
                entry.ResetRequestsCount();
            }
            Profiler.EndSample();
            
            args.ResolvePersistentHandle.Complete();
            
            Profiler.BeginSample("PersistentVFXGraphEntries");
            foreach (var pair in vfxSingleton.PersistentAliveVFX)
            {
                ref var entry = ref vfxSingleton.GetPersistent(pair.Key);
                var vfxGraph = entry.HybridVisualEffect.Value.VisualEffect;
                var graphicsBuffers = graphicsBuffersObject.PersistentVFXGraphEntries[pair.Key];
                if (!graphicsBuffers.HasRequiredProperties()) continue;
                
                if (isPlaying)
                {
                    vfxGraph.playRate = deltaTimeMultiplier;
                }
                Common.TrySetInt(vfxGraph, VFXProperties.SpawnRequestsCount, entry.RequestsCount);
                Common.TrySetInt(vfxGraph, VFXProperties.SpawnArrayRequestsCount, entry.ArrayRequestsCount);
                
                if (entry.DataUploadRange.IsValid())
                {
                    graphicsBuffers.SetTransformBuffer(entry.TransformBuffer, entry.DataUploadRange);
                    
                    if (entry.DataSizeInBytes > 0)
                    {
                        graphicsBuffers.SetDataBuffer(entry.DataBuffer, entry.DataUploadRange);
                    }
                }
                if (entry.ArrayDataSizeInBytes > 0 && entry.ArrayDataUploadRange.IsValid())
                {
                    graphicsBuffers.SetArrayDataBuffer(entry.ArrayDataMemoryBuffer, entry.ArrayPtrBuffer,
                        entry.ArrayDataUploadRange, entry.DataUploadRange);
                }

                if (entry.RequestsCount == 0 && entry.ArrayRequestsCount == 0) continue;
                graphicsBuffers.SetIndexBuffers(entry.SpawnIndexBuffer, entry.ArraySpawnIndexBuffer);
                entry.ResetRequestsCount();
            }
            Profiler.EndSample();
        }

        private NativeList<VFXStateChange> _stateChanges;
        
        public void OnCreate(ref SystemState state)
        {
            const int VFX_CAPACITY = 32;
            
            state.EntityManager.CreateSingleton(new VFXSingleton(VFX_CAPACITY));
            state.EntityManager.CreateSingleton(new VFXGraphicsBuffersSingleton(VFX_CAPACITY));
            
            _stateChanges = new NativeList<VFXStateChange>(4, Allocator.Persistent);
        }
    }
}
