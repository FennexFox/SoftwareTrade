using System;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Pathfind;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using HarmonyLib;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using BuildingComponent = Game.Buildings.Building;
using CargoTransportStationComponent = Game.Buildings.CargoTransportStation;
using DeliveryTruckComponent = Game.Vehicles.DeliveryTruck;
using OutsideConnectionComponent = Game.Objects.OutsideConnection;
using TripNeeded = Game.Citizens.TripNeeded;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch(typeof(ResourcePathfindSetup), nameof(ResourcePathfindSetup.SetupResourceSeller), new[] { typeof(PathfindSetupSystem), typeof(PathfindSetupSystem.SetupData), typeof(JobHandle) })]
    public static class OutsideConnectionVirtualSellerFixPatch
    {
        private static readonly EntityQueryDesc s_ResourceSellerQueryDesc = new EntityQueryDesc
        {
            All = new ComponentType[2]
            {
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.ReadOnly<Game.Economy.Resources>()
            },
            Any = new ComponentType[3]
            {
                ComponentType.ReadOnly<Game.Companies.StorageCompany>(),
                ComponentType.ReadOnly<CargoTransportStationComponent>(),
                ComponentType.ReadOnly<ResourceSeller>()
            },
            None = new ComponentType[6]
            {
                ComponentType.ReadOnly<ShipStop>(),
                ComponentType.ReadOnly<AirplaneStop>(),
                ComponentType.ReadOnly<TrainStop>(),
                ComponentType.ReadOnly<Deleted>(),
                ComponentType.ReadOnly<Destroyed>(),
                ComponentType.ReadOnly<Temp>()
            }
        };

        private static bool s_RuntimeFailureLogged;

        public static bool Prefix(
            PathfindSetupSystem system,
            PathfindSetupSystem.SetupData setupData,
            JobHandle inputDeps,
            ref JobHandle __result)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableOutsideConnectionVirtualSellerFix)
            {
                return true;
            }

            try
            {
                __result = SchedulePatchedSellerSetup(system, setupData, inputDeps);
                return false;
            }
            catch (Exception ex)
            {
                if (!s_RuntimeFailureLogged)
                {
                    Mod.log.Error($"Outside-connection virtual seller fix failed at runtime. Falling back to vanilla behavior. {ex}");
                    s_RuntimeFailureLogged = true;
                }

                return true;
            }
        }

        private static JobHandle SchedulePatchedSellerSetup(
            PathfindSetupSystem system,
            PathfindSetupSystem.SetupData setupData,
            JobHandle inputDeps)
        {
            EntityQuery query = system.GetSetupQuery(s_ResourceSellerQueryDesc);
            JobHandle jobHandle = new SetupResourceSellerJob
            {
                m_EntityType = system.GetEntityTypeHandle(),
                m_OwnedVehicles = system.GetBufferTypeHandle<OwnedVehicle>(isReadOnly: true),
                m_IndustrialProcessDatas = system.GetComponentLookup<IndustrialProcessData>(isReadOnly: true),
                m_ServiceAvailables = system.GetComponentLookup<ServiceAvailable>(isReadOnly: true),
                m_StorageCompanyDatas = system.GetComponentLookup<StorageCompanyData>(isReadOnly: true),
                m_PropertyRenters = system.GetComponentLookup<PropertyRenter>(isReadOnly: true),
                m_Resources = system.GetBufferLookup<Game.Economy.Resources>(isReadOnly: true),
                m_TradeCosts = system.GetBufferLookup<TradeCost>(isReadOnly: true),
                m_OutsideConnections = system.GetComponentLookup<OutsideConnectionComponent>(isReadOnly: true),
                m_CargoTransportStations = system.GetComponentLookup<CargoTransportStationComponent>(isReadOnly: true),
                m_StorageTransferRequestType = system.GetBufferTypeHandle<StorageTransferRequest>(isReadOnly: true),
                m_TransportCompanyDatas = system.GetComponentLookup<TransportCompanyData>(isReadOnly: true),
                m_TripNeededType = system.GetBufferTypeHandle<TripNeeded>(isReadOnly: true),
                m_Prefabs = system.GetComponentLookup<PrefabRef>(isReadOnly: true),
                m_Buildings = system.GetComponentLookup<BuildingComponent>(isReadOnly: true),
                m_DeliveryTrucks = system.GetComponentLookup<DeliveryTruckComponent>(isReadOnly: true),
                m_GuestVehicleBufs = system.GetBufferLookup<GuestVehicle>(isReadOnly: true),
                m_LayoutElementBufs = system.GetBufferLookup<LayoutElement>(isReadOnly: true),
                m_RandomSeed = RandomSeed.Next(),
                m_SetupData = setupData
            }.ScheduleParallel(query, inputDeps);

            World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<ResourceSystem>().AddPrefabsReader(jobHandle);
            return jobHandle;
        }

        [BurstCompile]
        private struct SetupResourceSellerJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public BufferTypeHandle<OwnedVehicle> m_OwnedVehicles;

            [ReadOnly]
            public ComponentLookup<IndustrialProcessData> m_IndustrialProcessDatas;

            [ReadOnly]
            public ComponentLookup<ServiceAvailable> m_ServiceAvailables;

            [ReadOnly]
            public ComponentLookup<StorageCompanyData> m_StorageCompanyDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> m_PropertyRenters;

            [ReadOnly]
            public BufferLookup<Game.Economy.Resources> m_Resources;

            [ReadOnly]
            public BufferLookup<TradeCost> m_TradeCosts;

            [ReadOnly]
            public ComponentLookup<OutsideConnectionComponent> m_OutsideConnections;

            [ReadOnly]
            public ComponentLookup<CargoTransportStationComponent> m_CargoTransportStations;

            [ReadOnly]
            public BufferTypeHandle<StorageTransferRequest> m_StorageTransferRequestType;

            [ReadOnly]
            public ComponentLookup<TransportCompanyData> m_TransportCompanyDatas;

            [ReadOnly]
            public BufferTypeHandle<TripNeeded> m_TripNeededType;

            [ReadOnly]
            public ComponentLookup<PrefabRef> m_Prefabs;

            [ReadOnly]
            public ComponentLookup<BuildingComponent> m_Buildings;

            [ReadOnly]
            public ComponentLookup<DeliveryTruckComponent> m_DeliveryTrucks;

            [ReadOnly]
            public BufferLookup<GuestVehicle> m_GuestVehicleBufs;

            [ReadOnly]
            public BufferLookup<LayoutElement> m_LayoutElementBufs;

            [ReadOnly]
            public RandomSeed m_RandomSeed;

            public PathfindSetupSystem.SetupData m_SetupData;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                BufferAccessor<StorageTransferRequest> storageTransferRequests = chunk.GetBufferAccessor(ref m_StorageTransferRequestType);
                BufferAccessor<TripNeeded> tripNeededs = chunk.GetBufferAccessor(ref m_TripNeededType);
                BufferAccessor<OwnedVehicle> ownedVehicles = chunk.GetBufferAccessor(ref m_OwnedVehicles);
                Unity.Mathematics.Random random = m_RandomSeed.GetRandom(unfilteredChunkIndex);

                for (int setupIndex = 0; setupIndex < m_SetupData.Length; setupIndex++)
                {
                    m_SetupData.GetItem(setupIndex, out Entity seekerEntity, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                    Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                    int requestedAmount = targetSeeker.m_SetupQueueTarget.m_Value;
                    SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;

                    if ((targetFlags & SetupTargetFlags.RequireTransport) != SetupTargetFlags.None && ownedVehicles.Length == 0)
                    {
                        continue;
                    }

                    for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                    {
                        Entity sellerEntity = entities[entityIndex];
                        if (sellerEntity.Equals(seekerEntity))
                        {
                            continue;
                        }

                        Entity prefab = m_Prefabs[sellerEntity].m_Prefab;
                        int transferRequestCount = storageTransferRequests.Length > 0 ? storageTransferRequests[entityIndex].Length : 0;
                        bool isOutsideConnection = m_OutsideConnections.HasComponent(sellerEntity);
                        bool isCargoTransportStation = m_CargoTransportStations.HasComponent(sellerEntity);
                        bool isStorageCompany = m_StorageCompanyDatas.HasComponent(prefab) && !isCargoTransportStation && !isOutsideConnection;
                        bool isCommercialService = m_ServiceAvailables.HasComponent(sellerEntity);
                        bool isIndustrialProducer = m_IndustrialProcessDatas.HasComponent(prefab) && !isCommercialService && !isStorageCompany;
                        bool isOfficeResource = EconomyUtils.IsOfficeResource(resource);
                        bool isBuildingUpkeep = (targetFlags & SetupTargetFlags.BuildingUpkeep) != 0;

                        if ((m_Buildings.HasComponent(sellerEntity) && BuildingUtils.CheckOption(m_Buildings[sellerEntity], BuildingOption.Inactive)) ||
                            ((isCommercialService || isIndustrialProducer) && (!m_PropertyRenters.HasComponent(sellerEntity) || m_PropertyRenters[sellerEntity].m_Property == Entity.Null)))
                        {
                            continue;
                        }

                        bool eligible = false;
                        if (isOfficeResource && isIndustrialProducer && (m_IndustrialProcessDatas[prefab].m_Output.m_Resource & resource) != Resource.NoResource)
                        {
                            eligible = true;
                        }
                        else if ((targetFlags & SetupTargetFlags.Commercial) != 0 && isCommercialService && (m_IndustrialProcessDatas[prefab].m_Output.m_Resource & resource) != Resource.NoResource)
                        {
                            eligible = true;
                        }
                        else if ((targetFlags & SetupTargetFlags.Industrial) != 0 && isIndustrialProducer && (m_IndustrialProcessDatas[prefab].m_Output.m_Resource & resource) != Resource.NoResource)
                        {
                            eligible = true;
                        }
                        else if ((targetFlags & SetupTargetFlags.Import) != SetupTargetFlags.None &&
                                 (isOutsideConnection || isCargoTransportStation || isStorageCompany) &&
                                 AllowsImportSeller(prefab, resource, isOfficeResource, isOutsideConnection))
                        {
                            eligible = true;
                        }

                        if (!eligible || (!isOutsideConnection && isBuildingUpkeep && tripNeededs.Length > 0 && tripNeededs[entityIndex].Length > 0))
                        {
                            continue;
                        }

                        int allBuyingResourcesTrucks = VehicleUtils.GetAllBuyingResourcesTrucks(sellerEntity, resource, ref m_DeliveryTrucks, ref m_GuestVehicleBufs, ref m_LayoutElementBufs);
                        int availableAmount = EconomyUtils.GetResources(resource, m_Resources[sellerEntity]) - allBuyingResourcesTrucks;
                        if (availableAmount <= 0 || (!isOutsideConnection && availableAmount < requestedAmount / 2))
                        {
                            continue;
                        }

                        float penalty = 0f;
                        if (m_ServiceAvailables.HasComponent(sellerEntity))
                        {
                            penalty -= math.min(availableAmount, m_ServiceAvailables[sellerEntity].m_ServiceAvailable) * 100f;
                        }
                        else
                        {
                            float fillRatio = math.min(1f, availableAmount * 1f / requestedAmount);
                            penalty += 100f * (1f - fillRatio);
                            if (isCargoTransportStation)
                            {
                                if ((targetFlags & SetupTargetFlags.RequireTransport) != SetupTargetFlags.None)
                                {
                                    if (!m_TransportCompanyDatas.HasComponent(prefab))
                                    {
                                        continue;
                                    }

                                    TransportCompanyData transportCompanyData = m_TransportCompanyDatas[prefab];
                                    if (ownedVehicles[entityIndex].Length >= transportCompanyData.m_MaxTransports)
                                    {
                                        continue;
                                    }
                                }

                                if (tripNeededs.Length > 0 && tripNeededs[entityIndex].Length >= ResourcePathfindSetup.kCargoStationMaxTripNeededQueue)
                                {
                                    continue;
                                }

                                penalty += ResourcePathfindSetup.kCargoStationAmountBasedPenalty * requestedAmount;
                                penalty += ResourcePathfindSetup.kCargoStationPerRequestPenalty * transferRequestCount;
                            }

                            if (isOutsideConnection)
                            {
                                penalty += ResourcePathfindSetup.kOutsideConnectionAmountBasedPenalty * requestedAmount;
                                if (isBuildingUpkeep)
                                {
                                    penalty += random.NextInt(300);
                                }
                            }
                        }

                        if (m_TradeCosts.HasBuffer(sellerEntity))
                        {
                            DynamicBuffer<TradeCost> costs = m_TradeCosts[sellerEntity];
                            penalty += EconomyUtils.GetTradeCost(resource, costs).m_BuyCost * requestedAmount * 0.01f;
                        }

                        targetSeeker.FindTargets(sellerEntity, penalty * 100f);
                    }
                }
            }

            private bool AllowsImportSeller(Entity prefab, Resource resource, bool isOfficeResource, bool isOutsideConnection)
            {
                if (isOfficeResource && isOutsideConnection)
                {
                    return m_StorageCompanyDatas.HasComponent(prefab);
                }

                return m_StorageCompanyDatas.HasComponent(prefab) &&
                       (m_StorageCompanyDatas[prefab].m_StoredResources & resource) != Resource.NoResource;
            }
        }
    }
}
