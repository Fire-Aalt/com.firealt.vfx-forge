using System.Collections;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace FireAlt.VFXForge.Tests
{
    public class PersistentVFXGameObjectPathTests
    {
        [UnityTest]
        public IEnumerator SpawnGameObject_WhenCapacityAvailable_BecomesAliveLikeEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(100, VFXType.Persistent, initialCapacity: 0,
                    useMaxCapacity: true, maxCapacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var expectedPosition = new Vector3(-3f, 1.25f, -8.5f);
                gameObject.transform.position = expectedPosition;
                var entity = fixture.CreateTrackedEntity();

                var gameObjectTrackedEntity = fixture.SpawnPersistent(definition, gameObject.GetEntityId(), trackingDuration: 1.25f);
                var entityTrackedEntity = fixture.SpawnPersistent(definition, entity, trackingDuration: 1.25f);
                fixture.UpdateSystems();

                Assert.IsTrue(gameObjectTrackedEntity.IsValid);
                Assert.IsTrue(entityTrackedEntity.IsValid);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, entityTrackedEntity));

                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                Assert.That(entry.TransformBuffer[gameObjectTrackedEntity.IndexInData].Position,
                    Is.EqualTo(expectedPosition));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObjects_WhenMultipleTrackedUnderSameKey_AllRemainAlive()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(110, VFXType.Persistent, initialCapacity: 3);
                fixture.CreateAndRegisterVisualEffect(definition);
                var first = fixture.CreateTrackedGameObject("First Tracked GameObject");
                var second = fixture.CreateTrackedGameObject("Second Tracked GameObject");

                var firstTrackedEntity = fixture.SpawnPersistent(definition, first.GetEntityId());
                var secondTrackedEntity = fixture.SpawnPersistent(definition, second.GetEntityId());
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.IsPersistentAlive(definition, firstTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, secondTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObjects_WhenMultipleKeys_AllRemainAlive()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var firstDefinition = fixture.CreateDefinition(120, VFXType.Persistent, initialCapacity: 2);
                var secondDefinition = fixture.CreateDefinition(121, VFXType.Persistent, initialCapacity: 2);
                fixture.CreateAndRegisterVisualEffect(firstDefinition, "First Key VFX");
                fixture.CreateAndRegisterVisualEffect(secondDefinition, "Second Key VFX");
                var first = fixture.CreateTrackedGameObject("First Key Tracked GameObject");
                var second = fixture.CreateTrackedGameObject("Second Key Tracked GameObject");

                var firstTrackedEntity = fixture.SpawnPersistent(firstDefinition, first.GetEntityId());
                var secondTrackedEntity = fixture.SpawnPersistent(secondDefinition, second.GetEntityId());
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.IsPersistentAlive(firstDefinition, firstTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(secondDefinition, secondTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObject_WhenCapacityExceeded_ReturnsInvalidLikeEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var gameObjectDefinition = fixture.CreateDefinition(130, VFXType.Persistent, initialCapacity: 1,
                    useMaxCapacity: true, maxCapacity: 1);
                var entityDefinition = fixture.CreateDefinition(131, VFXType.Persistent, initialCapacity: 1,
                    useMaxCapacity: true, maxCapacity: 1);
                fixture.CreateAndRegisterVisualEffect(gameObjectDefinition, "GameObject Capacity VFX");
                fixture.CreateAndRegisterVisualEffect(entityDefinition, "Entity Capacity VFX");
                var firstGameObject = fixture.CreateTrackedGameObject("First Capacity GameObject");
                var secondGameObject = fixture.CreateTrackedGameObject("Second Capacity GameObject");
                var firstEntity = fixture.CreateTrackedEntity();
                var secondEntity = fixture.CreateTrackedEntity();

                var firstGameObjectTrackedEntity = fixture.SpawnPersistent(gameObjectDefinition, firstGameObject.GetEntityId());
                var secondGameObjectTrackedEntity = fixture.SpawnPersistent(gameObjectDefinition, secondGameObject.GetEntityId());
                var firstEntityTrackedEntity = fixture.SpawnPersistent(entityDefinition, firstEntity);
                var secondEntityTrackedEntity = fixture.SpawnPersistent(entityDefinition, secondEntity);

                Assert.IsTrue(firstGameObjectTrackedEntity.IsValid);
                Assert.IsFalse(secondGameObjectTrackedEntity.IsValid);
                Assert.IsTrue(firstEntityTrackedEntity.IsValid);
                Assert.IsFalse(secondEntityTrackedEntity.IsValid);
            });
        }

        [UnityTest]
        public IEnumerator TryKillGameObjectPersistent_WhenResolved_KillsLikeEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(140, VFXType.Persistent, initialCapacity: 4);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();

                var gameObjectTrackedEntity = fixture.SpawnPersistent(definition, gameObject.GetEntityId());
                var entityTrackedEntity = fixture.SpawnPersistent(definition, entity);
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.TryKillPersistent(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.TryKillPersistent(definition, entityTrackedEntity));
                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsFalse(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator TryKillGameObjectPersistent_WhenDeferred_DoesNotBecomeAliveLikeEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(150, VFXType.Persistent, initialCapacity: 4);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();

                var gameObjectTrackedEntity = fixture.SpawnPersistent(definition, gameObject.GetEntityId());
                var entityTrackedEntity = fixture.SpawnPersistent(definition, entity);

                Assert.IsTrue(fixture.TryKillPersistent(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.TryKillPersistent(definition, entityTrackedEntity));
                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsFalse(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObject_WhenTrackedObjectDestroyedBeforeSync_DoesNotThrowAndDoesNotBecomeAliveLikeEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(160, VFXType.Persistent, initialCapacity: 4);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();

                var gameObjectTrackedEntity = fixture.SpawnPersistent(definition, gameObject.GetEntityId());
                var entityTrackedEntity = fixture.SpawnPersistent(definition, entity);
                Object.DestroyImmediate(gameObject);
                fixture.World.EntityManager.DestroyEntity(entity);

                Assert.DoesNotThrow(fixture.UpdateSystems);
                Assert.IsFalse(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsFalse(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObject_WhenResolvedTrackedObjectDestroyed_IsNotAliveAfterSystemsUpdate()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(165, VFXType.Persistent, initialCapacity: 4);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var trackedEntity = fixture.SpawnPersistent(definition, gameObject.GetEntityId());
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.IsPersistentAlive(definition, trackedEntity));

                Object.DestroyImmediate(gameObject);
                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObject_WithData_MatchesEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(170, VFXType.Persistent, initialCapacity: 4, hasData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();
                var gameObjectData = VFXTestData.CreateDecal(1f);
                var entityData = VFXTestData.CreateDecal(2f);

                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var gameObjectTrackedEntity = entry.Spawn(gameObject.GetEntityId(), gameObjectData);
                var entityTrackedEntity = entry.Spawn(entity, entityData);
                fixture.UpdateSystems();

                VFXTestData.AssertDataMatches(ref entry, gameObjectTrackedEntity, gameObjectData);
                VFXTestData.AssertDataMatches(ref entry, entityTrackedEntity, entityData);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObject_WithArrayData_MatchesEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(180, VFXType.Persistent, initialCapacity: 4, hasArrayData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();
                var gameObjectArrayData = VFXTestData.CreateDecalArray(3f);
                var entityArrayData = VFXTestData.CreateDecalArray(4f);

                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var gameObjectTrackedEntity = entry.Spawn(gameObject.GetEntityId(), gameObjectArrayData);
                var entityTrackedEntity = entry.Spawn(entity, entityArrayData);
                fixture.UpdateSystems();

                VFXTestData.AssertArrayDataMatches(ref entry, gameObjectTrackedEntity, gameObjectArrayData);
                VFXTestData.AssertArrayDataMatches(ref entry, entityTrackedEntity, entityArrayData);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnGameObject_WithDataAndArrayData_MatchesEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(190, VFXType.Persistent, initialCapacity: 4, hasData: true, hasArrayData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();
                var gameObjectData = VFXTestData.CreateDecal(5f);
                var entityData = VFXTestData.CreateDecal(6f);
                var gameObjectArrayData = VFXTestData.CreateDecalArray(7f);
                var entityArrayData = VFXTestData.CreateDecalArray(8f);

                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var gameObjectTrackedEntity = entry.Spawn(gameObject.GetEntityId(), gameObjectData, gameObjectArrayData);
                var entityTrackedEntity = entry.Spawn(entity, entityData, entityArrayData);
                fixture.UpdateSystems();

                VFXTestData.AssertDataMatches(ref entry, gameObjectTrackedEntity, gameObjectData);
                VFXTestData.AssertDataMatches(ref entry, entityTrackedEntity, entityData);
                VFXTestData.AssertArrayDataMatches(ref entry, gameObjectTrackedEntity, gameObjectArrayData);
                VFXTestData.AssertArrayDataMatches(ref entry, entityTrackedEntity, entityArrayData);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnUnsafeGameObject_WithDataAndArrayData_MatchesEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(200, VFXType.Persistent, initialCapacity: 4, hasData: true, hasArrayData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();
                var gameObjectData = VFXTestData.CreateDecal(9f);
                var entityData = VFXTestData.CreateDecal(10f);
                var gameObjectArrayData = VFXTestData.CreateDecalBytes(11f);
                var entityArrayData = VFXTestData.CreateDecalBytes(12f);

                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var gameObjectTrackedEntity = SpawnUnsafe(ref entry, gameObject.GetEntityId(), gameObjectData, gameObjectArrayData);
                var entityTrackedEntity = SpawnUnsafe(ref entry, entity, entityData, entityArrayData);
                fixture.UpdateSystems();

                VFXTestData.AssertDataMatches(ref entry, gameObjectTrackedEntity, gameObjectData);
                VFXTestData.AssertDataMatches(ref entry, entityTrackedEntity, entityData);
                VFXTestData.AssertUnsafeArrayDataMatches(ref entry, gameObjectTrackedEntity, gameObjectArrayData);
                VFXTestData.AssertUnsafeArrayDataMatches(ref entry, entityTrackedEntity, entityArrayData);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnUnsafeGameObject_WithArrayData_MatchesEntityPath()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(210, VFXType.Persistent, initialCapacity: 4, hasArrayData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var gameObject = fixture.CreateTrackedGameObject();
                var entity = fixture.CreateTrackedEntity();
                var gameObjectArrayData = VFXTestData.CreateDecalBytes(13f);
                var entityArrayData = VFXTestData.CreateDecalBytes(14f);

                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var gameObjectTrackedEntity = entry.SpawnUnsafe(gameObject.GetEntityId(), gameObjectArrayData);
                var entityTrackedEntity = entry.SpawnUnsafe(entity, entityArrayData);
                fixture.UpdateSystems();

                VFXTestData.AssertUnsafeArrayDataMatches(ref entry, gameObjectTrackedEntity, gameObjectArrayData);
                VFXTestData.AssertUnsafeArrayDataMatches(ref entry, entityTrackedEntity, entityArrayData);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, gameObjectTrackedEntity));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, entityTrackedEntity));
            });
        }

        private static unsafe TrackedEntity SpawnUnsafe(
            ref PersistentVFXEntry entry,
            EntityId entityId,
            VFXDecal data,
            NativeArray<byte> arrayData)
        {
            return entry.SpawnUnsafe(entityId, (byte*)&data, arrayData);
        }

        private static unsafe TrackedEntity SpawnUnsafe(
            ref PersistentVFXEntry entry,
            Entity entity,
            VFXDecal data,
            NativeArray<byte> arrayData)
        {
            return entry.SpawnUnsafe(entity, (byte*)&data, arrayData);
        }
    }
}
