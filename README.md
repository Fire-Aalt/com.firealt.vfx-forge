# VFX Forge

![VFXForge.png](Documentation%7E/Images/VFXForge.png)

VFX Forge is a Unity VFX Graph framework for driving instant and persistent visual effects from ECS systems and hybrid MonoBehaviours.

It lets gameplay code submit VFX requests as unmanaged data, batch those requests into GPU buffers, and render many effect instances through a small number of registered VFX Graphs instead of spawning one GameObject per visual effect instance.

## Contents

- [Overview](#overview)
- [Installation](#installation)
- [Core Concepts](#core-concepts)
  - [Getting Started](Documentation~/GettingStarted.md)
  - [Instant VFX](Documentation~/InstantVFX.md)
  - [Persistent VFX](Documentation~/PersistentVFX.md)
  - [VFX Decal Projector](Documentation~/VFXDecalProjector.md)
- [Editor Preview](#editor-preview)
- [System Order](#system-order)
- [Parallel Safety](#parallel-safety)
- [GPU Memory Management](#memory-management)
- [Performance](#performance)
- [Limitations and What to Avoid](#limitations-and-what-to-avoid)
- [Samples](#samples)

## Overview

VFX Forge features:

- **Generic GPU buffer batching**: per-instance and array data are uploaded through `GraphicsBuffer` properties such as `DataBuffer`, `ArrayDataBuffer`, and `TransformBuffer`. No need to write boilerplate code for each effect.
- **Instant VFX**: one-frame spawn requests. Useful for one-off VFXs like explosions, damage numbers, hit effects and other.
- **Persistent VFX**: tracked effect instances. Each spawn returns a `TrackedEntity` handle that can be checked, updated, or killed while the effect is alive. Useful for controlled long-lived VFXs, like fire, gameplay abilities, status effects and other.
- **Complex Data Upload**: both Instant and Persistent VFX allow to pass optional single-per-effect-instance data and/or optional array-per-effect-instance data. The data can be used in any desirable way to achieve complex effects. VFX Templates are available for each data layout scenario.
- **ECS/GameObject Support**: VFX Forge was primarily designed for ECS, with GameObject support added on top of it. All public API is accessible for both ECS and GameObject workflows.
- **Type Safety**: Even though the underlying data is represented in `byte` to allow for any data type payload, a custom TypeManager was creted to ensure only the registered Type can be passed to a given VFX.
- **Editor Preview**: `HybridVisualEffect` has direct integration with editor mode to preview your effects without entering playmode. Custom data bakers, GUI overlay with VFX controls, and even a direct integration with `VFX Control` panel in the VFX Asset graph view.
This greatly increases iteration time allowing for trial and error method right in the VFX Asset window.
- **VFX-based DecalProjector**: a custom `DecalProjector` which utilizes VFX Forge under the hood. Comes with ECS and GameObject ready authoring and better batching mechanism to drive thousands of `DecalProjectors` with no wasted performance.

## Installation

Add these packages using git urls in the package manager:

1. FireAlt.Core: https://github.com/Fire-Aalt/com.firealt.core.git
2. FireAlt.VFXForge: https://github.com/Fire-Aalt/com.firealt.vfx-forge.git

The package has optional BovineLabs.Core integration for the Pause feature to work correctly.

*Note: VFX Forge depends on Unity.Entities (ECS) for its core functionality. In Unity 6.7 Unity.Entities will be a core package, but in previous versions VFX Forge will install the dependency if it is not in the project already. VFX Forge can still be used with GameObject workflow without any ECS knowledge.*

## Core Concepts

`VFXDefinition` is the ScriptableObject identity for a graph. It stores the necessary information to describe a VFX.

`VFXKey` is the runtime handle used to find registered graph entries. It stores a `ushort`, so a project can have at most `ushort.MaxValue` VFX keys.
Decals can consume keys quickly because each unique texture lookup creates its own definition; prefer sprite atlases where many decals should share a texture source.

`VFXSingleton` is the ECS singleton. It is the public API entry, stores registered instant and persistent entries and exposes `AsParallelWriter()` for job usage.

`InstantVFXEntry` accepts one-frame spawn requests with optional single data and optional array data.

`PersistentVFXEntry` accepts tracked spawn requests and returns `TrackedEntity` handles. A persistent handle may be deferred until `SyncVFXSystem` resolves it into the backing persistent buffers.

`VFXTransform` is the persistent transform payload uploaded to VFX Graph. It includes position, rotation, scale, tracking duration, and packed alive state.

## Getting Started

For a step-by-step setup walkthrough, see [Getting Started](Documentation~/GettingStarted.md).

## Instant VFX

One-frame spawn requests. Useful for instant VFXs like explosions, damage numbers, hit effects and other.

For full API breakdown and examples, see [Instant VFX](Documentation~/InstantVFX.md).

## Persistent VFX

Long-lived VFX with explicit Spawn/Update/Kill API and optional tracked Entity/GameObject transform data support. 
Forge storage grows elastically from the definition's Initial Capacity unless an optional Max Capacity is enabled.

For full API breakdown and examples, see [Persistent VFX](Documentation~/PersistentVFX.md).

## Editor Preview

`HybridVisualEffect` supports edit-mode initialization and preview spawning through its custom inspector. The inspector reads definition data and uses data bakers to spawn or update preview payloads.

`HybridVisualEffect` `SceneOverlay` Control panel which overrides and hides the `VisualEffect` `SceneOverlay` Control panel. The custom control panel internally hooks into `HybridVisualEffect` directly, to make all previews possible:

https://github.com/user-attachments/assets/7ce634a1-2f30-451c-99b9-7f350e2f3bfd

`VFXAsset` has a way to "Attach to a GameObject" to preview the VFX directly with controls in the VFX Graph panel. VFX Forge overrides this panel when attaching to a `HybridVisualEffect`, allowing to interact with `HybridVisualEffect` seamlessly:

https://github.com/user-attachments/assets/be23ba94-3b01-4a2f-af61-567947be2af5

## System Order

Runtime groups:

| Group/System                    | Order                                   | Role                                                                             |
|---------------------------------|-----------------------------------------|----------------------------------------------------------------------------------|
| `InitializeVFXSystemGroup`      | `LateInitializationSystemGroup`         | Registers new `HybridVisualEffect` instances.                                    |
| `UpdateVFXSystemGroup`          | `LateSimulationSystemGroup`, order last | Owns persistent transform and kill work.                                         |
| `BeforeVFXTransformSystemGroup` | Before `VFXTransformSystem`             | Hook point for systems that must run before transform upload data is finalized.  |
| `VFXTransformSystem`            | Inside `UpdateVFXSystemGroup`           | Updates persistent transform/lifetime state.                                     |
| `AfterVFXTransformSystemGroup`  | After `VFXTransformSystem`              | Hook point for systems that update persistent payloads after transform tracking. |
| `SyncVFXSystem`                 | End of `PresentationSystemGroup`        | Resolves requests and uploads data to VFX Graph buffers.                         |

Do not spawn persistent VFX after `VFXTransformSystem` has run for the frame and before `SyncVFXSystem`. 
If a persistent spawn reaches sync without transform data, the code throws with the concrete warning that persistent VFX should not be spawned in `LateUpdate`.

## Parallel Safety

Use `VFXSingleton.ParallelWriter` inside jobs. The writer exposes registered entry lookup through read-only hash maps while the entries themselves own thread-local request structures.

Safe patterns:

- Use `ParallelWriter.GetInstant(...).Spawn(...)` from parallel jobs.
- Use `ParallelWriter.GetPersistent(...).Spawn(...)` from parallel jobs.
- Use persistent `TrySet...`, `TryGet...`, `IsAlive`, and `TryKill` methods through the returned entry reference.
- Store `TrackedEntity` anywhere when the effect needs later updates/kill.

Be careful with:

- Ensure to never try to modify a Persistent data for the same `TrackedEntity` on a single `PersistentVFXEntry` in parallel. 
There is no easy way to check this from VFX Forge perspective, and so regular parallelism rules must be followed.
- The returned entry is a mutable `ref`. Do not store entry refs beyond the immediate operation.
- Respect the persistent spawn timing rule relative to `VFXTransformSystem` and `SyncVFXSystem`.

## Memory Management

Instant request buffers are reused and cleared after upload. Initial Capacity defines their startikng allocation, optional Max Capacity limits accepted logical spawn calls between sync updates.

Persistent CPU buffers grow geometrically from Initial Capacity and retain their allocation for the registered definition lifetime. Optional Max Capacity limits active plus accepted pending handles.
Persistent array payloads are copied into pooled deferred unsafe arrays first, then moved into `UnsafeHeapMemory` when the request resolves, similar to how `memory.alloc` works.

VFX Forge deallocates all GPU memory for VFXs, which were unused for a specified duration. 
This essentially allows to store all VFXs in a single scene without any need for asset management of the VFXs. Only active VFXs occupy GPU memory and are involved in GPU computations.
With Unity 6.6, `VisualEffect` component will be able to completely deallocate its own GPU resources, making the GPU memory management for VFXs with VFX Forge trivial. 

## Performance

VFX Forge is designed around batched uploads and stable backing storage, using ECS, Bursted Jobs, and optimized data structures wherever possible:

- Effects are uploaded through VFX Graph `GraphicsBuffer` properties.
- Instant requests avoid shared contention by gathering per thread and merging once.
- Persistent entries upload only relevant transform/data ranges when possible.
- Inactive graphs are disabled after timeout to release graphics buffers and stop unnecessary graph updates.

Tradeoffs:

- Persistent growth reallocates double-capacity CPU/GPU backing storage. Existing resolved indices remain stable.
- Array payloads are copied on spawn.
- Prefer fewer batched definitions over many unique graph definitions when spawn counts are high.

## Limitations and What to Avoid

- Do not register two `HybridVisualEffect` instances with the same `VFXDefinition` key.
- Do not spawn persistent VFX in `LateUpdate` or any path that runs after `VFXTransformSystem` but before `SyncVFXSystem`.
- Do not assume a capped spawn succeeds: check `TrackedEntity.IsValid` for Persistent VFX and the boolean return value for Instant VFX.
- Forge buffer growth does not change the VFX Graph system's authored particle capacity. VFX Graph discards overspawn beyond that independent compiled ceiling.
- Do not mismatch definition payload type and generic `Spawn<T>` or `TrySetUpdateData<T>` calls.

## Samples

The package includes a Unity Package Manager sample:

- `Samples~/Demo/DEMO.unity`: basic demo scene.
- `Samples~/Demo/DamageNumbers`: damage-number graph, data types, glyph baker, and definition.
- `Samples~/Demo/TemplatesDefinitions`: definition assets for the built-in instant/persistent templates.

Import the sample from Package Manager to inspect a complete graph/data/definition setup.
*Note*: "DamageNumbersDEMO.vfx" file uses custom VFX types and requires an additional manual Reimport after importing the sample.
