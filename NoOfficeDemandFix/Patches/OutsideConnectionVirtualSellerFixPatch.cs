using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Objects;
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
using DeliveryTruckComponent = Game.Vehicles.DeliveryTruck;
using OutsideConnectionComponent = Game.Objects.OutsideConnection;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch]
    public static class OutsideConnectionVirtualSellerFixPatch
    {
        private const string PatchVariant = "findtargets-postfix-v3";
        private const int kMaxProbeSampleLogs = 3;
        private const float kTargetPenaltyScale = 100f;
        private const int kBuildingUpkeepPenaltyRandomRange = 300;

        private static bool s_ActivationLogged;
        private static bool s_OutsideConnectionSellerQueryInitialized;
        private static bool s_RuntimeFailureLogged;
        private static int s_ResourceSellerCallCount;
        private static int s_CallsWithOfficeImportSeekers;
        private static int s_TotalOfficeImportSeekers;
        private static int s_SellerStateSamplesCaptured;
        private static int s_TotalOutsideConnectionSellers;
        private static int s_TotalMissingStoredResourcePairs;
        private static int s_TotalInactiveOutsideConnections;
        private static int s_ProbeSampleLogsEmitted;
        private static readonly HashSet<Resource> s_ObservedOfficeImportResources = new HashSet<Resource>();
        private static EntityQuery s_OutsideConnectionSellerQuery;
        private static World s_OutsideConnectionSellerQueryWorld;

        private readonly struct ProbeSnapshot
        {
            public ProbeSnapshot(
                int officeImportSeekers,
                int outsideConnectionSellers,
                int missingStoredResourcePairs,
                int inactiveOutsideConnections,
                string requestedResources)
            {
                OfficeImportSeekers = officeImportSeekers;
                OutsideConnectionSellers = outsideConnectionSellers;
                MissingStoredResourcePairs = missingStoredResourcePairs;
                InactiveOutsideConnections = inactiveOutsideConnections;
                RequestedResources = requestedResources;
            }

            public int OfficeImportSeekers { get; }
            public int OutsideConnectionSellers { get; }
            public int MissingStoredResourcePairs { get; }
            public int InactiveOutsideConnections { get; }
            public string RequestedResources { get; }
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(PathfindSetupSystem),
                "FindTargets",
                new[] { typeof(SetupTargetType), typeof(PathfindSetupSystem.SetupData).MakeByRefType() });
        }

        public static void Postfix(
            PathfindSetupSystem __instance,
            SetupTargetType targetType,
            in PathfindSetupSystem.SetupData setupData,
            ref JobHandle __result)
        {
            if (Mod.Settings == null
                || !Mod.Settings.EnableOutsideConnectionVirtualSellerFix
                || targetType != SetupTargetType.ResourceSeller)
            {
                return;
            }

            try
            {
                s_ResourceSellerCallCount++;

                if (!s_ActivationLogged)
                {
                    Mod.log.Info($"Outside-connection virtual seller fix active ({PatchVariant}); appending additional outside-connection office import targets after PathfindSetupSystem.FindTargets(ResourceSeller).");
                    s_ActivationLogged = true;
                }

                int officeImportSeekers = CountOfficeImportSeekers(setupData);
                if (officeImportSeekers == 0)
                {
                    return;
                }

                s_CallsWithOfficeImportSeekers++;
                s_TotalOfficeImportSeekers += officeImportSeekers;

                MaybeCaptureProbeSnapshot(__instance, setupData, officeImportSeekers);
                __result = ScheduleAdditionalOutsideConnectionTargets(__instance, setupData, __result);
            }
            catch (Exception ex)
            {
                if (!s_RuntimeFailureLogged)
                {
                    Mod.log.Error($"Outside-connection virtual seller fix failed while appending additional outside-connection targets ({PatchVariant}). Keeping vanilla seller setup only. {ex}");
                    s_RuntimeFailureLogged = true;
                }
            }
        }

        public static void LogProbeSummary()
        {
            if (!s_ActivationLogged || s_ResourceSellerCallCount == 0)
            {
                return;
            }

            StringBuilder summary = new StringBuilder();
            summary.Append("patch_variant=").Append(PatchVariant);
            summary.Append(", resource_seller_calls=").Append(s_ResourceSellerCallCount);
            summary.Append(", calls_with_office_import_seekers=").Append(s_CallsWithOfficeImportSeekers);
            summary.Append(", office_import_seekers=").Append(s_TotalOfficeImportSeekers);
            summary.Append(", requested_resources=[").Append(FormatResourceSet(s_ObservedOfficeImportResources)).Append(']');
            summary.Append(", seller_state_captured=").Append(s_SellerStateSamplesCaptured > 0);
            if (s_SellerStateSamplesCaptured > 0)
            {
                summary.Append(", seller_state_samples=").Append(s_SellerStateSamplesCaptured);
                summary.Append(", outside_connection_sellers=").Append(s_TotalOutsideConnectionSellers);
                summary.Append(", missing_stored_resource_pairs=").Append(s_TotalMissingStoredResourcePairs);
                summary.Append(", inactive_outside_connections=").Append(s_TotalInactiveOutsideConnections);
            }

            summary.Append(", sampled_calls=").Append(s_ProbeSampleLogsEmitted);
            Mod.log.Info(MachineParsedLogContract.FormatTradePatchProbe("summary", summary.ToString()));

            s_ActivationLogged = false;
            s_RuntimeFailureLogged = false;
            s_ResourceSellerCallCount = 0;
            s_CallsWithOfficeImportSeekers = 0;
            s_TotalOfficeImportSeekers = 0;
            s_SellerStateSamplesCaptured = 0;
            s_TotalOutsideConnectionSellers = 0;
            s_TotalMissingStoredResourcePairs = 0;
            s_TotalInactiveOutsideConnections = 0;
            s_ProbeSampleLogsEmitted = 0;
            s_ObservedOfficeImportResources.Clear();
        }

        private static int CountOfficeImportSeekers(in PathfindSetupSystem.SetupData setupData)
        {
            int officeImportSeekers = 0;

            for (int setupIndex = 0; setupIndex < setupData.Length; setupIndex++)
            {
                setupData.GetItem(setupIndex, out _, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;
                if (!EconomyUtils.IsOfficeResource(resource) || (targetFlags & SetupTargetFlags.Import) == SetupTargetFlags.None)
                {
                    continue;
                }

                officeImportSeekers++;
                s_ObservedOfficeImportResources.Add(resource);
            }

            return officeImportSeekers;
        }

        private static JobHandle ScheduleAdditionalOutsideConnectionTargets(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            JobHandle inputDeps)
        {
            if (setupData.Length == 0)
            {
                return inputDeps;
            }

            EntityQuery query = GetOutsideConnectionSellerQuery(system);
            if (query.IsEmptyIgnoreFilter)
            {
                return inputDeps;
            }

            JobHandle jobHandle = new AppendOutsideConnectionOfficeImportTargetsJob
            {
                m_EntityType = system.GetEntityTypeHandle(),
                m_PrefabType = system.GetComponentTypeHandle<PrefabRef>(isReadOnly: true),
                m_ResourceType = system.GetBufferTypeHandle<Game.Economy.Resources>(isReadOnly: true),
                m_SeekerOwnedVehicles = system.GetBufferLookup<OwnedVehicle>(isReadOnly: true),
                m_StorageCompanyDatas = system.GetComponentLookup<StorageCompanyData>(isReadOnly: true),
                m_TradeCosts = system.GetBufferLookup<TradeCost>(isReadOnly: true),
                m_Buildings = system.GetComponentLookup<BuildingComponent>(isReadOnly: true),
                m_DeliveryTrucks = system.GetComponentLookup<DeliveryTruckComponent>(isReadOnly: true),
                m_GuestVehicleBufs = system.GetBufferLookup<GuestVehicle>(isReadOnly: true),
                m_LayoutElementBufs = system.GetBufferLookup<LayoutElement>(isReadOnly: true),
                m_RandomSeed = RandomSeed.Next(),
                m_SetupData = setupData
            }.ScheduleParallel(query, inputDeps);

            system.World.GetOrCreateSystemManaged<ResourceSystem>().AddPrefabsReader(jobHandle);
            return jobHandle;
        }

        private static void MaybeCaptureProbeSnapshot(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            int officeImportSeekers)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableDemandDiagnostics || !Mod.Settings.VerboseLogging)
            {
                return;
            }

            ProbeSnapshot snapshot = CaptureProbeSnapshot(system, setupData, officeImportSeekers);
            s_SellerStateSamplesCaptured++;
            s_TotalOutsideConnectionSellers += snapshot.OutsideConnectionSellers;
            s_TotalMissingStoredResourcePairs += snapshot.MissingStoredResourcePairs;
            s_TotalInactiveOutsideConnections += snapshot.InactiveOutsideConnections;

            if (s_ProbeSampleLogsEmitted >= kMaxProbeSampleLogs)
            {
                return;
            }

            if (snapshot.MissingStoredResourcePairs <= 0)
            {
                return;
            }

            s_ProbeSampleLogsEmitted++;
            Mod.log.Info(MachineParsedLogContract.FormatTradePatchProbe(
                "sample",
                $"patch_variant={PatchVariant}, call_index={s_ResourceSellerCallCount}, office_import_seekers={snapshot.OfficeImportSeekers}, outside_connection_sellers={snapshot.OutsideConnectionSellers}, missing_stored_resource_pairs={snapshot.MissingStoredResourcePairs}, inactive_outside_connections={snapshot.InactiveOutsideConnections}, requested_resources=[{snapshot.RequestedResources}]"));
        }

        private static ProbeSnapshot CaptureProbeSnapshot(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            int officeImportSeekers)
        {
            EntityQuery query = GetOutsideConnectionSellerQuery(system);
            EntityManager entityManager = system.EntityManager;

            HashSet<Resource> requestedResources = new HashSet<Resource>();

            for (int setupIndex = 0; setupIndex < setupData.Length; setupIndex++)
            {
                setupData.GetItem(setupIndex, out _, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;
                if (!EconomyUtils.IsOfficeResource(resource) || (targetFlags & SetupTargetFlags.Import) == SetupTargetFlags.None)
                {
                    continue;
                }

                requestedResources.Add(resource);
            }

            if (officeImportSeekers == 0)
            {
                return default;
            }

            int outsideConnectionSellers = 0;
            int missingStoredResourcePairs = 0;
            int inactiveOutsideConnections = 0;

            using NativeArray<Entity> sellerEntities = query.ToEntityArray(Allocator.Temp);
            using NativeArray<PrefabRef> sellerPrefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp);

            for (int entityIndex = 0; entityIndex < sellerEntities.Length; entityIndex++)
            {
                Entity sellerEntity = sellerEntities[entityIndex];
                outsideConnectionSellers++;

                if (entityManager.HasComponent<BuildingComponent>(sellerEntity) &&
                    BuildingUtils.CheckOption(entityManager.GetComponentData<BuildingComponent>(sellerEntity), BuildingOption.Inactive))
                {
                    inactiveOutsideConnections++;
                    continue;
                }

                Entity prefab = sellerPrefabs[entityIndex].m_Prefab;
                if (!entityManager.HasComponent<StorageCompanyData>(prefab))
                {
                    continue;
                }

                StorageCompanyData storageCompanyData = entityManager.GetComponentData<StorageCompanyData>(prefab);
                foreach (Resource resource in requestedResources)
                {
                    if ((storageCompanyData.m_StoredResources & resource) == Resource.NoResource)
                    {
                        missingStoredResourcePairs++;
                    }
                }
            }

            return new ProbeSnapshot(
                officeImportSeekers,
                outsideConnectionSellers,
                missingStoredResourcePairs,
                inactiveOutsideConnections,
                FormatResourceSet(requestedResources));
        }

        private static EntityQuery GetOutsideConnectionSellerQuery(PathfindSetupSystem system)
        {
            if (!s_OutsideConnectionSellerQueryInitialized || s_OutsideConnectionSellerQueryWorld != system.World)
            {
                s_OutsideConnectionSellerQuery = system.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<OutsideConnectionComponent>(),
                        ComponentType.ReadOnly<PrefabRef>(),
                        ComponentType.ReadOnly<Game.Economy.Resources>()
                    },
                    None = new[]
                    {
                        ComponentType.ReadOnly<Deleted>(),
                        ComponentType.ReadOnly<Temp>()
                    }
                });
                s_OutsideConnectionSellerQueryWorld = system.World;
                s_OutsideConnectionSellerQueryInitialized = true;
            }

            return s_OutsideConnectionSellerQuery;
        }

        private static string FormatResourceSet(IEnumerable<Resource> resources)
        {
            StringBuilder builder = new StringBuilder();
            bool first = true;
            foreach (Resource resource in resources)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(resource);
                first = false;
            }

            return builder.ToString();
        }

        [BurstCompile]
        private struct AppendOutsideConnectionOfficeImportTargetsJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle m_EntityType;

            [ReadOnly]
            public ComponentTypeHandle<PrefabRef> m_PrefabType;

            [ReadOnly]
            public BufferTypeHandle<Game.Economy.Resources> m_ResourceType;

            [ReadOnly]
            public BufferLookup<OwnedVehicle> m_SeekerOwnedVehicles;

            [ReadOnly]
            public ComponentLookup<StorageCompanyData> m_StorageCompanyDatas;

            [ReadOnly]
            public BufferLookup<TradeCost> m_TradeCosts;

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
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref m_PrefabType);
                BufferAccessor<Game.Economy.Resources> resources = chunk.GetBufferAccessor(ref m_ResourceType);
                Unity.Mathematics.Random random = m_RandomSeed.GetRandom(unfilteredChunkIndex);

                for (int setupIndex = 0; setupIndex < m_SetupData.Length; setupIndex++)
                {
                    m_SetupData.GetItem(setupIndex, out Entity seekerEntity, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                    Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                    int requestedAmount = targetSeeker.m_SetupQueueTarget.m_Value;
                    SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;

                    if (!EconomyUtils.IsOfficeResource(resource) || (targetFlags & SetupTargetFlags.Import) == SetupTargetFlags.None)
                    {
                        continue;
                    }

                    if ((targetFlags & SetupTargetFlags.RequireTransport) != SetupTargetFlags.None &&
                        (!m_SeekerOwnedVehicles.HasBuffer(seekerEntity) || m_SeekerOwnedVehicles[seekerEntity].Length == 0))
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

                        Entity prefab = prefabs[entityIndex].m_Prefab;
                        bool isBuildingUpkeep = (targetFlags & SetupTargetFlags.BuildingUpkeep) != 0;

                        if (m_Buildings.HasComponent(sellerEntity) && BuildingUtils.CheckOption(m_Buildings[sellerEntity], BuildingOption.Inactive))
                        {
                            continue;
                        }

                        if (!m_StorageCompanyDatas.HasComponent(prefab))
                        {
                            continue;
                        }

                        StorageCompanyData storageCompanyData = m_StorageCompanyDatas[prefab];
                        if ((storageCompanyData.m_StoredResources & resource) != Resource.NoResource)
                        {
                            continue;
                        }

                        int allBuyingResourcesTrucks = VehicleUtils.GetAllBuyingResourcesTrucks(sellerEntity, resource, ref m_DeliveryTrucks, ref m_GuestVehicleBufs, ref m_LayoutElementBufs);
                        int availableAmount = EconomyUtils.GetResources(resource, resources[entityIndex]) - allBuyingResourcesTrucks;
                        if (availableAmount <= 0)
                        {
                            continue;
                        }

                        float fillRatio = math.min(1f, availableAmount * 1f / math.max(1, requestedAmount));
                        float penalty = kTargetPenaltyScale * (1f - fillRatio);
                        // Keep the amount-based component aligned with the vanilla outside-connection scoring curve.
                        penalty += ResourcePathfindSetup.kOutsideConnectionAmountBasedPenalty * requestedAmount;
                        if (isBuildingUpkeep)
                        {
                            penalty += random.NextInt(kBuildingUpkeepPenaltyRandomRange);
                        }

                        if (m_TradeCosts.HasBuffer(sellerEntity))
                        {
                            DynamicBuffer<TradeCost> costs = m_TradeCosts[sellerEntity];
                            penalty += EconomyUtils.GetTradeCost(resource, costs).m_BuyCost * requestedAmount * 0.01f;
                        }

                        targetSeeker.FindTargets(sellerEntity, penalty * kTargetPenaltyScale);
                    }
                }
            }
        }
    }
}
