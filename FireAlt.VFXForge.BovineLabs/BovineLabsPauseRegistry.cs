#if BL_CORE_EXTENSIONS && !BL_DISABLE_PAUSE
using BovineLabs.Nerve.Pause;
using UnityEngine;

namespace FireAlt.VFXForge.BovineLabs
{
    public static partial class BovineLabsPauseRegistry
    {
        [OnEnteringPlayMode]
        private static void Register()
        {
            PauseUtility.UpdateWhilePaused.Add(typeof(SyncVFXSystem));
        }
    }
}
#endif
