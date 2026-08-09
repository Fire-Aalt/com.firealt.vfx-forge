# Persistent VFX

Long-lived VFX with explicit Spawn/Update/Kill API and optional tracked Entity/GameObject transform data support. 
Forge storage grows elastically from Initial Capacity unless Use Max Capacity is enabled.

`VFXTransformSystem` maintains transform information, alive state, and tracking duration of the attached `Entity`/`GameObject` (if it was passed to the Spawn method).
Persistent VFX Spawn methods return a `TrackedEntity` handle, which is needed to do any operations on the effect instance:

```csharp
// For Entities
var tracked = vfx.GetPersistent(VFXKeys.ElectroArc).Spawn(targetEntity, duration);
```
```csharp
// For GameObjects
var tracked = vfx.GetPersistent(VFXKeys.ElectroArc).Spawn(targetGO.GetEntityId(), duration);
```

*Note: both `Entity` and `EntityId` paths are thread-safe. However, `EntityId` has to be aquired on the main thread, as `GameObject` cannot be passed to a Job or accessed in any thread outside of Main Thread.*

`PersistentVFXEntry` overloads contain both `Entity` path and `GameObject` path (via `EntityId`):

| Method                                                                                                                     | Use                                                           |
|----------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------|
| `Spawn(Entity entityToTrack, float trackingDuration = 0f)`                                                                 | Spawn with no additional data.                                |
| `Spawn<T>(Entity entityToTrack, T data, float trackingDuration = 0f)`                                                      | Spawn with single payload data.                               |
| `Spawn<U>(Entity entityToTrack, NativeArray<U> arrayData, float trackingDuration = 0f)`                                    | Spawn with array payload data.                                |
| `Spawn<T, U>(Entity entityToTrack, T data, NativeArray<U> arrayData, float trackingDuration = 0f)`                         | Spawn with both payload types.                                |
| `SpawnUnsafe(Entity entityToTrack, byte* data, NativeArray<byte> arrayData = default, float trackingDuration = 0f) `       | Unsafe raw byte path for single data and optional array data. |
| `SpawnUnsafe(Entity entityToTrack, NativeArray<byte> arrayData, float trackingDuration = 0f)`                              | Unsafe raw byte path for array-only spawns.                   |
| `Spawn(EntityId gameObjectToTrack, float trackingDuration = 0f)`                                                           | Spawn with no additional data.                                |
| `Spawn<T>(EntityId gameObjectToTrack, T data, float trackingDuration = 0f)`                                                | Spawn with single payload data.                               |
| `Spawn<U>(EntityId gameObjectToTrack, NativeArray<U> arrayData, float trackingDuration = 0f)`                              | Spawn with array payload data.                                |
| `Spawn<T, U>(EntityId gameObjectToTrack, T data, NativeArray<U> arrayData, float trackingDuration = 0f)`                   | Spawn with both payload types.                                |
| `SpawnUnsafe(EntityId gameObjectToTrack, byte* data, NativeArray<byte> arrayData = default, float trackingDuration = 0f) ` | Unsafe raw byte path for single data and optional array data. |
| `SpawnUnsafe(EntityId gameObjectToTrack, NativeArray<byte> arrayData, float trackingDuration = 0f)`                        | Unsafe raw byte path for array-only spawns.                   |

Persistent behavior:

- Initial Capacity may be zero and controls only the starting allocation. Growth is geometric and retains the CPU allocation until unregister.
- When Max Capacity is enabled, active plus accepted pending handles count toward it. A rejected spawn returns an invalid `TrackedEntity`, but the `Entity` inside it remains valid.
- An array payload consumes one logical capacity slot regardless of its element count.
- `Entity.Null` is valid for non-entity-tracked persistent effects.
- `trackingDuration == 0` keeps tracking until the VFX is killed or the tracked entity dies.
- Positive `trackingDuration` keeps the effect alive until `StartTrackingTime + trackingDuration`, then the transform system marks it dead.
- Negative tracking durations assert.

## Updating and Killing Persistent VFX

Persistent entries expose query, update, and kill methods for the returned handle:

```csharp
ref var entry = ref vfx.GetPersistent(VFXKeys.ElectroArc);

entry.TrySetUpdateData(trackedEntity, playerPosition);

if (!entry.IsAlive(trackedEntity))
{
    buffer.RemoveAtSwapBack(i); // Some gameplay buffer which keeps track of VFX instances
}

entry.TryKill(trackedEntity);
```

`Try` methods can be called without a separate `IsAlive` check. They return `false` when the handle cannot be resolved.

| Method                                                        | Use                                                                             |
|---------------------------------------------------------------|---------------------------------------------------------------------------------|
| `IsAlive(TrackedEntity)`                                      | Returns whether a persistent handle currently resolves to alive transform data. |
| `TryGetUpdateDataAsRef<T>(TrackedEntity, out Ref<T>)`         | Returns a mutable reference to the single payload data.                         |
| `TryGetArrayData<T>(TrackedEntity, out UnsafeArray<T>)`       | Returns the typed array payload.                                                |
| `TryGetArrayDataUnsafe(TrackedEntity, out UnsafeArray<byte>)` | Returns raw array payload bytes.                                                |
| `TrySetUpdateData<T>(TrackedEntity, T)`                       | Writes new single payload data.                                                 |
| `TrySetUpdateDataUnsafe(TrackedEntity, byte*)`                | Writes new single payload data from raw bytes.                                  |
| `TryKill(TrackedEntity)`                                      | Requests the persistent effect to be killed.                                    |

Handles spawned this frame are deferred until `SyncVFXSystem` resolves them, but all paths account for deferred handles.

Growing Persistent storage reallocates and rebinds its CPU/GPU buffers while preserving existing resolved indices. This can cause a frame hitch and a temporary memory spike during growth. It does not increase the VFX Graph particle system's authored capacity; particles spawned beyond that independent compiled ceiling are still discarded by VFX Graph.

## Performance

Even though both `Entity` and `EntityId` paths exist, `EntityId` path is significantly slower due to an unavoidable main thread transform + enabled state fetch. VFX Forge was primarily designed for ECS and to not worsen the ECS performance, GameObject support was not designed for thousands of tracked Persistent VFXs.
If you need thousands or tens of thousands of Persistent VFXs with tracked transform data, use `Entity` path with a single `LocalToWorld` component on each entity instead.

`Entity` path is fully parallelised and is near to negligible even on the scale of tens of thousands of tracked transforms.
