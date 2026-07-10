using FireAlt.VFXForge.Data;
using FireAlt.Core.Groups;
using Unity.Entities;

namespace FireAlt.VFXForge
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(CleanupSystemGroup))]
    public partial class CleanupVFXSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRW<VFXSingleton>();
            var vfxSingleton = SystemAPI.GetSingleton<VFXSingleton>();
            var graphicsBuffersObject = SystemAPI.GetSingleton<VFXGraphicsBuffersSingleton>().Value.Value;
            
            foreach (var registeredVFX in SystemAPI.Query<RefRO<RegisteredVFX>>()
                         .WithAbsent<HybridVisualEffectData>())
            {
                if (registeredVFX.ValueRO.Key.Equals(VFXKey.Null)) continue;
                RemoveVFXEntry(ref vfxSingleton, graphicsBuffersObject, registeredVFX.ValueRO.Key);
            }

            EntityManager.RemoveComponent<RegisteredVFX>(SystemAPI.QueryBuilder().WithAll<RegisteredVFX>()
                .WithAbsent<HybridVisualEffectData>().Build());
        }
        
        private void RemoveVFXEntry(ref VFXSingleton vfxSingleton, VFXGraphicsBuffersObject graphicsBuffersObject,
            VFXKey key)
        {
            vfxSingleton.InstantAliveVFX.Remove(key);
            vfxSingleton.PersistentAliveVFX.Remove(key);

            if (graphicsBuffersObject != null)
            {
                graphicsBuffersObject.EnsureInitialized();
                if (graphicsBuffersObject.InstantVFXGraphEntries.TryGetValue(key, out var instantGraphicsBuffers))
                {
                    graphicsBuffersObject.InstantVFXGraphEntries.Remove(key);
                    instantGraphicsBuffers.Dispose();
                }

                if (graphicsBuffersObject.PersistentVFXGraphEntries.TryGetValue(key, out var persistentGraphicsBuffers))
                {
                    graphicsBuffersObject.PersistentVFXGraphEntries.Remove(key);
                    persistentGraphicsBuffers.Dispose();
                }
            }

            if (vfxSingleton.InstantVFXGraphEntries.TryGetValue(key, out var instantEntry))
            {
                vfxSingleton.InstantVFXGraphEntries.Remove(key);
                instantEntry.Dispose();
            }

            if (vfxSingleton.PersistentVFXGraphEntries.TryGetValue(key, out var persistentEntry))
            {
                vfxSingleton.PersistentVFXGraphEntries.Remove(key);
                persistentEntry.Dispose();
            }

            vfxSingleton.IsPersistent.Remove(key);
        }
    }
}
