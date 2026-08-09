using System;
using FireAlt.VFXForge.Data;
using FireAlt.Core.Collections;
using FireAlt.Core.Extensions;
using FireAlt.Core.Utility;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace FireAlt.VFXForge
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    [BurstCompile]
    public partial struct SyncVFXSystem : ISystem
    {
        private struct VFXStateChange
        {
            public UnityObjectRef<HybridVisualEffect> HybridVisualEffect;
            public bool Enabled;
        }
        
        private static class Burst
        {
            public static readonly SharedStatic<BurstInterop> GetVFXActivityStatus = 
                SharedStatic<BurstInterop>.GetOrCreate<SyncVFXSystem, GetVFXActivityStatusContext>();
            public static readonly SharedStatic<BurstInterop> UploadData = 
                SharedStatic<BurstInterop>.GetOrCreate<SyncVFXSystem, UploadDataContext>();
            public static readonly SharedStatic<uint> VFXSystemVersion = 
                SharedStatic<uint>.GetOrCreate<SyncVFXSystem, SystemVersionContext>();
        }
        
        private struct GetVFXActivityStatusContext {}
        private struct UploadDataContext {}
        private struct SystemVersionContext {}
        
        public static uint SystemVersion => Burst.VFXSystemVersion.Data;
        
        static unsafe SyncVFXSystem()
        {
            Burst.GetVFXActivityStatus.Data = new BurstInterop(&GetVFXActivityStatusPacked);
            Burst.UploadData.Data = new BurstInterop(&UploadDataPacked);
            Burst.VFXSystemVersion.Data = 1;
        }

        public void OnDestroy(ref SystemState state)
        {
            // First dispose the graphics buffers, then clear VFX data + reinit VFX Graphs to not have a warning
            SystemAPI.GetSingleton<VFXGraphicsBuffersSingleton>().Dispose();
            SystemAPI.GetSingleton<VFXSingleton>().Dispose();
            _stateChanges.Dispose();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.CompleteDependencyBeforeRW<VFXSingleton>();
            
            ref var vfxSingleton = ref SystemAPI.GetSingletonRW<VFXSingleton>().ValueRW;
            Burst.VFXSystemVersion.Data++;

            var deltaTime = SystemAPI.Time.DeltaTime;
            
            if (Application.isPlaying)
            {
                using var toRemove = NativeListPool<AliveVFX>.Rent();
                RemoveTimedOutVFX(vfxSingleton.InstantAliveVFX, deltaTime, toRemove.List);
                RemoveTimedOutVFX(vfxSingleton.PersistentAliveVFX, deltaTime, toRemove.List);
            }
                
            AddVFX(vfxSingleton.PersistentVFXGraphEntries, vfxSingleton.PersistentAliveVFX); 
            AddVFX(vfxSingleton.InstantVFXGraphEntries, vfxSingleton.InstantAliveVFX);
    
            var persistentKeys = vfxSingleton.PersistentAliveVFX.GetKeyArray(state.WorldUpdateAllocator);
            var resolvePersistentHandle = new ResolvePersistentJob
            {
                VFXSingleton = vfxSingleton,
                KeysArray = persistentKeys,
            }.ScheduleParallel(persistentKeys.Length, 1, state.Dependency);
            
            var instantKeys = vfxSingleton.InstantAliveVFX.GetKeyArray(state.WorldUpdateAllocator);
            var resolveInstantHandle = new ResolveInstantJob
            {
                VFXSingleton = vfxSingleton,
                KeysArray = instantKeys,
            }.ScheduleParallel(instantKeys.Length, 1, state.Dependency);
            
            JobHandle.CombineDependencies(resolveInstantHandle, resolvePersistentHandle).Complete();

            var args = new ManagedArgs
            {
                ResolvePersistentHandle = resolvePersistentHandle,
                WorldDeltaTime = deltaTime,
                GraphicsBuffersObject = SystemAPI.GetSingleton<VFXGraphicsBuffersSingleton>().Value,
                StateChanges = _stateChanges
            };
            Burst.UploadData.Data.Invoke(vfxSingleton, args);
        }

        private void RemoveTimedOutVFX(NativeHashMap<VFXKey, AliveVFX> aliveVfxMap, float deltaTime, NativeList<AliveVFX> toRemove)
        {
            toRemove.Clear();
            foreach (var pair in aliveVfxMap)
            {
                ref var aliveVfx = ref pair.Value;
                Burst.GetVFXActivityStatus.Data.InvokeOut(aliveVfx.HybridVisualEffect, out bool isActive);
                if (isActive)
                {
                    aliveVfx.InactivityTimeRemaining = aliveVfx.TimeoutDuration;
                }
                else
                {
                    aliveVfx.InactivityTimeRemaining -= deltaTime;
                    if (aliveVfx.InactivityTimeRemaining <= 0)
                    {
                        toRemove.Add(aliveVfx);
                    }
                }
            }
            foreach (var aliveVfx in toRemove)
            {
                aliveVfxMap.Remove(aliveVfx.Key);
                _stateChanges.Add(new VFXStateChange
                {
                    HybridVisualEffect = aliveVfx.HybridVisualEffect,
                    Enabled = false,
                });
            }
        }
        
        private void AddVFX<T>(NativeHashMap<VFXKey, T> entryMap, NativeHashMap<VFXKey, AliveVFX> aliveVfxMap) 
            where T : unmanaged, IVFXGraphEntry
        {
            foreach (var pair in entryMap)
            {
                if ((pair.Value.HasPendingRequests || !Application.isPlaying) && !aliveVfxMap.ContainsKey(pair.Key))
                {
                    _stateChanges.Add(new VFXStateChange
                    {
                        HybridVisualEffect = pair.Value.HybridVisualEffect,
                        Enabled = true,
                    });
                    aliveVfxMap.Add(pair.Key, new AliveVFX
                    {
                        Key = pair.Key,
                        HybridVisualEffect = pair.Value.HybridVisualEffect,
                    });
                }
            }
        }
        
        [BurstCompile]
        private struct ResolveInstantJob : IJobFor
        {
            [ReadOnly]
            public NativeArray<VFXKey> KeysArray;
            [NativeDisableContainerSafetyRestriction]
            public VFXSingleton VFXSingleton;
            
            public void Execute(int index)
            {
                ref var entry = ref VFXSingleton.GetInstant(KeysArray[index]);
                
                if (entry.DataSizeInBytes > 0)
                {
                    entry.DataBuffer.CopyParallelToList();
                }
                if (entry.ArrayDataSizeInBytes > 0)
                {
                    entry.ArrayDataBuffer.CopyParallelToList();
                    
                    RemapIndices(ref entry);

                    entry.ArrayPtrBuffer.CopyParallelToList();
                    entry.ArraySpawnIndexBuffer.CopyParallelToList();
                }
            }
            
            private static void RemapIndices(ref InstantVFXEntry entry)
            {
                var arrayPtrBuffer = entry.ArrayPtrBuffer.ThreadList;
                var arraySpawnIndexBuffer = entry.ArraySpawnIndexBuffer.ThreadList;
                uint indexInDataOffset = 0;
                uint ptrOffset = 0;
                    
                for (int thread = 0; thread < JobsUtility.ThreadIndexCount; thread++)
                {
                    var pointers = arrayPtrBuffer.GetUnsafeList(thread);
                    var indices = arraySpawnIndexBuffer.GetUnsafeList(thread);
                    uint elementCount = 0;
                    
                    for (int i = 0; i < pointers.Length; i++)
                    {
                        ref var pointer = ref pointers.ElementAt(i);
                            
                        pointer.StartIndex += ptrOffset;
                        elementCount += pointer.Count;
                    }
                    for (int i = 0; i < indices.Length; i++)
                    {
                        ref var indexData = ref indices.ElementAt(i);
                        indexData.IndexInData += indexInDataOffset;
                    }

                    ptrOffset += elementCount;
                    indexInDataOffset += (uint)pointers.Length;
                }
            }
        }
        
        [BurstCompile]
        private struct ResolvePersistentJob : IJobFor
        {
            [ReadOnly]
            public NativeArray<VFXKey> KeysArray;
            [NativeDisableContainerSafetyRestriction]
            public VFXSingleton VFXSingleton;
            
            public void Execute(int index)
            {
                var internalApi = VFXSingleton.AsInternal();
                ref var entry = ref VFXSingleton.GetPersistent(KeysArray[index]);

                entry.EnsureCapacity(entry.UsedCapacity + entry.SpawnRequests.Length);
                SpawnPersistentRequests(ref entry, internalApi);
                // Kill is after spawn, so that the indices will be reused with 1 frame delay for the VFX to react
                KillPersistentRequests(ref entry, internalApi, out var deadRange);
                
                entry.SpawnRequests.Clear();
                entry.SpawnDataBuffer.Clear();
                entry.KillRequests.Clear();
                entry.CapacityReservations = 0;
                entry.UsedCapacity = entry.TrackedEntityIds.Count + entry.TrackedEntities.Count;

                entry.DataUploadRange = deadRange;
                if (entry.AliveMask.TryGetRange(out var aliveRange))
                {
                    entry.DataUploadRange.Encapsulate(new UploadRange(aliveRange));
                }
                
                entry.ArrayDataUploadRange.Reset();
                if (entry.ArrayDataMemoryBuffer.TryGetValidRange(out aliveRange))
                {
                    entry.ArrayDataUploadRange = new UploadRange(aliveRange);
                }
            }
            
            private static unsafe void SpawnPersistentRequests(ref PersistentVFXEntry entry,
                VFXSingleton.InternalAPI internalApi)
            {
                for (var threadIndex = 0; threadIndex < JobsUtility.ThreadIndexCount; threadIndex++)
                {
                    ref var requests = ref entry.SpawnRequests.GetUnsafeList(threadIndex);
                    ref var data = ref entry.SpawnDataBuffer.GetUnsafeList(threadIndex);
                    for (var localIndex = 0; localIndex < requests.Length; localIndex++)
                    {
                        ref var request = ref requests.ElementAt(localIndex);
                        var deferredKey = request.TrackedEntity;
                        using var pooledArrayData = TakeDeferredArrayData(ref request);
                        var deferredTransform = request.Transform;
                        if (!deferredTransform.DidTransformSystemRun())
                        {
                            throw new Exception($"A persistent VFXKey({entry.VFXKey.Value}) spawn was requested between `VFXTransformSystem` and `SyncVFXSystem` which means the upload data does not carry Transform information. Do not spawn persistent VFX in `LateUpdate`.");
                        }
                        if (!deferredTransform.IsAlive())
                        {
                            continue;
                        }

                        TrackedEntity resolvedKey;
                        if (entry.DataSizeInBytes > 0)
                        {
                            var ptr = (byte*)data.Ptr + localIndex * entry.DataSizeInBytes;
                            resolvedKey = internalApi.SpawnPersistentUnsafe(ref entry, deferredKey, ptr,
                                pooledArrayData.Array, deferredTransform.TrackingDuration);
                        }
                        else
                        {
                            resolvedKey = internalApi.SpawnPersistent(ref entry, deferredKey, pooledArrayData.Array,
                                deferredTransform.TrackingDuration);
                        }

                        if (!resolvedKey.IsValid) continue;

                        entry.TransformBuffer[resolvedKey.IndexInData] = deferredTransform;
                        entry.DeferredToResolvedMap.Add(deferredKey, resolvedKey);
                        entry.ResolvedToRequestMap.Add(resolvedKey, deferredKey);
                    }
                }
            }
            
            private static void KillPersistentRequests(ref PersistentVFXEntry entry, VFXSingleton.InternalAPI internalApi, out UploadRange deadRange)
            {
                deadRange = default;
                deadRange.Reset();
                foreach (var resolvedKey in entry.KillRequests)
                {
                    deadRange.Encapsulate(resolvedKey.IndexInData);
                    internalApi.KillPersistent(ref entry, resolvedKey);
                }
            }
            
            private static PooledUnsafeArray<byte> TakeDeferredArrayData(ref PersistentVFXDeferredRequest request)
            {
                var taken = request.ArrayData;
                request.ArrayData = default;
                return taken;
            }
        }
    }
}
