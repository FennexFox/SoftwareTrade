using System.Globalization;
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

        private ResourceSystem m_ResourceSystem;
        private EntityQuery m_OfficeCompanyQuery;

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
                ComponentType.ReadWrite<CitizenTripNeeded>(),
                ComponentType.Exclude<ResourceBuyer>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            RequireForUpdate(m_OfficeCompanyQuery);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (Mod.Settings == null || !Mod.Settings.EnableVirtualOfficeResourceBuyerFix)
            {
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
                MaybeLogProbe(company, buyerOverride);
            }
        }

        private bool TryBuildOverride(Entity company, ResourcePrefabs resourcePrefabs, out BuyerOverride buyerOverride)
        {
            buyerOverride = default;

            if (!EntityManager.HasComponent<PrefabRef>(company) ||
                !EntityManager.HasComponent<PropertyRenter>(company))
            {
                return false;
            }

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

            if (EntityManager.HasComponent<PathInformation>(company) ||
                EntityManager.HasBuffer<CurrentTrading>(company))
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
            effectiveStock = stock + buyingLoad + tripNeededAmount;
            threshold = (int)math.max(kResourceLowStockAmount, maxCapacity * 0.25f);
            if (effectiveStock >= threshold)
            {
                return false;
            }

            selectedResource = resource;
            return true;
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

        private static void MaybeLogProbe(Entity company, BuyerOverride buyerOverride)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableDemandDiagnostics || !Mod.Settings.VerboseLogging)
            {
                return;
            }

            if (buyerOverride.ResourceBuyer.m_AmountNeeded <= kResourceMinimumRequestAmount)
            {
                return;
            }

            Mod.log.Info(MachineParsedLogContract.FormatVirtualOfficeBuyerFixProbe(
                "override",
                $"company={FormatEntity(company)}, resource={buyerOverride.Resource}, original_amount={kResourceMinimumRequestAmount}, override_amount={buyerOverride.ResourceBuyer.m_AmountNeeded}, stock={buyerOverride.Stock}, buying_load={buyerOverride.BuyingLoad}, trip_needed_amount={buyerOverride.TripNeededAmount}, effective_stock={buyerOverride.EffectiveStock}, threshold={buyerOverride.Threshold}"));
        }

        private static string FormatEntity(Entity entity)
        {
            return entity.Index.ToString(CultureInfo.InvariantCulture) + ":" + entity.Version.ToString(CultureInfo.InvariantCulture);
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
