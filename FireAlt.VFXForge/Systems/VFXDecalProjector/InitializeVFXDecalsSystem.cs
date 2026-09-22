using System;
using FireAlt.VFXForge.Data;
using FireAlt.Core.Collections;
using FireAlt.Core.Utility;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace FireAlt.VFXForge
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(InitializeVFXSystemGroup))]
    public partial class InitializeVFXDecalsSystem : SystemBase
    {
        public struct DecalEntry
        {
            public VFXKey Key;
            public UnityObjectRef<HybridVisualEffect> HybridVisualEffect;
        }

        public struct KeyCandidate : IComparable<KeyCandidate>
        {
            public ushort Value;

            public int CompareTo(KeyCandidate other)
            {
                return Value.CompareTo(other.Value) * -1;
            }
        }
        
        private static readonly SecondarySpriteTexture[] SecondaryTexturesBuffer = new SecondarySpriteTexture[64];
        private NativeHashMap<DecalLookup, DecalEntry> _decalVFXMap;
        private NativePriorityHeap<KeyCandidate> _keyCandidates;

        protected override void OnCreate()
        {
            var indices = new NativeArray<KeyCandidate>(ushort.MaxValue / 4, Allocator.Temp);
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = new KeyCandidate { Value = (ushort)(ushort.MaxValue - indices.Length + i) };
            }
            
            _keyCandidates = new NativePriorityHeap<KeyCandidate>(indices, Allocator.Persistent);
            _decalVFXMap = new NativeHashMap<DecalLookup, DecalEntry>(8, Allocator.Persistent);
        }
        
        protected override void OnDestroy()
        {
            foreach (var kvp in _decalVFXMap)
            {
                var ve = kvp.Value.HybridVisualEffect.Value;
                if (ve == null || ve.gameObject == null) continue;
                DestroyObject(ve.gameObject);
            }
            
            _keyCandidates.Dispose();
            _decalVFXMap.Dispose();
        }
        
        protected override void OnUpdate()
        {
            var uninitializedQuery = SystemAPI.QueryBuilder().WithAll<RuntimeDecalLookup>().Build();
            var uninitializedEntities = !BurstUtils.IsEmpty(ref uninitializedQuery);

            var vfxSingleton = SystemAPI.GetSingleton<VFXSingleton>();

            NativeHashSet<DecalLookup> usedLookups = default;
            if (!Application.isPlaying || uninitializedEntities)
            {
                usedLookups = new NativeHashSet<DecalLookup>(8, WorldUpdateAllocator);
                var gatherQuery = Application.isPlaying ? uninitializedQuery : SystemAPI.QueryBuilder().WithPresent<RuntimeDecalLookup>().Build();
                
                Dependency = new GatherDecalLookups
                {
                    DecalLookups = usedLookups
                }.Schedule(gatherQuery, Dependency);
                Dependency.Complete();
            }

            if (_decalVFXMap.Count > 0)
            {
                DestroyUnusedDecalVFX(vfxSingleton, usedLookups);
            }
            if (uninitializedEntities)
            {
                CreateNewDecalVFX(vfxSingleton, usedLookups);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                foreach (var pair in _decalVFXMap)
                {
                    var hybridVisualEffect = pair.Value.HybridVisualEffect.Value;
                    if (hybridVisualEffect.VisualEffect.aliveParticleCount <= 0)
                    {
                        Dependency = new ResetJob
                        {
                            VFXSingleton = vfxSingleton.AsParallelWriter()
                        }.ScheduleParallel(Dependency);
                        break;
                    }
                }
            }
#endif
        }

#if UNITY_EDITOR
        [BurstCompile]
        private partial struct ResetJob : IJobEntity
        {
            public VFXSingleton.ParallelWriter VFXSingleton;
            
            private void Execute(ref DecalProjectorVFX vfx, Entity self)
            {
                if (vfx.TrackedEntity.Equals(TrackedEntity.Null)) return;
                vfx.TryKillDecal(VFXSingleton);
                vfx.TrackedEntity = VFXSingleton.GetPersistent(vfx.Key).Spawn(self);
            }
        }
#endif

        private void CreateNewDecalVFX(VFXSingleton vfxSingleton, NativeHashSet<DecalLookup> usedLookups)
        {
            var keyMap = new NativeHashMap<DecalLookup, VFXKey>(4, WorldUpdateAllocator);
            var initializedAny = false;
            
            foreach (var lookup in usedLookups)
            {
                VFXKey key;
                if (!_decalVFXMap.TryGetValue(lookup, out var value))
                {
                    var textureName = lookup.Sprite != null ? lookup.Sprite.Value.texture.name : "null";
                    var go = new GameObject($"[DECAL_VFX] {lookup.Definition.Value.name}_{textureName}");
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        go.hideFlags = HideFlags.HideAndDontSave | HideFlags.HideInInspector;
                    }
#endif
                    var ve = go.AddComponent<VisualEffect>();
                    var hve = go.AddComponent<HybridVisualEffect>();

                    if (!_keyCandidates.TryDequeue(out var keyCandidate))
                    {
                        throw new InvalidOperationException("Ran out of decal VFX keys.");
                    }
                    
                    var definition = lookup.Definition.Value.CreateDefinition(keyCandidate.Value);
                    hve.VFXDefinition = definition;
                    hve.Init();

                    SetTextures(ve, lookup);

                    _decalVFXMap[lookup] = new DecalEntry 
                    { 
                        Key = definition,
                        HybridVisualEffect = hve
                    };
                    key = definition;
                    initializedAny = true;
                }
                else
                {
                    key = value.Key;
                }
                keyMap.Add(lookup, key);
            }
            
            if (initializedAny)
            {
                World.GetExistingSystemManaged<InitializeVFXSystem>().Update();
            }
            
            Dependency = new InitJob
            {
                KeyMap = keyMap,
                VFXSingleton = vfxSingleton.AsParallelWriter()
            }.Schedule(Dependency);
        }

        private void DestroyUnusedDecalVFX(VFXSingleton vfxSingleton, NativeHashSet<DecalLookup> usedLookups)
        {
            using var toRemove = NativeListPool<DecalLookup>.Rent();
            foreach (var kvPair in _decalVFXMap)
            {
                var lookup = kvPair.Key; 
                ref var entry = ref kvPair.Value;
                
                var visualEffect = entry.HybridVisualEffect;
                if (Application.isPlaying)
                {
                    if (!vfxSingleton.PersistentAliveVFX.ContainsKey(entry.Key))
                    {
                        FreeDecalEntry(visualEffect, toRemove.List, lookup, entry);
                    }
                }
#if UNITY_EDITOR
                else
                {
                    if (visualEffect == null || !usedLookups.Contains(lookup))
                    {
                        FreeDecalEntry(visualEffect, toRemove.List, lookup, entry);
                    }
                }
#endif
            }
            foreach (var key in toRemove.List)
            {
                _decalVFXMap.Remove(key);
            }
        }

        private void FreeDecalEntry(UnityObjectRef<HybridVisualEffect> visualEffect, NativeList<DecalLookup> toRemove,
            DecalLookup lookup, DecalEntry entry)
        {
            if (visualEffect != null)
            {
                DestroyObject(visualEffect.Value.gameObject);
            }

            toRemove.Add(lookup);
            _keyCandidates.Enqueue(new KeyCandidate { Value = entry.Key.Value });
        }

        private static void SetTextures(VisualEffect ve, DecalLookup lookup)
        {
            if (lookup.Sprite == null) return;
            
            ve.SetTexture("BaseMap", lookup.Sprite.Value.texture);
            var count = lookup.Sprite.Value.GetSecondaryTextures(SecondaryTexturesBuffer);
            for (int i = 0; i < count; i++)
            {
                var secondaryTexture = SecondaryTexturesBuffer[i];

                var veName = secondaryTexture.name.TrimStart('_');
                if (ve.HasTexture(veName) && secondaryTexture.texture != null)
                {
                    ve.SetTexture(veName, secondaryTexture.texture);
                }
            }
        }

        [BurstCompile]
        private partial struct GatherDecalLookups : IJobEntity
        {
            public NativeHashSet<DecalLookup> DecalLookups;
            
            private void Execute(in RuntimeDecalLookup lookup)
            {
                DecalLookups.Add(lookup.Value);
            }
        }
        
        [BurstCompile]
        private partial struct InitJob : IJobEntity
        {
            [ReadOnly] public NativeHashMap<DecalLookup, VFXKey> KeyMap;
            public VFXSingleton.ParallelWriter VFXSingleton;
            
            private void Execute(ref RuntimeDecalLookup lookup, ref DecalProjectorVFX vfx,
                EnabledRefRW<RuntimeDecalLookup> enabled, Entity self)
            {
                vfx.TryKillDecal(VFXSingleton);
                vfx.Key = KeyMap[lookup.Value];
                vfx.TrackedEntity = VFXSingleton.GetPersistent(vfx.Key).Spawn(self);
                
                enabled.ValueRW = false;
            }
        }

        private static void DestroyObject(GameObject gameObject)
        {
            if (Application.isPlaying)
                Object.Destroy(gameObject);
#if UNITY_EDITOR
            else
                Object.DestroyImmediate(gameObject);
#endif
        }
    }
}
