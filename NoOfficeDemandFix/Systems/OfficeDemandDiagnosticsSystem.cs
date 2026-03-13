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
        private const int kDiagnosticsDisabledPollInterval = 8;
        private const float kNotificationCostLimit = 5f;
        private const int kResourceLowStockAmount = 4000;
        private const string kTraceNeedNotSelected = "need_not_selected";
        private const string kTraceSelectedNoResourceBuyer = "selected_no_resource_buyer";
        private const string kTraceSelectedResourceBuyerNoPath = "selected_resource_buyer_no_path";
        private const string kTraceSelectedPathPending = "selected_path_pending";
        private const string kTraceSelectedResolvedVirtualNoTrackingExpected = "selected_resolved_virtual_no_tracking_expected";
        private const string kTraceSelectedResolvedNoTrackingUnexpected = "selected_resolved_no_tracking_unexpected";
        private const string kTraceSelectedTripPresent = "selected_trip_present";
        private const string kTraceSelectedCurrentTradingPresent = "selected_current_trading_present";
        private const string kTraceNeedCleared = "need_cleared";
        private const string kTraceTransitionUnobserved = "unobserved";

        private struct FactorEntry
        {
            public int Index;
            public int Weight;
        }

        private readonly struct DiagnosticsSettingsState : IEquatable<DiagnosticsSettingsState>
        {
            public DiagnosticsSettingsState(
                bool enableTradePatch,
                bool enablePhantomVacancyFix,
                bool enableDemandDiagnostics,
                int diagnosticsSamplesPerDay,
                bool captureStableEvidence,
                bool verboseLogging)
            {
                EnableTradePatch = enableTradePatch;
                EnablePhantomVacancyFix = enablePhantomVacancyFix;
                EnableDemandDiagnostics = enableDemandDiagnostics;
                DiagnosticsSamplesPerDay = diagnosticsSamplesPerDay;
                CaptureStableEvidence = captureStableEvidence;
                VerboseLogging = verboseLogging;
            }

            public bool EnableTradePatch { get; }
            public bool EnablePhantomVacancyFix { get; }
            public bool EnableDemandDiagnostics { get; }
            public int DiagnosticsSamplesPerDay { get; }
            public bool CaptureStableEvidence { get; }
            public bool VerboseLogging { get; }

            public bool Equals(DiagnosticsSettingsState other)
            {
                return EnableTradePatch == other.EnableTradePatch &&
                       EnablePhantomVacancyFix == other.EnablePhantomVacancyFix &&
                       EnableDemandDiagnostics == other.EnableDemandDiagnostics &&
                       DiagnosticsSamplesPerDay == other.DiagnosticsSamplesPerDay &&
                       CaptureStableEvidence == other.CaptureStableEvidence &&
                       VerboseLogging == other.VerboseLogging;
            }

            public override bool Equals(object obj)
            {
                return obj is DiagnosticsSettingsState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    EnableTradePatch,
                    EnablePhantomVacancyFix,
                    EnableDemandDiagnostics,
                    DiagnosticsSamplesPerDay,
                    CaptureStableEvidence,
                    VerboseLogging);
            }
        }

        private readonly struct SampleWindowContext
        {
            public SampleWindowContext(int day, int sampleIndex, int sampleSlot, string clockSource)
            {
                Day = day;
                SampleIndex = sampleIndex;
                SampleSlot = sampleSlot;
                ClockSource = clockSource;
            }

            public int Day { get; }
            public int SampleIndex { get; }
            public int SampleSlot { get; }
            public string ClockSource { get; }
        }

        private struct DiagnosticSnapshot
        {
            public int Day;
            public int SampleIndex;
            public int SampleSlot;
            public int SamplesPerDay;
            public string ClockSource;
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
            public int SoftwareConsumerResourceBuyerPresent;
            public int SoftwareConsumerTrackingExpectedSelected;
            public int SoftwareConsumerSelectedNoResourceBuyer;
            public int SoftwareConsumerSelectedRequestNoPath;
            public int SoftwareConsumerPathPending;
            public int SoftwareConsumerResolvedVirtualNoTrackingExpected;
            public int SoftwareConsumerResolvedNoTrackingUnexpected;
            public int SoftwareConsumerTripPresent;
            public int SoftwareConsumerCurrentTradingPresent;
            public string TopFactors;
            public string FreeSoftwareOfficePropertyDetails;
            public string OnMarketOfficePropertyDetails;
            public string SoftwareOfficeDetails;
            public string SoftwareTradeLifecycleDetails;
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

        private struct ResourceTripState
        {
            public int TotalCount;
            public int TotalAmount;
            public int ShoppingCount;
            public int ShoppingAmount;
            public int CompanyShoppingCount;
            public int CompanyShoppingAmount;
            public int OtherCount;
            public int OtherAmount;
        }

        private struct SoftwareTradeCostState
        {
            public bool HasEntry;
            public float BuyCost;
            public float SellCost;
            public long LastTransferRequestTime;
        }

        private struct SoftwareAcquisitionState
        {
            public bool ResourceBuyerPresent;
            public int ResourceBuyerAmount;
            public SetupTargetFlags ResourceBuyerFlags;
            public float ResourceWeight;
            public bool VirtualGood;
            public bool TripTrackingExpected;
            public bool PathComponentPresent;
            public bool PathPending;
            public PathFlags PathState;
            public PathMethod PathMethods;
            public Entity PathDestination;
            public float PathDistance;
            public float PathDuration;
            public float PathTotalCost;
            public int TripNeededCount;
            public int TripNeededAmount;
            public int ShoppingTripCount;
            public int ShoppingTripAmount;
            public int CompanyShoppingTripCount;
            public int CompanyShoppingTripAmount;
            public int OtherTripCount;
            public int OtherTripAmount;
            public int CurrentTradingCount;
            public int CurrentTradingAmount;
        }

        private struct SoftwareConsumerTraceState
        {
            public string CurrentClassification;
            public string LastTransitionLabel;
            public string LastTransitionFromLabel;
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
            public SoftwareAcquisitionState Acquisition;
            public SoftwareConsumerTraceState Trace;
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
        private int m_LastProcessedSampleIndex = int.MinValue;
        private int m_LastObservedSampleIndex = int.MinValue;
        private int m_DisplayedClockDay = int.MinValue;
        private int m_LastComputedSampleSlot = int.MinValue;
        private bool m_LastDiagnosticsEnabled;
        private string m_SessionId = CreateSessionId();
        private int m_RunSequence;
        private int m_RunStartDay = int.MinValue;
        private int m_RunStartSampleIndex = int.MinValue;
        private int m_RunObservationCount;
        private DiagnosticsSettingsState m_LastSettingsState;
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
                return 1;
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

            if (!diagnosticsEnabled)
            {
                if (!m_LastDiagnosticsEnabled &&
                    (m_SimulationSystem.frameIndex % (uint)kDiagnosticsDisabledPollInterval) != 0u)
                {
                    return;
                }

                m_LastDiagnosticsEnabled = false;
                m_LastSettingsState = default;
                m_LastSettingsSnapshot = string.Empty;
                m_LastPatchState = string.Empty;
                m_DisplayedClockDay = int.MinValue;
                m_LastComputedSampleSlot = int.MinValue;
                return;
            }

            DiagnosticsSettingsState settingsState = CaptureSettingsState();
            string patchState = FormatPatchState();
            bool runStateChanged = !m_LastDiagnosticsEnabled ||
                                   !settingsState.Equals(m_LastSettingsState) ||
                                   patchState != m_LastPatchState;
            if (runStateChanged)
            {
                BeginNewRun(settingsState, patchState);
            }

            TimeData timeData = m_TimeDataQuery.GetSingleton<TimeData>();
            TimeSettingsData timeSettings = m_TimeSettingsQuery.GetSingleton<TimeSettingsData>();
            int samplesPerDay = settingsState.DiagnosticsSamplesPerDay;
            SampleWindowContext sampleWindow = GetSampleWindowContext(timeSettings, timeData, samplesPerDay);
            if (runStateChanged)
            {
                m_LastProcessedSampleIndex = sampleWindow.SampleIndex - 1;
            }

            m_LastDiagnosticsEnabled = true;
            if (sampleWindow.SampleIndex <= m_LastProcessedSampleIndex)
            {
                return;
            }

            DiagnosticSnapshot currentSnapshot = CaptureSnapshot(
                sampleWindow.Day,
                sampleWindow.SampleIndex,
                sampleWindow.SampleSlot,
                samplesPerDay,
                sampleWindow.ClockSource,
                settingsState.VerboseLogging);
            int skippedSampleSlots = GetSkippedSampleSlots(sampleWindow.SampleIndex);
            m_LastProcessedSampleIndex = sampleWindow.SampleIndex;
            EmitObservationIfTriggered(currentSnapshot, settingsState, skippedSampleSlots);
        }

        private void EmitObservationIfTriggered(
            DiagnosticSnapshot snapshot,
            DiagnosticsSettingsState settingsState,
            int skippedSampleSlots)
        {
            if (!TryGetObservationTrigger(snapshot, settingsState, out string trigger))
            {
                return;
            }

            if (m_RunObservationCount == 0)
            {
                m_RunStartDay = snapshot.Day;
                m_RunStartSampleIndex = snapshot.SampleIndex;
            }

            int sampleCount = m_RunObservationCount + 1;

            Mod.log.Info(
                MachineParsedLogContract.FormatObservationWindow(
                    m_SessionId,
                    m_RunSequence,
                    m_RunStartDay,
                    snapshot.Day,
                    m_RunStartSampleIndex,
                    snapshot.SampleIndex,
                    snapshot.Day,
                    snapshot.SampleIndex,
                    snapshot.SampleSlot,
                    snapshot.SamplesPerDay,
                    sampleCount,
                    MachineParsedLogContract.ScheduledObservationKind,
                    skippedSampleSlots,
                    snapshot.ClockSource,
                    trigger,
                    m_LastSettingsSnapshot,
                    m_LastPatchState,
                    FormatDiagnosticCounters(snapshot),
                    snapshot.TopFactors));

            if (!string.IsNullOrEmpty(snapshot.FreeSoftwareOfficePropertyDetails))
            {
                Mod.log.Info(
                    MachineParsedLogContract.FormatDetail(
                        m_SessionId,
                        m_RunSequence,
                        snapshot.Day,
                        snapshot.SampleIndex,
                        MachineParsedLogContract.FreeSoftwareOfficePropertiesDetailType,
                        snapshot.FreeSoftwareOfficePropertyDetails));
            }

            if (!string.IsNullOrEmpty(snapshot.OnMarketOfficePropertyDetails))
            {
                Mod.log.Info(
                    MachineParsedLogContract.FormatDetail(
                        m_SessionId,
                        m_RunSequence,
                        snapshot.Day,
                        snapshot.SampleIndex,
                        MachineParsedLogContract.OnMarketOfficePropertiesDetailType,
                        snapshot.OnMarketOfficePropertyDetails));
            }

            if (!string.IsNullOrEmpty(snapshot.SoftwareOfficeDetails))
            {
                Mod.log.Info(
                    MachineParsedLogContract.FormatDetail(
                        m_SessionId,
                        m_RunSequence,
                        snapshot.Day,
                        snapshot.SampleIndex,
                        MachineParsedLogContract.SoftwareOfficeStatesDetailType,
                        snapshot.SoftwareOfficeDetails));
            }

            if (!string.IsNullOrEmpty(snapshot.SoftwareTradeLifecycleDetails))
            {
                Mod.log.Info(
                    MachineParsedLogContract.FormatDetail(
                        m_SessionId,
                        m_RunSequence,
                        snapshot.Day,
                        snapshot.SampleIndex,
                        MachineParsedLogContract.SoftwareTradeLifecycleDetailType,
                        snapshot.SoftwareTradeLifecycleDetails));
            }

            m_RunObservationCount = sampleCount;
            m_LastObservedSampleIndex = snapshot.SampleIndex;
        }

        private DiagnosticSnapshot CaptureSnapshot(
            int day,
            int sampleIndex,
            int sampleSlot,
            int samplesPerDay,
            string clockSource,
            bool verboseLogging)
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
                ClockSource = clockSource,
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
            CountSoftwareOffices(ref snapshot, verboseLogging);

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

        private void CountSoftwareOffices(ref DiagnosticSnapshot snapshot, bool verboseLogging)
        {
            StringBuilder details = new StringBuilder();
            int detailCount = 0;
            StringBuilder lifecycleDetails = new StringBuilder();
            int lifecycleDetailCount = 0;
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

                    if (softwareConsumerState.Acquisition.ResourceBuyerPresent)
                    {
                        snapshot.SoftwareConsumerResourceBuyerPresent++;
                    }

                    if (softwareConsumerState.Need.Selected && softwareConsumerState.Acquisition.TripTrackingExpected)
                    {
                        snapshot.SoftwareConsumerTrackingExpectedSelected++;
                    }

                    if (softwareConsumerState.Acquisition.PathPending)
                    {
                        snapshot.SoftwareConsumerPathPending++;
                    }

                    if (softwareConsumerState.Acquisition.TripNeededCount > 0)
                    {
                        snapshot.SoftwareConsumerTripPresent++;
                    }

                    if (softwareConsumerState.Acquisition.CurrentTradingCount > 0)
                    {
                        snapshot.SoftwareConsumerCurrentTradingPresent++;
                    }

                    if (string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal))
                    {
                        snapshot.SoftwareConsumerSelectedNoResourceBuyer++;
                    }

                    if (string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal))
                    {
                        snapshot.SoftwareConsumerSelectedRequestNoPath++;
                    }

                    if (string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedResolvedVirtualNoTrackingExpected, StringComparison.Ordinal))
                    {
                        snapshot.SoftwareConsumerResolvedVirtualNoTrackingExpected++;
                    }

                    if (string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedResolvedNoTrackingUnexpected, StringComparison.Ordinal))
                    {
                        snapshot.SoftwareConsumerResolvedNoTrackingUnexpected++;
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

                if (efficiencyZero || lackResourcesZero || softwareInputZero || (isConsumer && ShouldCaptureConsumerOfficeDetail(softwareConsumerState)))
                {
                    AppendDetail(details, ref detailCount, DescribeSoftwareOffice(company, prefabRef.m_Prefab, propertyRenter.m_Property, processData, isProducer, isConsumer, softwareInputZero, hasEfficiency, efficiency, lackResources, softwareConsumerState));
                }

                if (verboseLogging && isConsumer && ShouldCaptureConsumerTradeLifecycle(softwareConsumerState, snapshot.Day, snapshot.SampleIndex))
                {
                    AppendDetail(
                        lifecycleDetails,
                        ref lifecycleDetailCount,
                        DescribeConsumerTradeLifecycle(
                            company,
                            prefabRef.m_Prefab,
                            propertyRenter.m_Property,
                            processData,
                            hasEfficiency,
                            efficiency,
                            lackResources,
                            softwareConsumerState));
                }

                if (verboseLogging && isProducer && (efficiencyZero || lackResourcesZero))
                {
                    AppendDetail(
                        lifecycleDetails,
                        ref lifecycleDetailCount,
                        DescribeProducerTradeLifecycle(
                            company,
                            prefabRef.m_Prefab,
                            propertyRenter.m_Property,
                            processData,
                            hasEfficiency,
                            efficiency,
                            lackResources));
                }
            }

            snapshot.SoftwareOfficeDetails = details.ToString();
            snapshot.SoftwareTradeLifecycleDetails = lifecycleDetails.ToString();
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
                AppendSoftwareAcquisitionState(builder, softwareConsumerState.Acquisition, softwareConsumerState.Trace.CurrentClassification);
            }

            if (!isConsumer)
            {
                AppendCurrentResourceBuyerSnapshot(builder, company);
            }

            builder.Append(", efficiency=");
            AppendMetricValue(builder, hasEfficiency, efficiency);
            builder.Append(", lackResources=");
            AppendMetricValue(builder, hasEfficiency, lackResources);
            return builder.ToString();
        }

        private static bool ShouldCaptureConsumerOfficeDetail(SoftwareConsumerDiagnosticState state)
        {
            if (!state.Need.Selected)
            {
                return false;
            }

            return IsOfficeDetailAcquisitionState(state.Trace.CurrentClassification);
        }

        private static bool ShouldCaptureConsumerTradeLifecycle(SoftwareConsumerDiagnosticState state, int day, int sampleIndex)
        {
            if (state.Trace.LastTransitionDay != day || state.Trace.LastTransitionSampleIndex != sampleIndex)
            {
                return false;
            }

            return IsLifecycleTraceState(state.Trace.CurrentClassification) ||
                   IsLifecycleTraceState(state.Trace.LastTransitionFromLabel);
        }

        private static bool IsLifecycleTraceState(string classification)
        {
            return string.Equals(classification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedPathPending, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResolvedVirtualNoTrackingExpected, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResolvedNoTrackingUnexpected, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedTripPresent, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedCurrentTradingPresent, StringComparison.Ordinal);
        }

        private static bool IsOfficeDetailAcquisitionState(string classification)
        {
            return string.Equals(classification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedPathPending, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResolvedVirtualNoTrackingExpected, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResolvedNoTrackingUnexpected, StringComparison.Ordinal);
        }

        private string DescribeConsumerTradeLifecycle(
            Entity company,
            Entity companyPrefab,
            Entity property,
            IndustrialProcessData processData,
            bool hasEfficiency,
            float efficiency,
            float lackResources,
            SoftwareConsumerDiagnosticState softwareConsumerState)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("role=consumer");
            builder.Append(", company=").Append(FormatEntity(company));
            builder.Append(", prefab=").Append(GetPrefabLabel(companyPrefab));
            builder.Append(", property=").Append(FormatEntity(property));
            builder.Append(", capture=transition");
            builder.Append(", output=").Append(processData.m_Output.m_Resource);
            builder.Append(", outputStock=").Append(GetCompanyResourceAmount(company, processData.m_Output.m_Resource));
            AppendCompanyResourceState(builder, company, "input1", processData.m_Input1.m_Resource);
            AppendCompanyResourceState(builder, company, "input2", processData.m_Input2.m_Resource);
            AppendSoftwareTransitionState(builder, softwareConsumerState.Trace);
            AppendSoftwareNeedState(builder, softwareConsumerState.Need);
            AppendSoftwareTradeCostState(builder, softwareConsumerState.TradeCost);
            AppendSoftwareAcquisitionState(builder, softwareConsumerState.Acquisition, softwareConsumerState.Trace.CurrentClassification);
            AppendResourceTripState(builder, "softwareTripState", softwareConsumerState.Acquisition);
            AppendBuyingCompanyState(builder, company);
            if (TryGetPathSeller(softwareConsumerState, out Entity pathSeller))
            {
                AppendSellerSnapshot(builder, "pathSeller", pathSeller, Resource.Software);
            }

            if (TryGetLastTradePartner(company, out Entity lastTradePartner))
            {
                AppendSellerSnapshot(builder, "lastTradePartnerSeller", lastTradePartner, Resource.Software);
            }

            builder.Append(", efficiency=");
            AppendMetricValue(builder, hasEfficiency, efficiency);
            builder.Append(", lackResources=");
            AppendMetricValue(builder, hasEfficiency, lackResources);
            return builder.ToString();
        }

        private string DescribeProducerTradeLifecycle(
            Entity company,
            Entity companyPrefab,
            Entity property,
            IndustrialProcessData processData,
            bool hasEfficiency,
            float efficiency,
            float lackResources)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("role=producer");
            builder.Append(", company=").Append(FormatEntity(company));
            builder.Append(", prefab=").Append(GetPrefabLabel(companyPrefab));
            builder.Append(", property=").Append(FormatEntity(property));
            builder.Append(", capture=suspicious_producer");
            builder.Append(", output=").Append(processData.m_Output.m_Resource);
            builder.Append(", outputStock=").Append(GetCompanyResourceAmount(company, processData.m_Output.m_Resource));
            AppendCompanyResourceState(builder, company, "input1", processData.m_Input1.m_Resource);
            AppendCompanyResourceState(builder, company, "input2", processData.m_Input2.m_Resource);
            AppendTradeCostSnapshot(builder, "outputTradeCost", processData.m_Output.m_Resource, company);
            AppendTradeCostSnapshot(builder, "input1TradeCost", processData.m_Input1.m_Resource, company);
            AppendTradeCostSnapshot(builder, "input2TradeCost", processData.m_Input2.m_Resource, company);
            AppendCurrentResourceBuyerSnapshot(builder, company);
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
            // Note: We intentionally only log the current stock here to keep diagnostics concise.
            // If additional detail (e.g., trade-cost buffer or entry information) is required for debugging,
            // extend this formatter or use a more verbose diagnostic path instead of reintroducing it here.
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

        private void AppendSoftwareAcquisitionState(StringBuilder builder, SoftwareAcquisitionState state, string classification)
        {
            builder.Append(", softwareAcquisitionState(");
            builder.Append("classification=").Append(string.IsNullOrEmpty(classification) ? kTraceNeedNotSelected : classification);
            builder.Append(", resourceBuyerPresent=").Append(state.ResourceBuyerPresent);
            builder.Append(", resourceBuyerAmount=");
            builder.Append(state.ResourceBuyerPresent ? state.ResourceBuyerAmount.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", resourceBuyerFlags=");
            builder.Append(state.ResourceBuyerPresent ? state.ResourceBuyerFlags.ToString() : "none");
            builder.Append(", resourceWeight=").Append(state.ResourceWeight.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(", virtualGood=").Append(state.VirtualGood);
            builder.Append(", tripTrackingExpected=").Append(state.TripTrackingExpected);
            builder.Append(", pathComponentPresent=").Append(state.PathComponentPresent);
            builder.Append(", pathState=");
            builder.Append(state.PathComponentPresent ? state.PathState.ToString() : "none");
            builder.Append(", pathMethods=");
            builder.Append(state.PathComponentPresent ? state.PathMethods.ToString() : "none");
            builder.Append(", pathDestination=");
            builder.Append(state.PathComponentPresent && state.PathDestination != Entity.Null ? FormatEntity(state.PathDestination) : "none");
            builder.Append(", pathDistance=");
            builder.Append(state.PathComponentPresent ? state.PathDistance.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", pathDuration=");
            builder.Append(state.PathComponentPresent ? state.PathDuration.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", pathTotalCost=");
            builder.Append(state.PathComponentPresent ? state.PathTotalCost.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", tripNeededCount=").Append(state.TripNeededCount);
            builder.Append(", tripNeededAmount=").Append(state.TripNeededAmount);
            builder.Append(", tripShoppingCount=").Append(state.ShoppingTripCount);
            builder.Append(", tripCompanyShoppingCount=").Append(state.CompanyShoppingTripCount);
            builder.Append(", currentTradingCount=").Append(state.CurrentTradingCount);
            builder.Append(", currentTradingAmount=").Append(state.CurrentTradingAmount);
            builder.Append(')');
        }

        private void AppendSoftwareTransitionState(StringBuilder builder, SoftwareConsumerTraceState state)
        {
            builder.Append(", transition(");
            builder.Append("from=").Append(string.IsNullOrEmpty(state.LastTransitionFromLabel) ? kTraceTransitionUnobserved : state.LastTransitionFromLabel);
            builder.Append(", to=").Append(string.IsNullOrEmpty(state.CurrentClassification) ? kTraceNeedNotSelected : state.CurrentClassification);
            builder.Append(", day=").Append(state.LastTransitionDay == 0 ? "n/a" : state.LastTransitionDay.ToString(CultureInfo.InvariantCulture));
            builder.Append(", sampleIndex=").Append(state.LastTransitionSampleIndex == 0 ? "n/a" : state.LastTransitionSampleIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(')');
        }

        private void AppendResourceTripState(StringBuilder builder, string label, SoftwareAcquisitionState state)
        {
            builder.Append(", ").Append(label).Append('(');
            builder.Append("totalCount=").Append(state.TripNeededCount);
            builder.Append(", totalAmount=").Append(state.TripNeededAmount);
            builder.Append(", shoppingCount=").Append(state.ShoppingTripCount);
            builder.Append(", shoppingAmount=").Append(state.ShoppingTripAmount);
            builder.Append(", companyShoppingCount=").Append(state.CompanyShoppingTripCount);
            builder.Append(", companyShoppingAmount=").Append(state.CompanyShoppingTripAmount);
            builder.Append(", otherCount=").Append(state.OtherTripCount);
            builder.Append(", otherAmount=").Append(state.OtherTripAmount);
            builder.Append(')');
        }

        private void AppendCurrentResourceBuyerSnapshot(StringBuilder builder, Entity company)
        {
            if (!EntityManager.HasComponent<ResourceBuyer>(company))
            {
                return;
            }

            ResourceBuyer buyer = EntityManager.GetComponentData<ResourceBuyer>(company);
            builder.Append(", activeBuyer(");
            builder.Append("resource=").Append(buyer.m_ResourceNeeded);
            builder.Append(", amount=").Append(buyer.m_AmountNeeded);
            builder.Append(')');
        }

        private static bool TryGetPathSeller(SoftwareConsumerDiagnosticState state, out Entity seller)
        {
            if (state.Acquisition.PathComponentPresent && state.Acquisition.PathDestination != Entity.Null)
            {
                seller = state.Acquisition.PathDestination;
                return true;
            }

            if (state.Trace.HasLastPathDestination && state.Trace.LastPathDestination != Entity.Null)
            {
                seller = state.Trace.LastPathDestination;
                return true;
            }

            seller = Entity.Null;
            return false;
        }

        private void AppendBuyingCompanyState(StringBuilder builder, Entity company)
        {
            builder.Append(", buyingCompany(");
            if (EntityManager.HasComponent<BuyingCompany>(company))
            {
                BuyingCompany buyingCompany = EntityManager.GetComponentData<BuyingCompany>(company);
                builder.Append("lastTradePartner=");
                builder.Append(buyingCompany.m_LastTradePartner == Entity.Null ? "none" : FormatEntity(buyingCompany.m_LastTradePartner));
                builder.Append(", meanInputTripLength=").Append(buyingCompany.m_MeanInputTripLength.ToString("0.###", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append("lastTradePartner=n/a, meanInputTripLength=n/a");
            }

            builder.Append(')');
        }

        private void AppendTradeCostSnapshot(StringBuilder builder, string label, Resource resource, Entity company)
        {
            if (resource == Resource.NoResource)
            {
                return;
            }

            TryGetCompanyTradeCost(company, resource, out SoftwareTradeCostState tradeCostState);
            builder.Append(", ").Append(label).Append('(');
            builder.Append("resource=").Append(resource);
            builder.Append(", tradeCostEntry=").Append(tradeCostState.HasEntry);
            builder.Append(", buyCost=").Append(tradeCostState.HasEntry ? tradeCostState.BuyCost.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", sellCost=").Append(tradeCostState.HasEntry ? tradeCostState.SellCost.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastTransferRequestTime=").Append(tradeCostState.HasEntry ? tradeCostState.LastTransferRequestTime.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(')');
        }

        private void AppendSellerSnapshot(StringBuilder builder, string label, Entity seller, Resource resource)
        {
            if (seller == Entity.Null)
            {
                return;
            }

            builder.Append(", ").Append(label).Append('(');
            builder.Append("entity=").Append(FormatEntity(seller));
            builder.Append(", kind=").Append(GetSellerKindLabel(seller));
            if (TryGetEntityResourceAmount(seller, resource, out int stock))
            {
                int buyingLoad = GetCompanyBuyingLoad(seller, resource);
                builder.Append(", stock=").Append(stock);
                builder.Append(", buyingLoad=").Append(buyingLoad);
                builder.Append(", availableStock=").Append(Math.Max(0, stock - buyingLoad));
            }
            else
            {
                builder.Append(", stock=n/a, buyingLoad=n/a, availableStock=n/a");
            }

            if (TryGetCompanyTradeCost(seller, resource, out SoftwareTradeCostState tradeCostState))
            {
                builder.Append(", tradeCostEntry=True");
                builder.Append(", buyCost=").Append(tradeCostState.BuyCost.ToString("0.###", CultureInfo.InvariantCulture));
                builder.Append(", sellCost=").Append(tradeCostState.SellCost.ToString("0.###", CultureInfo.InvariantCulture));
                builder.Append(", lastTransferRequestTime=").Append(tradeCostState.LastTransferRequestTime.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(", tradeCostEntry=False, buyCost=n/a, sellCost=n/a, lastTransferRequestTime=n/a");
            }

            builder.Append(", outsideConnectionType=");
            builder.Append(TryGetOutsideConnectionType(seller, out OutsideConnectionTransferType outsideConnectionType)
                ? outsideConnectionType.ToString()
                : "none");
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
                tradeCostState.SellCost = tradeCost.m_SellCost;
                tradeCostState.LastTransferRequestTime = tradeCost.m_LastTransferRequestTime;
                return true;
            }

            return false;
        }

        private bool TryGetResourceBuyer(Entity company, Resource resource, out ResourceBuyer buyer)
        {
            buyer = default;
            if (!EntityManager.HasComponent<ResourceBuyer>(company))
            {
                return false;
            }

            buyer = EntityManager.GetComponentData<ResourceBuyer>(company);
            if (buyer.m_ResourceNeeded != resource)
            {
                buyer = default;
                return false;
            }

            return true;
        }

        private ResourceTripState GetCompanyTripState(Entity company, Resource resource)
        {
            ResourceTripState state = default;
            if (!EntityManager.HasBuffer<CitizenTripNeeded>(company))
            {
                return state;
            }

            DynamicBuffer<CitizenTripNeeded> trips = EntityManager.GetBuffer<CitizenTripNeeded>(company, isReadOnly: true);
            for (int i = 0; i < trips.Length; i++)
            {
                CitizenTripNeeded trip = trips[i];
                if (trip.m_Resource != resource)
                {
                    continue;
                }

                state.TotalCount++;
                state.TotalAmount += trip.m_Data;
                if (trip.m_Purpose == Game.Citizens.Purpose.Shopping)
                {
                    state.ShoppingCount++;
                    state.ShoppingAmount += trip.m_Data;
                    continue;
                }

                if (trip.m_Purpose == Game.Citizens.Purpose.CompanyShopping)
                {
                    state.CompanyShoppingCount++;
                    state.CompanyShoppingAmount += trip.m_Data;
                    continue;
                }

                state.OtherCount++;
                state.OtherAmount += trip.m_Data;
            }

            return state;
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

        private float GetResourceWeight(Resource resource)
        {
            if (resource == Resource.NoResource)
            {
                return 0f;
            }

            return EconomyUtils.GetWeight(EntityManager, resource, m_ResourceSystem.GetPrefabs());
        }

        private SoftwareConsumerDiagnosticState GetSoftwareConsumerDiagnosticState(Entity company, Entity companyPrefab, IndustrialProcessData processData, int day, int sampleIndex)
        {
            SoftwareNeedState needState = GetSoftwareNeedState(company, companyPrefab, processData);
            TryGetCompanyTradeCost(company, Resource.Software, out SoftwareTradeCostState tradeCostState);
            SoftwareAcquisitionState acquisitionState = GetSoftwareAcquisitionState(company);
            SoftwareConsumerTraceState traceState = UpdateSoftwareConsumerTrace(company, needState, acquisitionState, day, sampleIndex);
            return new SoftwareConsumerDiagnosticState
            {
                Need = needState,
                TradeCost = tradeCostState,
                Acquisition = acquisitionState,
                Trace = traceState
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
            // BuyingCompanySystem only adds Purpose.Shopping to need selection.
            ResourceTripState tripState = GetCompanyTripState(company, resource);
            int tripNeededAmount = tripState.ShoppingAmount;
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

        private SoftwareAcquisitionState GetSoftwareAcquisitionState(Entity company)
        {
            SoftwareAcquisitionState state = default;
            state.ResourceWeight = GetResourceWeight(Resource.Software);
            state.VirtualGood = state.ResourceWeight <= 0f;
            state.TripTrackingExpected = !state.VirtualGood;
            if (TryGetResourceBuyer(company, Resource.Software, out ResourceBuyer buyer))
            {
                state.ResourceBuyerPresent = true;
                state.ResourceBuyerAmount = buyer.m_AmountNeeded;
                state.ResourceBuyerFlags = buyer.m_Flags;
            }

            ResourceTripState tripState = GetCompanyTripState(company, Resource.Software);
            state.TripNeededCount = tripState.TotalCount;
            state.TripNeededAmount = tripState.TotalAmount;
            state.ShoppingTripCount = tripState.ShoppingCount;
            state.ShoppingTripAmount = tripState.ShoppingAmount;
            state.CompanyShoppingTripCount = tripState.CompanyShoppingCount;
            state.CompanyShoppingTripAmount = tripState.CompanyShoppingAmount;
            state.OtherTripCount = tripState.OtherCount;
            state.OtherTripAmount = tripState.OtherAmount;
            state.CurrentTradingAmount = GetCompanyCurrentTradingAmount(company, Resource.Software, out state.CurrentTradingCount);
            if (TryGetCompanyPathInformation(company, out PathInformation pathInformation))
            {
                state.PathComponentPresent = true;
                state.PathState = pathInformation.m_State;
                state.PathMethods = pathInformation.m_Methods;
                state.PathDestination = pathInformation.m_Destination;
                state.PathDistance = pathInformation.m_Distance;
                state.PathDuration = pathInformation.m_Duration;
                state.PathTotalCost = pathInformation.m_TotalCost;
                state.PathPending = (pathInformation.m_State & (PathFlags.Pending | PathFlags.Scheduled)) != 0;
            }

            return state;
        }

        private SoftwareConsumerTraceState UpdateSoftwareConsumerTrace(Entity company, SoftwareNeedState needState, SoftwareAcquisitionState acquisitionState, int day, int sampleIndex)
        {
            m_SoftwareConsumerTrace.TryGetValue(company, out SoftwareConsumerTraceState traceState);
            if (acquisitionState.PathComponentPresent && acquisitionState.PathDestination != Entity.Null)
            {
                traceState.LastPathDestination = acquisitionState.PathDestination;
                traceState.HasLastPathDestination = true;
                if (TryGetEntityResourceAmount(acquisitionState.PathDestination, Resource.Software, out int destinationSoftwareStock))
                {
                    traceState.LastPathDestinationSoftwareStock = destinationSoftwareStock;
                    traceState.HasLastPathDestinationSoftwareStock = true;
                }
                else
                {
                    traceState.HasLastPathDestinationSoftwareStock = false;
                }
            }

            string currentClassification = ClassifySoftwareConsumerState(needState, acquisitionState, traceState);
            if (!string.Equals(traceState.CurrentClassification, currentClassification, StringComparison.Ordinal))
            {
                traceState.LastTransitionFromLabel = traceState.CurrentClassification;
                traceState.LastTransitionLabel = currentClassification;
                traceState.LastTransitionDay = day;
                traceState.LastTransitionSampleIndex = sampleIndex;
            }

            traceState.CurrentClassification = currentClassification;
            m_SoftwareConsumerTrace[company] = traceState;
            return traceState;
        }

        private static bool HasResolvedPathSignal(SoftwareAcquisitionState acquisitionState, SoftwareConsumerTraceState traceState)
        {
            return (acquisitionState.PathComponentPresent && !acquisitionState.PathPending) ||
                   traceState.HasLastPathDestination;
        }

        private static string ClassifySoftwareConsumerState(SoftwareNeedState needState, SoftwareAcquisitionState acquisitionState, SoftwareConsumerTraceState traceState)
        {
            if (!needState.Selected)
            {
                if (!string.IsNullOrEmpty(traceState.CurrentClassification) &&
                    !string.Equals(traceState.CurrentClassification, kTraceNeedNotSelected, StringComparison.Ordinal) &&
                    !string.Equals(traceState.CurrentClassification, kTraceNeedCleared, StringComparison.Ordinal))
                {
                    return kTraceNeedCleared;
                }

                return kTraceNeedNotSelected;
            }

            if (acquisitionState.CurrentTradingCount > 0)
            {
                return kTraceSelectedCurrentTradingPresent;
            }

            if (acquisitionState.TripNeededCount > 0)
            {
                return kTraceSelectedTripPresent;
            }

            if (!acquisitionState.ResourceBuyerPresent)
            {
                if (acquisitionState.VirtualGood && HasResolvedPathSignal(acquisitionState, traceState))
                {
                    return kTraceSelectedResolvedVirtualNoTrackingExpected;
                }

                if (HasResolvedPathSignal(acquisitionState, traceState))
                {
                    return kTraceSelectedResolvedNoTrackingUnexpected;
                }

                return kTraceSelectedNoResourceBuyer;
            }

            if (!acquisitionState.PathComponentPresent)
            {
                return kTraceSelectedResourceBuyerNoPath;
            }

            if (acquisitionState.PathPending)
            {
                return kTraceSelectedPathPending;
            }

            return kTraceSelectedResolvedNoTrackingUnexpected;
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

        private bool TryGetLastTradePartner(Entity company, out Entity lastTradePartner)
        {
            lastTradePartner = Entity.Null;
            if (!EntityManager.HasComponent<BuyingCompany>(company))
            {
                return false;
            }

            lastTradePartner = EntityManager.GetComponentData<BuyingCompany>(company).m_LastTradePartner;
            return lastTradePartner != Entity.Null;
        }

        private bool TryGetOutsideConnectionType(Entity entity, out OutsideConnectionTransferType outsideConnectionType)
        {
            outsideConnectionType = default;
            if (!EntityManager.HasComponent<Game.Objects.OutsideConnection>(entity) || !EntityManager.HasComponent<PrefabRef>(entity))
            {
                return false;
            }

            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
            if (!EntityManager.HasComponent<OutsideConnectionData>(prefabRef.m_Prefab))
            {
                return false;
            }

            outsideConnectionType = EntityManager.GetComponentData<OutsideConnectionData>(prefabRef.m_Prefab).m_Type;
            return true;
        }

        private string GetSellerKindLabel(Entity entity)
        {
            if (EntityManager.HasComponent<Game.Objects.OutsideConnection>(entity))
            {
                return "outside_connection";
            }

            if (EntityManager.HasComponent<Game.Companies.StorageCompany>(entity))
            {
                return "storage_company";
            }

            if (EntityManager.HasComponent<ServiceAvailable>(entity))
            {
                return "service_company";
            }

            return "other";
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
                   snapshot.SoftwareConsumerSelectedNoResourceBuyer > 0 ||
                   snapshot.SoftwareConsumerSelectedRequestNoPath > 0 ||
                   snapshot.SoftwareConsumerResolvedNoTrackingUnexpected > 0;
        }

        private static DiagnosticsSettingsState CaptureSettingsState()
        {
            if (Mod.Settings == null)
            {
                return default;
            }

            return new DiagnosticsSettingsState(
                Mod.Settings.EnableTradePatch,
                Mod.Settings.EnablePhantomVacancyFix,
                Mod.Settings.EnableDemandDiagnostics,
                GetDiagnosticsSamplesPerDay(),
                Mod.Settings.CaptureStableEvidence,
                Mod.Settings.VerboseLogging);
        }

        private static string FormatSettingsSnapshot(DiagnosticsSettingsState settingsState)
        {
            return $"EnableTradePatch:{settingsState.EnableTradePatch}," +
                   $"EnablePhantomVacancyFix:{settingsState.EnablePhantomVacancyFix}," +
                   $"EnableDemandDiagnostics:{settingsState.EnableDemandDiagnostics}," +
                   $"DiagnosticsSamplesPerDay:{settingsState.DiagnosticsSamplesPerDay}," +
                   $"CaptureStableEvidence:{settingsState.CaptureStableEvidence}," +
                   $"VerboseLogging:{settingsState.VerboseLogging}";
        }

        private static string FormatPatchState()
        {
#if DEBUG
            return "debug-build";
#else
            return "unknown";
#endif
        }

        private static string FormatDiagnosticCounters(DiagnosticSnapshot snapshot)
        {
            return
                $"officeDemand(building={snapshot.OfficeBuildingDemand}, company={snapshot.OfficeCompanyDemand}, emptyBuildings={snapshot.EmptyBuildingsFactor}, buildingDemand={snapshot.BuildingDemandFactor}); " +
                $"freeOfficeProperties(total={snapshot.FreeOfficeProperties}, software={snapshot.FreeSoftwareOfficeProperties}, inOccupiedBuildings={snapshot.FreeOfficePropertiesInOccupiedBuildings}, softwareInOccupiedBuildings={snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings}); " +
                $"onMarketOfficeProperties(total={snapshot.OnMarketOfficeProperties}, activelyVacant={snapshot.ActivelyVacantOfficeProperties}, occupied={snapshot.OccupiedOnMarketOfficeProperties}, staleRenterOnly={snapshot.StaleRenterOnMarketOfficeProperties}); " +
                $"phantomVacancy(signatureOccupiedOnMarketOffice={snapshot.SignatureOccupiedOnMarketOffice}, signatureOccupiedOnMarketIndustrial={snapshot.SignatureOccupiedOnMarketIndustrial}, signatureOccupiedToBeOnMarket={snapshot.SignatureOccupiedToBeOnMarket}, nonSignatureOccupiedOnMarketOffice={snapshot.NonSignatureOccupiedOnMarketOffice}, nonSignatureOccupiedOnMarketIndustrial={snapshot.NonSignatureOccupiedOnMarketIndustrial}, guardCorrections={snapshot.GuardCorrections}); " +
                $"software(resourceProduction={snapshot.SoftwareProduction}, resourceDemand={snapshot.SoftwareDemand}, companies={snapshot.SoftwareProductionCompanies}, propertyless={snapshot.SoftwarePropertylessCompanies}); " +
                $"electronics(resourceProduction={snapshot.ElectronicsProduction}, resourceDemand={snapshot.ElectronicsDemand}, companies={snapshot.ElectronicsProductionCompanies}, propertyless={snapshot.ElectronicsPropertylessCompanies}); " +
                $"softwareProducerOffices(total={snapshot.SoftwareProducerOfficeCompanies}, propertyless={snapshot.SoftwareProducerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareProducerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareProducerOfficeLackResourcesZero}); " +
                $"softwareConsumerOffices(total={snapshot.SoftwareConsumerOfficeCompanies}, propertyless={snapshot.SoftwareConsumerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareConsumerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareConsumerOfficeLackResourcesZero}, softwareInputZero={snapshot.SoftwareConsumerOfficeSoftwareInputZero}); " +
                $"softwareConsumerBuyerState(needSelected={snapshot.SoftwareConsumerNeedSelected}, resourceBuyerPresent={snapshot.SoftwareConsumerResourceBuyerPresent}, trackingExpectedSelected={snapshot.SoftwareConsumerTrackingExpectedSelected}, selectedNoResourceBuyer={snapshot.SoftwareConsumerSelectedNoResourceBuyer}, selectedRequestNoPath={snapshot.SoftwareConsumerSelectedRequestNoPath}, pathPending={snapshot.SoftwareConsumerPathPending}, resolvedVirtualNoTrackingExpected={snapshot.SoftwareConsumerResolvedVirtualNoTrackingExpected}, resolvedNoTrackingUnexpected={snapshot.SoftwareConsumerResolvedNoTrackingUnexpected}, tripPresent={snapshot.SoftwareConsumerTripPresent}, currentTradingPresent={snapshot.SoftwareConsumerCurrentTradingPresent})";
        }

        private static bool TryGetObservationTrigger(
            DiagnosticSnapshot snapshot,
            DiagnosticsSettingsState settingsState,
            out string trigger)
        {
            if (HasSuspiciousSignals(snapshot))
            {
                trigger = "suspicious_state";
                return true;
            }

            if (settingsState.CaptureStableEvidence)
            {
                trigger = "capture_stable_evidence";
                return true;
            }

            if (settingsState.VerboseLogging)
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
            m_RunObservationCount = 0;
            m_LastProcessedSampleIndex = int.MinValue;
            m_LastObservedSampleIndex = int.MinValue;
            m_DisplayedClockDay = int.MinValue;
            m_LastComputedSampleSlot = int.MinValue;
            m_LastDiagnosticsEnabled = false;
            m_LastSettingsState = default;
            m_LastSettingsSnapshot = string.Empty;
            m_LastPatchState = string.Empty;
            m_SoftwareConsumerTrace.Clear();
        }

        private static string CreateSessionId()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'");
        }

        private void BeginNewRun(DiagnosticsSettingsState settingsState, string patchState)
        {
            m_RunSequence++;
            m_RunStartDay = int.MinValue;
            m_RunStartSampleIndex = int.MinValue;
            m_RunObservationCount = 0;
            m_LastObservedSampleIndex = int.MinValue;
            m_DisplayedClockDay = int.MinValue;
            m_LastComputedSampleSlot = int.MinValue;
            m_LastSettingsState = settingsState;
            m_LastSettingsSnapshot = FormatSettingsSnapshot(settingsState);
            m_LastPatchState = patchState;
            m_SoftwareConsumerTrace.Clear();
        }

        private int GetSkippedSampleSlots(int sampleIndex)
        {
            if (m_LastObservedSampleIndex == int.MinValue)
            {
                return 0;
            }

            return Math.Max(0, sampleIndex - m_LastObservedSampleIndex - 1);
        }

        private SampleWindowContext GetSampleWindowContext(TimeSettingsData timeSettings, TimeData timeData, int samplesPerDay)
        {
            int frameDay = TimeSystem.GetDay(m_SimulationSystem.frameIndex, timeData);
            int sampleSlot = GetRuntimeTimeSystemSampleSlot(timeSettings, timeData, samplesPerDay);
            int day = GetDisplayedClockDay(frameDay, sampleSlot, samplesPerDay);
            return new SampleWindowContext(
                day,
                GetSampleIndex(day, sampleSlot, samplesPerDay),
                sampleSlot,
                MachineParsedLogContract.RuntimeTimeSystemClockSource);
        }

        /// <summary>
        /// Derives the displayed-clock day from slot transitions so that day
        /// and slot are always consistent with the same time source.
        /// <para>
        /// <c>GetDay()</c> (frame-tick-based) and <c>GetTimeOfDay()</c>
        /// (runtime-time-system-based) are computed independently and can
        /// disagree at day boundaries.  Time-scaling mods such as
        /// RealisticTrips widen this disagreement window.  Rather than
        /// relying on <c>GetDay()</c> for the composite sample index, we
        /// detect displayed-clock midnight from the slot counter wrapping
        /// backward while using <c>GetDay()</c> only for initialisation and
        /// large-gap recovery.
        /// </para>
        /// </summary>
        private int GetDisplayedClockDay(int frameDay, int sampleSlot, int samplesPerDay)
        {
            if (m_LastComputedSampleSlot == int.MinValue)
            {
                m_DisplayedClockDay = frameDay;
                m_LastComputedSampleSlot = sampleSlot;
                return frameDay;
            }

            int previousSlot = m_LastComputedSampleSlot;
            m_LastComputedSampleSlot = sampleSlot;

            // Large frame-day gap (long pause, save load, etc.): re-sync.
            if (frameDay > m_DisplayedClockDay + 1)
            {
                m_DisplayedClockDay = frameDay;
                return frameDay;
            }

            // With a single sample per day the slot is always 0, so wrap
            // detection is impossible.  Boundary desync cannot produce a
            // multi-slot jump either (index == day), so frameDay is safe.
            if (samplesPerDay <= 1)
            {
                m_DisplayedClockDay = frameDay;
                return frameDay;
            }

            // Slot decreased: the displayed clock crossed midnight.
            if (sampleSlot < previousSlot)
            {
                m_DisplayedClockDay++;
            }

            return m_DisplayedClockDay;
        }

        private int GetRuntimeTimeSystemSampleSlot(TimeSettingsData timeSettings, TimeData timeData, int samplesPerDay)
        {
            float normalizedTimeOfDay = m_TimeSystem.GetTimeOfDay(timeSettings, timeData, m_SimulationSystem.frameIndex);
            int ticksIntoDay = (int)Math.Floor(normalizedTimeOfDay * TimeSystem.kTicksPerDay);
            return GetSampleSlotFromTicksIntoDay(ticksIntoDay, samplesPerDay);
        }

        private static int GetSampleSlotFromTicksIntoDay(int ticksIntoDay, int samplesPerDay)
        {
            int sampleSlot = (int)((long)ticksIntoDay * samplesPerDay / TimeSystem.kTicksPerDay);
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
