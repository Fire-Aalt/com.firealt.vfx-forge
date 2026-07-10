using UnityEngine;
using UnityEngine.VFX;
using Unity.Entities;
using Unity.Transforms;
using FireAlt.VFXForge.Data;
using FireAlt.Core;
using FireAlt.Core.Inspectors;

namespace FireAlt.VFXForge
{
    [ExecuteAlways]
    [RequireComponent(typeof(VisualEffect))]
    [DefaultExecutionOrder(-100)] // Needed for Start to be a valid place to use World
    public partial class HybridVisualEffect : MonoBehaviour
    {
        private static readonly int BoundsProperty = Shader.PropertyToID("Bounds");
        private const int BOUNDS_SIZE = 1_000_000;
        
        [SerializeField, HideInInspector]
        private VisualEffect _visualEffect;

        [SerializeField, InlineScriptableObject]
        private VFXDefinition _vfxDefinition;
        
        private Entity _entity;
        
        public VFXDefinition VFXDefinition
        {
            get => _vfxDefinition;
            set
            {
                if (_vfxDefinition == value)
                {
                    return;
                }

                _vfxDefinition = value;
                ValidateVFXGraph();
                OnVFXDefinitionChanged();
            }
        }

        partial void OnVFXDefinitionChanged();

        public VisualEffect VisualEffect
        {
            get
            {
                EnsureVisualEffectReference();
                return _visualEffect;
            }
        }

        private static World World => World.DefaultGameObjectInjectionWorld;
        
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

        internal void SetVFXActive(bool isActive)
        {
            VisualEffect.gameObject.SetActive(isActive);
        }

        public void Init()
        {
#if UNITY_EDITOR
            DefaultWorldInitialization.DefaultLazyEditModeInitialize();
            _singleton = GlobalVFXSingleton.Get();
#endif
            if (Application.isPlaying)
            {
                transform.position = Vector3.zero;
                transform.rotation =  Quaternion.identity;
                transform.localScale = Vector3.one;
            }
            
            if (VFXDefinition == null || _entity != Entity.Null) return;
            ValidateVFXGraph();
            RegisterVFX();
        }

        public void Cleanup()
        {
            DeregisterVFX();
        }

        private bool EnsureVisualEffectReference()
        {
            if (this == null) return false;
            if (_visualEffect == null)
            {
                _visualEffect = GetComponent<VisualEffect>();
            }

            return _visualEffect != null;
        }

        private void ValidateVFXGraph()
        {
            if (!EnsureVisualEffectReference())
            {
                return;
            }

            if (_vfxDefinition != null)
            {
                if (_visualEffect.visualEffectAsset != _vfxDefinition.visualEffectAsset)
                    _visualEffect.visualEffectAsset = _vfxDefinition.visualEffectAsset;
                
                if (_visualEffect.HasVector3(BoundsProperty))
                   _visualEffect.SetVector3(BoundsProperty, BOUNDS_SIZE * Vector3.one);
                else
                    Debug.LogError($"{gameObject} Missing <name: Bounds, type: Vector3> exposed property");
            }
        }

        private void RegisterVFX()
        {
            if (_entity != Entity.Null || World == null) return;

            var em = World.EntityManager;
            _entity = em.CreateEntity(typeof(LocalToWorld), typeof(RegisteredVFX));
            em.AddComponentData(_entity, new HybridVisualEffectData { HybridVisualEffect = this });
            em.SetComponentEnabled<HybridVisualEffectData>(_entity, true);
            
            // Only needed for editor time to show the preview effect
            if (!Application.isPlaying)
            {
                em.AddComponentData(_entity, new HybridEntitySync(this));
            }

            World.GetExistingSystemManaged<InitializeVFXSystem>().Update();
        }

        private void DeregisterVFX()
        {
            if (_entity == Entity.Null || World == null) return;
            World.EntityManager.DestroyEntity(_entity);
            _entity = Entity.Null;
            if (EnsureVisualEffectReference())
            {
                _visualEffect.Reinit();
            }
            
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                World.GetExistingSystemManaged<CleanupVFXSystem>()?.Update();

                _trackedEntity = TrackedEntity.Null;
            }
#endif
        }
    }
}
