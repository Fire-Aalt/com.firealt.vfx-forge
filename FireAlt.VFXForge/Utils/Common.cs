using System;
using System.Diagnostics;
using System.Threading;
using JetBrains.Annotations;
using FireAlt.VFXForge.Data;
using UnityEngine;
using UnityEngine.VFX;

namespace FireAlt.VFXForge
{
    public static class Common
    {
        internal static bool TryReserveCapacity(bool useMaxCapacity, int maxCapacity, int usedCapacity, ref int reservations)
        {
            if (!useMaxCapacity)
            {
                return true;
            }

            var availableCapacity = Math.Max(maxCapacity - usedCapacity, 0);
            while (true)
            {
                var current = Volatile.Read(ref reservations);
                if (current >= availableCapacity)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref reservations, current + 1, current) == current)
                {
                    return true;
                }
            }
        }

        public static void TrySetInt(VisualEffect visualEffect, ShaderProperty intProperty, int value)
        {
            if (visualEffect.HasInt(intProperty.Id))
            {
                visualEffect.SetInt(intProperty.Id, value);
            }
        }
        
        public static void SetGraphicsBuffer(VisualEffect visualEffect, ShaderProperty bufferProperty, GraphicsBuffer graphicsBuffer)
        {
            CheckHasBuffer(visualEffect, bufferProperty);
            visualEffect.SetGraphicsBuffer(bufferProperty.Id, graphicsBuffer);
        }
                
        [AssertionMethod]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [Conditional("UNITY_DOTS_DEBUG")]
        public static void CheckHasSpawnRequestsInt(VisualEffect visualEffect, int dataSize, int arrayDataSize)
        {
            if (dataSize == 0 && arrayDataSize == 0) return;
            var hasSpawnRequestsCount = visualEffect.HasInt(VFXProperties.SpawnRequestsCount.Id);
            
            if (arrayDataSize > 0 && !visualEffect.HasInt(VFXProperties.SpawnArrayRequestsCount.Id) && !hasSpawnRequestsCount)
            {
                throw new Exception($"No `int` found on {visualEffect.visualEffectAsset} with name: `{VFXProperties.SpawnArrayRequestsCount.Name}` or `{VFXProperties.SpawnRequestsCount.Name}`. Add one or both `int` and link the needed one to the `PeriodicBurst` node.");
            }
            if (arrayDataSize == 0 && !hasSpawnRequestsCount)
            {
                throw new Exception($"No `int` found on {visualEffect.visualEffectAsset} with name: `{VFXProperties.SpawnRequestsCount.Name}`. Add the required `int` and link it to the `PeriodicBurst` node.");
            }
        }
        
        [AssertionMethod]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [Conditional("UNITY_DOTS_DEBUG")]
        public static void CheckHasBuffer(VisualEffect visualEffect, ShaderProperty bufferProperty)
        {
            if (!visualEffect.HasGraphicsBuffer(bufferProperty.Id))
            {
                throw new Exception($"No `GraphicsBuffer` with name `{bufferProperty.Name}` found on {visualEffect.visualEffectAsset}. Add the required `{bufferProperty.Name}` `GraphicsBuffer`.");
            }
        }
        
        [AssertionMethod]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [Conditional("UNITY_DOTS_DEBUG")]
        public static void CheckStableTypeHash<T>(ulong stableTypeHash) where T : unmanaged
        {
            if (VFXTypeRegistry.GetStableTypeHash<T>() != stableTypeHash) 
                throw new InvalidOperationException($"Incorrect type was used: {new VFXTypeRegistry.TypeToIndex<T>()}");
        }
        
        [AssertionMethod]
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        [Conditional("UNITY_DOTS_DEBUG")]
        public static void CheckZeroSized(ulong stableTypeHash)
        {
            if (stableTypeHash == 0)
            {
                return;
            }
            
            var typeInfo = VFXTypeRegistry.GetTypeInfo(stableTypeHash);
            if (typeInfo.GpuSize != 0)
            {
                throw new InvalidOperationException($"Type used was not zero sized: {typeInfo}");
            }
        }
    }
}
