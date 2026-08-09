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
