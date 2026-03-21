using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class SignaturePropertyMarketGuardSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_SignaturePropertyQuery;
        private int m_CorrectionsSinceLastSample;

        [BurstCompile]
        private struct GuardOccupiedSignatureMarketStateJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public BufferLookup<Renter> Renters;

            [ReadOnly]
            public ComponentLookup<CompanyData> CompanyDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> PropertyRenters;

            [ReadOnly]
            public ComponentLookup<OfficeProperty> OfficeProperties;

            [ReadOnly]
            public ComponentLookup<IndustrialProperty> IndustrialProperties;

            [ReadOnly]
            public ComponentLookup<PropertyOnMarket> PropertyOnMarkets;

            [ReadOnly]
            public ComponentLookup<PropertyToBeOnMarket> PropertyToBeOnMarkets;

            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeQueue<Entity>.ParallelWriter CorrectedProperties;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity property = entities[i];
                    if (!OfficeProperties.HasComponent(property) && !IndustrialProperties.HasComponent(property))
                    {
                        continue;
                    }

                    bool hasOnMarket = PropertyOnMarkets.HasComponent(property);
                    bool hasToBeOnMarket = PropertyToBeOnMarkets.HasComponent(property);
                    if (!hasOnMarket && !hasToBeOnMarket)
                    {
                        continue;
                    }

                    if (!HasActiveCompanyRenter(property))
                    {
                        continue;
                    }

                    if (hasOnMarket)
                    {
                        CommandBuffer.RemoveComponent<PropertyOnMarket>(unfilteredChunkIndex, property);
                    }

                    if (hasToBeOnMarket)
                    {
                        CommandBuffer.RemoveComponent<PropertyToBeOnMarket>(unfilteredChunkIndex, property);
                    }

                    CorrectedProperties.Enqueue(property);
                }
            }

            private bool HasActiveCompanyRenter(Entity property)
            {
                if (!Renters.HasBuffer(property))
                {
                    return false;
                }

                DynamicBuffer<Renter> renters = Renters[property];
                for (int i = 0; i < renters.Length; i++)
                {
                    Entity renter = renters[i].m_Renter;
                    if (!CompanyDatas.HasComponent(renter) || !PropertyRenters.HasComponent(renter))
                    {
                        continue;
                    }

                    if (PropertyRenters[renter].m_Property == property)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SignaturePropertyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Signature>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<PropertyOnMarket>(),
                    ComponentType.ReadOnly<PropertyToBeOnMarket>()
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<Abandoned>(),
                    ComponentType.ReadOnly<Destroyed>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Condemned>()
                }
            });
            RequireForUpdate(m_SignaturePropertyQuery);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_CorrectionsSinceLastSample = 0;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_CorrectionsSinceLastSample = 0;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!IsFixEnabled())
            {
                return;
            }

            if (m_SignaturePropertyQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            if (IsVerboseLoggingEnabled())
            {
                RunVerboseCorrectionLoop();
                return;
            }

            using EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.TempJob);
            using NativeQueue<Entity> correctedProperties = new NativeQueue<Entity>(Allocator.TempJob);
            JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(
                new GuardOccupiedSignatureMarketStateJob
                {
                    EntityType = GetEntityTypeHandle(),
                    Renters = GetBufferLookup<Renter>(isReadOnly: true),
                    CompanyDatas = GetComponentLookup<CompanyData>(isReadOnly: true),
                    PropertyRenters = GetComponentLookup<PropertyRenter>(isReadOnly: true),
                    OfficeProperties = GetComponentLookup<OfficeProperty>(isReadOnly: true),
                    IndustrialProperties = GetComponentLookup<IndustrialProperty>(isReadOnly: true),
                    PropertyOnMarkets = GetComponentLookup<PropertyOnMarket>(isReadOnly: true),
                    PropertyToBeOnMarkets = GetComponentLookup<PropertyToBeOnMarket>(isReadOnly: true),
                    CommandBuffer = commandBuffer.AsParallelWriter(),
                    CorrectedProperties = correctedProperties.AsParallelWriter()
                },
                m_SignaturePropertyQuery,
                Dependency);

            jobHandle.Complete();
            commandBuffer.Playback(EntityManager);

            while (correctedProperties.TryDequeue(out _))
            {
                m_CorrectionsSinceLastSample++;
            }
        }

        private void RunVerboseCorrectionLoop()
        {
            using NativeArray<Entity> properties = m_SignaturePropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length; i++)
            {
                Entity property = properties[i];
                if (!TryGetTrackedPropertyType(property, out string propertyType))
                {
                    continue;
                }

                bool hasOnMarket = EntityManager.HasComponent<PropertyOnMarket>(property);
                bool hasToBeOnMarket = EntityManager.HasComponent<PropertyToBeOnMarket>(property);

                if (!HasActiveCompanyRenter(property))
                {
                    continue;
                }

                List<string> removedComponents = null;
                if (hasOnMarket)
                {
                    EntityManager.RemoveComponent<PropertyOnMarket>(property);
                    removedComponents = removedComponents ?? new List<string>(2);
                    removedComponents.Add(nameof(PropertyOnMarket));
                }

                if (hasToBeOnMarket)
                {
                    EntityManager.RemoveComponent<PropertyToBeOnMarket>(property);
                    removedComponents = removedComponents ?? new List<string>(2);
                    removedComponents.Add(nameof(PropertyToBeOnMarket));
                }

                if (removedComponents == null)
                {
                    continue;
                }

                m_CorrectionsSinceLastSample++;
                if (IsVerboseLoggingEnabled())
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(property);
                    Mod.log.Info(
                        MachineParsedLogContract.FormatPhantomVacancyCorrection(
                            propertyType,
                            FormatEntity(property),
                            GetPrefabLabel(prefabRef.m_Prefab),
                            removedComponents));
                }
            }
        }

        public int ConsumeCorrectionCount()
        {
            int correctionCount = m_CorrectionsSinceLastSample;
            m_CorrectionsSinceLastSample = 0;
            return correctionCount;
        }

        private bool HasActiveCompanyRenter(Entity property)
        {
            if (!EntityManager.HasBuffer<Renter>(property))
            {
                return false;
            }

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(property, isReadOnly: true);
            for (int i = 0; i < renters.Length; i++)
            {
                Entity renter = renters[i].m_Renter;
                if (!EntityManager.HasComponent<CompanyData>(renter) || !EntityManager.HasComponent<PropertyRenter>(renter))
                {
                    continue;
                }

                PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(renter);
                if (propertyRenter.m_Property == property)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTrackedPropertyType(Entity property, out string propertyType)
        {
            if (EntityManager.HasComponent<OfficeProperty>(property))
            {
                propertyType = "office";
                return true;
            }

            if (EntityManager.HasComponent<IndustrialProperty>(property))
            {
                propertyType = "industrial";
                return true;
            }

            propertyType = null;
            return false;
        }

        private string GetPrefabLabel(Entity prefab)
        {
            string prefabName = m_PrefabSystem.GetPrefabName(prefab);
            if (string.IsNullOrEmpty(prefabName))
            {
                return FormatEntity(prefab);
            }

            return '"' + prefabName + "\" (" + FormatEntity(prefab) + ')';
        }

        private static bool IsFixEnabled()
        {
            return Mod.Settings == null || Mod.Settings.EnablePhantomVacancyFix;
        }

        private static bool IsVerboseLoggingEnabled()
        {
            return Mod.Settings != null && Mod.Settings.VerboseLogging;
        }

        private static string FormatEntity(Entity entity)
        {
            return entity.Index + ":" + entity.Version;
        }
    }
}
