using System;
using FireAlt.Core.Collections;
using FireAlt.Core.Extensions;
using FireAlt.VFXForge.Data;
using Unity.Assertions;
using Unity.Collections;

namespace FireAlt.VFXForge
{
    public partial struct VFXSingleton
    {
        internal NativeHashMap<VFXKey, bool> IsPersistent;
        
        internal NativeHashMap<VFXKey, InstantVFXEntry> InstantVFXGraphEntries;
        internal NativeHashMap<VFXKey, PersistentVFXEntry> PersistentVFXGraphEntries;
        
        internal NativeHashMap<VFXKey, AliveVFX> InstantAliveVFX;
        internal NativeHashMap<VFXKey, AliveVFX> PersistentAliveVFX;
        
        internal VFXSingleton(int capacity)
        {
            IsPersistent = new NativeHashMap<VFXKey, bool>(capacity, Allocator.Persistent);
            
            InstantVFXGraphEntries = new NativeHashMap<VFXKey, InstantVFXEntry>(capacity, Allocator.Persistent);
            PersistentVFXGraphEntries = new NativeHashMap<VFXKey, PersistentVFXEntry>(capacity, Allocator.Persistent);
            
            InstantAliveVFX = new NativeHashMap<VFXKey, AliveVFX>(4, Allocator.Persistent);
            PersistentAliveVFX = new NativeHashMap<VFXKey, AliveVFX>(4, Allocator.Persistent);
        }

        internal InternalAPI AsInternal()
        {
            return new InternalAPI();
        }
        
        internal readonly struct InternalAPI
        {
            internal TrackedEntity SpawnPersistent(ref PersistentVFXEntry entry, TrackedEntity deferredKey,
                UnsafeArray<byte> arrayData, float trackingDuration)
            {
                Assert.IsTrue(trackingDuration >= 0f);
                var trackedEntity = TrackedEntity.FromTrackedEntity(deferredKey);
            
                if (!entry.FreeIndices.TryDequeue(out var index))
                {
                    throw new Exception("Impossible. Submit a bug report.");
                }
            
                trackedEntity.IndexInData = index;
                trackedEntity.PackedData.SetSystemVersion(SyncVFXSystem.SystemVersion);
                trackedEntity.PackedData.SetIsDeferred(false);
                
                var transform = default(VFXTransform);
                transform.SetAlive(true);
                transform.TrackingDuration = trackingDuration;
                entry.TransformBuffer[index] = transform;
                if (trackedEntity.IsEntityId)
                {
                    entry.TrackedEntityIds.Add(trackedEntity);
                }
                else
                {
                    entry.TrackedEntities.Add(trackedEntity);
                }
                
                entry.AliveMask.Set(index);

                if (entry.SpawnIndexBuffer.IsCreated)
                {
                    entry.SpawnIndexBuffer.Add(new VFXSpawnIndex((uint)index));
                }
                if (arrayData.IsCreated && arrayData.Length > 0)
                {
                    var arrayLength = arrayData.Length / entry.ArrayDataSizeInBytes;
                    entry.ArrayPtrBuffer[trackedEntity.IndexInData] = entry.ArrayDataMemoryBuffer.Allocate(arrayData);
                    
                    for (int i = 0; i < arrayLength; i++)
                    {
                        entry.ArraySpawnIndexBuffer.Add(new VFXArraySpawnIndex((uint)index, (uint)i));
                    }
                    entry.ArrayRequestsCount += arrayLength;
                }
                
                entry.RequestsCount++;
                return trackedEntity;
            }
            
            internal unsafe TrackedEntity SpawnPersistentUnsafe(ref PersistentVFXEntry entry, TrackedEntity deferredKey,
                byte* data, UnsafeArray<byte> arrayData, float trackingDuration)
            {
                Assert.IsTrue(data != null);

                var trackedEntity = SpawnPersistent(ref entry, deferredKey, arrayData, trackingDuration);
                if (!trackedEntity.IsValid) return trackedEntity;

                if (entry.DataSizeInBytes != 0)
                {
                    entry.DataBuffer.SetDataUnsafe(trackedEntity.IndexInData, data, entry.DataSizeInBytes);
                }
                return trackedEntity;
            }
            
            internal void KillPersistent(ref PersistentVFXEntry entry, TrackedEntity resolvedKey)
            {
                Assert.IsTrue(!resolvedKey.IsDeferred(SyncVFXSystem.SystemVersion));
                var index = resolvedKey.IndexInData;
                Assert.IsTrue(index >= 0 && index < entry.Capacity * 2);
                
                if (resolvedKey.IsEntityId 
                        ? !entry.TrackedEntityIds.Remove(resolvedKey) 
                        : !entry.TrackedEntities.Remove(resolvedKey))
                {
                    return;
                }
                
                ref var transform = ref entry.TransformBuffer.ElementAt(index);
                transform.Kill();
                entry.AliveMask.Unset(index);

                if (entry.ArrayDataMemoryBuffer.IsCreated)
                {
                    MemoryPtr ptr = entry.ArrayPtrBuffer[index];
                    if (ptr.IsValid && entry.ArrayDataMemoryBuffer.Contains(ptr))
                    {
                        entry.ArrayDataMemoryBuffer.Free(entry.ArrayPtrBuffer[index]);
                    }
                }
                
                if (entry.ResolvedToRequestMap.TryGetValue(resolvedKey, out var request))
                {
                    entry.ResolvedToRequestMap.Remove(resolvedKey);
                    entry.DeferredToResolvedMap.Remove(request);
                }
                entry.FreeIndices.Enqueue(index);
            }
        }
    }
}
