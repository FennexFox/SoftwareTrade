using System;
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
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class OfficeDemandDiagnosticsSystem : GameSystemBase
    {
        private const int kTopFactorCount = 5;
        private const int kMaxDetailEntries = 5;
        private const int kDefaultDiagnosticsSamplesPerDay = 2;
        private const int kMinDiagnosticsSamplesPerDay = 1;
        private const int kMaxDiagnosticsSamplesPerDay = 8;
        private const float kNotificationCostLimit = 5f;
        private const int kResourceLowStockAmount = 4000;
        private const string kTraceNeedNotSelected = "need_not_selected";
        private const string kTraceNeedSelectedNoBuyer = "need_selected_no_buyer";
        private const string kTraceBuyerActive = "buyer_active";
        private const string kTracePathPending = "path_pending";
        private const string kTracePathResolvedNoTradeState = "path_resolved_no_trade_state";
        private const string kTraceCurrentTradingPresent = "current_trading_present";
        private const string kTraceTripReservedPresent = "trip_reserved_present";
        private const string kTraceNeedCleared = "need_cleared";

        private struct FactorEntry
        {
            public int Index;
            public int Weight;
        }

        private struct DiagnosticSnapshot
        {
            public int Day;
            public int SampleIndex;
            public int SampleSlot;
            public int SamplesPerDay;
            public int OfficeBuildingDemand;
            public int OfficeCompanyDemand;
            public int EmptyBuildingsFactor;
            public int BuildingDemandFactor;
            public int FreeOfficeProperties;
            public int FreeSoftwareOfficeProperties;
            public int FreeOfficePropertiesInOccupiedBuildings;
            public int FreeSoftwareOfficePropertiesInOccupiedBuildings;
            public int OnMarketOfficeProperties;
            public int ActivelyVacantOfficeProperties;
            public int OccupiedOnMarketOfficeProperties;
            public int StaleRenterOnMarketOfficeProperties;
            public int SignatureOccupiedOnMarketOffice;
            public int SignatureOccupiedOnMarketIndustrial;
            public int SignatureOccupiedToBeOnMarket;
            public int NonSignatureOccupiedOnMarketOffice;
            public int NonSignatureOccupiedOnMarketIndustrial;
            public int GuardCorrections;
            public int SoftwareProduction;
            public int SoftwareDemand;
            public int SoftwareProductionCompanies;
            public int SoftwarePropertylessCompanies;
            public int ElectronicsProduction;
            public int ElectronicsDemand;
            public int ElectronicsProductionCompanies;
            public int ElectronicsPropertylessCompanies;
            public int SoftwareProducerOfficeCompanies;
            public int SoftwareProducerOfficePropertylessCompanies;
            public int SoftwareProducerOfficeEfficiencyZero;
            public int SoftwareProducerOfficeLackResourcesZero;
            public int SoftwareConsumerOfficeCompanies;
            public int SoftwareConsumerOfficePropertylessCompanies;
            public int SoftwareConsumerOfficeEfficiencyZero;
            public int SoftwareConsumerOfficeLackResourcesZero;
            public int SoftwareConsumerOfficeSoftwareInputZero;
            public int SoftwareConsumerNeedSelected;
            public int SoftwareConsumerBuyerActive;
            public int SoftwareConsumerPathPending;
            public int SoftwareConsumerTripNeededPresent;
            public int SoftwareConsumerCurrentTradingPresent;
            public int SoftwareConsumerNoBuyerDespiteNeed;
            public int SoftwareConsumerTradeCostOnly;
            public string TopFactors;
            public string FreeSoftwareOfficePropertyDetails;
            public string OnMarketOfficePropertyDetails;
            public string SoftwareOfficeDetails;
        }

        private struct SoftwareNeedState
        {
            public int Stock;
            public int BuyingLoad;
            public int TripNeededAmount;
            public int EffectiveStock;
            public int Threshold;
            public bool Selected;
            public bool Expensive;
        }

        private struct SoftwareTradeCostState
        {
            public bool HasEntry;
            public float BuyCost;
            public long LastTransferRequestTime;
        }

        private struct SoftwareBuyerState
        {
            public bool BuyerActive;
            public int BuyerAmount;
            public int TripNeededCount;
            public int TripNeededAmount;
            public int CurrentTradingCount;
            public int CurrentTradingAmount;
            public bool HasPath;
            public bool PathPending;
            public PathFlags PathState;
            public Entity PathDestination;
            public float PathDistance;
        }

        private struct SoftwareConsumerTraceState
        {
            public string CurrentClassification;
            public string LastTransitionLabel;
            public int LastTransitionDay;
            public int LastTransitionSampleIndex;
            public Entity LastPathDestination;
            public bool HasLastPathDestination;
            public int LastPathDestinationSoftwareStock;
            public bool HasLastPathDestinationSoftwareStock;
        }

        private struct SoftwareConsumerDiagnosticState
        {
            public SoftwareNeedState Need;
            public SoftwareTradeCostState TradeCost;
            public SoftwareBuyerState Buyer;
            public SoftwareConsumerTraceState Trace;
            public bool NoBuyerDespiteNeed;
            public bool TradeCostOnly;
        }

        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;
        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private CountCompanyDataSystem m_CountCompanyDataSystem;
        private SignaturePropertyMarketGuardSystem m_SignaturePropertyMarketGuardSystem;
        private PrefabSystem m_PrefabSystem;
        private ResourceSystem m_ResourceSystem;
        private EntityQuery m_TimeDataQuery;
        private EntityQuery m_TimeSettingsQuery;
        private EntityQuery m_FreeOfficePropertyQuery;
        private EntityQuery m_OnMarketPropertyQuery;
        private EntityQuery m_ToBeOnMarketPropertyQuery;
        private EntityQuery m_OfficeCompanyQuery;
        private int m_LastLoggedSampleIndex = int.MinValue;
        private bool m_LastDiagnosticsEnabled;
        private string m_SessionId = CreateSessionId();
        private int m_RunSequence;
        private int m_RunStartDay = int.MinValue;
        private int m_RunStartSampleIndex = int.MinValue;
        private string m_LastSettingsSnapshot = string.Empty;
        private string m_LastPatchState = string.Empty;
        private readonly Dictionary<Entity, SoftwareConsumerTraceState> m_SoftwareConsumerTrace = new Dictionary<Entity, SoftwareConsumerTraceState>();

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_IndustrialDemandSystem = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            m_CountCompanyDataSystem = World.GetOrCreateSystemManaged<CountCompanyDataSystem>();
            m_SignaturePropertyMarketGuardSystem = World.GetOrCreateSystemManaged<SignaturePropertyMarketGuardSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
            m_TimeSettingsQuery = GetEntityQuery(ComponentType.ReadOnly<TimeSettingsData>());
            m_FreeOfficePropertyQuery = GetEntityQuery(
                ComponentType.ReadOnly<OfficeProperty>(),
                ComponentType.ReadOnly<PropertyOnMarket>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Abandoned>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Condemned>());
            m_OnMarketPropertyQuery = GetEntityQuery(
                ComponentType.ReadOnly<PropertyOnMarket>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Abandoned>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Condemned>());
            m_ToBeOnMarketPropertyQuery = GetEntityQuery(
                ComponentType.ReadOnly<PropertyToBeOnMarket>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Abandoned>(),
                ComponentType.Exclude<Destroyed>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<Condemned>());
            m_OfficeCompanyQuery = GetEntityQuery(
                ComponentType.ReadOnly<OfficeCompany>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            ResetEvidenceSession();
            RequireForUpdate(m_TimeDataQuery);
            RequireForUpdate(m_TimeSettingsQuery);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            if (phase == SystemUpdatePhase.GameSimulation)
            {
                return 256;
            }

            return base.GetUpdateInterval(phase);
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ResetEvidenceSession();
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            ResetEvidenceSession();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool diagnosticsEnabled = IsDiagnosticsEnabled();
            string settingsSnapshot = FormatSettingsSnapshot();
            string patchState = FormatPatchState();

            if (!diagnosticsEnabled)
            {
                m_LastDiagnosticsEnabled = false;
                m_LastSettingsSnapshot = settingsSnapshot;
                m_LastPatchState = patchState;
                return;
            }

            TimeData timeData = m_TimeDataQuery.GetSingleton<TimeData>();
            TimeSettingsData timeSettings = m_TimeSettingsQuery.GetSingleton<TimeSettingsData>();
            int day = TimeSystem.GetDay(m_SimulationSystem.frameIndex, timeData);
            int samplesPerDay = GetDiagnosticsSamplesPerDay();
            int sampleSlot = GetSampleSlot(timeSettings, timeData, samplesPerDay);
            int sampleIndex = GetSampleIndex(day, sampleSlot, samplesPerDay);
            bool runStateChanged = !m_LastDiagnosticsEnabled ||
                                   settingsSnapshot != m_LastSettingsSnapshot ||
                                   patchState != m_LastPatchState;
            if (runStateChanged)
            {
                BeginNewRun(day, sampleIndex, settingsSnapshot, patchState);
                m_LastLoggedSampleIndex = int.MinValue;
            }

            m_LastDiagnosticsEnabled = true;
            if (sampleIndex == m_LastLoggedSampleIndex)
            {
                return;
            }

            DiagnosticSnapshot snapshot = CaptureSnapshot(day, sampleIndex, sampleSlot, samplesPerDay);
            m_LastLoggedSampleIndex = sampleIndex;

            if (!TryGetObservationTrigger(snapshot, out string trigger))
            {
                return;
            }

            Mod.log.Info(
                $"softwareEvidenceDiagnostics observation_window(session_id={m_SessionId}, run_id={m_RunSequence}, start_day={m_RunStartDay}, end_day={snapshot.Day}, start_sample_index={m_RunStartSampleIndex}, end_sample_index={snapshot.SampleIndex}, sample_day={snapshot.Day}, sample_index={snapshot.SampleIndex}, sample_slot={snapshot.SampleSlot}, samples_per_day={snapshot.SamplesPerDay}, sample_count={GetObservationSampleCount(snapshot.SampleIndex)}, trigger={trigger}); " +
                $"environment(settings={settingsSnapshot}, patch_state={patchState}); " +
                $"diagnostic_counters(" +
                $"officeDemand(building={snapshot.OfficeBuildingDemand}, company={snapshot.OfficeCompanyDemand}, emptyBuildings={snapshot.EmptyBuildingsFactor}, buildingDemand={snapshot.BuildingDemandFactor}); " +
                $"freeOfficeProperties(total={snapshot.FreeOfficeProperties}, software={snapshot.FreeSoftwareOfficeProperties}, inOccupiedBuildings={snapshot.FreeOfficePropertiesInOccupiedBuildings}, softwareInOccupiedBuildings={snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings}); " +
                $"onMarketOfficeProperties(total={snapshot.OnMarketOfficeProperties}, activelyVacant={snapshot.ActivelyVacantOfficeProperties}, occupied={snapshot.OccupiedOnMarketOfficeProperties}, staleRenterOnly={snapshot.StaleRenterOnMarketOfficeProperties}); " +
                $"phantomVacancy(signatureOccupiedOnMarketOffice={snapshot.SignatureOccupiedOnMarketOffice}, signatureOccupiedOnMarketIndustrial={snapshot.SignatureOccupiedOnMarketIndustrial}, signatureOccupiedToBeOnMarket={snapshot.SignatureOccupiedToBeOnMarket}, nonSignatureOccupiedOnMarketOffice={snapshot.NonSignatureOccupiedOnMarketOffice}, nonSignatureOccupiedOnMarketIndustrial={snapshot.NonSignatureOccupiedOnMarketIndustrial}, guardCorrections={snapshot.GuardCorrections}); " +
                $"software(resourceProduction={snapshot.SoftwareProduction}, resourceDemand={snapshot.SoftwareDemand}, companies={snapshot.SoftwareProductionCompanies}, propertyless={snapshot.SoftwarePropertylessCompanies}); " +
                $"electronics(resourceProduction={snapshot.ElectronicsProduction}, resourceDemand={snapshot.ElectronicsDemand}, companies={snapshot.ElectronicsProductionCompanies}, propertyless={snapshot.ElectronicsPropertylessCompanies}); " +
                $"softwareProducerOffices(total={snapshot.SoftwareProducerOfficeCompanies}, propertyless={snapshot.SoftwareProducerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareProducerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareProducerOfficeLackResourcesZero}); " +
                $"softwareConsumerOffices(total={snapshot.SoftwareConsumerOfficeCompanies}, propertyless={snapshot.SoftwareConsumerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareConsumerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareConsumerOfficeLackResourcesZero}, softwareInputZero={snapshot.SoftwareConsumerOfficeSoftwareInputZero}); " +
                $"softwareConsumerBuyerState(needSelected={snapshot.SoftwareConsumerNeedSelected}, buyerActive={snapshot.SoftwareConsumerBuyerActive}, pathPending={snapshot.SoftwareConsumerPathPending}, tripNeededPresent={snapshot.SoftwareConsumerTripNeededPresent}, currentTradingPresent={snapshot.SoftwareConsumerCurrentTradingPresent}, noBuyerDespiteNeed={snapshot.SoftwareConsumerNoBuyerDespiteNeed}, tradeCostOnly={snapshot.SoftwareConsumerTradeCostOnly})" +
                $"); " +
                $"diagnostic_context(topFactors=[{snapshot.TopFactors}])");

            if (!string.IsNullOrEmpty(snapshot.FreeSoftwareOfficePropertyDetails))
            {
                Mod.log.Info($"softwareEvidenceDiagnostics detail(session_id={m_SessionId}, run_id={m_RunSequence}, observation_end_day={snapshot.Day}, observation_end_sample_index={snapshot.SampleIndex}, detail_type=freeSoftwareOfficeProperties, values={snapshot.FreeSoftwareOfficePropertyDetails})");
            }

            if (!string.IsNullOrEmpty(snapshot.OnMarketOfficePropertyDetails))
            {
                Mod.log.Info($"softwareEvidenceDiagnostics detail(session_id={m_SessionId}, run_id={m_RunSequence}, observation_end_day={snapshot.Day}, observation_end_sample_index={snapshot.SampleIndex}, detail_type=onMarketOfficeProperties, values={snapshot.OnMarketOfficePropertyDetails})");
            }

            if (!string.IsNullOrEmpty(snapshot.SoftwareOfficeDetails))
            {
                Mod.log.Info($"softwareEvidenceDiagnostics detail(session_id={m_SessionId}, run_id={m_RunSequence}, observation_end_day={snapshot.Day}, observation_end_sample_index={snapshot.SampleIndex}, detail_type=softwareOfficeStates, values={snapshot.SoftwareOfficeDetails})");
            }
        }

        private DiagnosticSnapshot CaptureSnapshot(int day, int sampleIndex, int sampleSlot, int samplesPerDay)
        {
            JobHandle officeDeps;
            NativeArray<int> officeFactors = m_IndustrialDemandSystem.GetOfficeDemandFactors(out officeDeps);
            officeDeps.Complete();

            JobHandle companyDeps;
            CountCompanyDataSystem.IndustrialCompanyDatas industrialCompanyDatas = m_CountCompanyDataSystem.GetIndustrialCompanyDatas(out companyDeps);
            companyDeps.Complete();

            int softwareIndex = EconomyUtils.GetResourceIndex(Resource.Software);
            int electronicsIndex = EconomyUtils.GetResourceIndex(Resource.Electronics);
            DiagnosticSnapshot snapshot = new DiagnosticSnapshot
            {
                Day = day,
                SampleIndex = sampleIndex,
                SampleSlot = sampleSlot,
                SamplesPerDay = samplesPerDay,
                OfficeBuildingDemand = m_IndustrialDemandSystem.officeBuildingDemand,
                OfficeCompanyDemand = m_IndustrialDemandSystem.officeCompanyDemand,
                EmptyBuildingsFactor = officeFactors[(int)DemandFactor.EmptyBuildings],
                BuildingDemandFactor = officeFactors[(int)DemandFactor.BuildingDemand],
                SoftwareProduction = industrialCompanyDatas.m_Production[softwareIndex],
                SoftwareDemand = industrialCompanyDatas.m_Demand[softwareIndex],
                SoftwareProductionCompanies = industrialCompanyDatas.m_ProductionCompanies[softwareIndex],
                SoftwarePropertylessCompanies = industrialCompanyDatas.m_ProductionPropertyless[softwareIndex],
                ElectronicsProduction = industrialCompanyDatas.m_Production[electronicsIndex],
                ElectronicsDemand = industrialCompanyDatas.m_Demand[electronicsIndex],
                ElectronicsProductionCompanies = industrialCompanyDatas.m_ProductionCompanies[electronicsIndex],
                ElectronicsPropertylessCompanies = industrialCompanyDatas.m_ProductionPropertyless[electronicsIndex],
                GuardCorrections = m_SignaturePropertyMarketGuardSystem.ConsumeCorrectionCount(),
                TopFactors = FormatTopFactors(officeFactors)
            };

            CountFreeOfficeProperties(ref snapshot);
            CountOnMarketProperties(ref snapshot);
            CountToBeOnMarketProperties(ref snapshot);
            CountSoftwareOffices(ref snapshot);

            return snapshot;
        }

        private void CountFreeOfficeProperties(ref DiagnosticSnapshot snapshot)
        {
            StringBuilder details = new StringBuilder();
            int detailCount = 0;
            using NativeArray<Entity> properties = m_FreeOfficePropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length; i++)
            {
                Entity property = properties[i];
                snapshot.FreeOfficeProperties++;

                bool softwareCapable = false;
                if (EntityManager.HasComponent<PrefabRef>(property))
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(property);
                    if (EntityManager.HasComponent<BuildingPropertyData>(prefabRef.m_Prefab))
                    {
                        BuildingPropertyData propertyData = EntityManager.GetComponentData<BuildingPropertyData>(prefabRef.m_Prefab);
                        softwareCapable = (propertyData.m_AllowedManufactured & Resource.Software) != Resource.NoResource;
                    }
                }

                if (softwareCapable)
                {
                    snapshot.FreeSoftwareOfficeProperties++;
                    AppendDetail(details, ref detailCount, DescribeProperty(property));
                }

                if (EntityManager.HasComponent<Attached>(property))
                {
                    Attached attached = EntityManager.GetComponentData<Attached>(property);
                    if (GetActiveCompanyRenterCount(attached.m_Parent) > 0)
                    {
                        snapshot.FreeOfficePropertiesInOccupiedBuildings++;
                        if (softwareCapable)
                        {
                            snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings++;
                        }
                    }
                }
            }

            snapshot.FreeSoftwareOfficePropertyDetails = details.ToString();
        }

        private void CountOnMarketProperties(ref DiagnosticSnapshot snapshot)
        {
            StringBuilder details = new StringBuilder();
            int detailCount = 0;
            using NativeArray<Entity> properties = m_OnMarketPropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length; i++)
            {
                Entity property = properties[i];
                if (!TryGetTrackedPropertyType(property, out bool isOfficeProperty, out bool isIndustrialProperty))
                {
                    continue;
                }

                int activeCompanyRenters = GetActiveCompanyRenterCount(property);
                int companyRenters = GetCompanyRenterCount(property);
                bool isSignature = EntityManager.HasComponent<Signature>(property);
                if (isOfficeProperty)
                {
                    snapshot.OnMarketOfficeProperties++;
                    if (activeCompanyRenters > 0)
                    {
                        snapshot.OccupiedOnMarketOfficeProperties++;
                    }
                    else
                    {
                        snapshot.ActivelyVacantOfficeProperties++;
                        if (companyRenters > 0)
                        {
                            snapshot.StaleRenterOnMarketOfficeProperties++;
                        }
                    }

                    if (activeCompanyRenters > 0)
                    {
                        if (isSignature)
                        {
                            snapshot.SignatureOccupiedOnMarketOffice++;
                        }
                        else
                        {
                            snapshot.NonSignatureOccupiedOnMarketOffice++;
                        }
                    }

                    AppendDetail(details, ref detailCount, DescribeProperty(property));
                }

                if (isIndustrialProperty && activeCompanyRenters > 0)
                {
                    if (isSignature)
                    {
                        snapshot.SignatureOccupiedOnMarketIndustrial++;
                    }
                    else
                    {
                        snapshot.NonSignatureOccupiedOnMarketIndustrial++;
                    }
                }
            }

            snapshot.OnMarketOfficePropertyDetails = details.ToString();
        }

        private void CountToBeOnMarketProperties(ref DiagnosticSnapshot snapshot)
        {
            using NativeArray<Entity> properties = m_ToBeOnMarketPropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length; i++)
            {
                Entity property = properties[i];
                if (!EntityManager.HasComponent<Signature>(property) || !TryGetTrackedPropertyType(property, out _, out _))
                {
                    continue;
                }

                if (GetActiveCompanyRenterCount(property) > 0)
                {
                    snapshot.SignatureOccupiedToBeOnMarket++;
                }
            }
        }

        private void CountSoftwareOffices(ref DiagnosticSnapshot snapshot)
        {
            StringBuilder details = new StringBuilder();
            int detailCount = 0;
            using NativeArray<Entity> companies = m_OfficeCompanyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < companies.Length; i++)
            {
                Entity company = companies[i];
                if (!EntityManager.HasComponent<PrefabRef>(company))
                {
                    continue;
                }

                PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(company);
                if (!EntityManager.HasComponent<IndustrialProcessData>(prefabRef.m_Prefab))
                {
                    continue;
                }

                IndustrialProcessData processData = EntityManager.GetComponentData<IndustrialProcessData>(prefabRef.m_Prefab);
                bool isProducer = processData.m_Output.m_Resource == Resource.Software;
                bool isConsumer = processData.m_Input1.m_Resource == Resource.Software ||
                                  processData.m_Input2.m_Resource == Resource.Software;
                if (!isProducer && !isConsumer)
                {
                    continue;
                }

                if (isProducer)
                {
                    snapshot.SoftwareProducerOfficeCompanies++;
                }

                if (isConsumer)
                {
                    snapshot.SoftwareConsumerOfficeCompanies++;
                }

                if (!EntityManager.HasComponent<PropertyRenter>(company))
                {
                    if (isProducer)
                    {
                        snapshot.SoftwareProducerOfficePropertylessCompanies++;
                    }

                    if (isConsumer)
                    {
                        snapshot.SoftwareConsumerOfficePropertylessCompanies++;
                    }

                    continue;
                }

                PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(company);
                if (propertyRenter.m_Property == Entity.Null)
                {
                    if (isProducer)
                    {
                        snapshot.SoftwareProducerOfficePropertylessCompanies++;
                    }

                    if (isConsumer)
                    {
                        snapshot.SoftwareConsumerOfficePropertylessCompanies++;
                    }

                    continue;
                }

                int softwareInputStock = isConsumer ? GetCompanyResourceAmount(company, Resource.Software) : 0;
                SoftwareConsumerDiagnosticState softwareConsumerState = default;
                if (isConsumer)
                {
                    softwareConsumerState = GetSoftwareConsumerDiagnosticState(company, prefabRef.m_Prefab, processData, snapshot.Day, snapshot.SampleIndex);
                    softwareInputStock = softwareConsumerState.Need.Stock;
                    if (softwareConsumerState.Need.Selected)
                    {
                        snapshot.SoftwareConsumerNeedSelected++;
                    }

                    if (softwareConsumerState.Buyer.BuyerActive)
                    {
                        snapshot.SoftwareConsumerBuyerActive++;
                    }

                    if (softwareConsumerState.Buyer.PathPending)
                    {
                        snapshot.SoftwareConsumerPathPending++;
                    }

                    if (softwareConsumerState.Buyer.TripNeededCount > 0)
                    {
                        snapshot.SoftwareConsumerTripNeededPresent++;
                    }

                    if (softwareConsumerState.Buyer.CurrentTradingCount > 0)
                    {
                        snapshot.SoftwareConsumerCurrentTradingPresent++;
                    }

                    if (softwareConsumerState.NoBuyerDespiteNeed)
                    {
                        snapshot.SoftwareConsumerNoBuyerDespiteNeed++;
                    }

                    if (softwareConsumerState.TradeCostOnly)
                    {
                        snapshot.SoftwareConsumerTradeCostOnly++;
                    }
                }

                bool softwareInputZero = isConsumer && softwareInputStock <= 0;

                bool hasEfficiency = EntityManager.HasBuffer<Efficiency>(propertyRenter.m_Property);
                float efficiency = float.NaN;
                float lackResources = float.NaN;
                bool efficiencyZero = false;
                bool lackResourcesZero = false;
                if (hasEfficiency)
                {
                    DynamicBuffer<Efficiency> efficiencyBuffer = EntityManager.GetBuffer<Efficiency>(propertyRenter.m_Property, isReadOnly: true);
                    efficiency = BuildingUtils.GetEfficiency(efficiencyBuffer);
                    efficiencyZero = efficiency <= 0f;
                    if (efficiencyZero)
                    {
                        if (isProducer)
                        {
                            snapshot.SoftwareProducerOfficeEfficiencyZero++;
                        }

                        if (isConsumer)
                        {
                            snapshot.SoftwareConsumerOfficeEfficiencyZero++;
                        }
                    }

                    lackResources = BuildingUtils.GetEfficiencyFactor(efficiencyBuffer, EfficiencyFactor.LackResources);
                    lackResourcesZero = lackResources <= 0f;
                    if (lackResourcesZero)
                    {
                        if (isProducer)
                        {
                            snapshot.SoftwareProducerOfficeLackResourcesZero++;
                        }

                        if (isConsumer)
                        {
                            snapshot.SoftwareConsumerOfficeLackResourcesZero++;
                        }
                    }
                }

                if (softwareInputZero)
                {
                    snapshot.SoftwareConsumerOfficeSoftwareInputZero++;
                }

                if (efficiencyZero || lackResourcesZero || softwareInputZero || (isConsumer && softwareConsumerState.NoBuyerDespiteNeed))
                {
                    AppendDetail(details, ref detailCount, DescribeSoftwareOffice(company, prefabRef.m_Prefab, propertyRenter.m_Property, processData, isProducer, isConsumer, softwareInputZero, hasEfficiency, efficiency, lackResources, softwareConsumerState));
                }
            }

            snapshot.SoftwareOfficeDetails = details.ToString();
        }

        private string DescribeSoftwareOffice(Entity company, Entity companyPrefab, Entity property, IndustrialProcessData processData, bool isProducer, bool isConsumer, bool softwareInputZero, bool hasEfficiency, float efficiency, float lackResources, SoftwareConsumerDiagnosticState softwareConsumerState)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("role=").Append(GetSoftwareOfficeRoleLabel(isProducer, isConsumer));
            builder.Append(", ");
            builder.Append("company=").Append(FormatEntity(company));
            builder.Append(", prefab=").Append(GetPrefabLabel(companyPrefab));
            builder.Append(", property=").Append(FormatEntity(property));
            builder.Append(", output=").Append(processData.m_Output.m_Resource);
            builder.Append(", outputStock=").Append(GetCompanyResourceAmount(company, processData.m_Output.m_Resource));
            AppendCompanyResourceState(builder, company, "input1", processData.m_Input1.m_Resource);
            AppendCompanyResourceState(builder, company, "input2", processData.m_Input2.m_Resource);
            if (isConsumer)
            {
                builder.Append(", softwareInputZero=").Append(softwareInputZero);
                AppendSoftwareNeedState(builder, softwareConsumerState.Need);
                AppendSoftwareTradeCostState(builder, softwareConsumerState.TradeCost);
                AppendSoftwareBuyerState(builder, softwareConsumerState.Buyer);
                AppendSoftwareTraceState(builder, softwareConsumerState.Trace);
                builder.Append(", noBuyerDespiteNeed=").Append(softwareConsumerState.NoBuyerDespiteNeed);
                builder.Append(", tradeCostOnly=").Append(softwareConsumerState.TradeCostOnly);
            }

            if (EntityManager.HasComponent<ResourceBuyer>(company))
            {
                ResourceBuyer buyer = EntityManager.GetComponentData<ResourceBuyer>(company);
                builder.Append(", activeBuyer(");
                builder.Append("resource=").Append(buyer.m_ResourceNeeded);
                builder.Append(", amount=").Append(buyer.m_AmountNeeded);
                builder.Append(')');
            }

            builder.Append(", efficiency=");
            AppendMetricValue(builder, hasEfficiency, efficiency);
            builder.Append(", lackResources=");
            AppendMetricValue(builder, hasEfficiency, lackResources);
            return builder.ToString();
        }

        private static void AppendMetricValue(StringBuilder builder, bool hasMetric, float value)
        {
            builder.Append(hasMetric ? value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
        }

        private static string GetSoftwareOfficeRoleLabel(bool isProducer, bool isConsumer)
        {
            if (isProducer && isConsumer)
            {
                return "producer_consumer";
            }

            if (isProducer)
            {
                return "producer";
            }

            return "consumer";
        }

        private void AppendCompanyResourceState(StringBuilder builder, Entity company, string label, Resource resource)
        {
            if (resource == Resource.NoResource)
            {
                return;
            }

            builder.Append(", ").Append(label).Append('=').Append(resource);
            builder.Append("(stock=").Append(GetCompanyResourceAmount(company, resource)).Append(')');
        }

        private int GetCompanyResourceAmount(Entity company, Resource resource)
        {
            if (resource == Resource.NoResource || !EntityManager.HasBuffer<Resources>(company))
            {
                return 0;
            }

            DynamicBuffer<Resources> resources = EntityManager.GetBuffer<Resources>(company, isReadOnly: true);
            return EconomyUtils.GetResources(resource, resources);
        }

        private void AppendSoftwareNeedState(StringBuilder builder, SoftwareNeedState state)
        {
            builder.Append(", softwareNeed(");
            builder.Append("stock=").Append(state.Stock);
            builder.Append(", buyingLoad=").Append(state.BuyingLoad);
            builder.Append(", tripNeededAmount=").Append(state.TripNeededAmount);
            builder.Append(", effectiveStock=").Append(state.EffectiveStock);
            builder.Append(", threshold=").Append(state.Threshold);
            builder.Append(", selected=").Append(state.Selected);
            builder.Append(", expensive=").Append(state.Expensive);
            builder.Append(')');
        }

        private void AppendSoftwareTradeCostState(StringBuilder builder, SoftwareTradeCostState state)
        {
            builder.Append(", softwareTradeCost(");
            builder.Append("tradeCostEntry=").Append(state.HasEntry);
            builder.Append(", buyCost=");
            builder.Append(state.HasEntry ? state.BuyCost.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastTransferRequestTime=");
            builder.Append(state.HasEntry ? state.LastTransferRequestTime.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(')');
        }

        private void AppendSoftwareBuyerState(StringBuilder builder, SoftwareBuyerState state)
        {
            builder.Append(", softwareBuyerState(");
            builder.Append("buyerActive=").Append(state.BuyerActive);
            builder.Append(", buyerAmount=");
            builder.Append(state.BuyerActive ? state.BuyerAmount.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", tripNeededCount=").Append(state.TripNeededCount);
            builder.Append(", tripNeededAmount=").Append(state.TripNeededAmount);
            builder.Append(", currentTradingCount=").Append(state.CurrentTradingCount);
            builder.Append(", currentTradingAmount=").Append(state.CurrentTradingAmount);
            builder.Append(", pathPending=").Append(state.PathPending);
            builder.Append(", pathState=");
            builder.Append(state.HasPath ? state.PathState.ToString() : "none");
            builder.Append(", pathDestination=");
            builder.Append(state.HasPath ? FormatEntity(state.PathDestination) : "none");
            builder.Append(", pathDistance=");
            builder.Append(state.HasPath ? state.PathDistance.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(')');
        }

        private void AppendSoftwareTraceState(StringBuilder builder, SoftwareConsumerTraceState state)
        {
            builder.Append(", softwareTrace(");
            builder.Append("current=").Append(string.IsNullOrEmpty(state.CurrentClassification) ? kTraceNeedNotSelected : state.CurrentClassification);
            builder.Append(", lastTransition=").Append(string.IsNullOrEmpty(state.LastTransitionLabel) ? kTraceNeedNotSelected : state.LastTransitionLabel);
            builder.Append(", lastTransitionDay=");
            builder.Append(state.LastTransitionDay == 0 && string.IsNullOrEmpty(state.LastTransitionLabel) ? "n/a" : state.LastTransitionDay.ToString(CultureInfo.InvariantCulture));
            builder.Append(", lastTransitionSampleIndex=");
            builder.Append(state.LastTransitionSampleIndex == 0 && string.IsNullOrEmpty(state.LastTransitionLabel) ? "n/a" : state.LastTransitionSampleIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(", lastPathDestination=");
            builder.Append(state.HasLastPathDestination ? FormatEntity(state.LastPathDestination) : "none");
            builder.Append(", lastPathDestinationSoftwareStock=");
            builder.Append(state.HasLastPathDestinationSoftwareStock ? state.LastPathDestinationSoftwareStock.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(')');
        }

        private bool TryGetCompanyTradeCost(Entity company, Resource resource, out SoftwareTradeCostState tradeCostState)
        {
            tradeCostState = default;
            if (resource == Resource.NoResource || !EntityManager.HasBuffer<TradeCost>(company))
            {
                return false;
            }

            DynamicBuffer<TradeCost> costs = EntityManager.GetBuffer<TradeCost>(company, isReadOnly: true);
            for (int i = 0; i < costs.Length; i++)
            {
                TradeCost tradeCost = costs[i];
                if (tradeCost.m_Resource != resource)
                {
                    continue;
                }

                tradeCostState.HasEntry = true;
                tradeCostState.BuyCost = tradeCost.m_BuyCost;
                tradeCostState.LastTransferRequestTime = tradeCost.m_LastTransferRequestTime;
                return true;
            }

            return false;
        }

        private bool TryGetActiveBuyer(Entity company, Resource resource, out int amountNeeded)
        {
            amountNeeded = 0;
            if (!EntityManager.HasComponent<ResourceBuyer>(company))
            {
                return false;
            }

            ResourceBuyer buyer = EntityManager.GetComponentData<ResourceBuyer>(company);
            if (buyer.m_ResourceNeeded != resource)
            {
                return false;
            }

            amountNeeded = buyer.m_AmountNeeded;
            return true;
        }

        private int GetCompanyTripNeededAmount(Entity company, Resource resource, out int tripCount)
        {
            tripCount = 0;
            if (!EntityManager.HasBuffer<CitizenTripNeeded>(company))
            {
                return 0;
            }

            int amount = 0;
            DynamicBuffer<CitizenTripNeeded> trips = EntityManager.GetBuffer<CitizenTripNeeded>(company, isReadOnly: true);
            for (int i = 0; i < trips.Length; i++)
            {
                CitizenTripNeeded trip = trips[i];
                if (trip.m_Resource != resource)
                {
                    continue;
                }

                tripCount++;
                amount += trip.m_Data;
            }

            return amount;
        }

        private int GetCompanyCurrentTradingAmount(Entity company, Resource resource, out int tradingCount)
        {
            tradingCount = 0;
            if (!EntityManager.HasBuffer<CurrentTrading>(company))
            {
                return 0;
            }

            int amount = 0;
            DynamicBuffer<CurrentTrading> currentTrading = EntityManager.GetBuffer<CurrentTrading>(company, isReadOnly: true);
            for (int i = 0; i < currentTrading.Length; i++)
            {
                CurrentTrading trade = currentTrading[i];
                if (trade.m_TradingResource != resource)
                {
                    continue;
                }

                tradingCount++;
                amount += trade.m_TradingResourceAmount;
            }

            return amount;
        }

        private bool TryGetCompanyPathInformation(Entity company, out PathInformation pathInformation)
        {
            if (!EntityManager.HasComponent<PathInformation>(company))
            {
                pathInformation = default;
                return false;
            }

            pathInformation = EntityManager.GetComponentData<PathInformation>(company);
            return true;
        }

        private SoftwareConsumerDiagnosticState GetSoftwareConsumerDiagnosticState(Entity company, Entity companyPrefab, IndustrialProcessData processData, int day, int sampleIndex)
        {
            SoftwareNeedState needState = GetSoftwareNeedState(company, companyPrefab, processData);
            TryGetCompanyTradeCost(company, Resource.Software, out SoftwareTradeCostState tradeCostState);
            SoftwareBuyerState buyerState = GetSoftwareBuyerState(company);
            bool noBuyerDespiteNeed = needState.Selected &&
                                      !buyerState.BuyerActive &&
                                      !buyerState.PathPending &&
                                      buyerState.TripNeededCount == 0 &&
                                      buyerState.CurrentTradingCount == 0;
            bool tradeCostOnly = tradeCostState.HasEntry && noBuyerDespiteNeed;
            SoftwareConsumerTraceState traceState = UpdateSoftwareConsumerTrace(company, needState, buyerState, day, sampleIndex);
            return new SoftwareConsumerDiagnosticState
            {
                Need = needState,
                TradeCost = tradeCostState,
                Buyer = buyerState,
                Trace = traceState,
                NoBuyerDespiteNeed = noBuyerDespiteNeed,
                TradeCostOnly = tradeCostOnly
            };
        }

        private SoftwareNeedState GetSoftwareNeedState(Entity company, Entity companyPrefab, IndustrialProcessData processData)
        {
            SoftwareNeedState state = default;
            int storageLimit = int.MaxValue;
            if (EntityManager.HasComponent<StorageLimitData>(companyPrefab))
            {
                storageLimit = EntityManager.GetComponentData<StorageLimitData>(companyPrefab).m_Limit;
            }

            bool hasSecondInput = processData.m_Input2.m_Resource != Resource.NoResource;
            int slotCount = hasSecondInput ? 2 : 1;
            if (processData.m_Output.m_Resource != processData.m_Input1.m_Resource && ResourceHasWeight(processData.m_Output.m_Resource))
            {
                slotCount++;
            }

            int maxCapacityPerSlot = slotCount > 0 ? storageLimit / slotCount : storageLimit;
            Resource needResource = Resource.NoResource;
            EvaluateNeedSelection(company, processData.m_Input1.m_Resource, maxCapacityPerSlot, ref needResource, ref state);
            if (hasSecondInput)
            {
                EvaluateNeedSelection(company, processData.m_Input2.m_Resource, maxCapacityPerSlot, ref needResource, ref state);
            }

            return state;
        }

        private void EvaluateNeedSelection(Entity company, Resource resource, int maxCapacity, ref Resource needResource, ref SoftwareNeedState softwareNeedState)
        {
            if (resource == Resource.NoResource)
            {
                return;
            }

            int stock = GetCompanyResourceAmount(company, resource);
            int buyingLoad = GetCompanyBuyingLoad(company, resource);
            int tripNeededAmount = GetCompanyTripNeededAmount(company, resource, out _);
            int effectiveStock = stock + buyingLoad + tripNeededAmount;
            int threshold = (int)Math.Max(kResourceLowStockAmount, maxCapacity * 0.25f);
            bool expensive = TryGetCompanyTradeCost(company, resource, out SoftwareTradeCostState tradeCostState) &&
                             tradeCostState.BuyCost > kNotificationCostLimit;
            bool selected = needResource == Resource.NoResource && effectiveStock < threshold;
            if (selected)
            {
                needResource = resource;
            }

            if (resource != Resource.Software)
            {
                return;
            }

            softwareNeedState.Stock = stock;
            softwareNeedState.BuyingLoad = buyingLoad;
            softwareNeedState.TripNeededAmount = tripNeededAmount;
            softwareNeedState.EffectiveStock = effectiveStock;
            softwareNeedState.Threshold = threshold;
            softwareNeedState.Selected = selected;
            softwareNeedState.Expensive = expensive;
        }

        private SoftwareBuyerState GetSoftwareBuyerState(Entity company)
        {
            SoftwareBuyerState state = default;
            state.BuyerActive = TryGetActiveBuyer(company, Resource.Software, out state.BuyerAmount);
            state.TripNeededAmount = GetCompanyTripNeededAmount(company, Resource.Software, out state.TripNeededCount);
            state.CurrentTradingAmount = GetCompanyCurrentTradingAmount(company, Resource.Software, out state.CurrentTradingCount);
            if (TryGetCompanyPathInformation(company, out PathInformation pathInformation))
            {
                state.HasPath = true;
                state.PathState = pathInformation.m_State;
                state.PathDestination = pathInformation.m_Destination;
                state.PathDistance = pathInformation.m_Distance;
                state.PathPending = (pathInformation.m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0;
            }

            return state;
        }

        private SoftwareConsumerTraceState UpdateSoftwareConsumerTrace(Entity company, SoftwareNeedState needState, SoftwareBuyerState buyerState, int day, int sampleIndex)
        {
            m_SoftwareConsumerTrace.TryGetValue(company, out SoftwareConsumerTraceState traceState);
            if (buyerState.HasPath && buyerState.PathDestination != Entity.Null)
            {
                traceState.LastPathDestination = buyerState.PathDestination;
                traceState.HasLastPathDestination = true;
                if (TryGetEntityResourceAmount(buyerState.PathDestination, Resource.Software, out int destinationSoftwareStock))
                {
                    traceState.LastPathDestinationSoftwareStock = destinationSoftwareStock;
                    traceState.HasLastPathDestinationSoftwareStock = true;
                }
                else
                {
                    traceState.HasLastPathDestinationSoftwareStock = false;
                }
            }

            string currentClassification = ClassifySoftwareConsumerState(needState, buyerState, traceState);
            if (!string.Equals(traceState.CurrentClassification, currentClassification, StringComparison.Ordinal))
            {
                traceState.LastTransitionLabel = currentClassification;
                traceState.LastTransitionDay = day;
                traceState.LastTransitionSampleIndex = sampleIndex;
            }

            traceState.CurrentClassification = currentClassification;
            m_SoftwareConsumerTrace[company] = traceState;
            return traceState;
        }

        private static string ClassifySoftwareConsumerState(SoftwareNeedState needState, SoftwareBuyerState buyerState, SoftwareConsumerTraceState traceState)
        {
            if (buyerState.CurrentTradingCount > 0)
            {
                return kTraceCurrentTradingPresent;
            }

            if (buyerState.TripNeededCount > 0)
            {
                return kTraceTripReservedPresent;
            }

            if (buyerState.BuyerActive)
            {
                return kTraceBuyerActive;
            }

            if (buyerState.PathPending)
            {
                return kTracePathPending;
            }

            if (needState.Selected)
            {
                if (traceState.HasLastPathDestination ||
                    string.Equals(traceState.CurrentClassification, kTraceBuyerActive, StringComparison.Ordinal) ||
                    string.Equals(traceState.CurrentClassification, kTracePathPending, StringComparison.Ordinal) ||
                    string.Equals(traceState.CurrentClassification, kTraceTripReservedPresent, StringComparison.Ordinal) ||
                    string.Equals(traceState.CurrentClassification, kTraceCurrentTradingPresent, StringComparison.Ordinal))
                {
                    return kTracePathResolvedNoTradeState;
                }

                return kTraceNeedSelectedNoBuyer;
            }

            if (!string.IsNullOrEmpty(traceState.CurrentClassification) &&
                !string.Equals(traceState.CurrentClassification, kTraceNeedNotSelected, StringComparison.Ordinal) &&
                !string.Equals(traceState.CurrentClassification, kTraceNeedCleared, StringComparison.Ordinal))
            {
                return kTraceNeedCleared;
            }

            return kTraceNeedNotSelected;
        }

        private int GetCompanyBuyingLoad(Entity company, Resource resource)
        {
            if (resource == Resource.NoResource || !EntityManager.HasBuffer<OwnedVehicle>(company))
            {
                return 0;
            }

            int amount = 0;
            DynamicBuffer<OwnedVehicle> vehicles = EntityManager.GetBuffer<OwnedVehicle>(company, isReadOnly: true);
            for (int i = 0; i < vehicles.Length; i++)
            {
                amount += GetBuyingTruckLoad(vehicles[i].m_Vehicle, resource);
            }

            return amount;
        }

        private int GetBuyingTruckLoad(Entity vehicle, Resource resource)
        {
            if (!EntityManager.HasComponent<Game.Vehicles.DeliveryTruck>(vehicle))
            {
                return 0;
            }

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

            if (truck.m_Resource == resource && (truck.m_State & DeliveryTruckFlags.Buying) != 0)
            {
                return truck.m_Amount;
            }

            return 0;
        }

        private bool TryGetEntityResourceAmount(Entity entity, Resource resource, out int amount)
        {
            amount = 0;
            if (entity == Entity.Null || resource == Resource.NoResource || !EntityManager.HasBuffer<Resources>(entity))
            {
                return false;
            }

            amount = EconomyUtils.GetResources(resource, EntityManager.GetBuffer<Resources>(entity, isReadOnly: true));
            return true;
        }

        private bool ResourceHasWeight(Resource resource)
        {
            return EconomyUtils.GetWeight(EntityManager, resource, m_ResourceSystem.GetPrefabs()) > 0f;
        }

        private int GetCompanyRenterCount(Entity entity)
        {
            if (!EntityManager.HasBuffer<Renter>(entity))
            {
                return 0;
            }

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(entity, isReadOnly: true);
            int count = 0;
            for (int i = 0; i < renters.Length; i++)
            {
                if (EntityManager.HasComponent<CompanyData>(renters[i].m_Renter))
                {
                    count++;
                }
            }

            return count;
        }

        private int GetActiveCompanyRenterCount(Entity entity)
        {
            if (!EntityManager.HasBuffer<Renter>(entity))
            {
                return 0;
            }

            DynamicBuffer<Renter> renters = EntityManager.GetBuffer<Renter>(entity, isReadOnly: true);
            int count = 0;
            for (int i = 0; i < renters.Length; i++)
            {
                Entity renter = renters[i].m_Renter;
                if (!EntityManager.HasComponent<CompanyData>(renter) || !EntityManager.HasComponent<PropertyRenter>(renter))
                {
                    continue;
                }

                PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(renter);
                if (propertyRenter.m_Property == entity)
                {
                    count++;
                }
            }

            return count;
        }

        private bool TryGetTrackedPropertyType(Entity entity, out bool isOfficeProperty, out bool isIndustrialProperty)
        {
            isOfficeProperty = EntityManager.HasComponent<OfficeProperty>(entity);
            isIndustrialProperty = EntityManager.HasComponent<IndustrialProperty>(entity);
            return isOfficeProperty || isIndustrialProperty;
        }

        private static string FormatTopFactors(NativeArray<int> factors)
        {
            List<FactorEntry> entries = new List<FactorEntry>(factors.Length);
            for (int i = 0; i < factors.Length; i++)
            {
                int weight = factors[i];
                if (weight == 0)
                {
                    continue;
                }

                entries.Add(new FactorEntry
                {
                    Index = i,
                    Weight = weight
                });
            }

            entries.Sort(static (left, right) =>
            {
                int compare = Math.Abs(right.Weight).CompareTo(Math.Abs(left.Weight));
                if (compare != 0)
                {
                    return compare;
                }

                return right.Index.CompareTo(left.Index);
            });

            if (entries.Count > kTopFactorCount)
            {
                entries.RemoveRange(kTopFactorCount, entries.Count - kTopFactorCount);
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                FactorEntry entry = entries[i];
                builder.Append(Enum.GetName(typeof(DemandFactor), entry.Index));
                builder.Append('=');
                builder.Append(entry.Weight);
            }

            return builder.ToString();
        }

        private static bool IsDiagnosticsEnabled()
        {
            return Mod.Settings != null && Mod.Settings.EnableDemandDiagnostics;
        }

        private static bool IsVerboseLoggingEnabled()
        {
            return Mod.Settings != null && Mod.Settings.VerboseLogging;
        }

        private static bool IsStableEvidenceCaptureEnabled()
        {
            return Mod.Settings != null && Mod.Settings.CaptureStableEvidence;
        }

        private static bool HasSuspiciousSignals(DiagnosticSnapshot snapshot)
        {
            return snapshot.OfficeBuildingDemand == 0 ||
                   snapshot.EmptyBuildingsFactor != 0 ||
                   snapshot.FreeOfficePropertiesInOccupiedBuildings > 0 ||
                   snapshot.StaleRenterOnMarketOfficeProperties > 0 ||
                   snapshot.SignatureOccupiedOnMarketOffice > 0 ||
                   snapshot.SignatureOccupiedOnMarketIndustrial > 0 ||
                   snapshot.SignatureOccupiedToBeOnMarket > 0 ||
                   snapshot.NonSignatureOccupiedOnMarketOffice > 0 ||
                   snapshot.NonSignatureOccupiedOnMarketIndustrial > 0 ||
                   snapshot.GuardCorrections > 0 ||
                   snapshot.SoftwarePropertylessCompanies > 0 ||
                   snapshot.SoftwareProducerOfficeEfficiencyZero > 0 ||
                   snapshot.SoftwareProducerOfficeLackResourcesZero > 0 ||
                   snapshot.SoftwareConsumerOfficeEfficiencyZero > 0 ||
                   snapshot.SoftwareConsumerOfficeLackResourcesZero > 0 ||
                   snapshot.SoftwareConsumerOfficeSoftwareInputZero > 0 ||
                   snapshot.SoftwareConsumerNoBuyerDespiteNeed > 0 ||
                   snapshot.SoftwareConsumerTradeCostOnly > 0;
        }

        private static string FormatSettingsSnapshot()
        {
            if (Mod.Settings == null)
            {
                return "unavailable";
            }

            return $"EnableTradePatch:{Mod.Settings.EnableTradePatch}," +
                   $"EnablePhantomVacancyFix:{Mod.Settings.EnablePhantomVacancyFix}," +
                   $"EnableDemandDiagnostics:{Mod.Settings.EnableDemandDiagnostics}," +
                   $"DiagnosticsSamplesPerDay:{GetDiagnosticsSamplesPerDay()}," +
                   $"CaptureStableEvidence:{Mod.Settings.CaptureStableEvidence}," +
                   $"VerboseLogging:{Mod.Settings.VerboseLogging}";
        }

        private static string FormatPatchState()
        {
#if DEBUG
            return "debug-build";
#else
            return "unknown";
#endif
        }

        private bool TryGetObservationTrigger(DiagnosticSnapshot snapshot, out string trigger)
        {
            if (HasSuspiciousSignals(snapshot))
            {
                trigger = "suspicious_state";
                return true;
            }

            if (IsStableEvidenceCaptureEnabled())
            {
                trigger = "capture_stable_evidence";
                return true;
            }

            if (IsVerboseLoggingEnabled())
            {
                trigger = "verbose_logging";
                return true;
            }

            trigger = null;
            return false;
        }

        private void ResetEvidenceSession()
        {
            m_SessionId = CreateSessionId();
            m_RunSequence = 0;
            m_RunStartDay = int.MinValue;
            m_RunStartSampleIndex = int.MinValue;
            m_LastLoggedSampleIndex = int.MinValue;
            m_LastDiagnosticsEnabled = false;
            m_LastSettingsSnapshot = string.Empty;
            m_LastPatchState = string.Empty;
            m_SoftwareConsumerTrace.Clear();
        }

        private static string CreateSessionId()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        }

        private void BeginNewRun(int day, int sampleIndex, string settingsSnapshot, string patchState)
        {
            m_RunSequence++;
            m_RunStartDay = day;
            m_RunStartSampleIndex = sampleIndex;
            m_LastSettingsSnapshot = settingsSnapshot;
            m_LastPatchState = patchState;
            m_SoftwareConsumerTrace.Clear();
        }

        private int GetObservationSampleCount(int endSampleIndex)
        {
            return Math.Max(1, endSampleIndex - m_RunStartSampleIndex + 1);
        }

        private int GetSampleSlot(TimeSettingsData timeSettings, TimeData timeData, int samplesPerDay)
        {
            float normalizedTimeOfDay = m_TimeSystem.GetTimeOfDay(timeSettings, timeData, m_SimulationSystem.frameIndex);
            int sampleSlot = (int)Math.Floor(normalizedTimeOfDay * samplesPerDay);
            return Math.Max(0, Math.Min(samplesPerDay - 1, sampleSlot));
        }

        private static int GetSampleIndex(int day, int sampleSlot, int samplesPerDay)
        {
            return unchecked(day * samplesPerDay + sampleSlot);
        }

        private static int GetDiagnosticsSamplesPerDay()
        {
            if (Mod.Settings == null)
            {
                return kDefaultDiagnosticsSamplesPerDay;
            }

            return Math.Max(kMinDiagnosticsSamplesPerDay, Math.Min(kMaxDiagnosticsSamplesPerDay, Mod.Settings.DiagnosticsSamplesPerDay));
        }

        private void AppendDetail(StringBuilder builder, ref int count, string detail)
        {
            if (count >= kMaxDetailEntries || string.IsNullOrEmpty(detail))
            {
                return;
            }

            if (count > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(detail);
            count++;
        }

        private string DescribeProperty(Entity entity)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("entity=").Append(FormatEntity(entity));

            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                builder.Append(", prefab=").Append(GetPrefabLabel(prefabRef.m_Prefab));

                if (EntityManager.HasComponent<BuildingPropertyData>(prefabRef.m_Prefab))
                {
                    BuildingPropertyData propertyData = EntityManager.GetComponentData<BuildingPropertyData>(prefabRef.m_Prefab);
                    builder.Append(", manufactured=").Append(propertyData.m_AllowedManufactured);
                    builder.Append(", input=").Append(propertyData.m_AllowedInput);
                    builder.Append(", propertyCount=").Append(propertyData.CountProperties());
                }
            }

            if (EntityManager.HasComponent<PropertyOnMarket>(entity))
            {
                PropertyOnMarket market = EntityManager.GetComponentData<PropertyOnMarket>(entity);
                builder.Append(", askingRent=").Append(market.m_AskingRent);
            }

            int companyRenters = GetCompanyRenterCount(entity);
            int activeCompanyRenters = GetActiveCompanyRenterCount(entity);
            int totalRenters = EntityManager.HasBuffer<Renter>(entity)
                ? EntityManager.GetBuffer<Renter>(entity, isReadOnly: true).Length
                : 0;
            builder.Append(", renters(total=").Append(totalRenters);
            builder.Append(", company=").Append(companyRenters);
            builder.Append(", active=").Append(activeCompanyRenters).Append(')');

            if (EntityManager.HasComponent<Attached>(entity))
            {
                Attached attached = EntityManager.GetComponentData<Attached>(entity);
                builder.Append(", parent=").Append(FormatEntity(attached.m_Parent));
                builder.Append(", parentActiveCompanies=").Append(GetActiveCompanyRenterCount(attached.m_Parent));
            }

            builder.Append(", toBeOnMarket=").Append(EntityManager.HasComponent<PropertyToBeOnMarket>(entity));
            builder.Append(", signature=").Append(EntityManager.HasComponent<Signature>(entity));
            builder.Append(", propertyType=");
            if (EntityManager.HasComponent<OfficeProperty>(entity))
            {
                builder.Append("office");
            }
            else if (EntityManager.HasComponent<IndustrialProperty>(entity))
            {
                builder.Append("industrial");
            }
            else
            {
                builder.Append("other");
            }
            return builder.ToString();
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

        private static string FormatEntity(Entity entity)
        {
            return entity.Index + ":" + entity.Version;
        }
    }
}
