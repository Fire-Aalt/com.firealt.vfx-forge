#if UNITY_EDITOR
using FireAlt.Core;
using FireAlt.Core.EntityCommands;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FireAlt.VFXForge
{
    public partial class VFXDecalProjector
    {
        private void Reset()
        {
            VFXDecalDefinition = Authoring.VFXSettings.DefaultDecalVFX;
            Sprite = null;
        }

        private void OnValidate()
        {
            if (_registeredDefinition != VFXDecalDefinition)
            {
                if (VFXDecalDefinition == null)
                {
                    Cleanup();
                }
                else
                {
                    Init();
                }
                _registeredDefinition = VFXDecalDefinition;
            }

            drawDistance = math.max(drawDistance, 0f);
            projectionDepth = math.max(projectionDepth, 0f);
            Sprite = _sprite;
        }
        
        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                Init();
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                Cleanup();
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            var globalCenter = (float3)transform.position + transform.rotation * _decalPivot * (float3)transform.lossyScale;
            var globalSize = _decalSize * transform.lossyScale;
            Gizmos.color = Color.white;
            GizmosEx.DrawWireCuboid(globalCenter, transform.rotation, globalSize);
        }
        
        private class HybridDecalProjectorBaker : Baker<VFXDecalProjector>
        {
            public override void Bake(VFXDecalProjector authoring)
            {
                DependsOn(authoring.VFXDecalDefinition);
                DependsOn(authoring._sprite);
                var entity = GetEntity(TransformUsageFlags.Renderable);

                var commands = new BakerCommands(this, entity);
                SetupDecalProjector(ref commands, authoring, true);
            }
        }
    }
}
#endif
