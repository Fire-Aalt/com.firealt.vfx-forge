using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.VFX;

namespace FireAlt.VFXForge.Data
{
    internal static class VFXTypeCache
    {
        public static readonly Dictionary<Type, List<Type>> DataBakerTypesMap = new();
        public static readonly Dictionary<Type, List<Type>> ArrayBakerTypesMap = new();
        public static readonly List<Type> TypesList = new();
        public static readonly List<Type> DataTypesList = new();
        public static readonly List<Type> ArrayTypesList = new();
        public static readonly Dictionary<Type, string> TypeNamesDictionary = new();
        
        private static readonly Dictionary<Type, string> UnitySupportedTypes = new()
        {
            { typeof(int), "Core/int" },
            { typeof(uint), "Core/uint" },
            { typeof(float), "Core/float" },
            { typeof(Vector2), "Core/Vector2" },
            { typeof(Vector3), "Core/Vector3" },
            { typeof(Vector4), "Core/Vector4" },
            { typeof(Matrix4x4), "Core/Matrix4x4" },
        };
        private static readonly HashSet<string> InternalVFXTypes = new()
        {
            "VFXSpawnIndex",
            "VFXArraySpawnIndex",
            "VFXTransform",
            "VFXArrayPtr",
        };
        
        internal static void Init()
        {
            var assemblies = GetLoadedAssemblies();

            DataBakerTypesMap.Clear();
            ArrayBakerTypesMap.Clear();
            TypesList.Clear();
            DataTypesList.Clear();
            ArrayTypesList.Clear();
            TypeNamesDictionary.Clear();
            
            AddVFXTypeBakers<IVFXDataTypeBaker>(typeof(VFXDataTypeBaker<>), DataBakerTypesMap, assemblies);
            AddVFXTypeBakers<IVFXArrayDataTypeBaker>(typeof(VFXArrayDataTypeBaker<>), ArrayBakerTypesMap, assemblies);
            AddUnitySupportedVFXTypes();
            AddReflectedVFXTypes(assemblies);
        }

        private static void AddVFXTypeBakers<TBakerInterface>(
            Type bakerBaseType,
            Dictionary<Type, List<Type>> bakerTypeMap,
            IReadOnlyList<Assembly> assemblies)
        {
            var dataAssembly = typeof(TBakerInterface).Assembly;

            var reflectedTypesList = assemblies
                .Where(assembly => IsAssemblyReferencingAssembly(assembly, dataAssembly))
                .Where(FilterAssembly)
                .SelectMany(domainAssembly => domainAssembly.GetTypes())
                .Where(type => typeof(TBakerInterface).IsAssignableFrom(type))
                .Where(type => type != typeof(TBakerInterface))
                .Where(type => !type.ContainsGenericParameters)
                .ToList();
             
            foreach (var type in reflectedTypesList)
            {
                if (!TryGetBakedVFXType(type, bakerBaseType, out var bakedType))
                {
                    continue;
                }

                if (!bakerTypeMap.TryGetValue(bakedType, out var bakerTypes))
                {
                    bakerTypes = new List<Type>();
                    bakerTypeMap.Add(bakedType, bakerTypes);
                }

                if (!bakerTypes.Contains(type))
                {
                    bakerTypes.Add(type);
                }
            }
        }

        private static void AddReflectedVFXTypes(IReadOnlyList<Assembly> assemblies)
        {
            var vfxAssembly = typeof(VFXTypeAttribute).Assembly;

            var reflectedTypesList = assemblies
                .Where(assembly => IsAssemblyReferencingAssembly(assembly, vfxAssembly))
                .Where(FilterAssembly)
                .SelectMany(domainAssembly => domainAssembly.GetTypes())
                .Where(type => type.GetCustomAttribute<VFXTypeAttribute>() != null)
                .Where(FilterTypes)
                .ToList();
            
            foreach (var type in reflectedTypesList)
            {
                AddType(type);
            }
        }
        
        private static void AddUnitySupportedVFXTypes()
        {
            foreach (var (type, displayName) in UnitySupportedTypes)
            {
                AddType(type, displayName);
            }
        }
        
        private static void AddType(Type type)
        {
            AddDefaultBaker(DataBakerTypesMap, type, typeof(DefaultVFXDataTypeBaker<>));
            AddDefaultBaker(ArrayBakerTypesMap, type, typeof(DefaultVFXArrayDataTypeBaker<>));

            var fullName = type.Assembly.GetName().Name + "/" + type.Name;
            AddType(type, fullName);
        }
        
        private static void AddType(Type type, string name)
        {
            AddDefaultBaker(DataBakerTypesMap, type, typeof(DefaultVFXDataTypeBaker<>));
            AddDefaultBaker(ArrayBakerTypesMap, type, typeof(DefaultVFXArrayDataTypeBaker<>));
            
            TypeNamesDictionary.TryAdd(type, name);
            
            AddTypeWithoutDuplicates(TypesList, type);
            AddTypeWithoutDuplicates(DataTypesList, type);
            AddTypeWithoutDuplicates(ArrayTypesList, type);
        }

        private static bool FilterAssembly(Assembly assembly)
        {
            var assemblyName = assembly.GetName().Name;
            
            if (assemblyName.EndsWith(".Editor")) return false;
            return true;
        }

        private static bool IsAssemblyReferencingAssembly(Assembly assembly, Assembly reference)
        {
            if (assembly == reference)
            {
                return true;
            }

            var referenceName = reference.GetName().Name;
            return assembly.GetReferencedAssemblies().Any(referenced => referenced.Name == referenceName);
        }

        private static IReadOnlyList<Assembly> GetLoadedAssemblies()
        {
#if UNITY_6000_6_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }
        
        private static bool FilterTypes(Type type)
        {
            var typeName = type.Name;
            return !InternalVFXTypes.Contains(typeName);
        }

        private static bool TryGetBakedVFXType(Type bakerType, Type expectedBakerBaseType, out Type bakedType)
        {
            bakedType = null;

            if (bakerType.IsAbstract)
            {
                Debug.LogError(
                    $"Failed to register VFX baker type '{bakerType.FullName}'. " +
                    $"The type is abstract and cannot be instantiated.");
                return false;
            }

            var inheritanceChain = new List<Type>();
            for (var current = bakerType; current != null; current = current.BaseType)
            {
                inheritanceChain.Add(current);
            }
            
            for (var i = inheritanceChain.Count - 1; i >= 0; i--)
            {
                var current = inheritanceChain[i];
                if (!current.IsGenericType || current.GetGenericTypeDefinition() != expectedBakerBaseType)
                {
                    continue;
                }

                var genericArguments = current.GetGenericArguments();
                bakedType = genericArguments[0];

                if (!UnitySupportedTypes.ContainsKey(bakedType) && bakedType.GetCustomAttribute<VFXTypeAttribute>() == null)
                {
                    Debug.LogError(
                        $"Failed to register VFX baker type '{bakerType.FullName}'. " +
                        $"The baked VFX data type '{bakedType.FullName}' is missing the '{nameof(VFXTypeAttribute)}' attribute.");
                    bakedType = null;
                    return false;
                }

                return true;
            }

            Debug.LogError(
                $"Failed to register VFX baker type '{bakerType.FullName}'. " +
                $"No '{expectedBakerBaseType.FullName}' base type was found in its inheritance chain.");
            return false;
        }

        private static void AddTypeWithoutDuplicates(List<Type> typesList, Type type)
        {
            if (!typesList.Contains(type))
            {
                typesList.Add(type);
            }
        }

        private static void AddDefaultBaker(Dictionary<Type, List<Type>> bakerTypesMap, Type vfxDataType, Type defaultBakerGenericType)
        {
            if (!bakerTypesMap.TryGetValue(vfxDataType, out var bakerTypes))
            {
                bakerTypes = new List<Type>();
                bakerTypesMap.Add(vfxDataType, bakerTypes);
            }

            var defaultBakerType = defaultBakerGenericType.MakeGenericType(vfxDataType);
            if (!bakerTypes.Contains(defaultBakerType))
            {
                bakerTypes.Insert(0, defaultBakerType);
            }
        }

        public static bool TryGetBakerTypes(Type vfxDataType, VFXDataTypeBakerKind bakerKind, out List<Type> bakerTypes)
        {
            var bakersMap = bakerKind == VFXDataTypeBakerKind.Data
                ? DataBakerTypesMap
                : ArrayBakerTypesMap;

            return bakersMap.TryGetValue(vfxDataType, out bakerTypes);
        }
    }
}
