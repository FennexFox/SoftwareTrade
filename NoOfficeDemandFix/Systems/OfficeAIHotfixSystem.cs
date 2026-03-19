using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    public partial class OfficeAIHotfixSystem : GameSystemBase
    {
        private struct OfficeResourceConsumptionEvent
        {
            public Resource Resource;
            public int Amount;
        }

        [BurstCompile]
        private struct ResetOfficeConsumptionJob : IJob
        {
            public NativeQueue<OfficeResourceConsumptionEvent> ConsumptionQueue;
            public NativeArray<int> ResourceConsumeAccumulator;
            public NativeReference<int> OfficeConsumedAmount;

            public void Execute()
            {
                OfficeConsumedAmount.Value = 0;
                while (ConsumptionQueue.TryDequeue(out OfficeResourceConsumptionEvent item))
                {
                    ResourceConsumeAccumulator[EconomyUtils.GetResourceIndex(item.Resource)] += item.Amount;
                }
            }
        }

        [BurstCompile]
        private struct OfficeAIHotfixJob : IJobChunk
        {
            [ReadOnly]
            public NativeReference<int> OfficeConsumedAmount;

            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public SharedComponentTypeHandle<UpdateFrame> UpdateFrameType;

            [ReadOnly]
            public ComponentTypeHandle<PrefabRef> PrefabType;

            [ReadOnly]
            public ComponentTypeHandle<PropertyRenter> PropertyType;

            public BufferTypeHandle<Resources> ResourceType;

            [ReadOnly]
            public ComponentLookup<IndustrialProcessData> IndustrialProcessDatas;

            [ReadOnly]
            public ComponentLookup<ResourceData> ResourceDatas;

            [ReadOnly]
            public ComponentLookup<Building> Buildings;

            [ReadOnly]
            public ResourcePrefabs ResourcePrefabs;

            public NativeQueue<OfficeResourceConsumptionEvent>.ParallelWriter ConsumptionQueue;

            public EntityCommandBuffer.ParallelWriter CommandBuffer;

            public EconomyParameterData EconomyParameters;

            public int OfficeEntityCount;

            public uint UpdateFrameIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.GetSharedComponent(UpdateFrameType).m_Index != UpdateFrameIndex)
                {
                    return;
                }

                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabType);
                BufferAccessor<Resources> resourceBuffers = chunk.GetBufferAccessor(ref ResourceType);
                NativeArray<PropertyRenter> properties = chunk.GetNativeArray(ref PropertyType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity entity = entities[i];
                    Entity prefab = prefabs[i].m_Prefab;
                    Entity property = properties[i].m_Property;
                    if (!Buildings.HasComponent(property) || !IndustrialProcessDatas.TryGetComponent(prefab, out IndustrialProcessData processData))
                    {
                        continue;
                    }

                    DynamicBuffer<Resources> resources = resourceBuffers[i];
                    Resource resource = processData.m_Output.m_Resource;
                    int resourceAmount = EconomyUtils.GetResources(resource, resources);
                    if (resourceAmount <= kMinStorageAllow)
                    {
                        continue;
                    }

                    int consumedAmount = math.min(
                        resourceAmount,
                        (int)math.ceil((float)OfficeConsumedAmount.Value / (float)OfficeEntityCount) * EconomyParameters.m_OfficeResourceConsumedPerIndustrialUnit);

                    int remainingAmount = EconomyUtils.AddResources(resource, -consumedAmount, resources);
                    int money = (int)math.ceil((float)consumedAmount * EconomyUtils.GetIndustrialPrice(resource, ResourcePrefabs, ref ResourceDatas));
                    EconomyUtils.AddResources(Resource.Money, money, resources);

                    ConsumptionQueue.Enqueue(new OfficeResourceConsumptionEvent
                    {
                        Resource = resource,
                        Amount = consumedAmount
                    });

                    int exportThreshold = (int)((float)(IndustrialAISystem.kMaxVirtualResourceStorage * 2) / 3f);
                    if (remainingAmount > exportThreshold + kMinimumTradeResource)
                    {
                        int exportAmount = remainingAmount - exportThreshold;
                        CommandBuffer.AddComponent(unfilteredChunkIndex, entity, new ResourceExporter
                        {
                            m_Resource = resource,
                            m_Amount = math.max(0, exportAmount)
                        });
                    }
                }
            }
        }

        private static readonly int kUpdatesPerDay = 32;
        private static readonly int kMinimumTradeResource = 2000;
        private static readonly int kMinStorageAllow = 30000;

        private EntityQuery m_EconomyParameterQuery;
        private EntityQuery m_OfficeCompanyGroup;
        private NativeQueue<OfficeResourceConsumptionEvent> m_OfficeResourceConsumptionQueue;
        private CityProductionStatisticSystem m_CityProductionStatisticSystem;
        private EndFrameBarrier m_EndFrameBarrier;
        private OfficeAISystem m_OfficeAISystem;
        private ResourceSystem m_ResourceSystem;
        private SimulationSystem m_SimulationSystem;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 262144 / (kUpdatesPerDay * 16);
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_OfficeAISystem = World.GetOrCreateSystemManaged<OfficeAISystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_CityProductionStatisticSystem = World.GetOrCreateSystemManaged<CityProductionStatisticSystem>();
            m_OfficeCompanyGroup = GetEntityQuery(
                ComponentType.ReadWrite<Resources>(),
                ComponentType.ReadOnly<Game.Companies.ProcessingCompany>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.ReadOnly<OfficeCompany>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Employee>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Game.Companies.ExtractorCompany>());
            m_EconomyParameterQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<EconomyParameterData>() },
                Options = EntityQueryOptions.IncludeSystems
            });
            m_OfficeResourceConsumptionQueue = new NativeQueue<OfficeResourceConsumptionEvent>(Allocator.Persistent);
            RequireForUpdate(m_OfficeCompanyGroup);
            RequireForUpdate(m_EconomyParameterQuery);
        }

        [Preserve]
        protected override void OnDestroy()
        {
            m_OfficeResourceConsumptionQueue.Dispose();
            base.OnDestroy();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            uint updateFrame = SimulationUtils.GetUpdateFrame(m_SimulationSystem.frameIndex, kUpdatesPerDay, 16);
            int officeEntityCount = m_OfficeCompanyGroup.CalculateEntityCount();
            NativeReference<int> officeConsumedAmount = m_OfficeAISystem.GetIndustrialConsumptionAmount(out JobHandle writeDeps);

            JobHandle officeJob = JobChunkExtensions.ScheduleParallel(
                new OfficeAIHotfixJob
                {
                    OfficeConsumedAmount = officeConsumedAmount,
                    EntityType = GetEntityTypeHandle(),
                    UpdateFrameType = GetSharedComponentTypeHandle<UpdateFrame>(),
                    PrefabType = GetComponentTypeHandle<PrefabRef>(isReadOnly: true),
                    PropertyType = GetComponentTypeHandle<PropertyRenter>(isReadOnly: true),
                    ResourceType = GetBufferTypeHandle<Resources>(),
                    IndustrialProcessDatas = GetComponentLookup<IndustrialProcessData>(isReadOnly: true),
                    ResourceDatas = GetComponentLookup<ResourceData>(isReadOnly: true),
                    Buildings = GetComponentLookup<Building>(isReadOnly: true),
                    ResourcePrefabs = m_ResourceSystem.GetPrefabs(),
                    ConsumptionQueue = m_OfficeResourceConsumptionQueue.AsParallelWriter(),
                    CommandBuffer = m_EndFrameBarrier.CreateCommandBuffer().AsParallelWriter(),
                    EconomyParameters = m_EconomyParameterQuery.GetSingleton<EconomyParameterData>(),
                    OfficeEntityCount = officeEntityCount,
                    UpdateFrameIndex = updateFrame
                },
                m_OfficeCompanyGroup,
                JobHandle.CombineDependencies(writeDeps, Dependency));

            NativeArray<int> resourceConsumeAccumulator = m_CityProductionStatisticSystem.GetCityResourceUsageAccumulator(
                CityProductionStatisticSystem.CityResourceUsage.Consumer.Industrial,
                out JobHandle accumulatorDeps);

            ResetOfficeConsumptionJob resetJob = new ResetOfficeConsumptionJob
            {
                OfficeConsumedAmount = officeConsumedAmount,
                ConsumptionQueue = m_OfficeResourceConsumptionQueue,
                ResourceConsumeAccumulator = resourceConsumeAccumulator
            };

            Dependency = IJobExtensions.Schedule(resetJob, JobHandle.CombineDependencies(officeJob, accumulatorDeps));
            m_OfficeAISystem.AddWriteConsumptionDeps(Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
            m_ResourceSystem.AddPrefabsReader(Dependency);
        }
    }
}
