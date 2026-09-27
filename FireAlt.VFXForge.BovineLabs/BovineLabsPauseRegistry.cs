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
            // Needed as in some edge cases, pausing the game will not cause VFXTransformSystem to run
            // which would not set the DidVFXSystemRun flag which would result as an exception in SyncVFXSystem giving a false positive and crashing the game
            PauseUtility.UpdateWhilePaused.Add(typeof(VFXTransformSystem)); 
            PauseUtility.UpdateWhilePaused.Add(typeof(SyncVFXSystem));
        }
    }
}
#endif
