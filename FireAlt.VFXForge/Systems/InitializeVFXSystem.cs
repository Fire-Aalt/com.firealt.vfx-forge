using FireAlt.VFXForge.Data;
using FireAlt.Core.Collections;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace FireAlt.VFXForge
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializeVFXSystemGroup), OrderFirst = true)]
    public partial class InitializeVFXSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRW<VFXSingleton>();
            var vfxSingleton = SystemAPI.GetSingleton<VFXSingleton>();

            foreach (var (component, registeredVFX, initializeRW) in SystemAPI.Query<RefRO<HybridVisualEffectData>,
                         RefRW<RegisteredVFX>, EnabledRefRW<HybridVisualEffectData>>())
            {
                var hve = component.ValueRO.HybridVisualEffect.Value;
                if (TryAddVFXEntry(ref vfxSingleton, hve, out var key))
                {
                    registeredVFX.ValueRW.Key = key;
                }

                initializeRW.ValueRW = false;
            }
        }
        
        private bool TryAddVFXEntry(ref VFXSingleton vfxSingleton, HybridVisualEffect hybridVisualEffect,
            out VFXKey key)
        {
            var definition = hybridVisualEffect.VFXDefinition;
            
            key = definition;
            if (!vfxSingleton.IsPersistent.TryAdd(key, definition.IsPersistent))
            {
                Debug.LogError($"{key} was already added to the VFX system. There cannot be duplicates.", definition);
                return false;
            }
            
            if (!definition.IsPersistent)
            {
                var perThreadCapacity = (definition.InitialCapacity + JobsUtility.ThreadIndexCount - 1) /
                                        JobsUtility.ThreadIndexCount;
                var entry = new InstantVFXEntry(hybridVisualEffect)
                {
                    PendingRequestsCount = new UnsafeThreadData<InstantVFXEntry.Requests>(Allocator.Persistent),
                };
                
                if (entry.DataSizeInBytes > 0)
                {
                    entry.DataBuffer = new UnsafeThreadToListMapper<byte>(perThreadCapacity * entry.DataSizeInBytes,
                        Allocator.Persistent);
                }
                if (entry.ArrayDataSizeInBytes > 0)
                {
                    entry.ArrayDataBuffer = new UnsafeThreadToListMapper<byte>(perThreadCapacity * entry.ArrayDataSizeInBytes,
                        Allocator.Persistent);
                    entry.ArrayPtrBuffer = new UnsafeThreadToListMapper<VFXArrayPtr>(perThreadCapacity, Allocator.Persistent);
                    entry.ArraySpawnIndexBuffer = new UnsafeThreadToListMapper<VFXArraySpawnIndex>(perThreadCapacity,
                        Allocator.Persistent);
                }
                
                vfxSingleton.InstantVFXGraphEntries.Add(key, entry);
            }
            else
            {
                // We allocate twice the specified user capacity as the data lives for multiple frames
                // and destruction can create holes that have to exist when publishing the buffer to GPU
                var initialCapacity = definition.InitialCapacity;
                var doubleCapacity = initialCapacity * 2;
                var perThreadCapacity = (initialCapacity + JobsUtility.ThreadIndexCount - 1) /
                                        JobsUtility.ThreadIndexCount;
                
                var entry = new PersistentVFXEntry(hybridVisualEffect)
                {
                    Capacity = initialCapacity,
                    UseMaxCapacity = definition.useMaxCapacity,
                    MaxCapacity = definition.MaxCapacity,
                    AliveMask = new UnsafeBitMaskRange(doubleCapacity, Allocator.Persistent),
                    TrackedEntities = new UnsafeHashSet<TrackedEntity>(doubleCapacity, Allocator.Persistent),
                    TrackedEntityIds = new UnsafeHashSet<TrackedEntity>(doubleCapacity, Allocator.Persistent),
                    EntityIdFrameData = new UnsafeList<EntityIdData>(32, Allocator.Persistent),
                    SpawnRequests = new UnsafeThreadList<PersistentVFXDeferredRequest>(perThreadCapacity, Allocator.Persistent),
                    SpawnDataBuffer = new UnsafeThreadList<byte>(perThreadCapacity * definition.DataGpuSize, Allocator.Persistent),
                    KillRequests = new UnsafeThreadList<TrackedEntity>(32, Allocator.Persistent),
                    ResolvedToRequestMap = new UnsafeHashMap<TrackedEntity, TrackedEntity>(32, Allocator.Persistent),
                    DeferredToResolvedMap = new UnsafeHashMap<TrackedEntity, TrackedEntity>(32, Allocator.Persistent),
                };

                if (entry.DataSizeInBytes > 0 || entry.ArrayDataSizeInBytes == 0)
                {
                    entry.SpawnIndexBuffer = new UnsafeList<VFXSpawnIndex>(doubleCapacity, Allocator.Persistent);
                }

                if (doubleCapacity > 0)
                {
                    entry.TransformBuffer = new UnsafeArray<VFXTransform>(doubleCapacity, Allocator.Persistent);
                }
                
                if (entry.DataSizeInBytes > 0)
                {
                    if (doubleCapacity > 0)
                    {
                        entry.DataBuffer = new UnsafeArray<byte>(doubleCapacity * definition.DataGpuSize, Allocator.Persistent);
                    }
                }
                if (entry.ArrayDataSizeInBytes > 0)
                {
                    entry.ArraySpawnIndexBuffer = new UnsafeList<VFXArraySpawnIndex>(initialCapacity, Allocator.Persistent);
                    entry.ArrayDataMemoryBuffer = new UnsafeHeapMemory(definition.ArrayDataGpuSize, doubleCapacity, Allocator.Persistent);
                    if (doubleCapacity > 0)
                    {
                        entry.ArrayPtrBuffer = new UnsafeArray<VFXArrayPtr>(doubleCapacity, Allocator.Persistent);
                    }
                }

                // FreeIndices queue also uses doubleCapacity because we still need the indices looping
                var freeIndices = new NativeArray<int>(doubleCapacity, Allocator.Temp);
                for (int i = 0; i < doubleCapacity; i++)
                {
                    freeIndices[i] = i;
                }
                entry.FreeIndices = new UnsafePriorityHeap<int>(freeIndices, Allocator.Persistent);
                
                vfxSingleton.PersistentVFXGraphEntries.Add(key, entry);
            }

            if (Application.isPlaying)
            {
                hybridVisualEffect.SetVFXActive(false);
            }
            return true;
        }
    }
}
