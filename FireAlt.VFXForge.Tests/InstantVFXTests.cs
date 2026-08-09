using System;
using System.Collections;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools;

namespace FireAlt.VFXForge.Tests
{
    public class InstantVFXTests
    {
        [UnityTest]
        public IEnumerator Spawn_WhenRequestsSubmitted_ArePendingUntilSyncThenReset()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var singleDefinition = fixture.CreateDefinition(35, VFXType.Instant);
                var arrayDefinition = fixture.CreateDefinition(36, VFXType.Instant, hasData: true, hasArrayData: true);
                var singleVisualEffect = fixture.CreateAndRegisterVisualEffect(singleDefinition, "Instant Single VFX");
                var arrayVisualEffect = fixture.CreateAndRegisterVisualEffect(arrayDefinition, "Instant Array VFX");
                var arrayData = VFXTestData.CreateDecalArray(20f);
                var singleton = fixture.GetSingleton();

                ref var singleEntry = ref singleton.GetInstant(singleDefinition);
                Assert.IsTrue(singleEntry.Spawn());
                ref var arrayEntry = ref singleton.GetInstant(arrayDefinition);
                Assert.IsTrue(arrayEntry.Spawn(VFXTestData.CreateDecal(10f), arrayData));

                Assert.IsTrue(singleEntry.HasPendingRequests);
                Assert.That(singleEntry.RequestsCount, Is.EqualTo(1));
                Assert.That(singleEntry.ArrayRequestsCount, Is.EqualTo(0));
                Assert.IsTrue(arrayEntry.HasPendingRequests);
                Assert.That(arrayEntry.RequestsCount, Is.EqualTo(1));
                Assert.That(arrayEntry.ArrayRequestsCount, Is.EqualTo(arrayData.Length));

                fixture.UpdateSystems();

                Assert.IsTrue(singleVisualEffect.gameObject.activeSelf);
                Assert.IsTrue(arrayVisualEffect.gameObject.activeSelf);
                Assert.IsFalse(singleton.GetInstant(singleDefinition).HasPendingRequests);
                Assert.IsFalse(singleton.GetInstant(arrayDefinition).HasPendingRequests);
            });
        }

        [UnityTest]
        public IEnumerator SpawnUnsafe_WhenDataAndArrayRequestsSubmitted_ArePendingUntilSyncThenReset()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(37, VFXType.Instant, hasData: true, hasArrayData: true);
                fixture.CreateAndRegisterVisualEffect(definition, "Instant Unsafe VFX");
                var data = VFXTestData.CreateDecal(70f);
                var arrayData = VFXTestData.CreateDecalBytes(80f);
                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetInstant(definition);

                Assert.IsTrue(SpawnUnsafe(ref entry, data, arrayData));

                Assert.IsTrue(entry.HasPendingRequests);
                Assert.That(entry.RequestsCount, Is.EqualTo(1));
                Assert.That(entry.ArrayRequestsCount, Is.EqualTo(2));

                fixture.UpdateSystems();

                Assert.IsFalse(singleton.GetInstant(definition).HasPendingRequests);
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenDataTypeMismatchesDefinition_Throws()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(38, VFXType.Instant, hasData: true);
                fixture.CreateAndRegisterVisualEffect(definition, "Instant Type Guard VFX");
                var singleton = fixture.GetSingleton();

                Assert.Throws<InvalidOperationException>(() => singleton.GetInstant(definition).Spawn(1));
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenMaxReached_RejectsWithoutPayloadAndResetsAfterSync()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(39, VFXType.Instant, initialCapacity: 0,
                    hasArrayData: true, useMaxCapacity: true, maxCapacity: 1);
                fixture.CreateAndRegisterVisualEffect(definition, "Instant Max VFX");
                var firstArray = VFXTestData.CreateDecalArray(110f);
                var rejectedArray = VFXTestData.CreateDecalArray(120f);
                var singleton = fixture.GetSingleton();
                ref var entry = ref singleton.GetInstant(definition);

                Assert.IsTrue(entry.Spawn(firstArray));
                Assert.IsFalse(entry.Spawn(rejectedArray));
                Assert.That(entry.RequestsCount, Is.EqualTo(1));
                Assert.That(entry.ArrayRequestsCount, Is.EqualTo(firstArray.Length));
                Assert.That(entry.ArrayPtrBuffer.ThreadList.Length, Is.EqualTo(1));
                Assert.That(entry.ArrayDataBuffer.ThreadList.Length,
                    Is.EqualTo(firstArray.Length * entry.ArrayDataSizeInBytes));

                fixture.UpdateSystems();

                Assert.IsTrue(singleton.GetInstant(definition).Spawn(rejectedArray));
            });
        }

        [UnityTest]
        public IEnumerator Spawn_WhenCalledInParallel_EnforcesExactMax()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                const int MAX_CAPACITY = 17;
                var definition = fixture.CreateDefinition(42, VFXType.Instant, initialCapacity: 0,
                    useMaxCapacity: true, maxCapacity: MAX_CAPACITY);
                fixture.CreateAndRegisterVisualEffect(definition, "Instant Parallel Max VFX");
                var singleton = fixture.GetSingleton();

                new ParallelSpawnJob
                {
                    Entry = singleton.AsParallelWriter(),
                    Key = definition,
                }.ScheduleParallel(128, 1, default).Complete();

                Assert.That(singleton.GetInstant(definition).RequestsCount, Is.EqualTo(MAX_CAPACITY));
            });
        }

        private static unsafe bool SpawnUnsafe(ref InstantVFXEntry entry, VFXDecal data, NativeArray<byte> arrayData)
        {
            return entry.SpawnUnsafe((byte*)&data, arrayData);
        }

        private struct ParallelSpawnJob : IJobFor
        {
            public VFXSingleton.ParallelWriter Entry;
            public VFXKey Key;

            public void Execute(int index)
            {
                Entry.GetInstant(Key).Spawn();
            }
        }
    }
}
