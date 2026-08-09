using System;
using FireAlt.VFXForge.Data;
using FireAlt.Core.Collections;
using FireAlt.Core.Extensions;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;

namespace FireAlt.VFXForge
{
    /// <summary>
    /// Entry point for enqueueing instant VFX spawn requests for one registered VFX definition.
    /// </summary>
    public struct InstantVFXEntry : IVFXGraphEntry, IDisposable
    {
        internal struct Requests
        {
            public int RequestsCount;
            public int ArrayRequestsCount;
        }

        /// <summary>
        /// Gets the registered hybrid visual effect backing this entry.
        /// </summary>
        public UnityObjectRef<HybridVisualEffect> HybridVisualEffect { get; }

        /// <summary>
        /// Gets the key of the registered VFX definition.
        /// </summary>
        public VFXKey VFXKey { get; }

        /// <summary>
        /// Gets the stable type hash required for per-spawn data.
        /// </summary>
        public ulong DataStableTypeHash { get; }

        /// <summary>
        /// Gets the stable type hash required for per-spawn array data.
        /// </summary>
        public ulong ArrayDataStableTypeHash { get; }

        /// <summary>
        /// Gets the GPU byte size of one per-spawn data element.
        /// </summary>
        public int DataSizeInBytes { get; }

        /// <summary>
        /// Gets the GPU byte size of one per-spawn array element.
        /// </summary>
        public int ArrayDataSizeInBytes { get; }

        internal UnsafeThreadData<Requests> PendingRequestsCount;
        internal UnsafeThreadToListMapper<byte> DataBuffer;
        
        internal UnsafeThreadToListMapper<byte> ArrayDataBuffer;
        internal UnsafeThreadToListMapper<VFXArraySpawnIndex> ArraySpawnIndexBuffer;
        internal UnsafeThreadToListMapper<VFXArrayPtr> ArrayPtrBuffer;

        internal bool UseMaxCapacity;
        internal int MaxCapacity;
        internal int CapacityReservations;
        
        /// <summary>
        /// Gets the number of pending instant spawn requests.
        /// </summary>
        public int RequestsCount
        {
            get
            {
                var sum = 0;
                foreach (var threadCount in PendingRequestsCount)
                {
                    sum += threadCount.RequestsCount;
                }
                return sum;
            }
        }

        /// <summary>
        /// Gets the number of pending array elements submitted by instant spawn requests.
        /// </summary>
        public int ArrayRequestsCount
        {
            get
            {
                var sum = 0;
                foreach (var threadCount in PendingRequestsCount)
                {
                    sum += threadCount.ArrayRequestsCount;
                }
                return sum;
            }
        }
        
        /// <summary>
        /// Gets a value indicating whether this entry has requests waiting for the sync system.
        /// </summary>
        public bool HasPendingRequests => RequestsCount > 0 || ArrayRequestsCount > 0;
        
        internal void ResetRequestsCount()
        {
            PendingRequestsCount.Clear();
            CapacityReservations = 0;
        }

        internal InstantVFXEntry(HybridVisualEffect hybridVisualEffect)
        {
            this = default;
            var definition = hybridVisualEffect.VFXDefinition;
            HybridVisualEffect = hybridVisualEffect;
            DataStableTypeHash = definition.vfxDataType;
            ArrayDataStableTypeHash = definition.vfxArrayDataType;
            DataSizeInBytes = definition.DataGpuSize;
            ArrayDataSizeInBytes = definition.ArrayDataGpuSize;
            UseMaxCapacity = definition.useMaxCapacity;
            MaxCapacity = definition.MaxCapacity;
            VFXKey = definition;
        }
        
        /// <summary>
        /// Enqueues one instant spawn request with no per-spawn data.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown in check builds when this VFX expects non-zero data.</exception>
        /// <returns><see langword="true"/> when the request was accepted; otherwise, <see langword="false"/> when Max Capacity was reached.</returns>
        public bool Spawn()
        {
            Common.CheckZeroSized(DataStableTypeHash);
            return SpawnBase();
        }

        /// <summary>
        /// Enqueues one instant spawn request with typed per-spawn data.
        /// </summary>
        /// <typeparam name="T">The type of the data element expected by the VFX definition.</typeparam>
        /// <param name="spawnData">The data to upload for this spawn request.</param>
        /// <exception cref="InvalidOperationException">Thrown in check builds when <typeparamref name="T"/> does not match the VFX definition.</exception>
        /// <returns><see langword="true"/> when the request was accepted; otherwise, <see langword="false"/> when Max Capacity was reached.</returns>
        public bool Spawn<T>(T spawnData)
            where T : unmanaged
        {
            Common.CheckStableTypeHash<T>(DataStableTypeHash);
            if (!SpawnBase())
            {
                return false;
            }

            SetData(spawnData);
            return true;
        }

        /// <summary>
        /// Enqueues one instant spawn request with typed array data.
        /// </summary>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="arrayData">The array data to upload for this spawn request.</param>
        /// <exception cref="InvalidOperationException">Thrown in check builds when the data or array type does not match the VFX definition.</exception>
        /// <returns><see langword="true"/> when the request was accepted; otherwise, <see langword="false"/> when Max Capacity was reached.</returns>
        public bool Spawn<U>(NativeArray<U> arrayData) 
            where U : unmanaged
        {
            Common.CheckZeroSized(DataStableTypeHash);
            Common.CheckStableTypeHash<U>(ArrayDataStableTypeHash);
            if (!SpawnBase())
            {
                return false;
            }

            SpawnArray(arrayData.AsBytes());
            return true;
        }

        /// <summary>
        /// Enqueues one instant spawn request with typed per-spawn data and typed array data.
        /// </summary>
        /// <typeparam name="T">The type of the data element expected by the VFX definition.</typeparam>
        /// <typeparam name="U">The type of each array element expected by the VFX definition.</typeparam>
        /// <param name="spawnData">The data to upload for this spawn request.</param>
        /// <param name="arrayData">The array data to upload for this spawn request.</param>
        /// <exception cref="InvalidOperationException">Thrown in check builds when either type does not match the VFX definition.</exception>
        /// <returns><see langword="true"/> when the request was accepted; otherwise, <see langword="false"/> when Max Capacity was reached.</returns>
        public bool Spawn<T, U>(T spawnData, NativeArray<U> arrayData)
            where T : unmanaged
            where U : unmanaged
        {
            Common.CheckStableTypeHash<T>(DataStableTypeHash);
            Common.CheckStableTypeHash<U>(ArrayDataStableTypeHash);
            if (!SpawnBase())
            {
                return false;
            }

            SetData(spawnData);
            SpawnArray(arrayData.AsBytes());
            return true;
        }

        /// <summary>
        /// Enqueues one instant spawn request from raw per-spawn bytes and optional raw array bytes.
        /// </summary>
        /// <param name="spawnData">Pointer to one data element matching this entry's configured data size.</param>
        /// <param name="arrayData">Optional raw bytes for array data matching this entry's configured array element size.</param>
        /// <exception cref="UnityEngine.Assertions.AssertionException">Thrown in check builds when <paramref name="spawnData"/> is null.</exception>
        /// <returns><see langword="true"/> when the request was accepted; otherwise, <see langword="false"/> when Max Capacity was reached.</returns>
        public unsafe bool SpawnUnsafe(byte* spawnData, NativeArray<byte> arrayData = default)
        {
            Assert.IsTrue(spawnData != null);
            if (!SpawnBase())
            {
                return false;
            }

            if (arrayData.IsCreated)
            {
                SpawnArray(arrayData);
            }
            GetThreadList(DataBuffer).AddDataUnsafe(spawnData, DataSizeInBytes);
            return true;
        }

        /// <summary>
        /// Enqueues one instant spawn request with raw array bytes and no per-spawn data.
        /// </summary>
        /// <param name="arrayData">Raw bytes for array data matching this entry's configured array element size.</param>
        /// <exception cref="InvalidOperationException">Thrown in check builds when this VFX expects non-zero per-spawn data.</exception>
        /// <returns><see langword="true"/> when the request was accepted; otherwise, <see langword="false"/> when Max Capacity was reached.</returns>
        public bool SpawnUnsafe(NativeArray<byte> arrayData)
        {
            Common.CheckZeroSized(DataStableTypeHash);
            if (!SpawnBase())
            {
                return false;
            }

            if (arrayData.IsCreated)
            {
                SpawnArray(arrayData);
            }

            return true;
        }
        
        private bool SpawnBase()
        {
            if (!Common.TryReserveCapacity(UseMaxCapacity, MaxCapacity, 0, ref CapacityReservations))
            {
                return false;
            }

            PendingRequestsCount.GetUnsafeThreadData(JobsUtility.ThreadIndex).RequestsCount++;
            return true;
        }
        
        private void SetData<T>(T spawnData)
            where T : unmanaged
        {
            GetThreadList(DataBuffer).AddData(spawnData);
        }
        
        private unsafe void SpawnArray(NativeArray<byte> arrayData)
        {
            ref var arrayDataBuffer = ref GetThreadList(ArrayDataBuffer);
            ref var arrayPtrBuffer = ref GetThreadList(ArrayPtrBuffer);
            ref var arraySpawnIndexBuffer = ref GetThreadList(ArraySpawnIndexBuffer);

            var indexInData = (uint)arrayPtrBuffer.Length;
            var arrayStartIndex = arrayDataBuffer.Length / ArrayDataSizeInBytes;
            var arrayLength = arrayData.Length / ArrayDataSizeInBytes;
            
            arrayDataBuffer.AddRange(arrayData.GetUnsafePtr(), arrayData.Length);
            arrayPtrBuffer.Add(new VFXArrayPtr(arrayStartIndex, arrayLength));
            for (uint indexInArray = 0; indexInArray < arrayLength; indexInArray++)
            {
                arraySpawnIndexBuffer.Add(new VFXArraySpawnIndex(indexInData, indexInArray));
            }
            
            PendingRequestsCount.GetUnsafeThreadData(JobsUtility.ThreadIndex).ArrayRequestsCount += arrayLength;
        }

        private ref UnsafeList<T> GetThreadList<T>(UnsafeThreadToListMapper<T> mapper) 
            where T : unmanaged
        {
            return ref mapper.ThreadList.GetUnsafeList(JobsUtility.ThreadIndex);
        }
        
        public void Dispose()
        {
            PendingRequestsCount.Dispose();
            if (DataBuffer.IsCreated) DataBuffer.Dispose();
            if (ArrayDataBuffer.IsCreated) ArrayDataBuffer.Dispose();
            if (ArrayPtrBuffer.IsCreated) ArrayPtrBuffer.Dispose();
            if (ArraySpawnIndexBuffer.IsCreated) ArraySpawnIndexBuffer.Dispose();
        }
    }
}
