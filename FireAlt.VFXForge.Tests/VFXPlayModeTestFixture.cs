using System;
using System.Collections;
using System.Collections.Generic;
using FireAlt.Core.ObjectManagement;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using Unity.Core;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.VFX;

namespace FireAlt.VFXForge.Tests
{
    internal sealed class VFXPlayModeTestFixture : IDisposable
    {
        private const string INSTANT_VFX_ASSET_PATH = "Packages/com.firealt.vfx-forge/Shaders/Templates/Instant(Single).vfx";
        private const string INSTANT_ARRAY_VFX_ASSET_PATH = "Packages/com.firealt.vfx-forge/Shaders/Templates/Instant(Array).vfx";
        private const string INSTANT_SINGLE_ARRAY_VFX_ASSET_PATH =
            "Packages/com.firealt.vfx-forge/Shaders/Templates/Instant(Single+Array).vfx";
        private const string PERSISTENT_VFX_ASSET_PATH = "Packages/com.firealt.vfx-forge/Shaders/Templates/Persistent(Single).vfx";
        private const string PERSISTENT_ARRAY_VFX_ASSET_PATH = "Packages/com.firealt.vfx-forge/Shaders/Templates/Persistent(Array).vfx";
        private const string PERSISTENT_SINGLE_ARRAY_VFX_ASSET_PATH =
            "Packages/com.firealt.vfx-forge/Shaders/Templates/Persistent(Single+Array).vfx";

        private readonly List<GameObject> _gameObjects = new();
        private readonly List<VFXDefinition> _definitions = new();
        private readonly World _previousWorld;

        private VFXPlayModeTestFixture(string worldName)
        {
            _previousWorld = World.DefaultGameObjectInjectionWorld;
            World = World.DefaultGameObjectInjectionWorld = new World(worldName, WorldFlags.Game);
            SyncVFXSystem = World.CreateSystem<SyncVFXSystem>();
            VFXTransformSystem = World.CreateSystem<VFXTransformSystem>();
            World.CreateSystemManaged<InitializeVFXSystem>();
            CleanupVFXSystem = World.CreateSystemManaged<CleanupVFXSystem>();
        }

        internal World World { get; }

        internal SystemHandle SyncVFXSystem { get; }

        internal SystemHandle VFXTransformSystem { get; }

        internal CleanupVFXSystem CleanupVFXSystem { get; }

        internal static IEnumerator Run(Action<VFXPlayModeTestFixture> test, string worldName = "VFX Forge Test World")
        {
            using (var fixture = new VFXPlayModeTestFixture(worldName))
            {
                test(fixture);
            }

            yield return null;
        }

        internal VFXDefinition CreateDefinition(
            int id,
            VFXType vfxType,
            int capacity = 100,
            float timeoutDuration = 5f,
            bool hasData = false,
            bool hasArrayData = false)
        {
            var definition = ScriptableObject.CreateInstance<VFXDefinition>();
            ((IUID)definition).ID = id;
#if UNITY_EDITOR
            definition.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(
                GetVFXAssetPath(vfxType, hasData, hasArrayData));
#else
            throw new InvalidOperationException("VFX Forge playmode tests require the Unity Editor to load package VFX assets.");
#endif
            definition.capacity = capacity;
            definition.timeoutDuration = timeoutDuration;
            definition.vfxType = vfxType;
            definition.vfxDataType = hasData ? VFXTypeRegistry.GetStableTypeHash<VFXDecal>() : 0;
            definition.vfxArrayDataType = hasArrayData ? VFXTypeRegistry.GetStableTypeHash<VFXDecal>() : 0;
            _definitions.Add(definition);
            return definition;
        }

        internal HybridVisualEffect CreateAndRegisterVisualEffect(VFXDefinition definition, string name = "VFX Forge Test")
        {
            var gameObject = new GameObject(name);
            _gameObjects.Add(gameObject);

            gameObject.AddComponent<VisualEffect>();
            var hybridVisualEffect = gameObject.AddComponent<HybridVisualEffect>();
            hybridVisualEffect.VFXDefinition = definition;
            hybridVisualEffect.Init();
            return hybridVisualEffect;
        }

        internal GameObject CreateTrackedGameObject(string name = "VFX Forge Tracked GameObject")
        {
            var gameObject = new GameObject(name);
            _gameObjects.Add(gameObject);
            return gameObject;
        }

        internal Entity CreateTrackedEntity()
        {
            var entity = World.EntityManager.CreateEntity(typeof(LocalToWorld));
            World.EntityManager.SetComponentData(entity, new LocalToWorld { Value = float4x4.TRS(float3.zero, quaternion.identity, 1f) });
            return entity;
        }

        internal VFXSingleton GetSingleton()
        {
            var query = World.EntityManager.CreateEntityQuery(typeof(VFXSingleton));
            return query.GetSingleton<VFXSingleton>();
        }

        internal void UpdateSystems()
        {
            VFXTransformSystem.Update(World.Unmanaged);
            SyncVFXSystem.Update(World.Unmanaged);
            World.EntityManager.CompleteAllTrackedJobs();
        }

        internal void SetTime(double elapsedTime, float deltaTime)
        {
            World.SetTime(new TimeData(elapsedTime, deltaTime));
        }

        internal void SpawnInstant(VFXDefinition definition)
        {
            var singleton = GetSingleton();
            ref var entry = ref singleton.GetInstant(definition);
            entry.Spawn();
        }

        internal TrackedEntity SpawnPersistent(VFXDefinition definition, Entity entityToTrack, float trackingDuration = 0f)
        {
            var singleton = GetSingleton();
            ref var entry = ref singleton.GetPersistent(definition);
            return entry.Spawn(entityToTrack, trackingDuration);
        }

        internal TrackedEntity SpawnPersistent(VFXDefinition definition, EntityId entityIdToTrack, float trackingDuration = 0f)
        {
            var singleton = GetSingleton();
            ref var entry = ref singleton.GetPersistent(definition);
            return entry.Spawn(entityIdToTrack, trackingDuration);
        }

        internal bool IsPersistentAlive(VFXDefinition definition, TrackedEntity trackedEntity)
        {
            var singleton = GetSingleton();
            ref var entry = ref singleton.GetPersistent(definition);
            return entry.IsAlive(trackedEntity);
        }

        internal bool TryKillPersistent(VFXDefinition definition, TrackedEntity trackedEntity)
        {
            var singleton = GetSingleton();
            ref var entry = ref singleton.GetPersistent(definition);
            return entry.TryKill(trackedEntity);
        }

        public void Dispose()
        {
            for (var i = _gameObjects.Count - 1; i >= 0; i--)
            {
                if (_gameObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_gameObjects[i]);
                }
            }

            for (var i = _definitions.Count - 1; i >= 0; i--)
            {
                if (_definitions[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_definitions[i]);
                }
            }

            if (World.IsCreated)
            {
                World.EntityManager.CompleteAllTrackedJobs();
                World.DestroyAllSystemsAndLogException(out _);
                World.Dispose();
            }

            World.DefaultGameObjectInjectionWorld = _previousWorld;
        }

        private static string GetVFXAssetPath(VFXType vfxType, bool hasData, bool hasArrayData)
        {
            if (vfxType != VFXType.Persistent)
            {
                if (hasData && hasArrayData)
                {
                    return INSTANT_SINGLE_ARRAY_VFX_ASSET_PATH;
                }

                return hasArrayData ? INSTANT_ARRAY_VFX_ASSET_PATH : INSTANT_VFX_ASSET_PATH;
            }

            if (hasData && hasArrayData)
            {
                return PERSISTENT_SINGLE_ARRAY_VFX_ASSET_PATH;
            }

            return hasArrayData ? PERSISTENT_ARRAY_VFX_ASSET_PATH : PERSISTENT_VFX_ASSET_PATH;
        }
    }

    internal static class VFXTestData
    {
        internal static VFXDecal CreateDecal(float value)
        {
            return new VFXDecal
            {
                Size = new Vector3(value, value + 1f, value + 2f),
                UvAtlas = new Vector4(value + 3f, value + 4f, value + 5f, value + 6f),
                Pivot = new Vector3(value + 7f, value + 8f, value + 9f),
                Opacity = value + 10f,
                DrawDistance = value + 11f,
                StartFade = value + 12f,
                AngleFade = new Vector2(value + 13f, value + 14f),
                NormalBlend = value + 15f
            };
        }

        internal static NativeArray<VFXDecal> CreateDecalArray(float value)
        {
            var array = new NativeArray<VFXDecal>(2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            array[0] = CreateDecal(value);
            array[1] = CreateDecal(value + 100f);
            return array;
        }

        internal static unsafe NativeArray<byte> CreateDecalBytes(float value)
        {
            var source = CreateDecalArray(value);
            var size = source.Length * UnsafeUtility.SizeOf<VFXDecal>();
            var bytes = new NativeArray<byte>(size, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpy(bytes.GetUnsafePtr(), source.GetUnsafeReadOnlyPtr(), size);
            return bytes;
        }

        internal static void AssertDataMatches(ref PersistentVFXEntry entry, TrackedEntity trackedEntity, VFXDecal expected)
        {
            Assert.IsTrue(entry.TryGetUpdateDataAsRef<VFXDecal>(trackedEntity, out var dataRef));
            AssertDecalMatches(expected, dataRef.Value);
        }

        internal static void AssertArrayDataMatches(
            ref PersistentVFXEntry entry,
            TrackedEntity trackedEntity,
            NativeArray<VFXDecal> expected)
        {
            Assert.IsTrue(entry.TryGetArrayData<VFXDecal>(trackedEntity, out var array));
            Assert.That(array.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
            {
                AssertDecalMatches(expected[i], array[i]);
            }
        }

        internal static void AssertUnsafeArrayDataMatches(
            ref PersistentVFXEntry entry,
            TrackedEntity trackedEntity,
            NativeArray<byte> expected)
        {
            Assert.IsTrue(entry.TryGetArrayDataUnsafe(trackedEntity, out var array));
            Assert.That(array.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(array[i], Is.EqualTo(expected[i]));
            }
        }

        private static void AssertDecalMatches(VFXDecal expected, VFXDecal actual)
        {
            Assert.That(actual.Size, Is.EqualTo(expected.Size));
            Assert.That(actual.UvAtlas, Is.EqualTo(expected.UvAtlas));
            Assert.That(actual.Pivot, Is.EqualTo(expected.Pivot));
            Assert.That(actual.Opacity, Is.EqualTo(expected.Opacity));
            Assert.That(actual.DrawDistance, Is.EqualTo(expected.DrawDistance));
            Assert.That(actual.StartFade, Is.EqualTo(expected.StartFade));
            Assert.That(actual.AngleFade, Is.EqualTo(expected.AngleFade));
            Assert.That(actual.NormalBlend, Is.EqualTo(expected.NormalBlend));
        }
    }
}
