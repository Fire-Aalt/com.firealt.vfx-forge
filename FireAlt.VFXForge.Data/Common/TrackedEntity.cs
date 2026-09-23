using Unity.Scripting.LifecycleManagement;
using System;
using Unity.Assertions;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace FireAlt.VFXForge.Data
{
    [NoAutoStaticsCleanup]
    public struct TrackedEntity : IEquatable<TrackedEntity>
    {
        private readonly Entity _entity;
        public int IndexInData;
        public PackedData PackedData;

        public Entity Entity
        {
            get
            {
                Assert.IsFalse(PackedData.IsEntityId, "TrackedEntity was not created with Entity, but Entity was accessed.");
                return _entity;
            }
        }
        
        public EntityId EntityId
        {
            get
            {
                Assert.IsTrue(PackedData.IsEntityId, "TrackedEntity was not created with EntityId, but EntityId was accessed.");
                return EntityIdConverter.FromEntity(_entity);
            }
        }

        internal Entity RawEntityData => _entity;
        
        private TrackedEntity(Entity entity, int indexInData, uint systemVersion, bool isDeferred, bool isEntityId)
        {
            _entity = entity;
            IndexInData = indexInData;
            PackedData = new PackedData(systemVersion, isDeferred, isEntityId);
        }

        public bool IsValid => PackedData.SystemVersion != 0;
        public bool IsEntityId => PackedData.IsEntityId;
        public bool IsDeferred(uint currentSystemVersion) => PackedData.IsDeferred && PackedData.SystemVersion == currentSystemVersion;
        
        public static TrackedEntity Null => new(Entity.Null, 0, 0, false, false);
        public static TrackedEntity FromEntity(Entity entity) => new(entity, 0, 0, false, false);
        public static TrackedEntity FromEntityId(EntityId entityId) => new(EntityIdConverter.FromEntityId(entityId), 0, 0, false, true);
        public static TrackedEntity FromTrackedEntity(TrackedEntity trackedEntity) => new(trackedEntity.RawEntityData, 0, 0, false, trackedEntity.IsEntityId);
        
        public bool Equals(TrackedEntity other)
        {
            return _entity.Equals(other._entity)
                   && IndexInData.Equals(other.IndexInData)
                   && PackedData.Equals(other.PackedData);
        }

        public override int GetHashCode()
        {
            return (int)math.hash(new int4(_entity.Index, _entity.Version, IndexInData, (int)PackedData.Raw));
        }

        public override string ToString()
        {
            return $"{_entity}:I={IndexInData}:SysVer={PackedData}";
        }
    }
}