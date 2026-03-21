using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using CitizenTripNeeded = Game.Citizens.TripNeeded;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Objects;
using Game.Pathfind;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class VirtualOfficeResourceBuyerFixSystem : GameSystemBase
    {
        private const int kResourceLowStockAmount = 4000;
        private const int kResourceMinimumRequestAmount = 2000;
        private const int kMaxProbeSampleLogs = 3;
        private const float kLowStockThresholdRatio = 0.25f;
        private const uint kFallbackSweepIntervalMask = 127u;

        private ResourceSystem m_ResourceSystem;
        private SimulationSystem m_SimulationSystem;
        private EntityQuery m_OfficeCompanyQuery;
        private EntityQuery m_OfficeCompanyChangedQuery;
        private EntityQuery m_CorrectiveBuyerMarkerCleanupQuery;

        private readonly Dictionary<Resource, ResourceOverrideAggregate> m_ProbeResourceAggregates = new();
        private readonly Dictionary<Resource, bool> m_ResourceWeightCache = new();
        private readonly Dictionary<Entity, PrefabVirtualInputInfo> m_VirtualInputPrefabCache = new();
        private readonly HashSet<string> m_ProbeDistinctCompanies = new();
        private readonly List<BuyerOverrideSample> m_ProbeTopSamples = new();

        private int m_ProbeTotalOverrideCount;
        private int m_ProbeClampedMinimumOverrideCount;
        private int m_ProbeAboveMinimumOverrideCount;
        private int m_ProbeMaxOverrideAmount;
        private int m_ProbeMaxShortfall;

        private struct BuyerOverrideProbeRecord
        {
            public Entity Company;
            public Resource Resource;
            public int Stock;
            public int BuyingLoad;
            public int TripNeededAmount;
            public int EffectiveStock;
            public int Threshold;
            public int OverrideAmount;
        }

        [BurstCompile]
        private struct ApplyBuyerOverridesJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public ComponentTypeHandle<PrefabRef> PrefabType;

            [ReadOnly]
            public ComponentTypeHandle<PropertyRenter> PropertyType;

            [ReadOnly]
            public BufferTypeHandle<Resources> ResourceType;

            [ReadOnly]
            public BufferTypeHandle<CitizenTripNeeded> TripNeededType;

            [ReadOnly]
            public ComponentLookup<Transform> Transforms;

            [ReadOnly]
            public ComponentLookup<IndustrialProcessData> IndustrialProcessDatas;

            [ReadOnly]
            public ComponentLookup<StorageLimitData> StorageLimitDatas;

            [ReadOnly]
            public ComponentLookup<ResourceData> ResourceDatas;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> OwnedVehicles;

            [ReadOnly]
            public ComponentLookup<Game.Vehicles.DeliveryTruck> DeliveryTrucks;

            [ReadOnly]
            public BufferLookup<LayoutElement> LayoutElements;

            [ReadOnly]
            public ResourcePrefabs ResourcePrefabs;

            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            public NativeQueue<BuyerOverrideProbeRecord>.ParallelWriter ProbeResults;
            public bool CaptureProbeResults;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabType);
                NativeArray<PropertyRenter> properties = chunk.GetNativeArray(ref PropertyType);
                BufferAccessor<Resources> resources = chunk.GetBufferAccessor(ref ResourceType);
                BufferAccessor<CitizenTripNeeded> tripNeeds = chunk.GetBufferAccessor(ref TripNeededType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity company = entities[i];
                    Entity prefab = prefabs[i].m_Prefab;
                    Entity property = properties[i].m_Property;
                    if (property == Entity.Null || !Transforms.HasComponent(property))
                    {
                        continue;
                    }

                    PrefabVirtualInputInfo prefabInfo = GetPrefabVirtualInputInfo(prefab);
                    if (!prefabInfo.HasEligibleVirtualInput)
                    {
                        continue;
                    }

                    DynamicBuffer<Resources> companyResources = resources[i];
                    DynamicBuffer<CitizenTripNeeded> tripNeededBuffer = tripNeeds[i];
                    if (!TrySelectVirtualOfficeInput(
                            company,
                            prefabInfo.Input1,
                            prefabInfo.SlotCapacity,
                            companyResources,
                            tripNeededBuffer,
                            out Resource selectedResource,
                            out int stock,
                            out int buyingLoad,
                            out int tripNeededAmount,
                            out int effectiveStock,
                            out int threshold) &&
                        !TrySelectVirtualOfficeInput(
                            company,
                            prefabInfo.Input2,
                            prefabInfo.SlotCapacity,
                            companyResources,
                            tripNeededBuffer,
                            out selectedResource,
                            out stock,
                            out buyingLoad,
                            out tripNeededAmount,
                            out effectiveStock,
                            out threshold))
                    {
                        continue;
                    }

                    if (HasAnyTripForResource(tripNeededBuffer, selectedResource))
                    {
                        continue;
                    }

                    int overrideAmount = math.max(kResourceMinimumRequestAmount, threshold - effectiveStock);
                    if (overrideAmount <= 0)
                    {
                        continue;
                    }

                    ResourceBuyer resourceBuyer = new ResourceBuyer
                    {
                        m_Payer = company,
                        m_AmountNeeded = overrideAmount,
                        m_Flags = SetupTargetFlags.Industrial | SetupTargetFlags.Import,
                        m_Location = Transforms[property].m_Position,
                        m_ResourceNeeded = selectedResource
                    };

                    CommandBuffer.AddComponent(unfilteredChunkIndex, company, resourceBuyer);
                    CommandBuffer.AddComponent(unfilteredChunkIndex, company, new CorrectiveSoftwareBuyerTag
                    {
                        LastIssuedAmount = overrideAmount
                    });

                    if (!CaptureProbeResults)
                    {
                        continue;
                    }

                    ProbeResults.Enqueue(new BuyerOverrideProbeRecord
                    {
                        Company = company,
                        Resource = selectedResource,
                        Stock = stock,
                        BuyingLoad = buyingLoad,
                        TripNeededAmount = tripNeededAmount,
                        EffectiveStock = effectiveStock,
                        Threshold = threshold,
                        OverrideAmount = overrideAmount
                    });
                }
            }

            private PrefabVirtualInputInfo GetPrefabVirtualInputInfo(Entity prefab)
            {
                if (!IndustrialProcessDatas.HasComponent(prefab) || !StorageLimitDatas.HasComponent(prefab))
                {
                    return default;
                }

                IndustrialProcessData processData = IndustrialProcessDatas[prefab];
                int slotCapacity = StorageLimitDatas[prefab].m_Limit;
                bool hasSecondInput = processData.m_Input2.m_Resource != Resource.NoResource;
                int divisor = hasSecondInput ? 2 : 1;
                if (processData.m_Output.m_Resource != processData.m_Input1.m_Resource &&
                    ResourceHasWeight(processData.m_Output.m_Resource))
                {
                    divisor++;
                }

                if (divisor > 0 && slotCapacity != int.MaxValue)
                {
                    slotCapacity /= divisor;
                }

                Resource input1 = IsVirtualOfficeInput(processData.m_Input1.m_Resource)
                    ? processData.m_Input1.m_Resource
                    : Resource.NoResource;
                Resource input2 = hasSecondInput && IsVirtualOfficeInput(processData.m_Input2.m_Resource)
                    ? processData.m_Input2.m_Resource
                    : Resource.NoResource;
                return new PrefabVirtualInputInfo(input1, input2, slotCapacity);
            }

            private bool TrySelectVirtualOfficeInput(
                Entity company,
                Resource resource,
                int maxCapacity,
                DynamicBuffer<Resources> companyResources,
                DynamicBuffer<CitizenTripNeeded> tripNeededBuffer,
                out Resource selectedResource,
                out int stock,
                out int buyingLoad,
                out int tripNeededAmount,
                out int effectiveStock,
                out int threshold)
            {
                selectedResource = Resource.NoResource;
                stock = 0;
                buyingLoad = 0;
                tripNeededAmount = 0;
                effectiveStock = 0;
                threshold = 0;

                if (resource == Resource.NoResource)
                {
                    return false;
                }

                stock = EconomyUtils.GetResources(resource, companyResources);
                buyingLoad = GetCompanyBuyingLoad(company, resource);
                tripNeededAmount = GetCompanyShoppingTripAmount(tripNeededBuffer, resource);
                threshold = CalculateLowStockThreshold(maxCapacity);

                long effectiveStockTotal = (long)stock + buyingLoad + tripNeededAmount;
                if (effectiveStockTotal >= threshold)
                {
                    return false;
                }

                effectiveStock = (int)effectiveStockTotal;
                selectedResource = resource;
                return true;
            }

            private int GetCompanyBuyingLoad(Entity company, Resource resource)
            {
                if (!OwnedVehicles.HasBuffer(company))
                {
                    return 0;
                }

                int amount = 0;
                DynamicBuffer<OwnedVehicle> vehicles = OwnedVehicles[company];
                for (int i = 0; i < vehicles.Length; i++)
                {
                    Entity vehicle = vehicles[i].m_Vehicle;
                    if (!DeliveryTrucks.HasComponent(vehicle))
                    {
                        continue;
                    }

                    amount += GetBuyingTruckLoad(vehicle, resource);
                }

                return amount;
            }

            private int GetBuyingTruckLoad(Entity vehicle, Resource resource)
            {
                Game.Vehicles.DeliveryTruck truck = DeliveryTrucks[vehicle];
                if (LayoutElements.HasBuffer(vehicle))
                {
                    DynamicBuffer<LayoutElement> layout = LayoutElements[vehicle];
                    if (layout.Length > 0)
                    {
                        int layoutAmount = 0;
                        for (int i = 0; i < layout.Length; i++)
                        {
                            Entity layoutVehicle = layout[i].m_Vehicle;
                            if (!DeliveryTrucks.HasComponent(layoutVehicle))
                            {
                                continue;
                            }

                            Game.Vehicles.DeliveryTruck layoutTruck = DeliveryTrucks[layoutVehicle];
                            if (layoutTruck.m_Resource == resource && (layoutTruck.m_State & DeliveryTruckFlags.Buying) != 0)
                            {
                                layoutAmount += layoutTruck.m_Amount;
                            }
                        }

                        return layoutAmount;
                    }
                }

                return truck.m_Resource == resource && (truck.m_State & DeliveryTruckFlags.Buying) != 0
                    ? truck.m_Amount
                    : 0;
            }

            private static int GetCompanyShoppingTripAmount(DynamicBuffer<CitizenTripNeeded> trips, Resource resource)
            {
                int amount = 0;
                for (int i = 0; i < trips.Length; i++)
                {
                    CitizenTripNeeded trip = trips[i];
                    if ((trip.m_Purpose == Game.Citizens.Purpose.Shopping || trip.m_Purpose == Game.Citizens.Purpose.CompanyShopping) &&
                        trip.m_Resource == resource)
                    {
                        amount += trip.m_Data;
                    }
                }

                return amount;
            }

            private static bool HasAnyTripForResource(DynamicBuffer<CitizenTripNeeded> trips, Resource resource)
            {
                for (int i = 0; i < trips.Length; i++)
                {
                    if (trips[i].m_Resource == resource)
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool IsVirtualOfficeInput(Resource resource)
            {
                return resource != Resource.NoResource && !ResourceHasWeight(resource);
            }

            private bool ResourceHasWeight(Resource resource)
            {
                if (resource == Resource.NoResource)
                {
                    return false;
                }

                Entity resourcePrefab = ResourcePrefabs[resource];
                return ResourceDatas.HasComponent(resourcePrefab) &&
                       ResourceDatas[resourcePrefab].m_Weight > 0f;
            }

            private static int CalculateLowStockThreshold(int maxCapacity)
            {
                return (int)math.max(kResourceLowStockAmount, maxCapacity * kLowStockThresholdRatio);
            }
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            EntityQueryDesc officeCompanyQueryDesc = new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<OfficeCompany>(),
                    ComponentType.ReadOnly<BuyingCompany>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<PropertyRenter>(),
                    ComponentType.ReadOnly<Resources>(),
                    ComponentType.ReadOnly<CitizenTripNeeded>()
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<ResourceBuyer>(),
                    ComponentType.ReadOnly<PathInformation>(),
                    ComponentType.ReadOnly<CurrentTrading>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>()
                }
            };
            m_OfficeCompanyQuery = GetEntityQuery(officeCompanyQueryDesc);
            m_OfficeCompanyChangedQuery = EntityManager.CreateEntityQuery(officeCompanyQueryDesc);
            m_OfficeCompanyChangedQuery.SetChangedVersionFilter(new[]
            {
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.ReadOnly<Resources>(),
                ComponentType.ReadOnly<CitizenTripNeeded>()
            });
            m_CorrectiveBuyerMarkerCleanupQuery = GetEntityQuery(
                ComponentType.ReadOnly<CorrectiveSoftwareBuyerTag>(),
                ComponentType.Exclude<ResourceBuyer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            RequireForUpdate(m_OfficeCompanyQuery);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            CleanupCorrectiveBuyerMarkers();

            if (Mod.Settings == null || !Mod.Settings.EnableVirtualOfficeResourceBuyerFix)
            {
                ResetProbeState();
                return;
            }

            if (m_OfficeCompanyQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            EntityQuery officeCompanyQuery = ShouldRunFallbackSweep()
                ? m_OfficeCompanyQuery
                : m_OfficeCompanyChangedQuery;
            if (officeCompanyQuery.IsEmpty)
            {
                return;
            }

            ResourcePrefabs resourcePrefabs = m_ResourceSystem.GetPrefabs();
            bool captureProbeResults = Mod.Settings.EnableDemandDiagnostics;
            using EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.TempJob);
            using NativeQueue<BuyerOverrideProbeRecord> probeResults = new NativeQueue<BuyerOverrideProbeRecord>(Allocator.TempJob);

            JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(
                new ApplyBuyerOverridesJob
                {
                    EntityType = GetEntityTypeHandle(),
                    PrefabType = GetComponentTypeHandle<PrefabRef>(isReadOnly: true),
                    PropertyType = GetComponentTypeHandle<PropertyRenter>(isReadOnly: true),
                    ResourceType = GetBufferTypeHandle<Resources>(isReadOnly: true),
                    TripNeededType = GetBufferTypeHandle<CitizenTripNeeded>(isReadOnly: true),
                    Transforms = GetComponentLookup<Transform>(isReadOnly: true),
                    IndustrialProcessDatas = GetComponentLookup<IndustrialProcessData>(isReadOnly: true),
                    StorageLimitDatas = GetComponentLookup<StorageLimitData>(isReadOnly: true),
                    ResourceDatas = GetComponentLookup<ResourceData>(isReadOnly: true),
                    OwnedVehicles = GetBufferLookup<OwnedVehicle>(isReadOnly: true),
                    DeliveryTrucks = GetComponentLookup<Game.Vehicles.DeliveryTruck>(isReadOnly: true),
                    LayoutElements = GetBufferLookup<LayoutElement>(isReadOnly: true),
                    ResourcePrefabs = resourcePrefabs,
                    CommandBuffer = commandBuffer.AsParallelWriter(),
                    ProbeResults = probeResults.AsParallelWriter(),
                    CaptureProbeResults = captureProbeResults
                },
                officeCompanyQuery,
                Dependency);

            jobHandle.Complete();
            commandBuffer.Playback(EntityManager);
            Dependency = default;

            if (!captureProbeResults)
            {
                return;
            }

            while (probeResults.TryDequeue(out BuyerOverrideProbeRecord probeRecord))
            {
                AccumulateProbe(probeRecord);
            }
        }

        private bool ShouldRunFallbackSweep()
        {
            return (m_SimulationSystem.frameIndex & kFallbackSweepIntervalMask) == 0;
        }

        private void CleanupCorrectiveBuyerMarkers()
        {
            if (m_CorrectiveBuyerMarkerCleanupQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            EntityManager.RemoveComponent(m_CorrectiveBuyerMarkerCleanupQuery, ComponentType.ReadWrite<CorrectiveSoftwareBuyerTag>());
        }

        private bool TryBuildOverride(Entity company, ResourcePrefabs resourcePrefabs, out BuyerOverride buyerOverride)
        {
            buyerOverride = default;

            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(company);
            PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(company);
            if (propertyRenter.m_Property == Entity.Null ||
                !EntityManager.HasComponent<Transform>(propertyRenter.m_Property) ||
                !TryGetPrefabVirtualInputInfo(prefabRef.m_Prefab, resourcePrefabs, out PrefabVirtualInputInfo prefabInfo))
            {
                return false;
            }

            if (!TryGetSelectedVirtualOfficeInput(company, prefabInfo, out Resource resource, out int stock, out int buyingLoad, out int tripNeededAmount, out int effectiveStock, out int threshold))
            {
                return false;
            }

            DynamicBuffer<CitizenTripNeeded> tripNeededBuffer = EntityManager.GetBuffer<CitizenTripNeeded>(company, isReadOnly: true);
            if (tripNeededBuffer.Length > 0)
            {
                for (int tripIndex = 0; tripIndex < tripNeededBuffer.Length; tripIndex++)
                {
                    CitizenTripNeeded tripNeeded = tripNeededBuffer[tripIndex];
                    if (tripNeeded.m_Resource == resource)
                    {
                        return false;
                    }
                }
            }

            int overrideAmount = math.max(kResourceMinimumRequestAmount, threshold - effectiveStock);
            if (overrideAmount <= 0)
            {
                return false;
            }

            buyerOverride = new BuyerOverride(
                resource,
                stock,
                buyingLoad,
                tripNeededAmount,
                effectiveStock,
                threshold,
                new ResourceBuyer
                {
                    m_Payer = company,
                    m_AmountNeeded = overrideAmount,
                    m_Flags = SetupTargetFlags.Industrial | SetupTargetFlags.Import,
                    m_Location = EntityManager.GetComponentData<Transform>(propertyRenter.m_Property).m_Position,
                    m_ResourceNeeded = resource
                });
            return true;
        }

        private bool TryGetSelectedVirtualOfficeInput(
            Entity company,
            PrefabVirtualInputInfo prefabInfo,
            out Resource selectedResource,
            out int stock,
            out int buyingLoad,
            out int tripNeededAmount,
            out int effectiveStock,
            out int threshold)
        {
            selectedResource = Resource.NoResource;
            stock = 0;
            buyingLoad = 0;
            tripNeededAmount = 0;
            effectiveStock = 0;
            threshold = 0;

            if (!prefabInfo.HasEligibleVirtualInput)
            {
                return false;
            }

            if (TrySelectVirtualOfficeInput(company, prefabInfo.Input1, prefabInfo.SlotCapacity, ref selectedResource, ref stock, ref buyingLoad, ref tripNeededAmount, ref effectiveStock, ref threshold))
            {
                return true;
            }

            if (TrySelectVirtualOfficeInput(company, prefabInfo.Input2, prefabInfo.SlotCapacity, ref selectedResource, ref stock, ref buyingLoad, ref tripNeededAmount, ref effectiveStock, ref threshold))
            {
                return true;
            }

            return false;
        }

        private bool TrySelectVirtualOfficeInput(
            Entity company,
            Resource resource,
            int maxCapacity,
            ref Resource selectedResource,
            ref int stock,
            ref int buyingLoad,
            ref int tripNeededAmount,
            ref int effectiveStock,
            ref int threshold)
        {
            if (resource == Resource.NoResource)
            {
                return false;
            }

            stock = GetCompanyResourceAmount(company, resource);
            buyingLoad = GetCompanyBuyingLoad(company, resource);
            tripNeededAmount = GetCompanyShoppingTripAmount(company, resource);
            threshold = CalculateLowStockThreshold(maxCapacity);

            long effectiveStockTotal = (long)stock + buyingLoad + tripNeededAmount;
            if (effectiveStockTotal >= threshold)
            {
                return false;
            }

            effectiveStock = (int)effectiveStockTotal;
            selectedResource = resource;
            return true;
        }

        private static int CalculateLowStockThreshold(int maxCapacity)
        {
            return (int)math.max(kResourceLowStockAmount, maxCapacity * kLowStockThresholdRatio);
        }

        private int GetCompanyResourceAmount(Entity company, Resource resource)
        {
            DynamicBuffer<Resources> resources = EntityManager.GetBuffer<Resources>(company, isReadOnly: true);
            return EconomyUtils.GetResources(resource, resources);
        }

        private int GetCompanyBuyingLoad(Entity company, Resource resource)
        {
            if (!EntityManager.HasBuffer<OwnedVehicle>(company))
            {
                return 0;
            }

            int amount = 0;
            DynamicBuffer<OwnedVehicle> vehicles = EntityManager.GetBuffer<OwnedVehicle>(company, isReadOnly: true);
            for (int i = 0; i < vehicles.Length; i++)
            {
                Entity vehicle = vehicles[i].m_Vehicle;
                if (!EntityManager.HasComponent<Game.Vehicles.DeliveryTruck>(vehicle))
                {
                    continue;
                }

                amount += GetBuyingTruckLoad(vehicle, resource);
            }

            return amount;
        }

        private int GetBuyingTruckLoad(Entity vehicle, Resource resource)
        {
            Game.Vehicles.DeliveryTruck truck = EntityManager.GetComponentData<Game.Vehicles.DeliveryTruck>(vehicle);
            if (EntityManager.HasBuffer<LayoutElement>(vehicle))
            {
                DynamicBuffer<LayoutElement> layout = EntityManager.GetBuffer<LayoutElement>(vehicle, isReadOnly: true);
                if (layout.Length > 0)
                {
                    int layoutAmount = 0;
                    for (int i = 0; i < layout.Length; i++)
                    {
                        Entity layoutVehicle = layout[i].m_Vehicle;
                        if (!EntityManager.HasComponent<Game.Vehicles.DeliveryTruck>(layoutVehicle))
                        {
                            continue;
                        }

                        Game.Vehicles.DeliveryTruck layoutTruck = EntityManager.GetComponentData<Game.Vehicles.DeliveryTruck>(layoutVehicle);
                        if (layoutTruck.m_Resource == resource && (layoutTruck.m_State & DeliveryTruckFlags.Buying) != 0)
                        {
                            layoutAmount += layoutTruck.m_Amount;
                        }
                    }

                    return layoutAmount;
                }
            }

            return truck.m_Resource == resource && (truck.m_State & DeliveryTruckFlags.Buying) != 0
                ? truck.m_Amount
                : 0;
        }

        private int GetCompanyShoppingTripAmount(Entity company, Resource resource)
        {
            int amount = 0;
            DynamicBuffer<CitizenTripNeeded> trips = EntityManager.GetBuffer<CitizenTripNeeded>(company, isReadOnly: true);
            for (int i = 0; i < trips.Length; i++)
            {
                CitizenTripNeeded trip = trips[i];
                if ((trip.m_Purpose == Game.Citizens.Purpose.Shopping || trip.m_Purpose == Game.Citizens.Purpose.CompanyShopping) &&
                    trip.m_Resource == resource)
                {
                    amount += trip.m_Data;
                }
            }

            return amount;
        }

        private bool TryGetPrefabVirtualInputInfo(Entity prefab, ResourcePrefabs resourcePrefabs, out PrefabVirtualInputInfo prefabInfo)
        {
            if (m_VirtualInputPrefabCache.TryGetValue(prefab, out prefabInfo))
            {
                return prefabInfo.HasEligibleVirtualInput;
            }

            if (!EntityManager.HasComponent<IndustrialProcessData>(prefab) ||
                !EntityManager.HasComponent<StorageLimitData>(prefab))
            {
                prefabInfo = default;
                m_VirtualInputPrefabCache[prefab] = prefabInfo;
                return false;
            }

            IndustrialProcessData processData = EntityManager.GetComponentData<IndustrialProcessData>(prefab);
            int slotCapacity = EntityManager.GetComponentData<StorageLimitData>(prefab).m_Limit;
            bool hasSecondInput = processData.m_Input2.m_Resource != Resource.NoResource;
            int divisor = hasSecondInput ? 2 : 1;
            if (processData.m_Output.m_Resource != processData.m_Input1.m_Resource &&
                ResourceHasWeight(processData.m_Output.m_Resource, resourcePrefabs))
            {
                divisor++;
            }

            if (divisor > 0 && slotCapacity != int.MaxValue)
            {
                slotCapacity /= divisor;
            }

            Resource input1 = IsVirtualOfficeInput(processData.m_Input1.m_Resource, resourcePrefabs)
                ? processData.m_Input1.m_Resource
                : Resource.NoResource;
            Resource input2 = hasSecondInput && IsVirtualOfficeInput(processData.m_Input2.m_Resource, resourcePrefabs)
                ? processData.m_Input2.m_Resource
                : Resource.NoResource;

            prefabInfo = new PrefabVirtualInputInfo(input1, input2, slotCapacity);
            m_VirtualInputPrefabCache[prefab] = prefabInfo;
            return prefabInfo.HasEligibleVirtualInput;
        }

        private bool IsVirtualOfficeInput(Resource resource, ResourcePrefabs resourcePrefabs)
        {
            return resource != Resource.NoResource && !ResourceHasWeight(resource, resourcePrefabs);
        }

        private bool ResourceHasWeight(Resource resource, ResourcePrefabs resourcePrefabs)
        {
            if (resource == Resource.NoResource)
            {
                return false;
            }

            if (m_ResourceWeightCache.TryGetValue(resource, out bool hasWeight))
            {
                return hasWeight;
            }

            Entity resourcePrefab = resourcePrefabs[resource];
            hasWeight = EntityManager.HasComponent<ResourceData>(resourcePrefab) &&
                        EntityManager.GetComponentData<ResourceData>(resourcePrefab).m_Weight > 0f;
            m_ResourceWeightCache[resource] = hasWeight;
            return hasWeight;
        }

        private void AccumulateProbe(BuyerOverrideProbeRecord probeRecord)
        {
            if (Mod.Settings == null)
            {
                ResetProbeState();
                return;
            }

            if (!Mod.Settings.EnableDemandDiagnostics)
            {
                return;
            }

            int overrideAmount = probeRecord.OverrideAmount;
            int shortfall = math.max(0, probeRecord.Threshold - probeRecord.EffectiveStock);
            string companyKey = FormatEntity(probeRecord.Company);

            m_ProbeTotalOverrideCount++;
            m_ProbeDistinctCompanies.Add(companyKey);

            if (overrideAmount <= kResourceMinimumRequestAmount)
            {
                m_ProbeClampedMinimumOverrideCount++;
            }
            else
            {
                m_ProbeAboveMinimumOverrideCount++;
            }

            m_ProbeMaxOverrideAmount = math.max(m_ProbeMaxOverrideAmount, overrideAmount);
            m_ProbeMaxShortfall = math.max(m_ProbeMaxShortfall, shortfall);

            if (!m_ProbeResourceAggregates.TryGetValue(probeRecord.Resource, out ResourceOverrideAggregate aggregate))
            {
                aggregate = new ResourceOverrideAggregate();
            }

            aggregate.Count++;
            aggregate.TotalOverrideAmount += overrideAmount;
            aggregate.MaxOverrideAmount = math.max(aggregate.MaxOverrideAmount, overrideAmount);
            aggregate.MaxShortfall = math.max(aggregate.MaxShortfall, shortfall);
            m_ProbeResourceAggregates[probeRecord.Resource] = aggregate;

            if (!Mod.Settings.VerboseLogging)
            {
                return;
            }

            TryCaptureTopSample(new BuyerOverrideSample(
                companyKey,
                probeRecord.Resource,
                probeRecord.Stock,
                probeRecord.BuyingLoad,
                probeRecord.TripNeededAmount,
                probeRecord.EffectiveStock,
                probeRecord.Threshold,
                overrideAmount,
                shortfall));
        }

        public void EmitProbeSummaryForObservation(int sampleDay, int sampleIndex, int sampleSlot)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableDemandDiagnostics || m_ProbeTotalOverrideCount <= 0)
            {
                ResetProbeState();
                return;
            }

            Mod.log.Info(
                MachineParsedLogContract.FormatVirtualOfficeBuyerFixProbe(
                    "summary",
                    $"sample_day={sampleDay}, sample_index={sampleIndex}, sample_slot={sampleSlot}, total_overrides={m_ProbeTotalOverrideCount}, distinct_companies={m_ProbeDistinctCompanies.Count}, clamped_minimum={m_ProbeClampedMinimumOverrideCount}, above_minimum={m_ProbeAboveMinimumOverrideCount}, max_override_amount={m_ProbeMaxOverrideAmount}, max_shortfall={m_ProbeMaxShortfall}, resources=[{FormatResourceSummary()}], sampled_overrides={m_ProbeTopSamples.Count}"));

            if (Mod.Settings.VerboseLogging)
            {
                for (int i = 0; i < m_ProbeTopSamples.Count; i++)
                {
                    BuyerOverrideSample sample = m_ProbeTopSamples[i];
                    Mod.log.Info(
                        MachineParsedLogContract.FormatVirtualOfficeBuyerFixProbe(
                            "sample",
                            $"sample_day={sampleDay}, sample_index={sampleIndex}, sample_slot={sampleSlot}, rank={i + 1}, company={sample.Company}, resource={sample.Resource}, override_amount={sample.OverrideAmount}, shortfall={sample.Shortfall}, stock={sample.Stock}, buying_load={sample.BuyingLoad}, trip_needed_amount={sample.TripNeededAmount}, effective_stock={sample.EffectiveStock}, threshold={sample.Threshold}"));
                }
            }

            ResetProbeState();
        }

        public void ResetProbeState()
        {
            m_ProbeResourceAggregates.Clear();
            m_ProbeDistinctCompanies.Clear();
            m_ProbeTopSamples.Clear();
            m_ProbeTotalOverrideCount = 0;
            m_ProbeClampedMinimumOverrideCount = 0;
            m_ProbeAboveMinimumOverrideCount = 0;
            m_ProbeMaxOverrideAmount = 0;
            m_ProbeMaxShortfall = 0;
        }

        private void TryCaptureTopSample(BuyerOverrideSample sample)
        {
            for (int i = 0; i < m_ProbeTopSamples.Count; i++)
            {
                BuyerOverrideSample existing = m_ProbeTopSamples[i];
                if (existing.Company == sample.Company && existing.Resource == sample.Resource)
                {
                    if (sample.OverrideAmount > existing.OverrideAmount ||
                        (sample.OverrideAmount == existing.OverrideAmount && sample.Shortfall > existing.Shortfall))
                    {
                        m_ProbeTopSamples[i] = sample;
                    }

                    SortTopSamplesDescending();
                    TrimTopSamples();
                    return;
                }
            }

            m_ProbeTopSamples.Add(sample);
            SortTopSamplesDescending();
            TrimTopSamples();
        }

        private void SortTopSamplesDescending()
        {
            m_ProbeTopSamples.Sort((left, right) =>
            {
                int overrideComparison = right.OverrideAmount.CompareTo(left.OverrideAmount);
                if (overrideComparison != 0)
                {
                    return overrideComparison;
                }

                return right.Shortfall.CompareTo(left.Shortfall);
            });
        }

        private void TrimTopSamples()
        {
            if (m_ProbeTopSamples.Count <= kMaxProbeSampleLogs)
            {
                return;
            }

            m_ProbeTopSamples.RemoveRange(kMaxProbeSampleLogs, m_ProbeTopSamples.Count - kMaxProbeSampleLogs);
        }

        private string FormatResourceSummary()
        {
            if (m_ProbeResourceAggregates.Count == 0)
            {
                return string.Empty;
            }

            List<KeyValuePair<Resource, ResourceOverrideAggregate>> entries =
                new List<KeyValuePair<Resource, ResourceOverrideAggregate>>(m_ProbeResourceAggregates);
            entries.Sort((left, right) => left.Key.CompareTo(right.Key));

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append("; ");
                }

                KeyValuePair<Resource, ResourceOverrideAggregate> entry = entries[i];
                builder.Append(entry.Key)
                    .Append("{count=").Append(entry.Value.Count)
                    .Append(", total_override=").Append(entry.Value.TotalOverrideAmount)
                    .Append(", max_override=").Append(entry.Value.MaxOverrideAmount)
                    .Append(", max_shortfall=").Append(entry.Value.MaxShortfall)
                    .Append('}');
            }

            return builder.ToString();
        }

        private static string FormatEntity(Entity entity)
        {
            return entity.Index.ToString(CultureInfo.InvariantCulture) + ":" + entity.Version.ToString(CultureInfo.InvariantCulture);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ResetCachedPrefabState();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            ResetCachedPrefabState();
        }

        private void ResetCachedPrefabState()
        {
            m_ResourceWeightCache.Clear();
            m_VirtualInputPrefabCache.Clear();
        }

        private sealed class ResourceOverrideAggregate
        {
            public int Count;
            public long TotalOverrideAmount;
            public int MaxOverrideAmount;
            public int MaxShortfall;
        }

        private readonly struct PrefabVirtualInputInfo
        {
            public PrefabVirtualInputInfo(Resource input1, Resource input2, int slotCapacity)
            {
                Input1 = input1;
                Input2 = input2;
                SlotCapacity = slotCapacity;
            }

            public Resource Input1 { get; }
            public Resource Input2 { get; }
            public int SlotCapacity { get; }
            public bool HasEligibleVirtualInput => Input1 != Resource.NoResource || Input2 != Resource.NoResource;
        }

        private readonly struct BuyerOverrideSample
        {
            public BuyerOverrideSample(
                string company,
                Resource resource,
                int stock,
                int buyingLoad,
                int tripNeededAmount,
                int effectiveStock,
                int threshold,
                int overrideAmount,
                int shortfall)
            {
                Company = company;
                Resource = resource;
                Stock = stock;
                BuyingLoad = buyingLoad;
                TripNeededAmount = tripNeededAmount;
                EffectiveStock = effectiveStock;
                Threshold = threshold;
                OverrideAmount = overrideAmount;
                Shortfall = shortfall;
            }

            public string Company { get; }
            public Resource Resource { get; }
            public int Stock { get; }
            public int BuyingLoad { get; }
            public int TripNeededAmount { get; }
            public int EffectiveStock { get; }
            public int Threshold { get; }
            public int OverrideAmount { get; }
            public int Shortfall { get; }
        }

        private readonly struct BuyerOverride
        {
            public BuyerOverride(
                Resource resource,
                int stock,
                int buyingLoad,
                int tripNeededAmount,
                int effectiveStock,
                int threshold,
                ResourceBuyer resourceBuyer)
            {
                Resource = resource;
                Stock = stock;
                BuyingLoad = buyingLoad;
                TripNeededAmount = tripNeededAmount;
                EffectiveStock = effectiveStock;
                Threshold = threshold;
                ResourceBuyer = resourceBuyer;
            }

            public Resource Resource { get; }
            public int Stock { get; }
            public int BuyingLoad { get; }
            public int TripNeededAmount { get; }
            public int EffectiveStock { get; }
            public int Threshold { get; }
            public ResourceBuyer ResourceBuyer { get; }
        }
    }
}
