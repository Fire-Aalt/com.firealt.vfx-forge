## [1.2.5] - 2026-08-10

### Fixed
* Keep the editor-only VFX type refresh event out of the Burst-visible registry static constructor.
* Use Unity's code lifecycle callback to refresh VFX types on Unity 6000.5 and newer, with the assembly-load polling fallback retained for older Editors.

## [1.2.4] - 2026-08-09

### Fixed
* Recreate stale open-window VFX subgraph copies after reloads and Play Mode tests, before a VisualEffect inspector can attach.

## [1.2.3] - 2026-08-09

### Fixed
* Defer HybridVisualEffect VFX Graph attachment while subgraph resources are temporarily incomplete.

## [1.2.2] - 2026-08-09

### Fixed
* Refresh VFX types, stable hashes, baker mappings, and open dropdowns when assemblies change without a domain reload.

## [1.2.1] - 2026-08-09

### Fixed
* Restore stale VFX Graph component-board state when inspecting HybridVisualEffect after a domain reload.

## [1.2.0] - 2026-08-09

### Changed
* Replace fixed VFX definition capacity with Initial Capacity and an optional Max Capacity for both Instant and Persistent VFX.
* Make CPU/GPU buffers grow elastically while preserving persistent handles and existing VFX Graph asset contracts.
* Make all Instant `Spawn` and `SpawnUnsafe` overloads report acceptance with a bool return value.
* Keep Persistent editor previews connected to their GameObject transform instead of temp ECS entity.

## [1.1.1] - 2026-07-10

### Fixed
* Fix project build causing "key is duplicated" errors being spammed in the console and build failing.

## [1.1.0] - 2026-07-01

### Changed
* Add support for `UNITY_DISABLE_MANAGED_COMPONENTS` and Entities 6.6.1b/6.7.1a.

## [1.0.0] - 2026-06-06

Release
