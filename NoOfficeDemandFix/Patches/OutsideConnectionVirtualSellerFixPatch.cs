using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using NoOfficeDemandFix.Telemetry;
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
        private static int s_CallsWithOfficeImportCandidatesPrefilter;
        private static int s_TotalOfficeImportCandidatesPrefilter;
        private static int s_SellerStateSamplesCaptured;
        private static int s_TotalOutsideConnectionSellers;
        private static int s_TotalMissingStoredResourcePairs;
        private static int s_TotalInactiveOutsideConnections;
        private static int s_ProbeSampleLogsEmitted;
        private static readonly HashSet<Resource> s_ObservedRequestedResourcesPrefilter = new HashSet<Resource>();
        private static readonly Dictionary<Entity, OfficeImportProbeSnapshot> s_LastOfficeImportProbeSnapshots = new Dictionary<Entity, OfficeImportProbeSnapshot>();
        private static EntityQuery s_OutsideConnectionSellerQuery;
        private static World s_OutsideConnectionSellerQueryWorld;

        private struct OfficeImportRequest
        {
            public Entity SeekerEntity;
            public PathfindTargetSeeker<PathfindSetupBuffer> TargetSeeker;
            public int RequestedAmount;
            public SetupTargetFlags TargetFlags;
            public int ResourceIndex;
        }

        private struct ResourceRequestRange
        {
            public int StartIndex;
            public int Count;
        }

        private readonly struct ProbeSnapshot
        {
            public ProbeSnapshot(
                int officeImportCandidatesPrefilter,
                int outsideConnectionSellers,
                int missingStoredResourcePairs,
                int inactiveOutsideConnections,
                string requestedResourcesPrefilter)
            {
                OfficeImportCandidatesPrefilter = officeImportCandidatesPrefilter;
                OutsideConnectionSellers = outsideConnectionSellers;
                MissingStoredResourcePairs = missingStoredResourcePairs;
                InactiveOutsideConnections = inactiveOutsideConnections;
                RequestedResourcesPrefilter = requestedResourcesPrefilter;
            }

            public int OfficeImportCandidatesPrefilter { get; }
            public int OutsideConnectionSellers { get; }
            public int MissingStoredResourcePairs { get; }
            public int InactiveOutsideConnections { get; }
            public string RequestedResourcesPrefilter { get; }
        }

        public readonly struct OfficeImportProbeSnapshot
        {
            public OfficeImportProbeSnapshot(
                int simulationFrame,
                Resource resource,
                int requestedAmount,
                int totalOutsideConnectionSellers,
                int missingStoredResourcePairs,
                int inactiveOutsideConnections,
                int availableCandidateCount,
                int zeroOrNegativeStockSellerCount,
                int topAvailableStock,
                bool appendedOutsideConnectionCandidates)
            {
                SimulationFrame = simulationFrame;
                Resource = resource;
                RequestedAmount = requestedAmount;
                TotalOutsideConnectionSellers = totalOutsideConnectionSellers;
                MissingStoredResourcePairs = missingStoredResourcePairs;
                InactiveOutsideConnections = inactiveOutsideConnections;
                AvailableCandidateCount = availableCandidateCount;
                ZeroOrNegativeStockSellerCount = zeroOrNegativeStockSellerCount;
                TopAvailableStock = topAvailableStock;
                AppendedOutsideConnectionCandidates = appendedOutsideConnectionCandidates;
            }

            public int SimulationFrame { get; }
            public Resource Resource { get; }
            public int RequestedAmount { get; }
            public int TotalOutsideConnectionSellers { get; }
            public int MissingStoredResourcePairs { get; }
            public int InactiveOutsideConnections { get; }
            public int AvailableCandidateCount { get; }
            public int ZeroOrNegativeStockSellerCount { get; }
            public int TopAvailableStock { get; }
            public bool AppendedOutsideConnectionCandidates { get; }
        }

        private readonly struct SoftwareOutsideConnectionProbeAggregate
        {
            public SoftwareOutsideConnectionProbeAggregate(
                int totalOutsideConnectionSellers,
                int missingStoredResourcePairs,
                int inactiveOutsideConnections,
                int availableCandidateCount,
                int zeroOrNegativeStockSellerCount,
                int topAvailableStock)
            {
                TotalOutsideConnectionSellers = totalOutsideConnectionSellers;
                MissingStoredResourcePairs = missingStoredResourcePairs;
                InactiveOutsideConnections = inactiveOutsideConnections;
                AvailableCandidateCount = availableCandidateCount;
                ZeroOrNegativeStockSellerCount = zeroOrNegativeStockSellerCount;
                TopAvailableStock = topAvailableStock;
            }

            public int TotalOutsideConnectionSellers { get; }
            public int MissingStoredResourcePairs { get; }
            public int InactiveOutsideConnections { get; }
            public int AvailableCandidateCount { get; }
            public int ZeroOrNegativeStockSellerCount { get; }
            public int TopAvailableStock { get; }
        }

        public static bool TryGetLatestOfficeImportProbeSnapshot(Entity seekerEntity, Resource resource, out OfficeImportProbeSnapshot snapshot)
        {
            if (resource != Resource.Software)
            {
                snapshot = default;
                return false;
            }

            return s_LastOfficeImportProbeSnapshots.TryGetValue(seekerEntity, out snapshot);
        }

        public static void ResetDetailedRequestProbes()
        {
            s_LastOfficeImportProbeSnapshots.Clear();
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
            bool captureTelemetry = PerformanceTelemetryCollector.IsCollecting;
            long telemetryStart = captureTelemetry ? Stopwatch.GetTimestamp() : 0L;
            int appendedRequestCount = 0;

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

                int officeImportCandidatesPrefilter = CountOfficeImportCandidatesPrefilter(setupData);
                if (officeImportCandidatesPrefilter == 0)
                {
                    return;
                }

                s_CallsWithOfficeImportCandidatesPrefilter++;
                s_TotalOfficeImportCandidatesPrefilter += officeImportCandidatesPrefilter;

                MaybeCaptureProbeSnapshot(__instance, setupData, officeImportCandidatesPrefilter);
                __result = ScheduleAdditionalOutsideConnectionTargets(__instance, setupData, __result, out appendedRequestCount);
            }
            catch (Exception ex)
            {
                if (!s_RuntimeFailureLogged)
                {
                    Mod.log.Error($"Outside-connection virtual seller fix failed while appending additional outside-connection targets ({PatchVariant}). Keeping vanilla seller setup only. {ex}");
                    s_RuntimeFailureLogged = true;
                }
            }
            finally
            {
                if (captureTelemetry)
                {
                    PerformanceTelemetryCollector.RecordModUpdateElapsedTicks(Stopwatch.GetTimestamp() - telemetryStart);
                    PerformanceTelemetryCollector.RecordModActivity(0, appendedRequestCount);
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
            summary.Append(", calls_with_office_import_candidates=").Append(s_CallsWithOfficeImportCandidatesPrefilter);
            summary.Append(", office_import_candidates_prefilter=").Append(s_TotalOfficeImportCandidatesPrefilter);
            summary.Append(", requested_resources_prefilter=[").Append(FormatResourceSet(s_ObservedRequestedResourcesPrefilter)).Append(']');
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
            s_CallsWithOfficeImportCandidatesPrefilter = 0;
            s_TotalOfficeImportCandidatesPrefilter = 0;
            s_SellerStateSamplesCaptured = 0;
            s_TotalOutsideConnectionSellers = 0;
            s_TotalMissingStoredResourcePairs = 0;
            s_TotalInactiveOutsideConnections = 0;
            s_ProbeSampleLogsEmitted = 0;
            s_ObservedRequestedResourcesPrefilter.Clear();
            s_LastOfficeImportProbeSnapshots.Clear();
        }

        private static int CountOfficeImportCandidatesPrefilter(in PathfindSetupSystem.SetupData setupData)
        {
            // Keep this snapshot intentionally pre-filtered so diagnostics stay cheap:
            // it records office import candidates before TryCreateOfficeImportRequests()
            // applies the transport gate.
            int officeImportCandidatesPrefilter = 0;

            for (int setupIndex = 0; setupIndex < setupData.Length; setupIndex++)
            {
                setupData.GetItem(setupIndex, out _, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;
                if (!EconomyUtils.IsOfficeResource(resource) || (targetFlags & SetupTargetFlags.Import) == SetupTargetFlags.None)
                {
                    continue;
                }

                officeImportCandidatesPrefilter++;
                s_ObservedRequestedResourcesPrefilter.Add(resource);
            }

            return officeImportCandidatesPrefilter;
        }

        private static JobHandle ScheduleAdditionalOutsideConnectionTargets(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            JobHandle inputDeps,
            out int appendedRequestCount)
        {
            appendedRequestCount = 0;
            if (setupData.Length == 0)
            {
                return inputDeps;
            }

            EntityQuery query = GetOutsideConnectionSellerQuery(system);
            if (query.IsEmptyIgnoreFilter)
            {
                return inputDeps;
            }

            if (!TryCreateOfficeImportRequests(
                    system,
                    setupData,
                    out NativeArray<OfficeImportRequest> officeImportRequests,
                    out NativeArray<Resource> requestedResources,
                    out NativeArray<ResourceRequestRange> resourceRequestRanges))
            {
                return inputDeps;
            }

            appendedRequestCount = officeImportRequests.Length;
            MaybeCaptureDetailedRequestProbes(system, officeImportRequests, requestedResources, resourceRequestRanges);

            JobHandle jobHandle = new AppendOutsideConnectionOfficeImportTargetsJob
            {
                m_EntityType = system.GetEntityTypeHandle(),
                m_PrefabType = system.GetComponentTypeHandle<PrefabRef>(isReadOnly: true),
                m_ResourceType = system.GetBufferTypeHandle<Game.Economy.Resources>(isReadOnly: true),
                m_StorageCompanyDatas = system.GetComponentLookup<StorageCompanyData>(isReadOnly: true),
                m_TradeCosts = system.GetBufferLookup<TradeCost>(isReadOnly: true),
                m_Buildings = system.GetComponentLookup<BuildingComponent>(isReadOnly: true),
                m_DeliveryTrucks = system.GetComponentLookup<DeliveryTruckComponent>(isReadOnly: true),
                m_GuestVehicleBufs = system.GetBufferLookup<GuestVehicle>(isReadOnly: true),
                m_LayoutElementBufs = system.GetBufferLookup<LayoutElement>(isReadOnly: true),
                m_RandomSeed = RandomSeed.Next(),
                m_OfficeImportRequests = officeImportRequests,
                m_RequestedResources = requestedResources,
                m_ResourceRequestRanges = resourceRequestRanges
            }.ScheduleParallel(query, inputDeps);

            // Keep reader registration behind both the scheduled work and TempJob cleanup.
            jobHandle = officeImportRequests.Dispose(jobHandle);
            jobHandle = requestedResources.Dispose(jobHandle);
            jobHandle = resourceRequestRanges.Dispose(jobHandle);
            system.World.GetOrCreateSystemManaged<ResourceSystem>().AddPrefabsReader(jobHandle);
            return jobHandle;
        }

        private static bool TryCreateOfficeImportRequests(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            out NativeArray<OfficeImportRequest> officeImportRequests,
            out NativeArray<Resource> requestedResources,
            out NativeArray<ResourceRequestRange> resourceRequestRanges)
        {
            officeImportRequests = default;
            requestedResources = default;
            resourceRequestRanges = default;

            if (setupData.Length == 0)
            {
                return false;
            }

            EntityManager entityManager = system.EntityManager;
            List<OfficeImportRequest> requests = new List<OfficeImportRequest>(setupData.Length);
            List<Resource> resources = new List<Resource>(4);
            for (int setupIndex = 0; setupIndex < setupData.Length; setupIndex++)
            {
                setupData.GetItem(setupIndex, out Entity seekerEntity, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;
                if (!EconomyUtils.IsOfficeResource(resource) || (targetFlags & SetupTargetFlags.Import) == SetupTargetFlags.None)
                {
                    continue;
                }

                if ((targetFlags & SetupTargetFlags.RequireTransport) != SetupTargetFlags.None &&
                    (!entityManager.HasBuffer<OwnedVehicle>(seekerEntity) || entityManager.GetBuffer<OwnedVehicle>(seekerEntity, isReadOnly: true).Length == 0))
                {
                    continue;
                }

                int resourceIndex = resources.IndexOf(resource);
                if (resourceIndex < 0)
                {
                    resourceIndex = resources.Count;
                    resources.Add(resource);
                }

                requests.Add(new OfficeImportRequest
                {
                    SeekerEntity = seekerEntity,
                    TargetSeeker = targetSeeker,
                    RequestedAmount = targetSeeker.m_SetupQueueTarget.m_Value,
                    TargetFlags = targetFlags,
                    ResourceIndex = resourceIndex
                });
            }

            if (requests.Count == 0)
            {
                return false;
            }

            requests.Sort((left, right) => left.ResourceIndex.CompareTo(right.ResourceIndex));

            officeImportRequests = new NativeArray<OfficeImportRequest>(requests.Count, Allocator.TempJob);
            for (int i = 0; i < requests.Count; i++)
            {
                officeImportRequests[i] = requests[i];
            }

            requestedResources = new NativeArray<Resource>(resources.Count, Allocator.TempJob);
            for (int i = 0; i < resources.Count; i++)
            {
                requestedResources[i] = resources[i];
            }

            resourceRequestRanges = new NativeArray<ResourceRequestRange>(resources.Count, Allocator.TempJob);
            int requestStartIndex = 0;
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                int startIndex = requestStartIndex;
                while (requestStartIndex < requests.Count && requests[requestStartIndex].ResourceIndex == resourceIndex)
                {
                    requestStartIndex++;
                }

                resourceRequestRanges[resourceIndex] = new ResourceRequestRange
                {
                    StartIndex = startIndex,
                    Count = requestStartIndex - startIndex
                };
            }

            return true;
        }

        private static void MaybeCaptureProbeSnapshot(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            int officeImportCandidatesPrefilter)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableDemandDiagnostics || !Mod.Settings.VerboseLogging)
            {
                return;
            }

            ProbeSnapshot snapshot = CaptureProbeSnapshot(system, setupData, officeImportCandidatesPrefilter);
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
                $"patch_variant={PatchVariant}, call_index={s_ResourceSellerCallCount}, office_import_candidates_prefilter={snapshot.OfficeImportCandidatesPrefilter}, outside_connection_sellers={snapshot.OutsideConnectionSellers}, missing_stored_resource_pairs={snapshot.MissingStoredResourcePairs}, inactive_outside_connections={snapshot.InactiveOutsideConnections}, requested_resources_prefilter=[{snapshot.RequestedResourcesPrefilter}]"));
        }

        private static ProbeSnapshot CaptureProbeSnapshot(
            PathfindSetupSystem system,
            in PathfindSetupSystem.SetupData setupData,
            int officeImportCandidatesPrefilter)
        {
            EntityQuery query = GetOutsideConnectionSellerQuery(system);
            EntityManager entityManager = system.EntityManager;

            HashSet<Resource> requestedResourcesPrefilter = new HashSet<Resource>();

            for (int setupIndex = 0; setupIndex < setupData.Length; setupIndex++)
            {
                setupData.GetItem(setupIndex, out _, out PathfindTargetSeeker<PathfindSetupBuffer> targetSeeker);
                Resource resource = targetSeeker.m_SetupQueueTarget.m_Resource;
                SetupTargetFlags targetFlags = targetSeeker.m_SetupQueueTarget.m_Flags;
                if (!EconomyUtils.IsOfficeResource(resource) || (targetFlags & SetupTargetFlags.Import) == SetupTargetFlags.None)
                {
                    continue;
                }

                requestedResourcesPrefilter.Add(resource);
            }

            if (officeImportCandidatesPrefilter == 0)
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
                foreach (Resource resource in requestedResourcesPrefilter)
                {
                    if ((storageCompanyData.m_StoredResources & resource) == Resource.NoResource)
                    {
                        missingStoredResourcePairs++;
                    }
                }
            }

            return new ProbeSnapshot(
                officeImportCandidatesPrefilter,
                outsideConnectionSellers,
                missingStoredResourcePairs,
                inactiveOutsideConnections,
                FormatResourceSet(requestedResourcesPrefilter));
        }

        private static void MaybeCaptureDetailedRequestProbes(
            PathfindSetupSystem system,
            NativeArray<OfficeImportRequest> officeImportRequests,
            NativeArray<Resource> requestedResources,
            NativeArray<ResourceRequestRange> resourceRequestRanges)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableDemandDiagnostics || !Mod.Settings.VerboseLogging)
            {
                return;
            }

            int softwareResourceIndex = -1;
            for (int i = 0; i < requestedResources.Length; i++)
            {
                if (requestedResources[i] == Resource.Software)
                {
                    softwareResourceIndex = i;
                    break;
                }
            }

            if (softwareResourceIndex < 0)
            {
                return;
            }

            ResourceRequestRange requestRange = resourceRequestRanges[softwareResourceIndex];
            if (requestRange.Count <= 0)
            {
                return;
            }

            SimulationSystem simulationSystem = system.World?.GetExistingSystemManaged<SimulationSystem>();
            int simulationFrame = simulationSystem != null ? (int)simulationSystem.frameIndex : -1;
            SoftwareOutsideConnectionProbeAggregate aggregate = CaptureSoftwareOutsideConnectionProbeAggregate(system);
            for (int requestOffset = 0; requestOffset < requestRange.Count; requestOffset++)
            {
                OfficeImportRequest request = officeImportRequests[requestRange.StartIndex + requestOffset];
                s_LastOfficeImportProbeSnapshots[request.SeekerEntity] = new OfficeImportProbeSnapshot(
                    simulationFrame,
                    Resource.Software,
                    request.RequestedAmount,
                    aggregate.TotalOutsideConnectionSellers,
                    aggregate.MissingStoredResourcePairs,
                    aggregate.InactiveOutsideConnections,
                    aggregate.AvailableCandidateCount,
                    aggregate.ZeroOrNegativeStockSellerCount,
                    aggregate.TopAvailableStock,
                    aggregate.AvailableCandidateCount > 0);
            }
        }

        private static SoftwareOutsideConnectionProbeAggregate CaptureSoftwareOutsideConnectionProbeAggregate(PathfindSetupSystem system)
        {
            EntityQuery query = GetOutsideConnectionSellerQuery(system);
            if (query.IsEmptyIgnoreFilter)
            {
                return default;
            }

            EntityManager entityManager = system.EntityManager;
            int totalOutsideConnectionSellers = 0;
            int missingStoredResourcePairs = 0;
            int inactiveOutsideConnections = 0;
            int availableCandidateCount = 0;
            int zeroOrNegativeStockSellerCount = 0;
            int topAvailableStock = 0;

            using NativeArray<Entity> sellerEntities = query.ToEntityArray(Allocator.Temp);
            using NativeArray<PrefabRef> sellerPrefabs = query.ToComponentDataArray<PrefabRef>(Allocator.Temp);
            for (int entityIndex = 0; entityIndex < sellerEntities.Length; entityIndex++)
            {
                Entity sellerEntity = sellerEntities[entityIndex];
                totalOutsideConnectionSellers++;

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
                if ((storageCompanyData.m_StoredResources & Resource.Software) != Resource.NoResource)
                {
                    continue;
                }

                missingStoredResourcePairs++;

                if (!entityManager.HasBuffer<Game.Economy.Resources>(sellerEntity))
                {
                    zeroOrNegativeStockSellerCount++;
                    continue;
                }

                int stock = EconomyUtils.GetResources(Resource.Software, entityManager.GetBuffer<Game.Economy.Resources>(sellerEntity, isReadOnly: true));
                int buyingLoad = GetBuyingTruckLoad(entityManager, sellerEntity, Resource.Software);
                int availableAmount = stock - buyingLoad;
                if (availableAmount > 0)
                {
                    availableCandidateCount++;
                    topAvailableStock = Math.Max(topAvailableStock, availableAmount);
                }
                else
                {
                    zeroOrNegativeStockSellerCount++;
                }
            }

            return new SoftwareOutsideConnectionProbeAggregate(
                totalOutsideConnectionSellers,
                missingStoredResourcePairs,
                inactiveOutsideConnections,
                availableCandidateCount,
                zeroOrNegativeStockSellerCount,
                topAvailableStock);
        }

        private static int GetBuyingTruckLoad(EntityManager entityManager, Entity sellerEntity, Resource resource)
        {
            int amount = 0;
            if (!entityManager.HasBuffer<GuestVehicle>(sellerEntity))
            {
                return amount;
            }

            DynamicBuffer<GuestVehicle> guestVehicles = entityManager.GetBuffer<GuestVehicle>(sellerEntity, isReadOnly: true);
            for (int i = 0; i < guestVehicles.Length; i++)
            {
                amount += GetVehicleBuyingLoad(entityManager, guestVehicles[i].m_Vehicle, resource);
            }

            return amount;
        }

        private static int GetVehicleBuyingLoad(EntityManager entityManager, Entity vehicle, Resource resource)
        {
            if (!entityManager.HasComponent<DeliveryTruckComponent>(vehicle))
            {
                return 0;
            }

            int amount = 0;
            DeliveryTruckComponent truck = entityManager.GetComponentData<DeliveryTruckComponent>(vehicle);
            if (truck.m_Resource == resource && (truck.m_State & DeliveryTruckFlags.Buying) != 0)
            {
                amount += truck.m_Amount;
            }

            if (!entityManager.HasBuffer<LayoutElement>(vehicle))
            {
                return amount;
            }

            DynamicBuffer<LayoutElement> layout = entityManager.GetBuffer<LayoutElement>(vehicle, isReadOnly: true);
            for (int i = 0; i < layout.Length; i++)
            {
                Entity layoutVehicle = layout[i].m_Vehicle;
                if (layoutVehicle == vehicle)
                {
                    continue;
                }

                if (!entityManager.HasComponent<DeliveryTruckComponent>(layoutVehicle))
                {
                    continue;
                }

                DeliveryTruckComponent layoutTruck = entityManager.GetComponentData<DeliveryTruckComponent>(layoutVehicle);
                if (layoutTruck.m_Resource == resource && (layoutTruck.m_State & DeliveryTruckFlags.Buying) != 0)
                {
                    amount += layoutTruck.m_Amount;
                }
            }

            return amount;
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

            [ReadOnly]
            public NativeArray<OfficeImportRequest> m_OfficeImportRequests;

            [ReadOnly]
            public NativeArray<Resource> m_RequestedResources;

            [ReadOnly]
            public NativeArray<ResourceRequestRange> m_ResourceRequestRanges;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(m_EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref m_PrefabType);
                BufferAccessor<Game.Economy.Resources> resources = chunk.GetBufferAccessor(ref m_ResourceType);
                Unity.Mathematics.Random random = m_RandomSeed.GetRandom(unfilteredChunkIndex);

                for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
                {
                    Entity sellerEntity = entities[entityIndex];
                    Entity prefab = prefabs[entityIndex].m_Prefab;
                    if (m_Buildings.HasComponent(sellerEntity) && BuildingUtils.CheckOption(m_Buildings[sellerEntity], BuildingOption.Inactive))
                    {
                        continue;
                    }

                    if (!m_StorageCompanyDatas.HasComponent(prefab))
                    {
                        continue;
                    }

                    StorageCompanyData storageCompanyData = m_StorageCompanyDatas[prefab];

                    for (int resourceIndex = 0; resourceIndex < m_RequestedResources.Length; resourceIndex++)
                    {
                        Resource resource = m_RequestedResources[resourceIndex];
                        ResourceRequestRange requestRange = m_ResourceRequestRanges[resourceIndex];
                        if (requestRange.Count == 0)
                        {
                            continue;
                        }

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

                        float buyCost = 0f;
                        if (m_TradeCosts.HasBuffer(sellerEntity))
                        {
                            DynamicBuffer<TradeCost> costs = m_TradeCosts[sellerEntity];
                            buyCost = EconomyUtils.GetTradeCost(resource, costs).m_BuyCost;
                        }

                        for (int requestOffset = 0; requestOffset < requestRange.Count; requestOffset++)
                        {
                            OfficeImportRequest request = m_OfficeImportRequests[requestRange.StartIndex + requestOffset];
                            if (sellerEntity.Equals(request.SeekerEntity))
                            {
                                continue;
                            }

                            float fillRatio = math.min(1f, availableAmount * 1f / math.max(1, request.RequestedAmount));
                            float penalty = kTargetPenaltyScale * (1f - fillRatio);
                            penalty += ResourcePathfindSetup.kOutsideConnectionAmountBasedPenalty * request.RequestedAmount;
                            if ((request.TargetFlags & SetupTargetFlags.BuildingUpkeep) != 0)
                            {
                                penalty += random.NextInt(kBuildingUpkeepPenaltyRandomRange);
                            }

                            penalty += buyCost * request.RequestedAmount * 0.01f;
                            request.TargetSeeker.FindTargets(sellerEntity, penalty * kTargetPenaltyScale);
                        }
                    }
                }
            }
        }
    }
}
