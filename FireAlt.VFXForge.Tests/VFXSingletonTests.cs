using System;
using System.Collections;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FireAlt.VFXForge.Tests
{
    public class VFXSingletonTests
    {
        [UnityTest]
        public IEnumerator RuntimeInitialization_CreatesValidSingleton()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var singleton = fixture.GetSingleton();

                Assert.IsTrue(singleton.IsValid());
            });
        }

        [UnityTest]
        public IEnumerator GetInstant_WhenKeyMissing_ThrowsAssertion()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var singleton = fixture.GetSingleton();

                Assert.Throws<ArgumentException>(() => singleton.GetInstant((VFXKey)100));
            });
        }

        [UnityTest]
        public IEnumerator GetInstant_WhenRegisteredThroughRuntimePath_DoesNotThrow()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(10, VFXType.Instant);
                fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();

                Assert.IsTrue(singleton.ContainsInstant(definition));
                Assert.DoesNotThrow(() =>
                {
                    ref var instant = ref singleton.GetInstant(definition);
                    instant.ResetRequestsCount();
                });
            });
        }

        [UnityTest]
        public IEnumerator GetPersistent_WhenKeyMissing_ThrowsAssertion()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var singleton = fixture.GetSingleton();

                Assert.Throws<ArgumentException>(() => singleton.GetPersistent((VFXKey)100));
            });
        }

        [UnityTest]
        public IEnumerator GetPersistent_WhenRegisteredThroughRuntimePath_DoesNotThrow()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(20, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();

                Assert.IsTrue(singleton.ContainsPersistent(definition));
                Assert.DoesNotThrow(() =>
                {
                    ref var persistent = ref singleton.GetPersistent(definition);
                    Assert.IsFalse(persistent.HasPendingRequests);
                });
            });
        }

        [UnityTest]
        public IEnumerator CleanupBeforeSync_WhenGraphicsBuffersWereNotCreated_RemovesRegistration()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(21, VFXType.Instant);
                var hybridVisualEffect = fixture.CreateAndRegisterVisualEffect(definition);

                hybridVisualEffect.Cleanup();

                Assert.DoesNotThrow(() => fixture.CleanupVFXSystem.Update());
                Assert.IsFalse(fixture.GetSingleton().ContainsInstant(definition));
                Assert.DoesNotThrow(() => fixture.CleanupVFXSystem.Update());
            });
        }

        [UnityTest]
        public IEnumerator Cleanup_WhenRegistrationIsPartiallyRemoved_RepairsRemainingState()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(23, VFXType.Instant);
                var hybridVisualEffect = fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();

                singleton.GetInstant(definition).Dispose();
                singleton.InstantVFXGraphEntries.Remove(definition);
                Assert.IsTrue(singleton.IsPersistent.ContainsKey(definition));

                hybridVisualEffect.Cleanup();

                Assert.DoesNotThrow(() => fixture.CleanupVFXSystem.Update());
                Assert.IsFalse(singleton.IsPersistent.ContainsKey(definition));
            });
        }

        [UnityTest]
        public IEnumerator Cleanup_WhenManagedGraphicsStateWasLost_RemovesRegistration()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(24, VFXType.Instant);
                var hybridVisualEffect = fixture.CreateAndRegisterVisualEffect(definition);
                var graphicsBuffersQuery = fixture.World.EntityManager.CreateEntityQuery(typeof(VFXGraphicsBuffersSingleton));
                var graphicsBuffersObject = graphicsBuffersQuery.GetSingleton<VFXGraphicsBuffersSingleton>().Value.Value;
                graphicsBuffersObject.InstantVFXGraphEntries = null;
                graphicsBuffersObject.PersistentVFXGraphEntries = null;

                hybridVisualEffect.Cleanup();

                Assert.DoesNotThrow(() => fixture.CleanupVFXSystem.Update());
                Assert.IsFalse(fixture.GetSingleton().ContainsInstant(definition));
                Assert.IsNotNull(graphicsBuffersObject.InstantVFXGraphEntries);
                Assert.IsNotNull(graphicsBuffersObject.PersistentVFXGraphEntries);
            });
        }

        [UnityTest]
        public IEnumerator DuplicateRegistration_WhenInitializationRunsAgain_LogsOnlyOnce()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(22, VFXType.Instant);
                fixture.CreateAndRegisterVisualEffect(definition, "Original VFX");

                LogAssert.Expect(LogType.Error,
                    "VFXKey(22) was already added to the VFX system. There cannot be duplicates.");
                fixture.CreateAndRegisterVisualEffect(definition, "Duplicate VFX");

                Assert.DoesNotThrow(() => fixture.World.GetExistingSystemManaged<InitializeVFXSystem>().Update());
                LogAssert.NoUnexpectedReceived();
            });
        }

        [UnityTest]
        public IEnumerator ParallelWriterGetInstant_WhenRegisteredThroughRuntimePath_DoesNotThrow()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(30, VFXType.Instant);
                fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();

                Assert.DoesNotThrow(() =>
                {
                    ref var instant = ref singleton.AsParallelWriter().GetInstant(definition);
                    Assert.IsFalse(instant.HasPendingRequests);
                });
            });
        }

        [UnityTest]
        public IEnumerator ParallelWriterGetPersistent_WhenRegisteredThroughRuntimePath_DoesNotThrow()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(31, VFXType.Persistent, capacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                var singleton = fixture.GetSingleton();

                Assert.DoesNotThrow(() =>
                {
                    ref var persistent = ref singleton.AsParallelWriter().GetPersistent(definition);
                    Assert.IsFalse(persistent.HasPendingRequests);
                });
            });
        }

        [UnityTest]
        public IEnumerator ParallelWriterGetPersistent_WhenKeyMissing_ThrowsAssertion()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var singleton = fixture.GetSingleton();
                var writer = singleton.AsParallelWriter();

                Assert.Throws<ArgumentException>(() => writer.GetPersistent((VFXKey)200));
            });
        }
    }
}
