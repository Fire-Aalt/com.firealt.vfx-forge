using Unity.Scripting.LifecycleManagement;
using FireAlt.Core.Utility;
using FireAlt.VFXForge.Data;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace FireAlt.VFXForge
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(BeforeVFXTransformSystemGroup))]
    [BurstCompile]
    public partial struct UpdateVFXDecalsSystem : ISystem
    {
        [NoAutoStaticsCleanup]
        private static class Burst
        {
            public static readonly SharedStatic<BurstInterop> Camera = 
                SharedStatic<BurstInterop>.GetOrCreate<UpdateVFXDecalsSystem>();
        }
        
        static unsafe UpdateVFXDecalsSystem()
        {
            Burst.Camera.Data = new BurstInterop(&CameraPositionPacked);
        }
        
        private static unsafe void CameraPositionPacked(void* argumentsPtr, int argumentsSize)
        {
            ref var cameraPosition = ref BurstInterop.ArgumentsFromPtr<float3>(argumentsPtr, argumentsSize);
            
            Camera mainCamera = null;
            if (Application.isPlaying)
            {
                mainCamera = Camera.main;
            }
#if UNITY_EDITOR
            else
            {
                var lastActiveSceneView = UnityEditor.SceneView.lastActiveSceneView;
                if (lastActiveSceneView != null)
                {
                    mainCamera = lastActiveSceneView.camera;
                }
            }
#endif
            cameraPosition = mainCamera != null ? mainCamera.transform.position : default(float3);
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Burst.Camera.Data.InvokeOut(out float3 cameraPosition);
            var vfxSingleton = SystemAPI.GetSingletonRW<VFXSingleton>().ValueRW;
            
            state.Dependency = new DisableJob
            {
                VFXSingleton = vfxSingleton.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
            
            state.Dependency = new DrawDistanceJob
            {
                CameraPosition = cameraPosition,
                VFXSingleton = vfxSingleton.AsParallelWriter(),
            }.Schedule(state.Dependency);
            
            state.Dependency = new UpdateJob
            {
                VFXSingleton = vfxSingleton.AsParallelWriter()
            }.ScheduleParallel(state.Dependency);
        }
        
        [BurstCompile]
        [WithOptions(EntityQueryOptions.IncludeDisabledEntities)]
        [WithAll(typeof(Disabled))]
        [WithDisabled(typeof(RuntimeDecalLookup))]
        private partial struct DisableJob : IJobEntity
        {
            public VFXSingleton.ParallelWriter VFXSingleton;
            
            private void Execute(ref DecalProjectorVFX vfx, EnabledRefRW<RuntimeDecalLookup> enabled)
            {
                if (!VFXSingleton.ContainsPersistent(vfx.Key)) return;
                vfx.TryKillDecal(VFXSingleton);
                enabled.ValueRW = true;
            }
        }
        
        [BurstCompile]
        private partial struct DrawDistanceJob : IJobEntity
        {
            public VFXSingleton.ParallelWriter VFXSingleton;
            public float3 CameraPosition;
            
            private void Execute(in LocalToWorld ltw, in DecalProjectorData data, ref DecalProjectorVFX vfx, Entity self)
            {
                if (!VFXSingleton.ContainsPersistent(vfx.Key)) return;
                
                var dist = math.distance(CameraPosition, ltw.Position);
                if (dist > data.DrawDistance && !vfx.TrackedEntity.Equals(TrackedEntity.Null))
                {
                    vfx.TryKillDecal(VFXSingleton);
                }
                else if (dist < data.DrawDistance && vfx.TrackedEntity.Equals(TrackedEntity.Null))
                {
                    vfx.TrackedEntity = VFXSingleton.GetPersistent(vfx.Key).Spawn(self);
                }
            }
        }
        
        [BurstCompile]
        [WithDisabled(typeof(RuntimeDecalLookup))]
        private partial struct UpdateJob : IJobEntity
        {
            public VFXSingleton.ParallelWriter VFXSingleton;
            
            private void Execute(in DecalProjectorVFX vfx, in DecalProjectorData data)
            {
                if (!VFXSingleton.ContainsPersistent(vfx.Key)) return;
                if (!VFXSingleton.GetPersistent(vfx.Key).TryGetUpdateDataAsRef<VFXDecal>(vfx.TrackedEntity, out var updateData))
                {
                    return;
                }

                var spriteProps = data.SpriteProperties;
                var pivot = spriteProps.rectScale * (0.5f - spriteProps.normalizedPivot);
                
                updateData.Value = new VFXDecal
                {
                    Size = new Vector3(spriteProps.rectScale.x, spriteProps.rectScale.y, data.ProjectionDepth),
                    UvAtlas = spriteProps.uvAtlas,
                    Pivot = new Vector3(pivot.x, pivot.y, data.ProjectionDepthPivot),
                    Opacity = data.Opacity,
                    DrawDistance = data.DrawDistance,
                    StartFade = data.StartFade,
                    AngleFade = data.AngleFade,
                    NormalBlend = data.NormalBlend,
                };
            }
        }
    }
}