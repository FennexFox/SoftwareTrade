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
using Unity.Collections;
using Unity.Entities;
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

        private ResourceSystem m_ResourceSystem;
        private EntityQuery m_OfficeCompanyQuery;
        private EntityQuery m_CorrectiveBuyerMarkerCleanupQuery;

        private readonly Dictionary<Resource, ResourceOverrideAggregate> m_ProbeResourceAggregates = new();
        private readonly HashSet<string> m_ProbeDistinctCompanies = new();
        private readonly List<BuyerOverrideSample> m_ProbeTopSamples = new();

        private int m_ProbeTotalOverrideCount;
        private int m_ProbeClampedMinimumOverrideCount;
        private int m_ProbeAboveMinimumOverrideCount;
        private int m_ProbeMaxOverrideAmount;
        private int m_ProbeMaxShortfall;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_OfficeCompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<OfficeCompany>(),
                ComponentType.ReadOnly<BuyingCompany>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<PropertyRenter>(),
                ComponentType.ReadOnly<Resources>(),
                ComponentType.ReadOnly<CitizenTripNeeded>(),
                ComponentType.Exclude<ResourceBuyer>(),
                ComponentType.Exclude<PathInformation>(),
                ComponentType.Exclude<CurrentTrading>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
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

            ResourcePrefabs resourcePrefabs = m_ResourceSystem.GetPrefabs();

            using NativeArray<Entity> companies = m_OfficeCompanyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < companies.Length; i++)
            {
                Entity company = companies[i];
                if (!TryBuildOverride(company, resourcePrefabs, out BuyerOverride buyerOverride))
                {
                    continue;
                }

                EntityManager.AddComponentData(company, buyerOverride.ResourceBuyer);
                EntityManager.AddComponentData(company, new CorrectiveSoftwareBuyerTag
                {
                    LastIssuedAmount = buyerOverride.ResourceBuyer.m_AmountNeeded
                });
                AccumulateProbe(company, buyerOverride);
            }
        }

        private void CleanupCorrectiveBuyerMarkers()
        {
            using NativeArray<Entity> taggedCompanies = m_CorrectiveBuyerMarkerCleanupQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < taggedCompanies.Length; i++)
            {
                Entity company = taggedCompanies[i];
                if (EntityManager.HasComponent<CorrectiveSoftwareBuyerTag>(company))
                {
                    EntityManager.RemoveComponent<CorrectiveSoftwareBuyerTag>(company);
                }
            }
        }

        private bool TryBuildOverride(Entity company, ResourcePrefabs resourcePrefabs, out BuyerOverride buyerOverride)
        {
            buyerOverride = default;

            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(company);
            PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(company);
            if (propertyRenter.m_Property == Entity.Null ||
                !EntityManager.HasComponent<Transform>(propertyRenter.m_Property) ||
                !EntityManager.HasComponent<IndustrialProcessData>(prefabRef.m_Prefab))
            {
                return false;
            }

            IndustrialProcessData processData = EntityManager.GetComponentData<IndustrialProcessData>(prefabRef.m_Prefab);
            if (!TryGetSelectedVirtualOfficeInput(company, prefabRef.m_Prefab, processData, resourcePrefabs, out Resource resource, out int stock, out int buyingLoad, out int tripNeededAmount, out int effectiveStock, out int threshold))
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
            Entity prefab,
            IndustrialProcessData processData,
            ResourcePrefabs resourcePrefabs,
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

            if (!EntityManager.HasComponent<StorageLimitData>(prefab))
            {
                return false;
            }

            int storageLimit = EntityManager.GetComponentData<StorageLimitData>(prefab).m_Limit;

            bool hasSecondInput = processData.m_Input2.m_Resource != Resource.NoResource;
            int slotCapacity = storageLimit;
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

            if (TrySelectVirtualOfficeInput(company, processData.m_Input1.m_Resource, slotCapacity, resourcePrefabs, ref selectedResource, ref stock, ref buyingLoad, ref tripNeededAmount, ref effectiveStock, ref threshold))
            {
                return true;
            }

            if (hasSecondInput &&
                TrySelectVirtualOfficeInput(company, processData.m_Input2.m_Resource, slotCapacity, resourcePrefabs, ref selectedResource, ref stock, ref buyingLoad, ref tripNeededAmount, ref effectiveStock, ref threshold))
            {
                return true;
            }

            return false;
        }

        private bool TrySelectVirtualOfficeInput(
            Entity company,
            Resource resource,
            int maxCapacity,
            ResourcePrefabs resourcePrefabs,
            ref Resource selectedResource,
            ref int stock,
            ref int buyingLoad,
            ref int tripNeededAmount,
            ref int effectiveStock,
            ref int threshold)
        {
            if (resource == Resource.NoResource || ResourceHasWeight(resource, resourcePrefabs))
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

        private bool ResourceHasWeight(Resource resource, ResourcePrefabs resourcePrefabs)
        {
            if (resource == Resource.NoResource)
            {
                return false;
            }

            Entity resourcePrefab = resourcePrefabs[resource];
            return EntityManager.HasComponent<ResourceData>(resourcePrefab) &&
                   EntityManager.GetComponentData<ResourceData>(resourcePrefab).m_Weight > 0f;
        }

        private void AccumulateProbe(Entity company, BuyerOverride buyerOverride)
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

            int overrideAmount = buyerOverride.ResourceBuyer.m_AmountNeeded;
            int shortfall = math.max(0, buyerOverride.Threshold - buyerOverride.EffectiveStock);
            string companyKey = FormatEntity(company);

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

            if (!m_ProbeResourceAggregates.TryGetValue(buyerOverride.Resource, out ResourceOverrideAggregate aggregate))
            {
                aggregate = new ResourceOverrideAggregate();
            }

            aggregate.Count++;
            aggregate.TotalOverrideAmount += overrideAmount;
            aggregate.MaxOverrideAmount = math.max(aggregate.MaxOverrideAmount, overrideAmount);
            aggregate.MaxShortfall = math.max(aggregate.MaxShortfall, shortfall);
            m_ProbeResourceAggregates[buyerOverride.Resource] = aggregate;

            if (!Mod.Settings.VerboseLogging)
            {
                return;
            }

            TryCaptureTopSample(new BuyerOverrideSample(
                companyKey,
                buyerOverride.Resource,
                buyerOverride.Stock,
                buyerOverride.BuyingLoad,
                buyerOverride.TripNeededAmount,
                buyerOverride.EffectiveStock,
                buyerOverride.Threshold,
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

        private sealed class ResourceOverrideAggregate
        {
            public int Count;
            public int TotalOverrideAmount;
            public int MaxOverrideAmount;
            public int MaxShortfall;
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
