using System.Collections;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace FireAlt.VFXForge.Tests
{
    public class PersistentVFXTests
    {
        [UnityTest]
        public IEnumerator Spawn_WhenCapacityAvailable_BecomesAliveAfterSystemsUpdate()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(40, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);

                var trackedEntity = fixture.SpawnPersistent(definition, Entity.Null, trackingDuration: 1.25f);
                fixture.UpdateSystems();

                Assert.IsTrue(trackedEntity.IsValid);
                Assert.IsTrue(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator SpawnWithoutPayload_WhenResolved_PublishesDistinctSpawnIndices()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(41, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var internalApi = singleton.AsInternal();

                var first = internalApi.SpawnPersistent(
                    ref entry, TrackedEntity.FromEntity(Entity.Null), default, 0f);
                var second = internalApi.SpawnPersistent(
                    ref entry, TrackedEntity.FromEntity(Entity.Null), default, 0f);

                Assert.IsTrue(entry.SpawnIndexBuffer.IsCreated);
                Assert.That(entry.SpawnIndexBuffer.Length, Is.EqualTo(2));
                Assert.That(entry.SpawnIndexBuffer[0].IndexInData, Is.EqualTo((uint)first.IndexInData));
                Assert.That(entry.SpawnIndexBuffer[1].IndexInData, Is.EqualTo((uint)second.IndexInData));
                Assert.That(entry.SpawnIndexBuffer[0].IndexInData,
                    Is.Not.EqualTo(entry.SpawnIndexBuffer[1].IndexInData));
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenCapacityExceeded_ReturnsInvalidEntity()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(50, VFXType.Persistent, capacity: 1);
                fixture.CreateAndRegisterVisualEffect(definition);

                var first = fixture.SpawnPersistent(definition, Entity.Null);
                var second = fixture.SpawnPersistent(definition, Entity.Null);

                Assert.IsTrue(first.IsValid);
                Assert.IsFalse(second.IsValid);
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenKilledAfterResolution_CapacityCanBeReused()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(55, VFXType.Persistent, capacity: 1);
                fixture.CreateAndRegisterVisualEffect(definition);
                var first = fixture.SpawnPersistent(definition, Entity.Null);
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.TryKillPersistent(definition, first));
                fixture.UpdateSystems();

                var second = fixture.SpawnPersistent(definition, Entity.Null);
                fixture.UpdateSystems();

                Assert.IsTrue(first.IsValid);
                Assert.IsTrue(second.IsValid);
                Assert.IsFalse(fixture.IsPersistentAlive(definition, first));
                Assert.IsTrue(fixture.IsPersistentAlive(definition, second));
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenTrackingDurationExpires_IsNotAliveAfterSystemsUpdate()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(56, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                fixture.SetTime(1.0, 0.1f);
                var trackedEntity = fixture.SpawnPersistent(definition, Entity.Null, trackingDuration: 0.25f);
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.IsPersistentAlive(definition, trackedEntity));

                fixture.SetTime(1.5, 0.1f);
                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenTrackingDurationNegative_ThrowsAssertion()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(60, VFXType.Persistent, capacity: 1);
                fixture.CreateAndRegisterVisualEffect(definition);

                Assert.Throws<UnityEngine.Assertions.AssertionException>(() =>
                    fixture.SpawnPersistent(definition, Entity.Null, trackingDuration: -0.01f));
            });
        }

        [UnityTest]
        public IEnumerator TrySetUpdateData_WhenDeferredAndResolved_UpdatesVisibleData()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(86, VFXType.Persistent, capacity: 2, hasData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var initialData = VFXTestData.CreateDecal(30f);
                var deferredData = VFXTestData.CreateDecal(40f);
                var resolvedData = VFXTestData.CreateDecal(50f);
                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var trackedEntity = entry.Spawn(Entity.Null, initialData);

                Assert.IsTrue(entry.TrySetUpdateData(trackedEntity, deferredData));
                VFXTestData.AssertDataMatches(ref entry, trackedEntity, deferredData);
                fixture.UpdateSystems();

                VFXTestData.AssertDataMatches(ref entry, trackedEntity, deferredData);
                Assert.IsTrue(entry.TrySetUpdateData(trackedEntity, resolvedData));
                VFXTestData.AssertDataMatches(ref entry, trackedEntity, resolvedData);
            });
        }

        [UnityTest]
        public IEnumerator TryGetArrayData_WhenDeferredAndResolved_ReturnsCopiedData()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(87, VFXType.Persistent, capacity: 2, hasArrayData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var arrayData = VFXTestData.CreateDecalArray(60f);
                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var trackedEntity = entry.Spawn(Entity.Null, arrayData);

                VFXTestData.AssertArrayDataMatches(ref entry, trackedEntity, arrayData);
                fixture.UpdateSystems();

                VFXTestData.AssertArrayDataMatches(ref entry, trackedEntity, arrayData);
            });
        }

        [UnityTest]
        public IEnumerator TrySetUpdateData_WhenTrackedEntityKilled_ReturnsFalse()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(88, VFXType.Persistent, capacity: 2, hasData: true);
                fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetPersistent(definition);
                var trackedEntity = entry.Spawn(Entity.Null, VFXTestData.CreateDecal(90f));
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.TryKillPersistent(definition, trackedEntity));
                fixture.UpdateSystems();

                Assert.IsFalse(entry.TrySetUpdateData(trackedEntity, VFXTestData.CreateDecal(100f)));
                Assert.IsFalse(entry.TryGetUpdateDataAsRef<VFXDecal>(trackedEntity, out _));
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenResolvedTrackedEntityDestroyed_IsNotAliveAfterSystemsUpdate()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(89, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                var entity = fixture.CreateTrackedEntity();
                var trackedEntity = fixture.SpawnPersistent(definition, entity);
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.IsPersistentAlive(definition, trackedEntity));

                fixture.World.EntityManager.DestroyEntity(entity);
                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator TryKill_WhenResolvedEntityKilled_IsNotAliveAfterSystemsUpdate()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(70, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);

                var trackedEntity = fixture.SpawnPersistent(definition, Entity.Null);
                fixture.UpdateSystems();

                Assert.IsTrue(fixture.IsPersistentAlive(definition, trackedEntity));
                Assert.IsTrue(fixture.TryKillPersistent(definition, trackedEntity));

                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }

        [UnityTest]
        public IEnumerator TryKill_WhenDeferredEntityKilled_DoesNotBecomeAliveAfterSystemsUpdate()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(80, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);

                var trackedEntity = fixture.SpawnPersistent(definition, Entity.Null);

                Assert.IsTrue(fixture.TryKillPersistent(definition, trackedEntity));
                fixture.UpdateSystems();

                Assert.IsFalse(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }
    }
}
