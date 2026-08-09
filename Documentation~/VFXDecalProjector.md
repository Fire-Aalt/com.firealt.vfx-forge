# VFX Decal Projector

![VFXDecalProjector.png](Images/VFXDecalProjector.png)

`VFXDecalProjector` is a hybrid authoring component for sprite-backed decals. It bakes sprite, projection, opacity, fade, normal blend, and draw-distance data into ECS components, then renders the decals through VFX Forge Persistent VFX path.

Unlike texture-only decal workflows, VFX Forge decals render `Sprite` assets. This allows decal art to be packed into `SpriteAtlas` assets, which should be preferred for large decal sets because VFX Forge batches decals per texture. Decals that share an atlas texture can share batching better than decals spread across many unique textures.

## Definitions

VFX Forge supports both a default decal definition and custom decal definitions.

The default decal definition is configured in `Preferences/FireAlt/VFX Forge`. Use it when most decals should share the same graph setup and material behavior.

Create your own `VFXDecalDefinition` when a decal set needs a different VFX Graph, initial or optional maximum capacity, timeout, payload type, or graph behavior. Select the decal VFX Graph asset and run `FireAlt/Create VFX Decal Definitions from VFX Assets` on the main toolbar.

`VFXDecalDefinition` assets are persistent graph templates for decals. Runtime decal definitions are produced from the template and the sprite lookup data used by `VFXDecalProjector`.

## Sprite and Texture Data

Decals are authored from sprites, not raw textures. The sprite provides the rectangle, pivot, and packed texture coordinates used by the decal projection.

VFX Forge decals carry both base and normal map texture data, matching the regular URP decal projector workflow. The controls are intended to have feature parity with `DecalProjector`, including projection depth, projection depth pivot, opacity, draw distance, start fade, angle fade, and normal blend.

Because decals are batched per texture, many unique decal textures can consume more runtime definitions and `VFXKey` IDs. Pack decals into sprite atlases where possible.

## Decal Workflow

`VFXDecalProjector` creates or updates ECS data for the authoring GameObject. The decal systems map that data to a persistent VFX entry, spawn a tracked persistent VFX instance, and keep its payload synchronized while the projector is active and in range.

## VFX Graph Limitation

VFX Graph currently cannot create a VFX decal asset with a decal shader output. That means VFX Forge decals cannot use a native VFX Graph decal-output asset in the same way a regular decal shader output would be authored.

Unity has this functionality under review, and it is expected to eventually become part of the VFX Graph package. Until then, VFX Forge uses basic VFX Graph decal output to provide feature parity with the regular URP `DecalProjector` controls where possible.

## Editor Preferences

`VFXSettings.DefaultDecalVFX` is stored in EditorPrefs through the Preferences provider at `Preferences/FireAlt/VFX Forge`.
