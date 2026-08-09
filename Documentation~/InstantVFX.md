# Instant VFX

Spawn from ECS/MonoBehavior/Job by getting the registered instant entry:

```csharp
using FireAlt.VFXForge;
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
private partial struct SpawnExplosionJob : IJobEntity
{
    public VFXSingleton.ParallelWriter VFX;

    private void Execute(in ExplosionRequest request)
    {
        VFX.GetInstant(VFXKeys.Explosion).Spawn(new VFXExplosion
        {
            Position = request.Position,
        });
    }
}
```

`InstantVFXEntry` overloads:

All overloads return `bool`: `true` when accepted, or `false` before any counters or payload buffers are changed when Max Capacity is exhausted.

| Method                                                                | Use                                                           |
|-----------------------------------------------------------------------|---------------------------------------------------------------|
| `bool Spawn()`                                                        | Spawn with no payload data.                                   |
| `bool Spawn<T>(T spawnData)`                                          | Spawn with single payload value.                              |
| `bool Spawn<U>(NativeArray<U> arrayData)`                             | Spawn with array payload data only.                           |
| `bool Spawn<T, U>(T spawnData, NativeArray<U> arrayData)`             | Spawn with single payload and array payload data.             |
| `bool SpawnUnsafe(byte* spawnData, NativeArray<byte> arrayData = default)` | Unsafe raw byte path for single data and optional array data. |
| `bool SpawnUnsafe(NativeArray<byte> arrayData)`                       | Unsafe raw byte path for array-only spawns.                   |

Instant requests are gathered per worker thread, merged and remapped during `SyncVFXSystem`, uploaded to VFX Graph, then cleared.
Instant VFX are by design "instant" and thus the only way to supply data to the VFX is to pass it in the request method.

Initial Capacity seeds the per-thread CPU and GPU collectors. When Use Max Capacity is enabled, Max Capacity counts accepted spawn calls between successful `SyncVFXSystem` uploads.
An array spawn is one logical call regardless of its element count. With both capacities set to zero and Max enabled, every spawn returns `false`.
