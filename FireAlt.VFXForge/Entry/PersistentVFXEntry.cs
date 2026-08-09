using System;
using FireAlt.VFXForge.Data;
using FireAlt.Core;
using FireAlt.Core.Collections;
using FireAlt.Core.Extensions;
using Unity.Assertions;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

namespace FireAlt.VFXForge
{
    internal struct PersistentVFXDeferredRequest
    {
        public TrackedEntity TrackedEntity;
        public VFXTransform Transform;
        public PooledUnsafeArray<byte> ArrayData;
    }

    /// <summary>
    /// Entry point for spawning, updating, and killing persistent VFX instances for one registered VFX definition.
    /// </summary>
    public struct PersistentVFXEntry : IVFXGraphEntry, IDisposable
    {
        /// <summary>
        /// Gets the registered hybrid visual effect backing this entry.
        /// </summary>
        public UnityObjectRef<HybridVisualEffect> HybridVisualEffect { get; }

        /// <summary>
        /// Gets the key of the registered VFX definition.
        /// </summary>
        public VFXKey VFXKey { get; }

        /// <summary>
        /// Gets the stable type hash required for per-instance update data.
        /// </summary>
        public ulong DataStableTypeHash { get; }

        /// <summary>
        /// Gets the stable type hash required for per-instance array data.
        /// </summary>
        public ulong ArrayDataStableTypeHash { get; }

        /// <summary>
        /// Gets the GPU byte size of one update data element.
        /// </summary>
        public int DataSizeInBytes { get; }

        /// <summary>
        /// Gets the GPU byte size of one array data element.
        /// </summary>
        public int ArrayDataSizeInBytes { get; }
        
        internal int Capacity;
        internal int UsedCapacity;
        internal bool UseMaxCapacity;
        internal int MaxCapacity;
        internal int CapacityReservations;
        internal UploadRange DataUploadRange;
        internal UploadRange ArrayDataUploadRange;
        
        internal UnsafeThreadList<byte> SpawnDataBuffer;
        
        internal UnsafeList<VFXSpawnIndex> SpawnIndexBuffer;
        internal UnsafeList<VFXArraySpawnIndex> ArraySpawnIndexBuffer;
        internal UnsafeArray<VFXTransform> TransformBuffer;
        internal UnsafeBitMaskRange AliveMask;
        internal UnsafeArray<byte> DataBuffer;
        internal UnsafeHeapMemory ArrayDataMemoryBuffer;
        internal UnsafeArray<VFXArrayPtr> ArrayPtrBuffer;
        internal UnsafePriorityHeap<int> FreeIndices;
        internal UnsafeHashSet<TrackedEntity> TrackedEntities;
        internal UnsafeHashSet<TrackedEntity> TrackedEntityIds; // TODO: Remove with Unity 6.7

        internal UnsafeList<EntityIdData> EntityIdFrameData; // TODO: Remove with Unity 6.7
        
        internal UnsafeThreadList<PersistentVFXDeferredRequest> SpawnRequests;
        internal UnsafeThreadList<TrackedEntity> KillRequests;

        internal UnsafeHashMap<TrackedEntity, TrackedEntity> DeferredToResolvedMap;
        internal UnsafeHashMap<TrackedEntity, TrackedEntity> ResolvedToRequestMap;

        /// <summary>
        /// Gets the number of spawn requests consumed by the current sync pass.
        /// </summary>
        public int RequestsCount { get; internal set; }

        /// <summary>
        /// Gets the number of array elements consumed by the current sync pass.
        /// </summary>
        public int ArrayRequestsCount { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether this entry has spawn requests waiting for the sync system.
        /// </summary>
        public bool HasPendingRequests => SpawnRequests.Length > 0;
        
        internal void ResetRequestsCount()
        {
            SpawnIndexBuffer.Clear();
            ArraySpawnIndexBuffer.Clear();
            RequestsCount = 0;
            ArrayRequestsCount = 0;
        }

        internal PersistentVFXEntry(HybridVisualEffect hybridVisualEffect)
        {
            this = default;
            var definition = hybridVisualEffect.VFXDefinition;
            HybridVisualEffect = hybridVisualEffect;
            DataStableTypeHash = definition.vfxDataType;
            DataSizeInBytes = definition.DataGpuSize;
            ArrayDataStableTypeHash = definition.vfxArrayDataType;
            ArrayDataSizeInBytes = definition.ArrayDataGpuSize;
            VFXKey = definition;
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks an entity.
        /// </summary>
        /// <param name="entityToTrack">The entity whose transform drives the persistent VFX.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the entity disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="UnityEngine.Assertions.AssertionException">Thrown in check builds when <paramref name="trackingDuration"/> is negative.</exception>
        public TrackedEntity Spawn(Entity entityToTrack, float trackingDuration = 0f)
        {
            return Spawn(TrackedEntity.FromEntity(entityToTrack), trackingDuration);
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks a GameObject entity id.
        /// </summary>
        /// <param name="gameObjectToTrack">The GameObject entity id whose transform drives the persistent VFX.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the object disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="UnityEngine.Assertions.AssertionException">Thrown in check builds when <paramref name="trackingDuration"/> is negative.</exception>
        public TrackedEntity Spawn(EntityId gameObjectToTrack, float trackingDuration = 0f)
        {
            return Spawn(TrackedEntity.FromEntityId(gameObjectToTrack), trackingDuration);
        }

        private TrackedEntity Spawn(TrackedEntity trackedEntity, float trackingDuration)
        {
            Assert.IsTrue(trackingDuration >= 0f);
            if (!Common.TryReserveCapacity(UseMaxCapacity, MaxCapacity, UsedCapacity, ref CapacityReservations))
            {
                return trackedEntity;
            }

            var threadIndex = JobsUtility.ThreadIndex;
            ref var requests = ref SpawnRequests.GetUnsafeList(threadIndex);
            var localIndex = requests.Length;

            trackedEntity.IndexInData = EncodeDeferredIndex(threadIndex, localIndex);
            trackedEntity.PackedData.SetIsDeferred(true);
            trackedEntity.PackedData.SetSystemVersion(SyncVFXSystem.SystemVersion);

            VFXTransform transformData = default;
            transformData.SetAlive(true);
            transformData.TrackingDuration = trackingDuration;
            requests.Add(new PersistentVFXDeferredRequest
            {
                TrackedEntity = trackedEntity,
                Transform = transformData,
            });

            if (DataSizeInBytes > 0)
            {
                ref var data = ref SpawnDataBuffer.GetUnsafeList(threadIndex);
                data.Resize((localIndex + 1) * DataSizeInBytes, NativeArrayOptions.ClearMemory);
            }

            return trackedEntity;
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks an entity and uploads typed array data.
        /// </summary>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="entityToTrack">The entity whose transform drives the persistent VFX.</param>
        /// <param name="arrayData">The array data to upload for this instance.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the entity disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="U"/> does not match the VFX definition.</exception>
        public TrackedEntity Spawn<U>(Entity entityToTrack, NativeArray<U> arrayData, float trackingDuration = 0f)
            where U : unmanaged
        {
            return Spawn(TrackedEntity.FromEntity(entityToTrack), arrayData, trackingDuration);
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks a GameObject entity id and uploads typed array data.
        /// </summary>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="gameObjectToTrack">The GameObject entity id whose transform drives the persistent VFX.</param>
        /// <param name="arrayData">The array data to upload for this instance.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the object disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="U"/> does not match the VFX definition.</exception>
        public TrackedEntity Spawn<U>(EntityId gameObjectToTrack, NativeArray<U> arrayData, float trackingDuration = 0f)
            where U : unmanaged
        {
            return Spawn(TrackedEntity.FromEntityId(gameObjectToTrack), arrayData, trackingDuration);
        }

        private TrackedEntity Spawn<U>(TrackedEntity entityToTrack, NativeArray<U> arrayData, float trackingDuration) 
            where U : unmanaged
        {
            Common.CheckStableTypeHash<U>(ArrayDataStableTypeHash);
            
            var trackedEntity = Spawn(entityToTrack, trackingDuration);
            if (!trackedEntity.IsValid) return trackedEntity;

            SetArray(trackedEntity, arrayData);
            return trackedEntity;
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks an entity and uploads typed update data.
        /// </summary>
        /// <typeparam name="T">The update data type expected by the VFX definition.</typeparam>
        /// <param name="entityToTrack">The entity whose transform drives the persistent VFX.</param>
        /// <param name="data">The update data to upload for this instance.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the entity disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="T"/> does not match the VFX definition.</exception>
        public TrackedEntity Spawn<T>(Entity entityToTrack, T data, float trackingDuration = 0f)
            where T : unmanaged
        {
            return Spawn(TrackedEntity.FromEntity(entityToTrack), data, trackingDuration);
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks a GameObject entity id and uploads typed update data.
        /// </summary>
        /// <typeparam name="T">The update data type expected by the VFX definition.</typeparam>
        /// <param name="gameObjectToTrack">The GameObject entity id whose transform drives the persistent VFX.</param>
        /// <param name="data">The update data to upload for this instance.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the object disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="T"/> does not match the VFX definition.</exception>
        public TrackedEntity Spawn<T>(EntityId gameObjectToTrack, T data, float trackingDuration = 0f)
            where T : unmanaged
        {
            return Spawn(TrackedEntity.FromEntityId(gameObjectToTrack), data, trackingDuration);
        }

        private TrackedEntity Spawn<T>(TrackedEntity entityToTrack, T data, float trackingDuration) 
            where T : unmanaged
        {
            Common.CheckStableTypeHash<T>(DataStableTypeHash);
            
            var trackedEntity = Spawn(entityToTrack, trackingDuration);
            if (!trackedEntity.IsValid) return trackedEntity;
            
            SetDeferredData(trackedEntity, data);
            return trackedEntity;
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks an entity and uploads typed update and array data.
        /// </summary>
        /// <typeparam name="T">The update data type expected by the VFX definition.</typeparam>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="entityToTrack">The entity whose transform drives the persistent VFX.</param>
        /// <param name="data">The update data to upload for this instance.</param>
        /// <param name="arrayData">The array data to upload for this instance.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the entity disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when either type does not match the VFX definition.</exception>
        public TrackedEntity Spawn<T, U>(Entity entityToTrack, T data, NativeArray<U> arrayData,
            float trackingDuration = 0f)
            where T : unmanaged
            where U : unmanaged
        {
            return Spawn(TrackedEntity.FromEntity(entityToTrack), data, arrayData, trackingDuration);
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks a GameObject entity id and uploads typed update and array data.
        /// </summary>
        /// <typeparam name="T">The update data type expected by the VFX definition.</typeparam>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="gameObjectToTrack">The GameObject entity id whose transform drives the persistent VFX.</param>
        /// <param name="data">The update data to upload for this instance.</param>
        /// <param name="arrayData">The array data to upload for this instance.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the object disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when either type does not match the VFX definition.</exception>
        public TrackedEntity Spawn<T, U>(EntityId gameObjectToTrack, T data, NativeArray<U> arrayData,
            float trackingDuration = 0f)
            where T : unmanaged
            where U : unmanaged
        {
            return Spawn(TrackedEntity.FromEntityId(gameObjectToTrack), data, arrayData, trackingDuration);
        }

        private TrackedEntity Spawn<T, U>(TrackedEntity entityToTrack, T data, NativeArray<U> arrayData, float trackingDuration) 
            where T : unmanaged
            where U : unmanaged
        {
            Common.CheckStableTypeHash<T>(DataStableTypeHash);
            Common.CheckStableTypeHash<U>(ArrayDataStableTypeHash);
            
            var trackedEntity = Spawn(entityToTrack, trackingDuration);
            if (!trackedEntity.IsValid) return trackedEntity;
            
            SetArray(trackedEntity, arrayData);
            SetDeferredData(trackedEntity, data);
            return trackedEntity;
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks an entity and uploads raw update and optional array bytes.
        /// </summary>
        /// <param name="entityToTrack">The entity whose transform drives the persistent VFX.</param>
        /// <param name="data">Pointer to one update data element matching this entry's configured data size.</param>
        /// <param name="arrayData">Optional raw bytes for array data matching this entry's configured array element size.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the entity disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        public unsafe TrackedEntity SpawnUnsafe(Entity entityToTrack, byte* data, NativeArray<byte> arrayData = default,
            float trackingDuration = 0f)
        {
            return SpawnUnsafe(TrackedEntity.FromEntity(entityToTrack), data, arrayData, trackingDuration);
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks a GameObject entity id and uploads raw update and optional array bytes.
        /// </summary>
        /// <param name="gameObjectToTrack">The GameObject entity id whose transform drives the persistent VFX.</param>
        /// <param name="data">Pointer to one update data element matching this entry's configured data size.</param>
        /// <param name="arrayData">Optional raw bytes for array data matching this entry's configured array element size.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the object disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        public unsafe TrackedEntity SpawnUnsafe(EntityId gameObjectToTrack, byte* data, NativeArray<byte> arrayData = default,
            float trackingDuration = 0f)
        {
            return SpawnUnsafe(TrackedEntity.FromEntityId(gameObjectToTrack), data, arrayData, trackingDuration);
        }

        private unsafe TrackedEntity SpawnUnsafe(TrackedEntity entityToTrack, byte* data, NativeArray<byte> arrayData = default, float trackingDuration = 0f)
        {
            var trackedEntity = Spawn(entityToTrack, trackingDuration);
            if (!trackedEntity.IsValid) return trackedEntity;

            if (arrayData.IsCreated)
            {
                SetArray(trackedEntity, arrayData);
            }
            SetDeferredDataUnsafe(trackedEntity, data);
            return trackedEntity;
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks an entity and uploads raw array bytes.
        /// </summary>
        /// <param name="entityToTrack">The entity whose transform drives the persistent VFX.</param>
        /// <param name="arrayData">Raw bytes for array data matching this entry's configured array element size.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the entity disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        public TrackedEntity SpawnUnsafe(Entity entityToTrack, NativeArray<byte> arrayData, float trackingDuration = 0f)
        {
            return SpawnUnsafe(TrackedEntity.FromEntity(entityToTrack), arrayData, trackingDuration);
        }

        /// <summary>
        /// Enqueues a persistent VFX spawn that tracks a GameObject entity id and uploads raw array bytes.
        /// </summary>
        /// <param name="gameObjectToTrack">The GameObject entity id whose transform drives the persistent VFX.</param>
        /// <param name="arrayData">Raw bytes for array data matching this entry's configured array element size.</param>
        /// <param name="trackingDuration">Optional duration in seconds to keep tracking; zero tracks until killed or the object disappears.</param>
        /// <returns>A tracked handle for later update or kill operations, or an invalid handle when capacity is exhausted.</returns>
        public TrackedEntity SpawnUnsafe(EntityId gameObjectToTrack, NativeArray<byte> arrayData, float trackingDuration = 0f)
        {
            return SpawnUnsafe(TrackedEntity.FromEntityId(gameObjectToTrack), arrayData, trackingDuration);
        }

        private TrackedEntity SpawnUnsafe(TrackedEntity entityToTrack, NativeArray<byte> arrayData, float trackingDuration = 0f)
        {
            var trackedEntity = Spawn(entityToTrack, trackingDuration);
            if (!trackedEntity.IsValid) return trackedEntity;

            if (arrayData.IsCreated)
            {
                SetArray(trackedEntity, arrayData);
            }
            return trackedEntity;
        }

        /// <summary>
        /// Returns whether the tracked persistent VFX instance is currently alive.
        /// </summary>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <returns><see langword="true"/> when the handle resolves to a live instance; otherwise, <see langword="false"/>.</returns>
        public bool IsAlive(TrackedEntity trackedEntity)
        {
            return TryResolveTransform(trackedEntity, out var transform)
                   && transform.IsAlive();
        }

        /// <summary>
        /// Tries to get a mutable reference to the typed update data for a tracked instance.
        /// </summary>
        /// <typeparam name="T">The update data type expected by the VFX definition.</typeparam>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <param name="dataRef">A mutable reference to the update data when the handle resolves.</param>
        /// <returns><see langword="true"/> when update data was found; otherwise, <see langword="false"/>.</returns>
        public bool TryGetUpdateDataAsRef<T>(TrackedEntity trackedEntity, out Ref<T> dataRef)
            where T : unmanaged
        {
            if (TryResolveDataIndex(trackedEntity, out var index, out var isDeferred))
            {
                dataRef = isDeferred
                    ? new Ref<T>(ref GetDeferredDataAsRef<T>(trackedEntity))
                    : new Ref<T>(ref DataBuffer.GetDataAsRef<T>(index));
                return true;
            }

            dataRef = new Ref<T>();
            return false;
        }

        /// <summary>
        /// Tries to get the typed array data for a tracked instance.
        /// </summary>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <param name="array">The array data when the handle resolves.</param>
        /// <returns><see langword="true"/> when array data was found; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="U"/> does not match the VFX definition.</exception>
        public bool TryGetArrayData<U>(TrackedEntity trackedEntity, out UnsafeArray<U> array)
            where U : unmanaged
        {
            Common.CheckStableTypeHash<U>(ArrayDataStableTypeHash);
            
            if (TryResolveDataIndex(trackedEntity, out var index, out var isDeferred))
            {
                array = isDeferred 
                    ? GetDeferredRequest(trackedEntity).ArrayData.Array.Reinterpret<U>(UnsafeUtility.SizeOf<byte>()) 
                    : ArrayDataMemoryBuffer.ArrayAt<U>(ArrayPtrBuffer[index]);
                return true;
            }
            array = default;
            return false;
        }

        /// <summary>
        /// Tries to get the raw array bytes for a tracked instance.
        /// </summary>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <param name="array">The raw array bytes when the handle resolves.</param>
        /// <returns><see langword="true"/> when array data was found; otherwise, <see langword="false"/>.</returns>
        public bool TryGetArrayDataUnsafe(TrackedEntity trackedEntity, out UnsafeArray<byte> array)
        {
            if (TryResolveDataIndex(trackedEntity, out var index, out var isDeferred))
            {
                if (isDeferred)
                {
                    array = GetDeferredRequest(trackedEntity).ArrayData.Array;
                    return true;
                }

                MemoryPtr memPtr = ArrayPtrBuffer[index];
                if (memPtr.IsValid && ArrayDataMemoryBuffer.Contains(memPtr))
                {
                    array = ArrayDataMemoryBuffer.ArrayAtUnsafe(memPtr);
                    return true;
                }
            }
            array = default;
            return false;
        }

        /// <summary>
        /// Tries to replace the typed update data for a tracked instance.
        /// </summary>
        /// <typeparam name="T">The update data type expected by the VFX definition.</typeparam>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <param name="updateData">The new update data for the tracked instance.</param>
        /// <returns><see langword="true"/> when the handle resolved and data was updated; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="T"/> does not match the VFX definition.</exception>
        public bool TrySetUpdateData<T>(TrackedEntity trackedEntity, T updateData)
            where T : unmanaged
        {
            Common.CheckStableTypeHash<T>(DataStableTypeHash);

            if (TryResolveDataIndex(trackedEntity, out var index, out var isDeferred))
            {
                if (isDeferred)
                {
                    SetDeferredData(trackedEntity, updateData);
                }
                else
                {
                    DataBuffer.SetData(index, updateData);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to replace the update data for a tracked instance from raw bytes.
        /// </summary>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <param name="updateData">Pointer to one update data element matching this entry's configured data size.</param>
        /// <returns><see langword="true"/> when the handle resolved and data was updated; otherwise, <see langword="false"/>.</returns>
        public unsafe bool TrySetUpdateDataUnsafe(TrackedEntity trackedEntity, byte* updateData)
        {
            if (TryResolveDataIndex(trackedEntity, out var index, out var isDeferred))
            {
                if (isDeferred)
                {
                    SetDeferredDataUnsafe(trackedEntity, updateData);
                }
                else
                {
                    DataBuffer.SetDataUnsafe(index, updateData, DataSizeInBytes);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to kill a tracked persistent VFX instance.
        /// </summary>
        /// <param name="trackedEntity">The handle returned by a spawn call.</param>
        /// <returns><see langword="true"/> when the handle resolved and a kill was queued or applied; otherwise, <see langword="false"/>.</returns>
        public bool TryKill(TrackedEntity trackedEntity)
        {
            if (TryResolveCheckIndex(trackedEntity, out var resolved, out var isDeferred))
            {
                if (isDeferred)
                {
                    GetDeferredRequest(resolved).Transform.Kill();
                }
                else
                {
                    ref var killRequests = ref KillRequests.GetUnsafeList(JobsUtility.ThreadIndex);
                    killRequests.Add(resolved);
                }

                return true;
            }

            return false;
        }
        
        private unsafe void SetArray<T>(TrackedEntity trackedEntity, NativeArray<T> arrayData) where T : unmanaged
        {
            var size = arrayData.Length * UnsafeUtility.SizeOf<T>();
            
            var byteArray = UnsafeArrayPool<byte>.Rent(size);
            UnsafeUtility.MemCpy(byteArray.Array.GetUnsafePtr(), arrayData.GetUnsafeReadOnlyPtr(), size);
            
            GetDeferredRequest(trackedEntity).ArrayData = byteArray;
        }
        
        private bool TryResolveCheckIndex(TrackedEntity trackedEntity, out TrackedEntity resolvedKey, out bool isDeferred)
        {
            if (!trackedEntity.IsValid)
            {
                resolvedKey = TrackedEntity.Null;
                isDeferred = false;
                return false;
            }

            if (trackedEntity.IsDeferred(SyncVFXSystem.SystemVersion))
            {
                DecodeDeferredIndex(trackedEntity.IndexInData, out var threadIndex, out var localIndex);
                Assert.IsTrue(localIndex >= 0 && localIndex < SpawnRequests.GetUnsafeList(threadIndex).Length);

                resolvedKey = trackedEntity;
                isDeferred = true;
                return true;
            }

            isDeferred = false;
            if (TryResolve(trackedEntity, out resolvedKey))
            {
                var index = resolvedKey.IndexInData;
                Assert.IsTrue(index >= 0 && index < Capacity * 2);
                return true;
            }

            return false;
        }

        private bool TryResolveDataIndex(TrackedEntity trackedEntity, out int index, out bool isDeferred)
        {
            if (TryResolveCheckIndex(trackedEntity, out var resolvedKey, out isDeferred))
            {
                index = resolvedKey.IndexInData;
                return true;
            }

            index = -1;
            return false;
        }

        private bool TryResolveTransform(TrackedEntity trackedEntity, out VFXTransform transform)
        {
            if (TryResolveCheckIndex(trackedEntity, out var resolvedKey, out var isDeferred))
            {
                transform = isDeferred
                    ? GetDeferredRequest(resolvedKey).Transform
                    : TransformBuffer[resolvedKey.IndexInData];
                return true;
            }

            transform = default;
            return false;
        }

        private bool TryResolve(TrackedEntity trackedEntity, out TrackedEntity resolvedKey)
        {
            if (Hint.Unlikely(trackedEntity.PackedData.SystemVersion == 0))
            {
                resolvedKey = TrackedEntity.Null;
                return false;
            }

            if (trackedEntity.PackedData.IsDeferred)
            {
                return DeferredToResolvedMap.TryGetValue(trackedEntity, out resolvedKey);
            }
            resolvedKey = trackedEntity;
            return true;
        }
        
        internal void EnsureCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= Capacity)
            {
                return;
            }

            var doubledCapacity = Capacity > int.MaxValue / 2 ? int.MaxValue : Capacity * 2;
            var newCapacity = math.max(requiredCapacity, math.max(1, doubledCapacity));
            if (UseMaxCapacity)
            {
                newCapacity = math.min(newCapacity, MaxCapacity);
            }

            if (newCapacity > int.MaxValue / 2)
            {
                throw new InvalidOperationException("Persistent VFX capacity is too large to allocate double-capacity storage.");
            }

            var oldStorageCapacity = Capacity * 2;
            var newStorageCapacity = newCapacity * 2;

            ResizeArray(ref TransformBuffer, newStorageCapacity);
            if (DataSizeInBytes > 0)
            {
                ResizeArray(ref DataBuffer, newStorageCapacity * DataSizeInBytes);
            }

            if (ArrayDataSizeInBytes > 0)
            {
                ResizeArray(ref ArrayPtrBuffer, newStorageCapacity);
            }

            var newAliveMask = new UnsafeBitMaskRange(newStorageCapacity, Allocator.Persistent);
            foreach (var trackedEntity in TrackedEntities)
            {
                newAliveMask.Set(trackedEntity.IndexInData);
            }

            foreach (var trackedEntity in TrackedEntityIds)
            {
                newAliveMask.Set(trackedEntity.IndexInData);
            }

            AliveMask.Dispose();
            AliveMask = newAliveMask;

            FreeIndices.EnsureCapacity(FreeIndices.Count + newStorageCapacity - oldStorageCapacity);
            for (var i = oldStorageCapacity; i < newStorageCapacity; i++)
            {
                FreeIndices.Enqueue(i);
            }

            Capacity = newCapacity;
        }

        internal ref PersistentVFXDeferredRequest GetDeferredRequest(TrackedEntity trackedEntity)
        {
            DecodeDeferredIndex(trackedEntity.IndexInData, out var threadIndex, out var localIndex);
            return ref SpawnRequests.GetUnsafeList(threadIndex).ElementAt(localIndex);
        }

        internal static int EncodeDeferredIndex(int threadIndex, int localIndex)
        {
            return localIndex * JobsUtility.ThreadIndexCount + threadIndex;
        }

        internal static void DecodeDeferredIndex(int index, out int threadIndex, out int localIndex)
        {
            threadIndex = index % JobsUtility.ThreadIndexCount;
            localIndex = index / JobsUtility.ThreadIndexCount;
        }

        private static unsafe void ResizeArray<T>(ref UnsafeArray<T> array, int newLength)
            where T : unmanaged
        {
            var resized = new UnsafeArray<T>(newLength, Allocator.Persistent);
            if (array.IsCreated)
            {
                UnsafeUtility.MemCpy(resized.GetUnsafePtr(), array.GetUnsafePtr(), array.Length * UnsafeUtility.SizeOf<T>());
                array.Dispose();
            }

            array = resized;
        }

        private unsafe ref T GetDeferredDataAsRef<T>(TrackedEntity trackedEntity)
            where T : unmanaged
        {
            DecodeDeferredIndex(trackedEntity.IndexInData, out var threadIndex, out var localIndex);
            ref var data = ref SpawnDataBuffer.GetUnsafeList(threadIndex);
            return ref UnsafeUtility.AsRef<T>((byte*)data.Ptr + localIndex * DataSizeInBytes);
        }

        private void SetDeferredData<T>(TrackedEntity trackedEntity, T data)
            where T : unmanaged
        {
            GetDeferredDataAsRef<T>(trackedEntity) = data;
        }

        private unsafe void SetDeferredDataUnsafe(TrackedEntity trackedEntity, byte* data)
        {
            DecodeDeferredIndex(trackedEntity.IndexInData, out var threadIndex, out var localIndex);
            ref var deferredData = ref SpawnDataBuffer.GetUnsafeList(threadIndex);
            UnsafeUtility.MemCpy((byte*)deferredData.Ptr + localIndex * DataSizeInBytes, data, DataSizeInBytes);
        }

        public void Dispose()
        {
            if (SpawnIndexBuffer.IsCreated) SpawnIndexBuffer.Dispose();
            if (TransformBuffer.IsCreated) TransformBuffer.Dispose();
            AliveMask.Dispose();
            FreeIndices.Dispose();
            TrackedEntities.Dispose();
            TrackedEntityIds.Dispose();
            EntityIdFrameData.Dispose();
            
            if (DataBuffer.IsCreated) DataBuffer.Dispose();
            if (ArrayDataMemoryBuffer.IsCreated) ArrayDataMemoryBuffer.Dispose();
            if (ArrayPtrBuffer.IsCreated) ArrayPtrBuffer.Dispose();
            if (ArraySpawnIndexBuffer.IsCreated) ArraySpawnIndexBuffer.Dispose();

            // Deferred
            for (var threadIndex = 0; threadIndex < JobsUtility.ThreadIndexCount; threadIndex++)
            {
                ref var requests = ref SpawnRequests.GetUnsafeList(threadIndex);
                for (var i = 0; i < requests.Length; i++)
                {
                    requests.ElementAt(i).ArrayData.Dispose();
                }
            }

            SpawnRequests.Dispose();
            SpawnDataBuffer.Dispose();
            KillRequests.Dispose();
            DeferredToResolvedMap.Dispose();
            ResolvedToRequestMap.Dispose();
        }
    }
}
