#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FireAlt.VFXForge.Tests
{
    public class HybridVisualEffectEditorTests
    {
        [Test]
        public void DelayedRespawnIgnoresDestroyedOwner()
        {
            var owner = new GameObject("VFX callback test");
            owner.SetActive(false);
            var effect = owner.AddComponent<HybridVisualEffect>();
            var callback = typeof(HybridVisualEffect).GetMethod("CheckEditorRespawn",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Object.DestroyImmediate(owner);

            Assert.DoesNotThrow(() => callback.Invoke(effect, null));
        }

        [Test]
        public void DisableCancelsPendingRespawn()
        {
            var owner = new GameObject("VFX callback test");
            owner.SetActive(false);
            var effect = owner.AddComponent<HybridVisualEffect>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var callback = (EditorApplication.CallbackFunction)System.Delegate.CreateDelegate(
                typeof(EditorApplication.CallbackFunction), effect,
                typeof(HybridVisualEffect).GetMethod("CheckEditorRespawn", flags));
            try
            {
                typeof(HybridVisualEffect).GetMethod("DelayEditorRespawn", flags).Invoke(effect, null);
                Assert.That(EditorApplication.delayCall.GetInvocationList(), Has.Member(callback));

                typeof(HybridVisualEffect).GetMethod("OnDisable", flags).Invoke(effect, null);
                Assert.That(EditorApplication.delayCall?.GetInvocationList() ?? System.Array.Empty<System.Delegate>(),
                    Has.No.Member(callback));
            }
            finally
            {
                EditorApplication.delayCall -= callback;
                Object.DestroyImmediate(owner);
            }
        }
    }
}
#endif
