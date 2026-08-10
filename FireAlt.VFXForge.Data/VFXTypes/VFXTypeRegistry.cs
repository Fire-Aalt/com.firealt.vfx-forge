using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
#if UNITY_EDITOR && UNITY_6000_5_OR_NEWER
using Unity.Scripting.LifecycleManagement;
#endif

// Special thanks to DreamingLatios: https://github.com/Dreaming381/Latios-Framework/blob/master/LifeFX/Internal/GraphicsEventTypeRegistry.cs

namespace FireAlt.VFXForge.Data
{
    public static partial class VFXTypeRegistry
    {
        private struct SharedKey { }

        public struct TypeToIndex<T> where T : unmanaged
        {
            private static readonly SharedStatic<int> IndexOffBy1 = SharedStatic<int>.GetOrCreate<SharedKey, T>();
            public static int Index
            {
                get
                {
                    if (IndexOffBy1.Data == 0)
                        throw new InvalidOperationException($"The vfx type for this {new TypeToIndex<T>()} has not been registered.");
                    return IndexOffBy1.Data - 1;
                }
            }
        }

        public struct TypeInfo
        {
            public ulong StableTypeHash;
            public short GpuSize;

            public Type Type => VFXTypeRegistry.GetType(StableTypeHash);

            public override string ToString()
            {
                return $"[StableTypeHash: {StableTypeHash}, GpuSize: {GpuSize}]";
            }
        }

        internal static readonly SharedStatic<UnsafeList<TypeInfo>> VFXTypeInfoList = SharedStatic<UnsafeList<TypeInfo>>.GetOrCreate<TypeInfo>();
        internal static readonly SharedStatic<UnsafeHashMap<ulong, int>> StableTypeHashMap = SharedStatic<UnsafeHashMap<ulong, int>>.GetOrCreate<ulong>();
        internal static Dictionary<ulong, Type> HashToTypesDictionary;
        internal static Dictionary<Type, ulong> TypesToHashDictionary;
        
        private static bool _initialized;
        
#if UNITY_EDITOR
#if UNITY_6000_5_OR_NEWER
        [OnCodeLoaded]
        private static void InitializeEditor()
        {
            Refresh();
        }
#else
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            UnityEditor.EditorApplication.update -= RefreshIfPending;
            UnityEditor.EditorApplication.update += RefreshIfPending;
            Init();
        }
#endif
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        internal static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            VFXTypeCache.Init();
            
            VFXTypeInfoList.Data = new UnsafeList<TypeInfo>(16, Allocator.Domain);
            StableTypeHashMap.Data = new UnsafeHashMap<ulong, int>(16, Allocator.Domain);
            HashToTypesDictionary = new Dictionary<ulong, Type>();
            TypesToHashDictionary = new Dictionary<Type, ulong>();
            
            var types = VFXTypeCache.TypesList;
            var hashDictionary = new Dictionary<Type, ulong>();
            
            var sharedKeyType = typeof(SharedKey);
            foreach (var type in types)
            {
                var cpuSize = UnsafeUtility.SizeOf(type);

                var typeInfo = new TypeInfo
                {
                    StableTypeHash = TypeHash.CalculateStableTypeHash(type, hashDictionary),
                    GpuSize = cpuSize >= 4 ? (short)cpuSize : (short)0,
                };
                VFXTypeInfoList.Data.Add(typeInfo);
                var typeIndex = VFXTypeInfoList.Data.Length;
                
                StableTypeHashMap.Data.Add(typeInfo.StableTypeHash, typeIndex);
                HashToTypesDictionary.Add(typeInfo.StableTypeHash, type);
                TypesToHashDictionary.Add(type, typeInfo.StableTypeHash);
                SharedStatic<int>.GetOrCreate(sharedKeyType, type).Data = typeIndex;
            }
        }

#if UNITY_EDITOR
        internal static event Action Refreshed;

#if !UNITY_6000_5_OR_NEWER
        private static bool _refreshPending;
#endif

        internal static void Refresh()
        {
#if !UNITY_6000_5_OR_NEWER
            _refreshPending = false;
#endif

            if (_initialized)
            {
                ref var typeInfoList = ref VFXTypeInfoList.Data;
                if (typeInfoList.IsCreated)
                {
                    typeInfoList.Dispose();
                }

                ref var stableTypeHashMap = ref StableTypeHashMap.Data;
                if (stableTypeHashMap.IsCreated)
                {
                    stableTypeHashMap.Dispose();
                }

                _initialized = false;
            }

            Init();
            Refreshed?.Invoke();
        }

#if !UNITY_6000_5_OR_NEWER
        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            _refreshPending = true;
        }
#endif

        internal static void RefreshIfPending()
        {
#if !UNITY_6000_5_OR_NEWER
            if (!_refreshPending
                || UnityEditor.EditorApplication.isCompiling
                || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Refresh();
#endif
        }
#endif
        
        public static Type GetType(ulong stableTypeHash)
        {
            Init(); // This method can be called from an import worker before the registry is initialized
            return HashToTypesDictionary.GetValueOrDefault(stableTypeHash);
        }
        
        public static ulong GetStableTypeHash(Type type)
        {
            Init(); // This method can be called from an import worker before the registry is initialized
            return TypesToHashDictionary[type];
        }
        
        public static TypeInfo GetTypeInfo<T>() where T : unmanaged
        {
            return VFXTypeInfoList.Data[GetTypeIndex<T>()];
        }
        
        public static TypeInfo GetTypeInfo(ulong stableTypeHash)
        {
            if (TryGetTypeIndex(stableTypeHash, out var typeIndex))
            {
                return VFXTypeInfoList.Data[typeIndex];
            }
            return default;
        }
        
        public static int GetTypeIndex<T>() where T : unmanaged
        {
            return TypeToIndex<T>.Index;
        }
        
        public static bool TryGetTypeIndex(ulong stableTypeHash, out int typeIndex)
        {
            var result = StableTypeHashMap.Data.TryGetValue(stableTypeHash, out typeIndex);
            typeIndex--;
            return result;
        }

        public static ulong GetStableTypeHash<T>() where T : unmanaged => GetTypeInfo<T>().StableTypeHash;
    }
}

