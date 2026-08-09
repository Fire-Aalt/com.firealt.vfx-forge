using FireAlt.VFXForge.Data;
using FireAlt.Core.Extensions;
using FireAlt.Core.Utility;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace FireAlt.VFXForge
{
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(UpdateVFXSystemGroup))]
    public partial struct VFXTransformSystem : ISystem
    {
        private static class Burst
        {
            public static readonly SharedStatic<BurstInterop> IsEnabled =
                SharedStatic<BurstInterop>.GetOrCreate<VFXTransformSystem>();
        }
        
        private static unsafe void IsEnabledPacked(void* arguments, int argumentsSize)
        {
            ref var args = ref BurstInterop.ArgumentsFromPtr<BurstManagedPair<EntityId, EntityIdState>>(arguments, argumentsSize);
            ref var entityId = ref args.First;
            ref var state = ref args.Second;
            
            var go = Resources.EntityIdToObject(entityId) as GameObject;
            var exists = go != null;
            state = new EntityIdState { TransformHandle = exists ? go.transformHandle : default, IsEnabled = exists && go.activeInHierarchy };
        }
        
        static unsafe VFXTransformSystem()
        {
            Burst.IsEnabled.Data = new BurstInterop(&IsEnabledPacked);
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var singleton = SystemAPI.GetSingletonRW<VFXSingleton>().ValueRW;
            var persistentKeys = singleton.PersistentVFXGraphEntries.GetKeyArray(state.WorldUpdateAllocator); // First runs before Alive status is determined

            var entityIdPresent = false;
            foreach (var key in persistentKeys)
            {
                ref var entry = ref singleton.GetPersistent(key);

                entry.EntityIdFrameData.Clear();
                if (entry.HasPendingRequests || !entry.TrackedEntityIds.IsEmpty)
                {
                    entityIdPresent = true;
                }

                for (var threadIndex = 0; threadIndex < JobsUtility.ThreadIndexCount; threadIndex++)
                {
                    ref var requests = ref entry.SpawnRequests.GetUnsafeList(threadIndex);
                    for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                    {
                        ref var request = ref requests.ElementAt(requestIndex);
                        if (!request.TrackedEntity.IsEntityId)
                        {
                            continue;
                        }

                        Burst.IsEnabled.Data.InvokeOut(request.TrackedEntity.EntityId, out EntityIdState entityIdState);
                        if (Hint.Unlikely(entityIdState.TransformHandle == default))
                        {
                            // Very unlikely that anyone will be destroying GameObjects
                            request.Transform.SetAlive(false);
                            request.Transform.SetEntityAlive(false);
                            request.Transform.SetDidTransformSystemRun();

                            entry.EntityIdFrameData.Add(default);
                        }
                        else
                        {
                            entry.EntityIdFrameData.Add(new EntityIdData
                            {
                                LocalToWorld = new LocalToWorld { Value = entityIdState.TransformHandle.localToWorldMatrix },
                                IsEnabled = entityIdState.IsEnabled
                            });
                        }
                    }
                }
                
                foreach (var resolvedKey in entry.TrackedEntityIds)
                {
                    Burst.IsEnabled.Data.InvokeOut(resolvedKey.EntityId, out EntityIdState entityIdState);
 
                    if (Hint.Unlikely(entityIdState.TransformHandle == default))
                    {
                        // Very unlikely that anyone will be destroying GameObjects
                        ref var transformData = ref entry.TransformBuffer.ElementAt(resolvedKey.IndexInData);
                        transformData.SetAlive(false);
                        transformData.SetEntityAlive(false);
                        
                        entry.EntityIdFrameData.Add(default);
                    }
                    else
                    {
                        entry.EntityIdFrameData.Add(new EntityIdData
                        {
                            LocalToWorld = new LocalToWorld { Value = entityIdState.TransformHandle.localToWorldMatrix },
                            IsEnabled = entityIdState.IsEnabled
                        });
                    }
                }
            }

            if (entityIdPresent)
            {
                state.Dependency = new FetchGameObjectTransformJob
                {
                    KeysArray = persistentKeys,
                    VFXSingleton = singleton.AsParallelWriter(),
                    ElapsedTime = SystemAPI.Time.ElapsedTime
                }.ScheduleParallel(persistentKeys.Length, 1, state.Dependency);
            }
            
            state.Dependency = new FetchEntityTransformJob
            {
                LocalToWorldLookup = SystemAPI.GetComponentLookup<LocalToWorld>(true),
                DisabledLookup = SystemAPI.GetComponentLookup<Disabled>(true),
                KeysArray = persistentKeys,
                VFXSingleton = singleton.AsParallelWriter(),
                ElapsedTime = SystemAPI.Time.ElapsedTime
            }.ScheduleParallel(persistentKeys.Length, 1, state.Dependency);
        }
        
        [BurstCompile]
        private struct FetchGameObjectTransformJob : IJobFor
        {
            [ReadOnly]
            public NativeArray<VFXKey> KeysArray;
            
            public VFXSingleton.ParallelWriter VFXSingleton;

            public double ElapsedTime;
            
            public void Execute(int index)
            {
                ref var entry = ref VFXSingleton.GetPersistent(KeysArray[index]);

                var i = 0;
                if (entry.HasPendingRequests)
                {
                    for (var threadIndex = 0; threadIndex < JobsUtility.ThreadIndexCount; threadIndex++)
                    {
                        ref var requests = ref entry.SpawnRequests.GetUnsafeList(threadIndex);
                        for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                        {
                            ref var request = ref requests.ElementAt(requestIndex);
                            if (!request.TrackedEntity.IsEntityId)
                            {
                                continue;
                            }

                            ref var entityIdData = ref entry.EntityIdFrameData.ElementAt(i);
                            SetTransformData(ref request.Transform, ref entityIdData);
                            request.Transform.SetDidTransformSystemRun();
                            i++;
                        }
                    }
                }
                
                foreach (var trackedEntity in entry.TrackedEntityIds)
                {
                    ref var data = ref entry.TransformBuffer.ElementAt(trackedEntity.IndexInData);
                    ref var entityIdData = ref entry.EntityIdFrameData.ElementAt(i);
                    SetTransformData(ref data, ref entityIdData);
                    i++;
                }
            }

            private void SetTransformData(ref VFXTransform data, ref EntityIdData entityIdData)
            {
                if (data.StartTrackingTime == 0f) data.StartTrackingTime = (float)ElapsedTime;

                var isEntityEnabled = entityIdData.IsEnabled;
                
                var isActive = data.IsAlive();
                var isStillTracking = data.TrackingDuration == 0f
                                      || data.StartTrackingTime + data.TrackingDuration > ElapsedTime;

                data.SetEntityAlive(true);
                data.SetInTrackingDuration(isStillTracking);
                data.SetEntityEnabled(isEntityEnabled);
                    
                var isAlive = isActive && isStillTracking;
                if (isAlive)
                {
                    // if (!entityExists) return;  <- already filtered out
                    data.Position = entityIdData.LocalToWorld.Position;
                    data.Rotation = math.degrees(math.EulerZXY(entityIdData.LocalToWorld.Rotation));
                    data.Scale = entityIdData.LocalToWorld.Value.DecomposeScale();
                }
                else
                {
                    data.Kill();
                }
            }
        }
        
        [BurstCompile]
        private struct FetchEntityTransformJob : IJobFor
        {
            [ReadOnly]
            public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly]
            public ComponentLookup<Disabled> DisabledLookup;
            
            public NativeArray<VFXKey> KeysArray;
            public VFXSingleton.ParallelWriter VFXSingleton;

            public double ElapsedTime;
            
            public void Execute(int index)
            {
                ref var entry = ref VFXSingleton.GetPersistent(KeysArray[index]);

                if (entry.HasPendingRequests)
                {
                    for (var threadIndex = 0; threadIndex < JobsUtility.ThreadIndexCount; threadIndex++)
                    {
                        ref var requests = ref entry.SpawnRequests.GetUnsafeList(threadIndex);
                        for (var requestIndex = 0; requestIndex < requests.Length; requestIndex++)
                        {
                            ref var request = ref requests.ElementAt(requestIndex);
                            if (request.TrackedEntity.IsEntityId)
                            {
                                continue;
                            }

                            SetTransformData(ref request.Transform, request.TrackedEntity);
                            request.Transform.SetDidTransformSystemRun();
                        }
                    }
                }
                
                foreach (var trackedEntity in entry.TrackedEntities)
                {
                    ref var data = ref entry.TransformBuffer.ElementAt(trackedEntity.IndexInData);
                    SetTransformData(ref data, trackedEntity);
                }
            }

            private void SetTransformData(ref VFXTransform data, TrackedEntity trackedEntity)
            {
                var entity = trackedEntity.Entity;
                if (data.StartTrackingTime == 0f) data.StartTrackingTime = (float)ElapsedTime;

                var isTrackedEntityNull = entity.Equals(Entity.Null);
                var entityExists = LocalToWorldLookup.TryGetComponent(entity, out var ltw);
                var isEntityEnabled = !DisabledLookup.HasComponent(entity);
                    
                var isActive = data.IsAlive();
                var isEntityAlive = isTrackedEntityNull || entityExists;
                var isStillTracking = data.TrackingDuration == 0f
                                      || data.StartTrackingTime + data.TrackingDuration > ElapsedTime;

                data.SetEntityAlive(isEntityAlive);
                data.SetInTrackingDuration(isStillTracking);
                data.SetEntityEnabled(isEntityEnabled);
                    
                var isAlive = isActive && isEntityAlive && isStillTracking;
                if (isAlive)
                {
                    if (!entityExists) return;
                    data.Position = ltw.Position;
                    data.Rotation = math.degrees(math.EulerZXY(ltw.Rotation));
                    data.Scale = ltw.Value.DecomposeScale();
                }
                else
                {
                    data.Kill();
                }
            }
        }
    }
}
