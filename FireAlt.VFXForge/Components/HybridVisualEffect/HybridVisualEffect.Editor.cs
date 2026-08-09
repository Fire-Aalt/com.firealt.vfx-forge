#if UNITY_EDITOR
using FireAlt.VFXForge.Data;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace FireAlt.VFXForge
{
    public partial class HybridVisualEffect
    {
        internal const string VFX_DEFINITION_PROPERTY_NAME = nameof(_vfxDefinition);
        internal const string UPLOAD_DATA_PROPERTY_NAME = nameof(_uploadData);
        internal const string UPLOAD_ARRAY_DATA_PROPERTY_NAME = nameof(_uploadArrayData);
        internal const string TRACKING_DURATION_PROPERTY_NAME = nameof(_trackingDuration);
        internal const string FOCUSED_BOUNDS_SIZE_PROPERTY_NAME = nameof(focusedBoundsSize);

        [SerializeField, VFXTypeBakerField(nameof(UploadData))]
        private VFXDataTypeBakerWrapper _uploadData = new();

        [SerializeField, VFXTypeBakerField(nameof(UploadData))]
        private VFXArrayDataTypeBakerWrapper _uploadArrayData = new();
        
        [SerializeField] 
        private float _trackingDuration;
        
        [SerializeField]
        private float focusedBoundsSize = 4f;
        
        private VFXSingleton _singleton;
        private TrackedEntity _trackedEntity = TrackedEntity.Null;
        private bool _isEditorInspectionActive;
        
        private bool IsPersistent => _vfxDefinition != null && _vfxDefinition.IsPersistent;
        
        internal bool HasEditorVisualEffect() => VisualEffect != null;
        internal bool IsEditorPaused() => HasEditorVisualEffect() && _visualEffect.pause;
        internal float GetEditorPlayRate() => HasEditorVisualEffect() ? _visualEffect.playRate : 1f;
        
        private void OnEnable()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Init();
                if (_entity != Entity.Null && _isEditorInspectionActive)
                {
                    DelayEditorRespawn();
                }
            }
        }

        private void OnDisable()
        {
            if (!EditorApplication.isPlaying)
            {
                Cleanup();
            }
        }

        private void DelayEditorRespawn()
        {
            Kill();
            Spawn();
             
            EditorApplication.delayCall += () =>
            {
                var visualEffect = VisualEffect;
                if (gameObject.activeInHierarchy && visualEffect != null && visualEffect.aliveParticleCount <= 0)
                {
                    DelayEditorRespawn();
                }
            };
        }

        internal void SetEditorPlayRate(float value)
        {
            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.playRate = value;
        }

        internal void EditorControlStop()
        {
            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.Reinit();
            _visualEffect.pause = true;
        }

        internal void EditorControlPlayPause()
        {
            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.pause = !_visualEffect.pause;
        }

        internal void EditorControlStep()
        {
            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.pause = true;
            _visualEffect.AdvanceOneFrame();
        }

        internal void EditorControlRestart()
        {
            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.Reinit();
            _visualEffect.pause = false;
            Kill();
            Spawn();
        }

        internal void EditorPlay()
        {
            Spawn();
        }

        internal void EditorStop()
        {
            Kill();

            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.Reinit();
        }
        
        private void OnValidate()
        {
            ValidateVFXGraph();
            SetVFXDataBaker();

            if (_isEditorInspectionActive)
            {
                SetFocusedBounds();
            }
            if (_vfxDefinition != null)
            {
                gameObject.name = _vfxDefinition.name.EndsWith("Definition")
                    ? _vfxDefinition.name[..^"Definition".Length] + "VFX"
                    : _vfxDefinition.name;
            }
            if (_trackingDuration <= 0f)
            {
                _trackingDuration = 0f;
            }
        }
        
        internal void RefreshDataAndReinit()
        {
            SetVFXDataBaker();

            if (!HasEditorVisualEffect())
            {
                return;
            }
             
            if (!IsDefinitionValid())
            {
                DeregisterVFX();
                _visualEffect.visualEffectAsset = null;
                return;
            }
            
            _visualEffect.visualEffectAsset = _vfxDefinition.visualEffectAsset;
            DeregisterVFX();
            RegisterVFX();
            ReinitializeVFX();
        }

        partial void OnVFXDefinitionChanged()
        {
            SetVFXDataBaker();
        }

        private void SetVFXDataBaker()
        {
            _uploadData ??= new VFXDataTypeBakerWrapper();
            _uploadArrayData ??= new VFXArrayDataTypeBakerWrapper();

            var isDefinitionValid = IsDefinitionValid();
            _uploadData.SetBaker(isDefinitionValid ? _vfxDefinition.DataTypeInfo.Type : null);
            _uploadArrayData.SetBaker(isDefinitionValid ? _vfxDefinition.ArrayDataTypeInfo.Type : null);
        }

        private unsafe void Spawn()
        {
            if (!IsDefinitionValid()) return;
            
            if (IsPersistent)
            {
                if (IsPersistentAlive()) return;
                ref var entry = ref _singleton.GetPersistent(_vfxDefinition);
                var entityId = gameObject.GetEntityId();
                
                var data = _uploadData.TryGetTempDataRaw(out var ptr);
                var arrayData = _uploadArrayData.TryGetTempBytesDataRaw(out var bytes);
                if (data)
                {
                    _trackedEntity = entry.SpawnUnsafe(entityId, ptr, bytes, _trackingDuration);
                }
                else if (arrayData)
                {
                    _trackedEntity = entry.SpawnUnsafe(entityId, bytes, _trackingDuration);
                }
                else
                {
                    _trackedEntity = entry.Spawn(entityId, _trackingDuration);
                }
            }
            else
            {
                ref var entry = ref _singleton.GetInstant(_vfxDefinition);
                
                var data = _uploadData.TryGetTempDataRaw(out var ptr);
                var arrayData = _uploadArrayData.TryGetTempBytesDataRaw(out var bytes);
                if (data)
                {
                    entry.SpawnUnsafe(ptr, bytes);
                }
                else if (arrayData)
                {
                    entry.SpawnUnsafe(bytes);
                }
                else
                {
                    entry.Spawn();
                }
            }
        }

        private void Kill()
        {
            if (!IsDefinitionValid() || !IsPersistentAlive()) return;
            _singleton.GetPersistent(_vfxDefinition).TryKill(_trackedEntity);
            _trackedEntity = TrackedEntity.Null;
        }
        
        private unsafe void UploadData()
        {
            if (!IsDefinitionValid() || !IsPersistentAlive()) return;
            ref var entry = ref _singleton.GetPersistent(_vfxDefinition);
            
            if (_vfxDefinition.DataGpuSize > 0 && _uploadData.TryGetTempDataRaw(out var ptr))
            {
                entry.TrySetUpdateDataUnsafe(_trackedEntity, ptr);
            }
            if (_vfxDefinition.ArrayDataGpuSize > 0 && _uploadArrayData.TryGetTempBytesDataRaw(out var bytes))
            {
                if (entry.TryGetArrayDataUnsafe(_trackedEntity, out var arrayData) && bytes.Length == arrayData.Length)
                {
                    arrayData.CopyFrom(bytes);
                }
                else
                {
                    Kill();
                    Spawn();
                }
            }
        }

        private void ReinitializeVFX()
        {
            if (!HasEditorVisualEffect())
            {
                return;
            }

            _visualEffect.Reinit();
            if (IsPersistent)
            {
                Kill();
            }
            Spawn();
        }

        private bool IsPersistentAlive() =>
            IsPersistent
            && !_trackedEntity.Equals(TrackedEntity.Null)
            && _singleton.GetPersistent(_vfxDefinition).IsAlive(_trackedEntity);
        
        internal bool IsDefinitionValid() => !Application.isPlaying && _vfxDefinition != null;
        internal bool ShowTrackingDuration() => IsDefinitionValid() && IsPersistent;
        
        private void SetFocusedBounds()
        {
            if (HasEditorVisualEffect() && _visualEffect.HasVector3(BoundsProperty))
            {
                _visualEffect.SetVector3(BoundsProperty, focusedBoundsSize * Vector3.one);
            }
        }
        
        internal void SetEditorInspectionActive(bool isInspected)
        {
            if (Application.isPlaying)
            {
                return;
            }

            if (isInspected)
            {
                if (_isEditorInspectionActive)
                {
                    return;
                }

                Init();
                if (World == null || !HasEditorVisualEffect())
                {
                    return;
                }

                _isEditorInspectionActive = true;
                VFXDefinition.OnVFXDefinitionChanged -= RefreshDataAndReinit;
                VFXDefinition.OnVFXDefinitionChanged += RefreshDataAndReinit;

                SetFocusedBounds();
                if (gameObject.activeInHierarchy)
                {
                    EditorPlay();
                }

                return;
            }

            if (!_isEditorInspectionActive)
            {
                return;
            }

            _isEditorInspectionActive = false;
            VFXDefinition.OnVFXDefinitionChanged -= RefreshDataAndReinit;
            if (this != null && gameObject.activeInHierarchy)
            {
                EditorStop();
            }
        }

        internal void OnInspectorOpened() => SetEditorInspectionActive(true);

        internal void OnInspectorClosed() => SetEditorInspectionActive(false);
    }
}
#endif
