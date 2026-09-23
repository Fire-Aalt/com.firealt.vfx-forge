using Unity.Scripting.LifecycleManagement;
using System;
using FireAlt.VFXForge.Data;
using FireAlt.Core;
using FireAlt.Core.EntityCommands;
using FireAlt.Core.Inspectors;
using FireAlt.Core.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

namespace FireAlt.VFXForge
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)] // Needed for Start to be a valid place to use World
    [NoAutoStaticsCleanup]
    public partial class VFXDecalProjector : MonoBehaviour
    {
        [Header("References")]
        public VFXDecalDefinition VFXDecalDefinition;

        [SerializeField, HideInInspector]
        private VFXDecalDefinition _registeredDefinition;
        
        [Header("Data")]
        [FormerlySerializedAs("sprite")]
        [SerializeField] private Sprite _sprite;
        
        public float projectionDepth = 1f;
        public float projectionDepthPivot = 0.5f;
        [Range(0f, 1f)] public float opacity = 1f;
        public float drawDistance = 1000f;
        [Range(0f, 1f)] public float startFade = 0.9f;
        [MinMaxRange(0f, 180f)] public float2 angleFade;
        [Range(0f, 1f)] public float normalBlend;
        
        private float3 _decalSize;
        private float3 _decalPivot;
        
        private Sprite _curSprite;
        private Sprite _oldSprite;
        
        private static World World => World.DefaultGameObjectInjectionWorld;
        private Entity _entity;
        
        public Sprite Sprite
        {
            get => _sprite;
            set
            {
                SetSprite(value);
                BakeComponents(_curSprite != _oldSprite || value == null);
            }
        }
        public Entity Entity => _entity;

        private void SetSprite(Sprite sprite)
        {
            _sprite = sprite;
            _oldSprite = _curSprite;
            _curSprite = sprite;
            var spriteProps = new SpriteProperties(_sprite);
            _decalSize = new float3(spriteProps.rectScale.x, spriteProps.rectScale.y, projectionDepth);
            
            var spritePivot = spriteProps.rectScale * (0.5f - spriteProps.normalizedPivot);
            _decalPivot = new float3(spritePivot.x, spritePivot.y, projectionDepth * projectionDepthPivot);
        }
        
        private void Start()
        {
            if (Application.isPlaying)
            {
                Init();
            }
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                Cleanup();
            }
        }
        
        private void Init()
        {
#if UNITY_EDITOR
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();
#endif
            if (World != null && _entity == Entity.Null)
            {
                _entity = World.EntityManager.CreateEntity(typeof(LocalToWorld));
                World.EntityManager.AddComponentData(_entity, new HybridEntitySync(this));
                BakeComponents(true);
            }
        }

        private void Cleanup()
        {
            if (World != null)
            {
                World.EntityManager.DestroyEntity(_entity);
            }
            _entity = Entity.Null;
        }

        private void BakeComponents(bool resetDecal)
        {
            if (World != null && World.EntityManager.HasComponent<HybridEntitySync>(_entity))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                var commands = new EntityCommandBufferCommands(ecb, _entity);

                SetupDecalProjector(ref commands, this, resetDecal);
                ecb.Playback(World.EntityManager);
            }
        }
        
        private static void SetupDecalProjector<T>(ref T commands, VFXDecalProjector authoring, bool resetDecal)
            where T : IEntityCommands
        {
            if (authoring.VFXDecalDefinition == null)
            {
                Debug.LogError($"{nameof(VFXDecalDefinition)} was null. Decal cannot be rendered.", authoring.gameObject);
                return;
            }
            
            commands.AddComponent(new DecalProjectorData
            {
                SpriteProperties = new SpriteProperties(authoring._sprite),
                NormalBlend = authoring.normalBlend,
                AngleFade = authoring.angleFade,
                StartFade = authoring.startFade,
                DrawDistance = authoring.drawDistance,
                Opacity = authoring.opacity,
                ProjectionDepth = authoring.projectionDepth,
                ProjectionDepthPivot = authoring.projectionDepthPivot,
            });
            
            if (resetDecal)
            {
                commands.AddComponent(new RuntimeDecalLookup
                {
                    Value = new DecalLookup(authoring.VFXDecalDefinition, authoring._sprite)
                });
                commands.SetComponentEnabled<RuntimeDecalLookup>(true);
                commands.AddComponent<DecalProjectorVFX>();
            }
        }
    }
}
