using System.Collections;
using FireAlt.VFXForge.Data;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine.TestTools;

namespace FireAlt.VFXForge.Tests
{
    public class SyncVFXSystemPlayModeTests
    {
        [UnityTest]
        public IEnumerator ForceTimeout_WhenSyncVfxSystemUpdates_DisablesVisualEffect()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(10, VFXType.Instant);
                var hybridVisualEffect = fixture.CreateAndRegisterVisualEffect(definition, "ForceTimeout VFX Test");
                var singleton = fixture.GetSingleton();
                Assert.IsTrue(singleton.ContainsInstant(definition));

                fixture.SpawnInstant(definition);

                fixture.UpdateSystems();
                Assert.IsTrue(hybridVisualEffect.gameObject.activeSelf);

                var visualEffect = hybridVisualEffect.VisualEffect;
                visualEffect.Reinit();
                visualEffect.pause = true;
                Assert.That(visualEffect.aliveParticleCount, Is.LessThanOrEqualTo(0));

                singleton.ForceTimeout(definition);
                Assert.That(singleton.GetInstant(definition).HasPendingRequests, Is.False);
                fixture.UpdateSystems();

                Assert.IsFalse(hybridVisualEffect.gameObject.activeSelf);
            }, "ForceTimeout Test World");
        }

        [UnityTest]
        public IEnumerator SyncPersistent_WhenTransformSystemRunsBeforeSync_ResolvesSpawn()
        {
            yield return VFXPlayModeTestFixture.Run(fixture =>
            {
                var definition = fixture.CreateDefinition(85, VFXType.Persistent, initialCapacity: 2);
                fixture.CreateAndRegisterVisualEffect(definition);
                var trackedEntity = fixture.SpawnPersistent(definition, Entity.Null);

                fixture.VFXTransformSystem.Update(fixture.World.Unmanaged);
                fixture.SyncVFXSystem.Update(fixture.World.Unmanaged);

                Assert.IsTrue(fixture.IsPersistentAlive(definition, trackedEntity));
            });
        }
    }
}
