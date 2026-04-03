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
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        private const uint kVanillaBuyerUpdateInterval = 256u;
        private const int kVanillaBuyerUpdateGroupCount = 16;
        private const int kResourceLowStockAmount = 4000;
        private const int kResourceMinimumRequestAmount = 2000;
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
                bool enablePhantomVacancyFix,
                bool enableOutsideConnectionVirtualSellerFix,
                bool enableVirtualOfficeResourceBuyerFix,
                bool enableOfficeDemandDirectPatch,
                bool enableDemandDiagnostics,
                int diagnosticsSamplesPerDay,
                bool captureStableEvidence,
                bool verboseLogging)
            {
                EnablePhantomVacancyFix = enablePhantomVacancyFix;
                EnableOutsideConnectionVirtualSellerFix = enableOutsideConnectionVirtualSellerFix;
                EnableVirtualOfficeResourceBuyerFix = enableVirtualOfficeResourceBuyerFix;
                EnableOfficeDemandDirectPatch = enableOfficeDemandDirectPatch;
                EnableDemandDiagnostics = enableDemandDiagnostics;
                DiagnosticsSamplesPerDay = diagnosticsSamplesPerDay;
                CaptureStableEvidence = captureStableEvidence;
                VerboseLogging = verboseLogging;
            }

            public bool EnablePhantomVacancyFix { get; }
            public bool EnableOutsideConnectionVirtualSellerFix { get; }
            public bool EnableVirtualOfficeResourceBuyerFix { get; }
            public bool EnableOfficeDemandDirectPatch { get; }
            public bool EnableDemandDiagnostics { get; }
            public int DiagnosticsSamplesPerDay { get; }
            public bool CaptureStableEvidence { get; }
            public bool VerboseLogging { get; }

            public bool Equals(DiagnosticsSettingsState other)
            {
                return EnablePhantomVacancyFix == other.EnablePhantomVacancyFix &&
                       EnableOutsideConnectionVirtualSellerFix == other.EnableOutsideConnectionVirtualSellerFix &&
                       EnableVirtualOfficeResourceBuyerFix == other.EnableVirtualOfficeResourceBuyerFix &&
                       EnableOfficeDemandDirectPatch == other.EnableOfficeDemandDirectPatch &&
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
                    EnablePhantomVacancyFix,
                    EnableOutsideConnectionVirtualSellerFix,
                    EnableVirtualOfficeResourceBuyerFix,
                    EnableOfficeDemandDirectPatch,
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
            public int LocalDemandFactor;
            public bool LocalDemandFactorKnown;
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
            public int SoftwareConsumerSelectedNoBuyerShortGap;
            public int SoftwareConsumerSelectedNoBuyerPersistent;
            public int SoftwareConsumerSelectedNoBuyerMissedVanillaPass;
            public int SoftwareConsumerSelectedNoBuyerMissedMultipleVanillaPasses;
            public int SoftwareConsumerSelectedNoBuyerMaxMissedVanillaPasses;
            public int SoftwareConsumerSelectedRequestNoPathShortGap;
            public int SoftwareConsumerSelectedRequestNoPathPersistent;
            public int SoftwareConsumerVirtualResolvedThisWindow;
            public int SoftwareConsumerVirtualResolvedAmount;
            public int SoftwareConsumerCorrectiveBuyerPresent;
            public int SoftwareConsumerVanillaBuyerPresent;
            public string TopFactors;
            public string FreeSoftwareOfficePropertyDetails;
            public string OnMarketOfficePropertyDetails;
            public string SoftwareOfficeDetails;
            public string SoftwareTradeLifecycleDetails;
            public string SoftwareVirtualResolutionProbeDetails;
            public string SoftwareBuyerTimingProbeDetails;
            public ObservationDetailCapture DetailCapture;
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
            public bool CurrentTradingExpected;
            public bool PathExpected;
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
            public bool CorrectiveBuyerTagged;
            public string BuyerOrigin;
            public bool BuyerSeenThisWindow;
            public int LastBuyerSeenSampleAge;
            public string NoBuyerReason;
            public int CompanyUpdateFrame;
            public int CurrentVanillaBuyerUpdateFrame;
            public int FramesUntilNextVanillaBuyerPass;
            public int EstimatedMissedVanillaBuyerPasses;
            public int SelectedNoBuyerConsecutiveWindows;
            public int SelectedRequestNoPathConsecutiveWindows;
            public int BelowThresholdConsecutiveWindows;
            public string PathStage;
            public int LastPathSeenSampleAge;
            public bool VirtualResolvedThisWindow;
            public int VirtualResolvedAmount;
            public int LastVirtualResolutionSampleAge;
        }

        private struct SoftwareConsumerTraceState
        {
            public string CurrentClassification;
            public string LastTransitionLabel;
            public string LastTransitionFromLabel;
            public int LastTransitionDay;
            public int LastTransitionSampleIndex;
            public int LastObservedSoftwareStock;
            public bool HasObservedSoftwareStock;
            public int PreviousSoftwareStock;
            public bool HasPreviousSoftwareStock;
            public Entity LastObservedLastTradePartner;
            public bool HasObservedLastTradePartner;
            public Entity PreviousLastTradePartner;
            public bool HasPreviousLastTradePartnerObservation;
            public Entity LastPathDestination;
            public bool HasLastPathDestination;
            public int LastPathDestinationSoftwareStock;
            public bool HasLastPathDestinationSoftwareStock;
            public int LastBuyerSeenSampleIndex;
            public bool HasLastBuyerSeenSampleIndex;
            public int LastPathSeenSampleIndex;
            public bool HasLastPathSeenSampleIndex;
            public int LastVirtualResolutionSampleIndex;
            public bool HasLastVirtualResolutionSampleIndex;
            public uint LastObservedSimulationFrame;
            public bool HasLastObservedSimulationFrame;
            public int SelectedNoBuyerConsecutiveWindows;
            public int SelectedNoBuyerEstimatedMissedVanillaPasses;
            public int SelectedRequestNoPathConsecutiveWindows;
            public int BelowThresholdConsecutiveWindows;
        }

        private struct SoftwareVirtualResolutionProbeState
        {
            public bool Eligible;
            public int CurrentSoftwareStock;
            public int PreviousSoftwareStock;
            public bool HasPreviousSoftwareStock;
            public Entity CurrentLastTradePartner;
            public bool HasCurrentLastTradePartnerObservation;
            public Entity PreviousLastTradePartner;
            public bool HasPreviousLastTradePartnerObservation;
            public bool StockIncreasedSincePreviousSample;
            public bool LastTradePartnerChanged;
            public bool PreviousPathSellerSeen;
            public Entity PreviousPathSeller;
            public bool CurrentTradePartnerMatchesPreviousPathSeller;
            public bool NeedClearedAfterSelected;
            public bool BuyerSeenRecently;
            public bool PathSeenRecently;
            public bool EvidenceResolvedVirtual;
        }

        private struct SoftwareConsumerDiagnosticState
        {
            public SoftwareNeedState Need;
            public SoftwareTradeCostState TradeCost;
            public SoftwareAcquisitionState Acquisition;
            public SoftwareConsumerTraceState Trace;
            public SoftwareVirtualResolutionProbeState Probe;
        }

        private struct FreeOfficePropertySummary
        {
            public int Total;
            public int SoftwareCapable;
            public int InOccupiedBuildings;
            public int SoftwareInOccupiedBuildings;
        }

        private struct OnMarketPropertySummary
        {
            public int OnMarketOffice;
            public int ActivelyVacantOffice;
            public int OccupiedOnMarketOffice;
            public int StaleRenterOnMarketOffice;
            public int SignatureOccupiedOnMarketOffice;
            public int SignatureOccupiedOnMarketIndustrial;
            public int NonSignatureOccupiedOnMarketOffice;
            public int NonSignatureOccupiedOnMarketIndustrial;
        }

        private struct ToBeOnMarketPropertySummary
        {
            public int SignatureOccupiedOnMarket;
        }

        private struct SoftwareOfficePrefabMetadata
        {
            public IndustrialProcessData ProcessData;
            public int StorageLimit;
            public bool IsRelevant;
            public bool IsProducer;
            public bool IsConsumer;
            public bool OutputHasWeight;
        }

        private struct SoftwareOfficeDetailCandidate
        {
            public Entity Company;
            public Entity CompanyPrefab;
            public Entity Property;
            public IndustrialProcessData ProcessData;
            public bool IsProducer;
            public bool IsConsumer;
            public bool SoftwareInputZero;
            public bool HasEfficiency;
            public float Efficiency;
            public float LackResources;
            public SoftwareConsumerDiagnosticState SoftwareConsumerState;
        }

        private readonly struct SoftwareOfficeDetailGroupKey : IEquatable<SoftwareOfficeDetailGroupKey>
        {
            public SoftwareOfficeDetailGroupKey(
                Entity prefab,
                bool isProducer,
                bool isConsumer,
                string classification,
                bool softwareInputZero,
                bool efficiencyZero,
                bool lackResourcesZero)
            {
                Prefab = prefab;
                IsProducer = isProducer;
                IsConsumer = isConsumer;
                Classification = classification ?? string.Empty;
                SoftwareInputZero = softwareInputZero;
                EfficiencyZero = efficiencyZero;
                LackResourcesZero = lackResourcesZero;
            }

            public Entity Prefab { get; }
            public bool IsProducer { get; }
            public bool IsConsumer { get; }
            public string Classification { get; }
            public bool SoftwareInputZero { get; }
            public bool EfficiencyZero { get; }
            public bool LackResourcesZero { get; }

            public bool Equals(SoftwareOfficeDetailGroupKey other)
            {
                return Prefab.Equals(other.Prefab) &&
                       IsProducer == other.IsProducer &&
                       IsConsumer == other.IsConsumer &&
                       string.Equals(Classification, other.Classification, StringComparison.Ordinal) &&
                       SoftwareInputZero == other.SoftwareInputZero &&
                       EfficiencyZero == other.EfficiencyZero &&
                       LackResourcesZero == other.LackResourcesZero;
            }

            public override bool Equals(object obj)
            {
                return obj is SoftwareOfficeDetailGroupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    Prefab,
                    IsProducer,
                    IsConsumer,
                    Classification,
                    SoftwareInputZero,
                    EfficiencyZero,
                    LackResourcesZero);
            }
        }

        private struct SoftwareOfficeDetailGroup
        {
            public SoftwareOfficeDetailGroupKey Key;
            public SoftwareOfficeDetailCandidate Representative;
            public int Count;
            public int Priority;
        }

        private struct SoftwareTradeLifecycleDetailCandidate
        {
            public Entity Company;
            public Entity CompanyPrefab;
            public Entity Property;
            public IndustrialProcessData ProcessData;
            public bool IsProducer;
            public bool HasEfficiency;
            public float Efficiency;
            public float LackResources;
            public SoftwareConsumerDiagnosticState SoftwareConsumerState;
        }

        private struct SoftwareVirtualResolutionProbeDetailCandidate
        {
            public Entity Company;
            public Entity CompanyPrefab;
            public Entity Property;
            public SoftwareConsumerDiagnosticState SoftwareConsumerState;
        }

        private struct SoftwareBuyerTimingProbeDetailCandidate
        {
            public Entity Company;
            public Entity CompanyPrefab;
            public Entity Property;
            public IndustrialProcessData ProcessData;
            public bool HasEfficiency;
            public float Efficiency;
            public float LackResources;
            public int Priority;
            public SoftwareConsumerDiagnosticState SoftwareConsumerState;
        }

        private sealed class ObservationDetailCapture
        {
            public readonly List<SoftwareOfficeDetailGroup> SoftwareOfficeDetailGroups =
                new List<SoftwareOfficeDetailGroup>(kMaxDetailEntries);
            public readonly Dictionary<SoftwareOfficeDetailGroupKey, int> SoftwareOfficeDetailGroupIndices =
                new Dictionary<SoftwareOfficeDetailGroupKey, int>(kMaxDetailEntries);
            public readonly List<SoftwareTradeLifecycleDetailCandidate> SoftwareTradeLifecycleDetailCandidates =
                new List<SoftwareTradeLifecycleDetailCandidate>(kMaxDetailEntries);
            public readonly List<SoftwareVirtualResolutionProbeDetailCandidate> SoftwareVirtualResolutionProbeDetailCandidates =
                new List<SoftwareVirtualResolutionProbeDetailCandidate>(kMaxDetailEntries);
            public readonly List<SoftwareBuyerTimingProbeDetailCandidate> SoftwareBuyerTimingProbeDetailCandidates =
                new List<SoftwareBuyerTimingProbeDetailCandidate>(kMaxDetailEntries);
        }

        [BurstCompile]
        private struct CountFreeOfficePropertySummaryJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public ComponentTypeHandle<PrefabRef> PrefabType;

            [ReadOnly]
            public ComponentLookup<BuildingPropertyData> BuildingPropertyDatas;

            [ReadOnly]
            public ComponentLookup<Attached> Attacheds;

            [ReadOnly]
            public BufferLookup<Renter> Renters;

            [ReadOnly]
            public ComponentLookup<CompanyData> CompanyDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> PropertyRenters;

            [NativeDisableParallelForRestriction]
            public NativeArray<FreeOfficePropertySummary> ChunkSummaries;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                NativeArray<PrefabRef> prefabs = chunk.GetNativeArray(ref PrefabType);
                FreeOfficePropertySummary summary = default;

                for (int i = 0; i < chunk.Count; i++)
                {
                    summary.Total++;
                    bool softwareCapable = false;
                    Entity prefab = prefabs[i].m_Prefab;
                    if (BuildingPropertyDatas.HasComponent(prefab))
                    {
                        softwareCapable = (BuildingPropertyDatas[prefab].m_AllowedManufactured & Resource.Software) != Resource.NoResource;
                    }

                    if (softwareCapable)
                    {
                        summary.SoftwareCapable++;
                    }

                    Entity property = entities[i];
                    if (!Attacheds.HasComponent(property) || !HasActiveCompanyRenter(Attacheds[property].m_Parent))
                    {
                        continue;
                    }

                    summary.InOccupiedBuildings++;
                    if (softwareCapable)
                    {
                        summary.SoftwareInOccupiedBuildings++;
                    }
                }

                ChunkSummaries[unfilteredChunkIndex] = summary;
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

        [BurstCompile]
        private struct CountOnMarketPropertySummaryJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public ComponentLookup<OfficeProperty> OfficeProperties;

            [ReadOnly]
            public ComponentLookup<IndustrialProperty> IndustrialProperties;

            [ReadOnly]
            public ComponentLookup<Signature> Signatures;

            [ReadOnly]
            public BufferLookup<Renter> Renters;

            [ReadOnly]
            public ComponentLookup<CompanyData> CompanyDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> PropertyRenters;

            [NativeDisableParallelForRestriction]
            public NativeArray<OnMarketPropertySummary> ChunkSummaries;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                OnMarketPropertySummary summary = default;

                for (int i = 0; i < chunk.Count; i++)
                {
                    Entity property = entities[i];
                    bool isOfficeProperty = OfficeProperties.HasComponent(property);
                    bool isIndustrialProperty = IndustrialProperties.HasComponent(property);
                    if (!isOfficeProperty && !isIndustrialProperty)
                    {
                        continue;
                    }

                    bool hasActiveCompanyRenter = HasActiveCompanyRenter(property);
                    bool hasCompanyRenter = hasActiveCompanyRenter || HasCompanyRenter(property);
                    bool isSignature = Signatures.HasComponent(property);
                    if (isOfficeProperty)
                    {
                        summary.OnMarketOffice++;
                        if (hasActiveCompanyRenter)
                        {
                            summary.OccupiedOnMarketOffice++;
                            if (isSignature)
                            {
                                summary.SignatureOccupiedOnMarketOffice++;
                            }
                            else
                            {
                                summary.NonSignatureOccupiedOnMarketOffice++;
                            }
                        }
                        else
                        {
                            summary.ActivelyVacantOffice++;
                            if (hasCompanyRenter)
                            {
                                summary.StaleRenterOnMarketOffice++;
                            }
                        }
                    }

                    if (!isIndustrialProperty || !hasActiveCompanyRenter)
                    {
                        continue;
                    }

                    if (isSignature)
                    {
                        summary.SignatureOccupiedOnMarketIndustrial++;
                    }
                    else
                    {
                        summary.NonSignatureOccupiedOnMarketIndustrial++;
                    }
                }

                ChunkSummaries[unfilteredChunkIndex] = summary;
            }

            private bool HasCompanyRenter(Entity property)
            {
                if (!Renters.HasBuffer(property))
                {
                    return false;
                }

                DynamicBuffer<Renter> renters = Renters[property];
                for (int i = 0; i < renters.Length; i++)
                {
                    if (CompanyDatas.HasComponent(renters[i].m_Renter))
                    {
                        return true;
                    }
                }

                return false;
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

        [BurstCompile]
        private struct CountToBeOnMarketPropertySummaryJob : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle EntityType;

            [ReadOnly]
            public BufferLookup<Renter> Renters;

            [ReadOnly]
            public ComponentLookup<CompanyData> CompanyDatas;

            [ReadOnly]
            public ComponentLookup<PropertyRenter> PropertyRenters;

            [NativeDisableParallelForRestriction]
            public NativeArray<ToBeOnMarketPropertySummary> ChunkSummaries;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                NativeArray<Entity> entities = chunk.GetNativeArray(EntityType);
                ToBeOnMarketPropertySummary summary = default;

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (HasActiveCompanyRenter(entities[i]))
                    {
                        summary.SignatureOccupiedOnMarket++;
                    }
                }

                ChunkSummaries[unfilteredChunkIndex] = summary;
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
        private readonly Dictionary<Entity, SoftwareOfficePrefabMetadata> m_SoftwareOfficePrefabCache = new Dictionary<Entity, SoftwareOfficePrefabMetadata>();
        private readonly Dictionary<Resource, float> m_ResourceWeightCache = new Dictionary<Resource, float>();

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
            m_OnMarketPropertyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PropertyOnMarket>(),
                    ComponentType.ReadOnly<PrefabRef>()
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<OfficeProperty>(),
                    ComponentType.ReadOnly<IndustrialProperty>()
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
            m_ToBeOnMarketPropertyQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PropertyToBeOnMarket>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Signature>()
                },
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<OfficeProperty>(),
                    ComponentType.ReadOnly<IndustrialProperty>()
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
                ResetVirtualOfficeBuyerProbeState();
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

            DiagnosticSnapshot currentSnapshot = CaptureSummarySnapshot(
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
            PopulateObservationDetails(ref snapshot, settingsState.VerboseLogging);

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

            if (!string.IsNullOrEmpty(snapshot.SoftwareVirtualResolutionProbeDetails))
            {
                Mod.log.Info(
                    MachineParsedLogContract.FormatDetail(
                        m_SessionId,
                        m_RunSequence,
                        snapshot.Day,
                        snapshot.SampleIndex,
                        MachineParsedLogContract.SoftwareVirtualResolutionProbeDetailType,
                        snapshot.SoftwareVirtualResolutionProbeDetails));
            }

            if (!string.IsNullOrEmpty(snapshot.SoftwareBuyerTimingProbeDetails))
            {
                Mod.log.Info(
                    MachineParsedLogContract.FormatDetail(
                        m_SessionId,
                        m_RunSequence,
                        snapshot.Day,
                        snapshot.SampleIndex,
                        MachineParsedLogContract.SoftwareBuyerTimingProbeDetailType,
                        snapshot.SoftwareBuyerTimingProbeDetails));
            }

            EmitVirtualOfficeBuyerProbeSummary(snapshot);

            m_RunObservationCount = sampleCount;
            m_LastObservedSampleIndex = snapshot.SampleIndex;
        }

        private DiagnosticSnapshot CaptureSummarySnapshot(
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
            bool localDemandFactorKnown = TryGetDemandFactorValue(officeFactors, "LocalDemand", out int localDemandFactor);
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
                LocalDemandFactor = localDemandFactor,
                LocalDemandFactorKnown = localDemandFactorKnown,
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

        private void PopulateObservationDetails(ref DiagnosticSnapshot snapshot, bool verboseLogging)
        {
            snapshot.FreeSoftwareOfficePropertyDetails = CollectFreeSoftwareOfficePropertyDetails();
            snapshot.OnMarketOfficePropertyDetails = CollectOnMarketOfficePropertyDetails();
            snapshot.SoftwareOfficeDetails = RenderSoftwareOfficeDetails(snapshot.DetailCapture);

            if (!verboseLogging)
            {
                snapshot.SoftwareTradeLifecycleDetails = string.Empty;
                snapshot.SoftwareVirtualResolutionProbeDetails = string.Empty;
                snapshot.SoftwareBuyerTimingProbeDetails = string.Empty;
                return;
            }

            snapshot.SoftwareTradeLifecycleDetails = RenderSoftwareTradeLifecycleDetails(snapshot.DetailCapture);
            snapshot.SoftwareVirtualResolutionProbeDetails = RenderSoftwareVirtualResolutionProbeDetails(snapshot.DetailCapture);
            snapshot.SoftwareBuyerTimingProbeDetails = RenderSoftwareBuyerTimingProbeDetails(snapshot.DetailCapture);
        }

        private void CountFreeOfficeProperties(ref DiagnosticSnapshot snapshot)
        {
            if (m_FreeOfficePropertyQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            int chunkCount = m_FreeOfficePropertyQuery.CalculateChunkCount();
            using NativeArray<FreeOfficePropertySummary> chunkSummaries = new NativeArray<FreeOfficePropertySummary>(chunkCount, Allocator.TempJob);
            JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(
                new CountFreeOfficePropertySummaryJob
                {
                    EntityType = GetEntityTypeHandle(),
                    PrefabType = GetComponentTypeHandle<PrefabRef>(isReadOnly: true),
                    BuildingPropertyDatas = GetComponentLookup<BuildingPropertyData>(isReadOnly: true),
                    Attacheds = GetComponentLookup<Attached>(isReadOnly: true),
                    Renters = GetBufferLookup<Renter>(isReadOnly: true),
                    CompanyDatas = GetComponentLookup<CompanyData>(isReadOnly: true),
                    PropertyRenters = GetComponentLookup<PropertyRenter>(isReadOnly: true),
                    ChunkSummaries = chunkSummaries
                },
                m_FreeOfficePropertyQuery,
                Dependency);

            jobHandle.Complete();
            Dependency = default;

            for (int i = 0; i < chunkSummaries.Length; i++)
            {
                FreeOfficePropertySummary summary = chunkSummaries[i];
                snapshot.FreeOfficeProperties += summary.Total;
                snapshot.FreeSoftwareOfficeProperties += summary.SoftwareCapable;
                snapshot.FreeOfficePropertiesInOccupiedBuildings += summary.InOccupiedBuildings;
                snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings += summary.SoftwareInOccupiedBuildings;
            }
        }

        private void CountOnMarketProperties(ref DiagnosticSnapshot snapshot)
        {
            if (m_OnMarketPropertyQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            int chunkCount = m_OnMarketPropertyQuery.CalculateChunkCount();
            using NativeArray<OnMarketPropertySummary> chunkSummaries = new NativeArray<OnMarketPropertySummary>(chunkCount, Allocator.TempJob);
            JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(
                new CountOnMarketPropertySummaryJob
                {
                    EntityType = GetEntityTypeHandle(),
                    OfficeProperties = GetComponentLookup<OfficeProperty>(isReadOnly: true),
                    IndustrialProperties = GetComponentLookup<IndustrialProperty>(isReadOnly: true),
                    Signatures = GetComponentLookup<Signature>(isReadOnly: true),
                    Renters = GetBufferLookup<Renter>(isReadOnly: true),
                    CompanyDatas = GetComponentLookup<CompanyData>(isReadOnly: true),
                    PropertyRenters = GetComponentLookup<PropertyRenter>(isReadOnly: true),
                    ChunkSummaries = chunkSummaries
                },
                m_OnMarketPropertyQuery,
                Dependency);

            jobHandle.Complete();
            Dependency = default;

            for (int i = 0; i < chunkSummaries.Length; i++)
            {
                OnMarketPropertySummary summary = chunkSummaries[i];
                snapshot.OnMarketOfficeProperties += summary.OnMarketOffice;
                snapshot.ActivelyVacantOfficeProperties += summary.ActivelyVacantOffice;
                snapshot.OccupiedOnMarketOfficeProperties += summary.OccupiedOnMarketOffice;
                snapshot.StaleRenterOnMarketOfficeProperties += summary.StaleRenterOnMarketOffice;
                snapshot.SignatureOccupiedOnMarketOffice += summary.SignatureOccupiedOnMarketOffice;
                snapshot.SignatureOccupiedOnMarketIndustrial += summary.SignatureOccupiedOnMarketIndustrial;
                snapshot.NonSignatureOccupiedOnMarketOffice += summary.NonSignatureOccupiedOnMarketOffice;
                snapshot.NonSignatureOccupiedOnMarketIndustrial += summary.NonSignatureOccupiedOnMarketIndustrial;
            }
        }

        private void CountToBeOnMarketProperties(ref DiagnosticSnapshot snapshot)
        {
            if (m_ToBeOnMarketPropertyQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            int chunkCount = m_ToBeOnMarketPropertyQuery.CalculateChunkCount();
            using NativeArray<ToBeOnMarketPropertySummary> chunkSummaries = new NativeArray<ToBeOnMarketPropertySummary>(chunkCount, Allocator.TempJob);
            JobHandle jobHandle = JobChunkExtensions.ScheduleParallel(
                new CountToBeOnMarketPropertySummaryJob
                {
                    EntityType = GetEntityTypeHandle(),
                    Renters = GetBufferLookup<Renter>(isReadOnly: true),
                    CompanyDatas = GetComponentLookup<CompanyData>(isReadOnly: true),
                    PropertyRenters = GetComponentLookup<PropertyRenter>(isReadOnly: true),
                    ChunkSummaries = chunkSummaries
                },
                m_ToBeOnMarketPropertyQuery,
                Dependency);

            jobHandle.Complete();
            Dependency = default;

            for (int i = 0; i < chunkSummaries.Length; i++)
            {
                snapshot.SignatureOccupiedToBeOnMarket += chunkSummaries[i].SignatureOccupiedOnMarket;
            }
        }

        private void CountSoftwareOffices(ref DiagnosticSnapshot snapshot, bool verboseLogging)
        {
            ObservationDetailCapture detailCapture = snapshot.DetailCapture;
            using NativeArray<Entity> companies = m_OfficeCompanyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < companies.Length; i++)
            {
                Entity company = companies[i];
                PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(company);
                if (!TryGetSoftwareOfficePrefabMetadata(prefabRef.m_Prefab, out SoftwareOfficePrefabMetadata prefabMetadata))
                {
                    continue;
                }

                IndustrialProcessData processData = prefabMetadata.ProcessData;
                bool isProducer = prefabMetadata.IsProducer;
                bool isConsumer = prefabMetadata.IsConsumer;
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
                    softwareConsumerState = GetSoftwareConsumerDiagnosticState(company, prefabMetadata, snapshot.Day, snapshot.SampleIndex);
                    softwareInputStock = softwareConsumerState.Need.Stock;
                    if (softwareConsumerState.Need.Selected)
                    {
                        snapshot.SoftwareConsumerNeedSelected++;
                    }

                    if (softwareConsumerState.Acquisition.ResourceBuyerPresent)
                    {
                        snapshot.SoftwareConsumerResourceBuyerPresent++;
                        if (string.Equals(softwareConsumerState.Acquisition.BuyerOrigin, "corrective", StringComparison.Ordinal))
                        {
                            snapshot.SoftwareConsumerCorrectiveBuyerPresent++;
                        }
                        else if (string.Equals(softwareConsumerState.Acquisition.BuyerOrigin, "vanilla", StringComparison.Ordinal))
                        {
                            snapshot.SoftwareConsumerVanillaBuyerPresent++;
                        }
                    }

                    if (softwareConsumerState.Acquisition.VirtualResolvedThisWindow)
                    {
                        snapshot.SoftwareConsumerVirtualResolvedThisWindow++;
                        snapshot.SoftwareConsumerVirtualResolvedAmount += softwareConsumerState.Acquisition.VirtualResolvedAmount;
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
                        if (softwareConsumerState.Acquisition.SelectedNoBuyerConsecutiveWindows >= 3)
                        {
                            snapshot.SoftwareConsumerSelectedNoBuyerPersistent++;
                        }
                        else
                        {
                            snapshot.SoftwareConsumerSelectedNoBuyerShortGap++;
                        }

                        if (softwareConsumerState.Acquisition.EstimatedMissedVanillaBuyerPasses >= 1)
                        {
                            snapshot.SoftwareConsumerSelectedNoBuyerMissedVanillaPass++;
                        }

                        if (softwareConsumerState.Acquisition.EstimatedMissedVanillaBuyerPasses >= 2)
                        {
                            snapshot.SoftwareConsumerSelectedNoBuyerMissedMultipleVanillaPasses++;
                        }

                        snapshot.SoftwareConsumerSelectedNoBuyerMaxMissedVanillaPasses = Math.Max(
                            snapshot.SoftwareConsumerSelectedNoBuyerMaxMissedVanillaPasses,
                            softwareConsumerState.Acquisition.EstimatedMissedVanillaBuyerPasses);
                    }

                    if (string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal))
                    {
                        snapshot.SoftwareConsumerSelectedRequestNoPath++;
                        if (softwareConsumerState.Acquisition.SelectedRequestNoPathConsecutiveWindows >= 3)
                        {
                            snapshot.SoftwareConsumerSelectedRequestNoPathPersistent++;
                        }
                        else
                        {
                            snapshot.SoftwareConsumerSelectedRequestNoPathShortGap++;
                        }
                    }

                    bool resolvedVirtualExpected = string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedResolvedVirtualNoTrackingExpected, StringComparison.Ordinal) ||
                                                 softwareConsumerState.Acquisition.VirtualResolvedThisWindow;
                    if (resolvedVirtualExpected)
                    {
                        snapshot.SoftwareConsumerResolvedVirtualNoTrackingExpected++;
                    }
                    else if (string.Equals(softwareConsumerState.Trace.CurrentClassification, kTraceSelectedResolvedNoTrackingUnexpected, StringComparison.Ordinal))
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

                if (efficiencyZero ||
                    lackResourcesZero ||
                    softwareInputZero ||
                    (isConsumer && ShouldCaptureConsumerOfficeDetail(softwareConsumerState)))
                {
                    EnsureObservationDetailCapture(ref snapshot, ref detailCapture);
                    AddSoftwareOfficeDetailCandidate(
                        detailCapture,
                        new SoftwareOfficeDetailCandidate
                        {
                            Company = company,
                            CompanyPrefab = prefabRef.m_Prefab,
                            Property = propertyRenter.m_Property,
                            ProcessData = processData,
                            IsProducer = isProducer,
                            IsConsumer = isConsumer,
                            SoftwareInputZero = softwareInputZero,
                            HasEfficiency = hasEfficiency,
                            Efficiency = efficiency,
                            LackResources = lackResources,
                            SoftwareConsumerState = softwareConsumerState
                        });
                }

                if (verboseLogging &&
                    isConsumer &&
                    ShouldCaptureConsumerTradeLifecycle(softwareConsumerState, snapshot.Day, snapshot.SampleIndex))
                {
                    EnsureObservationDetailCapture(ref snapshot, ref detailCapture);
                    TryAddCandidate(
                        detailCapture.SoftwareTradeLifecycleDetailCandidates,
                        new SoftwareTradeLifecycleDetailCandidate
                        {
                            Company = company,
                            CompanyPrefab = prefabRef.m_Prefab,
                            Property = propertyRenter.m_Property,
                            ProcessData = processData,
                            IsProducer = false,
                            HasEfficiency = hasEfficiency,
                            Efficiency = efficiency,
                            LackResources = lackResources,
                            SoftwareConsumerState = softwareConsumerState
                        });
                }

                if (verboseLogging &&
                    isConsumer &&
                    ShouldCaptureVirtualResolutionProbe(softwareConsumerState))
                {
                    EnsureObservationDetailCapture(ref snapshot, ref detailCapture);
                    TryAddCandidate(
                        detailCapture.SoftwareVirtualResolutionProbeDetailCandidates,
                        new SoftwareVirtualResolutionProbeDetailCandidate
                        {
                            Company = company,
                            CompanyPrefab = prefabRef.m_Prefab,
                            Property = propertyRenter.m_Property,
                            SoftwareConsumerState = softwareConsumerState
                        });
                }

                if (verboseLogging &&
                    isConsumer &&
                    ShouldCaptureBuyerTimingProbe(softwareConsumerState))
                {
                    EnsureObservationDetailCapture(ref snapshot, ref detailCapture);
                    TryAddBuyerTimingProbeCandidate(
                        detailCapture.SoftwareBuyerTimingProbeDetailCandidates,
                        new SoftwareBuyerTimingProbeDetailCandidate
                        {
                            Company = company,
                            CompanyPrefab = prefabRef.m_Prefab,
                            Property = propertyRenter.m_Property,
                            ProcessData = processData,
                            HasEfficiency = hasEfficiency,
                            Efficiency = efficiency,
                            LackResources = lackResources,
                            Priority = GetBuyerTimingProbePriority(softwareConsumerState, hasEfficiency, efficiency, lackResources),
                            SoftwareConsumerState = softwareConsumerState
                        });
                }

                if (verboseLogging &&
                    isProducer &&
                    (efficiencyZero || lackResourcesZero))
                {
                    EnsureObservationDetailCapture(ref snapshot, ref detailCapture);
                    TryAddCandidate(
                        detailCapture.SoftwareTradeLifecycleDetailCandidates,
                        new SoftwareTradeLifecycleDetailCandidate
                        {
                            Company = company,
                            CompanyPrefab = prefabRef.m_Prefab,
                            Property = propertyRenter.m_Property,
                            ProcessData = processData,
                            IsProducer = true,
                            HasEfficiency = hasEfficiency,
                            Efficiency = efficiency,
                            LackResources = lackResources
                        });
                }
            }
        }

        private static void EnsureObservationDetailCapture(ref DiagnosticSnapshot snapshot, ref ObservationDetailCapture detailCapture)
        {
            if (detailCapture != null)
            {
                return;
            }

            detailCapture = new ObservationDetailCapture();
            snapshot.DetailCapture = detailCapture;
        }

        private static void TryAddCandidate<T>(List<T> candidates, T candidate)
        {
            if (candidates.Count >= kMaxDetailEntries)
            {
                return;
            }

            candidates.Add(candidate);
        }

        private static void TryAddBuyerTimingProbeCandidate(List<SoftwareBuyerTimingProbeDetailCandidate> candidates, SoftwareBuyerTimingProbeDetailCandidate candidate)
        {
            int insertIndex = candidates.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidate.Priority > candidates[i].Priority)
                {
                    insertIndex = i;
                    break;
                }
            }

            if (insertIndex >= kMaxDetailEntries && candidates.Count >= kMaxDetailEntries)
            {
                return;
            }

            if (insertIndex < candidates.Count)
            {
                candidates.Insert(insertIndex, candidate);
            }
            else if (candidates.Count < kMaxDetailEntries)
            {
                candidates.Add(candidate);
            }

            if (candidates.Count > kMaxDetailEntries)
            {
                candidates.RemoveAt(candidates.Count - 1);
            }
        }

        private static void AddSoftwareOfficeDetailCandidate(ObservationDetailCapture detailCapture, SoftwareOfficeDetailCandidate candidate)
        {
            SoftwareOfficeDetailGroupKey key = BuildSoftwareOfficeDetailGroupKey(candidate);
            if (detailCapture.SoftwareOfficeDetailGroupIndices.TryGetValue(key, out int existingIndex))
            {
                SoftwareOfficeDetailGroup existingGroup = detailCapture.SoftwareOfficeDetailGroups[existingIndex];
                existingGroup.Count++;
                detailCapture.SoftwareOfficeDetailGroups[existingIndex] = existingGroup;
                return;
            }

            detailCapture.SoftwareOfficeDetailGroupIndices.Add(key, detailCapture.SoftwareOfficeDetailGroups.Count);
            detailCapture.SoftwareOfficeDetailGroups.Add(new SoftwareOfficeDetailGroup
            {
                Key = key,
                Representative = candidate,
                Count = 1,
                Priority = GetSoftwareOfficeDetailPriority(candidate)
            });
        }

        private static SoftwareOfficeDetailGroupKey BuildSoftwareOfficeDetailGroupKey(SoftwareOfficeDetailCandidate candidate)
        {
            bool efficiencyZero = candidate.HasEfficiency && candidate.Efficiency <= 0f;
            bool lackResourcesZero = candidate.HasEfficiency && candidate.LackResources <= 0f;
            string classification = candidate.IsConsumer
                ? candidate.SoftwareConsumerState.Trace.CurrentClassification
                : string.Empty;
            return new SoftwareOfficeDetailGroupKey(
                candidate.CompanyPrefab,
                candidate.IsProducer,
                candidate.IsConsumer,
                classification,
                candidate.SoftwareInputZero,
                efficiencyZero,
                lackResourcesZero);
        }

        private static int GetSoftwareOfficeDetailPriority(SoftwareOfficeDetailCandidate candidate)
        {
            int priority = 0;
            if (candidate.HasEfficiency && candidate.Efficiency <= 0f)
            {
                priority += 16;
            }

            if (candidate.HasEfficiency && candidate.LackResources <= 0f)
            {
                priority += 8;
            }

            if (candidate.SoftwareInputZero)
            {
                priority += 4;
            }

            if (candidate.IsConsumer)
            {
                if (string.Equals(candidate.SoftwareConsumerState.Trace.CurrentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal))
                {
                    priority += 3;
                }
                else if (string.Equals(candidate.SoftwareConsumerState.Trace.CurrentClassification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal))
                {
                    priority += 2;
                }
                else if (string.Equals(candidate.SoftwareConsumerState.Trace.CurrentClassification, kTraceSelectedPathPending, StringComparison.Ordinal))
                {
                    priority += 1;
                }
            }

            return priority;
        }

        private string RenderSoftwareOfficeDetails(ObservationDetailCapture detailCapture)
        {
            if (detailCapture == null || detailCapture.SoftwareOfficeDetailGroups.Count == 0)
            {
                return string.Empty;
            }

            List<SoftwareOfficeDetailGroup> groups = new List<SoftwareOfficeDetailGroup>(detailCapture.SoftwareOfficeDetailGroups);
            groups.Sort(static (left, right) =>
            {
                int compare = right.Priority.CompareTo(left.Priority);
                if (compare != 0)
                {
                    return compare;
                }

                compare = right.Count.CompareTo(left.Count);
                if (compare != 0)
                {
                    return compare;
                }

                compare = left.Representative.CompanyPrefab.Index.CompareTo(right.Representative.CompanyPrefab.Index);
                if (compare != 0)
                {
                    return compare;
                }

                return left.Representative.Property.Index.CompareTo(right.Representative.Property.Index);
            });

            int shownGroupCount = groups.Count;
            if (shownGroupCount > kMaxDetailEntries)
            {
                shownGroupCount = Math.Max(1, kMaxDetailEntries - 1);
            }

            StringBuilder details = null;
            int detailCount = 0;
            for (int i = 0; i < shownGroupCount; i++)
            {
                SoftwareOfficeDetailGroup group = groups[i];
                SoftwareOfficeDetailCandidate candidate = group.Representative;
                string detail = DescribeSoftwareOffice(
                    candidate.Company,
                    candidate.CompanyPrefab,
                    candidate.Property,
                    candidate.ProcessData,
                    candidate.IsProducer,
                    candidate.IsConsumer,
                    candidate.SoftwareInputZero,
                    candidate.HasEfficiency,
                    candidate.Efficiency,
                    candidate.LackResources,
                    candidate.SoftwareConsumerState);
                detail += $", similarKindCount={group.Count}, similarKindOmitted={Math.Max(0, group.Count - 1)}";
                AppendDetail(
                    ref details,
                    ref detailCount,
                    detail);
            }

            if (groups.Count > shownGroupCount)
            {
                int omittedKinds = groups.Count - shownGroupCount;
                int omittedCases = 0;
                for (int i = shownGroupCount; i < groups.Count; i++)
                {
                    omittedCases += groups[i].Count;
                }

                AppendDetail(
                    ref details,
                    ref detailCount,
                    $"detailSummary(grouping=prefab+classification+zero_flags, omittedKinds={omittedKinds}, omittedCases={omittedCases})");
            }

            return details == null ? string.Empty : details.ToString();
        }

        private string RenderSoftwareTradeLifecycleDetails(ObservationDetailCapture detailCapture)
        {
            if (detailCapture == null || detailCapture.SoftwareTradeLifecycleDetailCandidates.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder details = null;
            int detailCount = 0;
            for (int i = 0; i < detailCapture.SoftwareTradeLifecycleDetailCandidates.Count; i++)
            {
                SoftwareTradeLifecycleDetailCandidate candidate = detailCapture.SoftwareTradeLifecycleDetailCandidates[i];
                string detail = candidate.IsProducer
                    ? DescribeProducerTradeLifecycle(
                        candidate.Company,
                        candidate.CompanyPrefab,
                        candidate.Property,
                        candidate.ProcessData,
                        candidate.HasEfficiency,
                        candidate.Efficiency,
                        candidate.LackResources)
                    : DescribeConsumerTradeLifecycle(
                        candidate.Company,
                        candidate.CompanyPrefab,
                        candidate.Property,
                        candidate.ProcessData,
                        candidate.HasEfficiency,
                        candidate.Efficiency,
                        candidate.LackResources,
                        candidate.SoftwareConsumerState);
                AppendDetail(ref details, ref detailCount, detail);
            }

            return details == null ? string.Empty : details.ToString();
        }

        private string RenderSoftwareVirtualResolutionProbeDetails(ObservationDetailCapture detailCapture)
        {
            if (detailCapture == null || detailCapture.SoftwareVirtualResolutionProbeDetailCandidates.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder details = null;
            int detailCount = 0;
            for (int i = 0; i < detailCapture.SoftwareVirtualResolutionProbeDetailCandidates.Count; i++)
            {
                SoftwareVirtualResolutionProbeDetailCandidate candidate = detailCapture.SoftwareVirtualResolutionProbeDetailCandidates[i];
                AppendDetail(
                    ref details,
                    ref detailCount,
                    DescribeSoftwareVirtualResolutionProbe(
                        candidate.Company,
                        candidate.CompanyPrefab,
                        candidate.Property,
                        candidate.SoftwareConsumerState));
            }

            return details == null ? string.Empty : details.ToString();
        }

        private string RenderSoftwareBuyerTimingProbeDetails(ObservationDetailCapture detailCapture)
        {
            if (detailCapture == null || detailCapture.SoftwareBuyerTimingProbeDetailCandidates.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder details = null;
            int detailCount = 0;
            for (int i = 0; i < detailCapture.SoftwareBuyerTimingProbeDetailCandidates.Count; i++)
            {
                SoftwareBuyerTimingProbeDetailCandidate candidate = detailCapture.SoftwareBuyerTimingProbeDetailCandidates[i];
                AppendDetail(
                    ref details,
                    ref detailCount,
                    DescribeSoftwareBuyerTimingProbe(
                        candidate.Company,
                        candidate.CompanyPrefab,
                        candidate.Property,
                        candidate.ProcessData,
                        candidate.HasEfficiency,
                        candidate.Efficiency,
                        candidate.LackResources,
                        candidate.SoftwareConsumerState));
            }

            return details == null ? string.Empty : details.ToString();
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

        private static bool ShouldCaptureVirtualResolutionProbe(SoftwareConsumerDiagnosticState state)
        {
            return state.Need.Selected &&
                   state.Acquisition.VirtualGood &&
                   !state.Acquisition.ResourceBuyerPresent &&
                   !state.Acquisition.PathPending &&
                   state.Acquisition.TripNeededCount == 0 &&
                   state.Acquisition.CurrentTradingCount == 0;
        }

        private static bool ShouldCaptureBuyerTimingProbe(SoftwareConsumerDiagnosticState state)
        {
            if (!state.Need.Selected)
            {
                return false;
            }

            return string.Equals(state.Trace.CurrentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal) ||
                   string.Equals(state.Trace.CurrentClassification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal) ||
                   string.Equals(state.Trace.CurrentClassification, kTraceSelectedPathPending, StringComparison.Ordinal) ||
                   string.Equals(state.Trace.CurrentClassification, kTraceSelectedTripPresent, StringComparison.Ordinal) ||
                   string.Equals(state.Trace.CurrentClassification, kTraceSelectedCurrentTradingPresent, StringComparison.Ordinal);
        }

        private static int GetBuyerTimingProbePriority(SoftwareConsumerDiagnosticState state, bool hasEfficiency, float efficiency, float lackResources)
        {
            int priority = string.Equals(state.Trace.CurrentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal)
                ? 1_000_000
                : string.Equals(state.Trace.CurrentClassification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal)
                    ? 900_000
                    : string.Equals(state.Trace.CurrentClassification, kTraceSelectedPathPending, StringComparison.Ordinal)
                        ? 800_000
                        : string.Equals(state.Trace.CurrentClassification, kTraceSelectedTripPresent, StringComparison.Ordinal)
                            ? 700_000
                            : 600_000;

            priority += Math.Min(999, state.Acquisition.EstimatedMissedVanillaBuyerPasses) * 1_000;
            priority += Math.Min(999, state.Acquisition.SelectedNoBuyerConsecutiveWindows) * 100;
            priority += Math.Min(999, state.Acquisition.SelectedRequestNoPathConsecutiveWindows) * 10;

            if (state.Need.Stock == 0)
            {
                priority += 25;
            }

            if (hasEfficiency && efficiency <= 0f)
            {
                priority += 5;
            }

            if (hasEfficiency && lackResources <= 0f)
            {
                priority += 5;
            }

            return priority;
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

        private string DescribeSoftwareVirtualResolutionProbe(
            Entity company,
            Entity companyPrefab,
            Entity property,
            SoftwareConsumerDiagnosticState softwareConsumerState)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("role=consumer");
            builder.Append(", company=").Append(FormatEntity(company));
            builder.Append(", prefab=").Append(GetPrefabLabel(companyPrefab));
            builder.Append(", property=").Append(FormatEntity(property));
            builder.Append(", capture=virtual_resolution_probe");
            builder.Append(", currentClassification=").Append(string.IsNullOrEmpty(softwareConsumerState.Trace.CurrentClassification) ? kTraceNeedNotSelected : softwareConsumerState.Trace.CurrentClassification);
            builder.Append(", virtualGood=").Append(softwareConsumerState.Acquisition.VirtualGood);
            builder.Append(", resourceWeight=").Append(softwareConsumerState.Acquisition.ResourceWeight.ToString("0.###", CultureInfo.InvariantCulture));
            builder.Append(", currentSoftwareStock=").Append(softwareConsumerState.Probe.CurrentSoftwareStock);
            builder.Append(", previousSoftwareStock=");
            builder.Append(softwareConsumerState.Probe.HasPreviousSoftwareStock ? softwareConsumerState.Probe.PreviousSoftwareStock.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", stockIncreasedSincePreviousSample=").Append(softwareConsumerState.Probe.StockIncreasedSincePreviousSample);
            builder.Append(", currentLastTradePartner=");
            AppendObservedEntityValue(builder, softwareConsumerState.Probe.HasCurrentLastTradePartnerObservation, softwareConsumerState.Probe.CurrentLastTradePartner);
            builder.Append(", previousLastTradePartner=");
            AppendObservedEntityValue(builder, softwareConsumerState.Probe.HasPreviousLastTradePartnerObservation, softwareConsumerState.Probe.PreviousLastTradePartner);
            builder.Append(", lastTradePartnerChanged=").Append(softwareConsumerState.Probe.LastTradePartnerChanged);
            builder.Append(", previousPathSellerSeen=").Append(softwareConsumerState.Probe.PreviousPathSellerSeen);
            builder.Append(", previousPathSeller=");
            builder.Append(softwareConsumerState.Probe.PreviousPathSellerSeen && softwareConsumerState.Probe.PreviousPathSeller != Entity.Null ? FormatEntity(softwareConsumerState.Probe.PreviousPathSeller) : "none");
            builder.Append(", currentTradePartnerMatchesPreviousPathSeller=").Append(softwareConsumerState.Probe.CurrentTradePartnerMatchesPreviousPathSeller);
            builder.Append(", needClearedAfterSelected=").Append(softwareConsumerState.Probe.NeedClearedAfterSelected);
            builder.Append(", buyerSeenRecently=").Append(softwareConsumerState.Probe.BuyerSeenRecently);
            builder.Append(", pathSeenRecently=").Append(softwareConsumerState.Probe.PathSeenRecently);
            builder.Append(", evidenceResolvedVirtual=").Append(softwareConsumerState.Probe.EvidenceResolvedVirtual);
            return builder.ToString();
        }

        private string DescribeSoftwareBuyerTimingProbe(
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
            builder.Append(", capture=buyer_timing_probe");
            builder.Append(", currentClassification=").Append(string.IsNullOrEmpty(softwareConsumerState.Trace.CurrentClassification) ? kTraceNeedNotSelected : softwareConsumerState.Trace.CurrentClassification);
            builder.Append(", output=").Append(processData.m_Output.m_Resource);
            builder.Append(", outputStock=").Append(GetCompanyResourceAmount(company, processData.m_Output.m_Resource));
            AppendCompanyResourceState(builder, company, "input1", processData.m_Input1.m_Resource);
            AppendCompanyResourceState(builder, company, "input2", processData.m_Input2.m_Resource);
            AppendSoftwareNeedState(builder, softwareConsumerState.Need);
            AppendSoftwareTradeCostState(builder, softwareConsumerState.TradeCost);
            AppendSoftwareAcquisitionState(builder, softwareConsumerState.Acquisition, softwareConsumerState.Trace.CurrentClassification);
            AppendBuyerFixWindowState(builder, company);
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

        private void AppendBuyerFixWindowState(StringBuilder builder, Entity company)
        {
            builder.Append(", buyerFixWindow(");
            VirtualOfficeResourceBuyerFixSystem buyerFixSystem = World.GetExistingSystemManaged<VirtualOfficeResourceBuyerFixSystem>();
            if (buyerFixSystem == null || !buyerFixSystem.TryGetCompanyProbeWindowSnapshot(company, out VirtualOfficeResourceBuyerFixSystem.CompanyProbeWindowSnapshot snapshot))
            {
                builder.Append("seenThisObservation=False");
                builder.Append(", seenChangedQueryCount=0");
                builder.Append(", seenFullSweepCount=0");
                builder.Append(", lastSeenPass=none");
                builder.Append(", lastSeenFrameAge=n/a");
                builder.Append(", overrideCount=0");
                builder.Append(", lastOverridePass=none");
                builder.Append(", lastOverrideFrameAge=n/a");
                builder.Append(", lastOverrideAmount=n/a");
                builder.Append(", lastOverrideShortfall=n/a");
                builder.Append(", lastOverrideStock=n/a");
                builder.Append(", lastOverrideBuyingLoad=n/a");
                builder.Append(", lastOverrideTripNeededAmount=n/a");
                builder.Append(", lastOverrideEffectiveStock=n/a");
                builder.Append(", lastOverrideThreshold=n/a");
                builder.Append(')');
                return;
            }

            uint currentSimulationFrame = m_SimulationSystem.frameIndex;
            builder.Append("seenThisObservation=True");
            builder.Append(", seenChangedQueryCount=").Append(snapshot.SeenChangedQueryCount);
            builder.Append(", seenFullSweepCount=").Append(snapshot.SeenFullSweepCount);
            builder.Append(", lastSeenPass=").Append(VirtualOfficeResourceBuyerFixSystem.GetPassKindLabel(snapshot.LastSeenViaFullSweep));
            builder.Append(", lastSeenFrameAge=");
            builder.Append(snapshot.LastSeenFrame >= 0 ? Math.Max(0, (int)currentSimulationFrame - snapshot.LastSeenFrame).ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", overrideCount=").Append(snapshot.OverrideCount);
            builder.Append(", lastOverridePass=");
            builder.Append(snapshot.OverrideCount > 0 ? VirtualOfficeResourceBuyerFixSystem.GetPassKindLabel(snapshot.LastOverrideViaFullSweep) : "none");
            builder.Append(", lastOverrideFrameAge=");
            builder.Append(snapshot.LastOverrideFrame >= 0 ? Math.Max(0, (int)currentSimulationFrame - snapshot.LastOverrideFrame).ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideAmount=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideAmount.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideShortfall=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideShortfall.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideStock=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideStock.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideBuyingLoad=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideBuyingLoad.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideTripNeededAmount=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideTripNeededAmount.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideEffectiveStock=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideEffectiveStock.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastOverrideThreshold=");
            builder.Append(snapshot.OverrideCount > 0 ? snapshot.LastOverrideThreshold.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(')');
        }

        private static void AppendMetricValue(StringBuilder builder, bool hasMetric, float value)
        {
            builder.Append(hasMetric ? value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
        }

        private static void AppendObservedEntityValue(StringBuilder builder, bool hasObservation, Entity entity)
        {
            if (!hasObservation)
            {
                builder.Append("n/a");
                return;
            }

            builder.Append(entity == Entity.Null ? "none" : FormatEntity(entity));
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
            builder.Append(", deliveryMode=").Append(state.VirtualGood ? "virtual" : "physical");
            builder.Append(", tripTrackingExpected=").Append(state.TripTrackingExpected);
            builder.Append(", currentTradingExpected=").Append(state.CurrentTradingExpected);
            builder.Append(", pathExpected=").Append(state.PathExpected);
            builder.Append(", buyerOrigin=").Append(state.BuyerOrigin ?? "unknown");
            builder.Append(", buyerSeenThisWindow=").Append(state.BuyerSeenThisWindow);
            builder.Append(", lastBuyerSeenSampleAge=");
            builder.Append(state.LastBuyerSeenSampleAge >= 0 ? state.LastBuyerSeenSampleAge.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", noBuyerReason=").Append(string.IsNullOrEmpty(state.NoBuyerReason) ? "none" : state.NoBuyerReason);
            builder.Append(", companyUpdateFrame=");
            builder.Append(state.CompanyUpdateFrame >= 0 ? state.CompanyUpdateFrame.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", currentVanillaBuyerUpdateFrame=");
            builder.Append(state.CurrentVanillaBuyerUpdateFrame >= 0 ? state.CurrentVanillaBuyerUpdateFrame.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", framesUntilNextVanillaBuyerPass=");
            builder.Append(state.FramesUntilNextVanillaBuyerPass >= 0 ? state.FramesUntilNextVanillaBuyerPass.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", estimatedMissedVanillaBuyerPasses=").Append(state.EstimatedMissedVanillaBuyerPasses);
            builder.Append(", selectedNoBuyerConsecutiveWindows=").Append(state.SelectedNoBuyerConsecutiveWindows);
            builder.Append(", selectedRequestNoPathConsecutiveWindows=").Append(state.SelectedRequestNoPathConsecutiveWindows);
            builder.Append(", belowThresholdConsecutiveWindows=").Append(state.BelowThresholdConsecutiveWindows);
            builder.Append(", pathStage=").Append(string.IsNullOrEmpty(state.PathStage) ? "none" : state.PathStage);
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
            builder.Append(", virtualResolvedThisWindow=").Append(state.VirtualResolvedThisWindow);
            builder.Append(", virtualResolvedAmount=").Append(state.VirtualResolvedAmount);
            builder.Append(", lastVirtualResolutionSampleAge=");
            builder.Append(state.LastVirtualResolutionSampleAge >= 0 ? state.LastVirtualResolutionSampleAge.ToString(CultureInfo.InvariantCulture) : "n/a");
            builder.Append(", lastPathSeenSampleAge=");
            builder.Append(state.LastPathSeenSampleAge >= 0 ? state.LastPathSeenSampleAge.ToString(CultureInfo.InvariantCulture) : "n/a");
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

            if (!m_ResourceWeightCache.TryGetValue(resource, out float weight))
            {
                weight = EconomyUtils.GetWeight(EntityManager, resource, m_ResourceSystem.GetPrefabs());
                m_ResourceWeightCache[resource] = weight;
            }

            return weight;
        }

        private SoftwareConsumerDiagnosticState GetSoftwareConsumerDiagnosticState(Entity company, SoftwareOfficePrefabMetadata prefabMetadata, int day, int sampleIndex)
        {
            SoftwareNeedState needState = GetSoftwareNeedState(company, prefabMetadata);
            TryGetCompanyTradeCost(company, Resource.Software, out SoftwareTradeCostState tradeCostState);
            SoftwareAcquisitionState acquisitionState = GetSoftwareAcquisitionState(company);
            bool hasCurrentLastTradePartnerObservation = TryGetObservedLastTradePartner(company, out Entity currentLastTradePartner);
            SoftwareConsumerTraceState traceState = UpdateSoftwareConsumerTrace(company, needState, acquisitionState, day, sampleIndex, currentLastTradePartner, hasCurrentLastTradePartnerObservation);
            SoftwareVirtualResolutionProbeState probeState = GetSoftwareVirtualResolutionProbeState(needState, acquisitionState, traceState, currentLastTradePartner, hasCurrentLastTradePartnerObservation, sampleIndex);
            traceState = UpdateVirtualResolutionTrace(company, acquisitionState, probeState, traceState, sampleIndex);
            EnrichSoftwareAcquisitionState(company, needState, ref acquisitionState, traceState, probeState, sampleIndex);
            return new SoftwareConsumerDiagnosticState
            {
                Need = needState,
                TradeCost = tradeCostState,
                Acquisition = acquisitionState,
                Trace = traceState,
                Probe = probeState
            };
        }

        private static bool IsSelectedPipelineClassification(string classification)
        {
            return string.Equals(classification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedPathPending, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResolvedVirtualNoTrackingExpected, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedResolvedNoTrackingUnexpected, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedTripPresent, StringComparison.Ordinal) ||
                   string.Equals(classification, kTraceSelectedCurrentTradingPresent, StringComparison.Ordinal);
        }

        private static bool IsNeedClearedAfterSelected(SoftwareConsumerTraceState traceState)
        {
            return string.Equals(traceState.CurrentClassification, kTraceNeedCleared, StringComparison.Ordinal) &&
                   IsSelectedPipelineClassification(traceState.LastTransitionFromLabel);
        }

        private static SoftwareVirtualResolutionProbeState GetSoftwareVirtualResolutionProbeState(
            SoftwareNeedState needState,
            SoftwareAcquisitionState acquisitionState,
            SoftwareConsumerTraceState traceState,
            Entity currentLastTradePartner,
            bool hasCurrentLastTradePartnerObservation,
            int sampleIndex)
        {
            SoftwareVirtualResolutionProbeState state = default;
            state.CurrentSoftwareStock = needState.Stock;
            state.PreviousSoftwareStock = traceState.PreviousSoftwareStock;
            state.HasPreviousSoftwareStock = traceState.HasPreviousSoftwareStock;
            state.StockIncreasedSincePreviousSample = traceState.HasPreviousSoftwareStock &&
                                                     needState.Stock > traceState.PreviousSoftwareStock;
            state.CurrentLastTradePartner = currentLastTradePartner;
            state.HasCurrentLastTradePartnerObservation = hasCurrentLastTradePartnerObservation;
            state.PreviousLastTradePartner = traceState.PreviousLastTradePartner;
            state.HasPreviousLastTradePartnerObservation = traceState.HasPreviousLastTradePartnerObservation;
            state.LastTradePartnerChanged = traceState.HasPreviousLastTradePartnerObservation &&
                                           hasCurrentLastTradePartnerObservation &&
                                           currentLastTradePartner != traceState.PreviousLastTradePartner;
            state.PreviousPathSellerSeen = traceState.HasLastPathDestination && traceState.LastPathDestination != Entity.Null;
            state.PreviousPathSeller = traceState.LastPathDestination;
            state.CurrentTradePartnerMatchesPreviousPathSeller = state.PreviousPathSellerSeen &&
                                                                hasCurrentLastTradePartnerObservation &&
                                                                currentLastTradePartner == traceState.LastPathDestination;
            state.NeedClearedAfterSelected = IsNeedClearedAfterSelected(traceState);
            state.BuyerSeenRecently = traceState.HasLastBuyerSeenSampleIndex && sampleIndex - traceState.LastBuyerSeenSampleIndex <= 1;
            state.PathSeenRecently = traceState.HasLastPathSeenSampleIndex && sampleIndex - traceState.LastPathSeenSampleIndex <= 1;
            state.Eligible = acquisitionState.VirtualGood &&
                             !acquisitionState.ResourceBuyerPresent &&
                             !acquisitionState.PathPending &&
                             acquisitionState.TripNeededCount == 0 &&
                             acquisitionState.CurrentTradingCount == 0 &&
                             (needState.Selected || state.NeedClearedAfterSelected);

            bool recoveredAboveThreshold = !needState.Selected && needState.EffectiveStock >= needState.Threshold;
            bool selectedStateEvidence = state.LastTradePartnerChanged ||
                                         state.StockIncreasedSincePreviousSample ||
                                         state.CurrentTradePartnerMatchesPreviousPathSeller;
            bool clearedStateEvidence = state.StockIncreasedSincePreviousSample ||
                                        state.LastTradePartnerChanged ||
                                        state.CurrentTradePartnerMatchesPreviousPathSeller ||
                                        (recoveredAboveThreshold && (state.BuyerSeenRecently || state.PathSeenRecently || state.PreviousPathSellerSeen));

            state.EvidenceResolvedVirtual = state.Eligible &&
                                            ((needState.Selected && selectedStateEvidence) ||
                                             (state.NeedClearedAfterSelected && clearedStateEvidence));
            return state;
        }

        private SoftwareNeedState GetSoftwareNeedState(Entity company, SoftwareOfficePrefabMetadata prefabMetadata)
        {
            SoftwareNeedState state = default;
            IndustrialProcessData processData = prefabMetadata.ProcessData;
            bool hasSecondInput = processData.m_Input2.m_Resource != Resource.NoResource;
            int slotCount = hasSecondInput ? 2 : 1;
            if (processData.m_Output.m_Resource != processData.m_Input1.m_Resource && prefabMetadata.OutputHasWeight)
            {
                slotCount++;
            }

            int maxCapacityPerSlot = slotCount > 0 ? prefabMetadata.StorageLimit / slotCount : prefabMetadata.StorageLimit;
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
            uint currentSimulationFrame = m_SimulationSystem.frameIndex;
            state.ResourceWeight = GetResourceWeight(Resource.Software);
            state.VirtualGood = state.ResourceWeight <= 0f;
            state.TripTrackingExpected = !state.VirtualGood;
            state.CurrentTradingExpected = !state.VirtualGood;
            state.PathExpected = true;
            state.CorrectiveBuyerTagged = EntityManager.HasComponent<CorrectiveSoftwareBuyerTag>(company);
            if (TryGetCompanyUpdateFrame(company, out int companyUpdateFrame))
            {
                state.CompanyUpdateFrame = companyUpdateFrame;
                state.CurrentVanillaBuyerUpdateFrame = (int)SimulationUtils.GetUpdateFrameWithInterval(currentSimulationFrame, kVanillaBuyerUpdateInterval, kVanillaBuyerUpdateGroupCount);
                state.FramesUntilNextVanillaBuyerPass = GetFramesUntilNextVanillaBuyerPass(currentSimulationFrame, companyUpdateFrame);
            }
            else
            {
                state.CompanyUpdateFrame = -1;
                state.CurrentVanillaBuyerUpdateFrame = -1;
                state.FramesUntilNextVanillaBuyerPass = -1;
            }

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

        private SoftwareConsumerTraceState UpdateSoftwareConsumerTrace(Entity company, SoftwareNeedState needState, SoftwareAcquisitionState acquisitionState, int day, int sampleIndex, Entity currentLastTradePartner, bool hasCurrentLastTradePartnerObservation)
        {
            m_SoftwareConsumerTrace.TryGetValue(company, out SoftwareConsumerTraceState traceState);
            uint currentSimulationFrame = m_SimulationSystem.frameIndex;
            if (traceState.HasObservedSoftwareStock)
            {
                traceState.PreviousSoftwareStock = traceState.LastObservedSoftwareStock;
                traceState.HasPreviousSoftwareStock = true;
            }

            traceState.LastObservedSoftwareStock = needState.Stock;
            traceState.HasObservedSoftwareStock = true;
            if (hasCurrentLastTradePartnerObservation)
            {
                if (traceState.HasObservedLastTradePartner)
                {
                    traceState.PreviousLastTradePartner = traceState.LastObservedLastTradePartner;
                    traceState.HasPreviousLastTradePartnerObservation = true;
                }

                traceState.LastObservedLastTradePartner = currentLastTradePartner;
                traceState.HasObservedLastTradePartner = true;
            }

            if (acquisitionState.ResourceBuyerPresent)
            {
                traceState.LastBuyerSeenSampleIndex = sampleIndex;
                traceState.HasLastBuyerSeenSampleIndex = true;
            }

            if (acquisitionState.PathComponentPresent)
            {
                traceState.LastPathSeenSampleIndex = sampleIndex;
                traceState.HasLastPathSeenSampleIndex = true;
            }

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
            bool stayedSelectedNoBuyer = string.Equals(traceState.CurrentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal) &&
                                         string.Equals(currentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal);
            traceState.BelowThresholdConsecutiveWindows = needState.Selected ? traceState.BelowThresholdConsecutiveWindows + 1 : 0;
            traceState.SelectedNoBuyerConsecutiveWindows = string.Equals(currentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal)
                ? traceState.SelectedNoBuyerConsecutiveWindows + 1
                : 0;
            // Estimate missed vanilla buyer opportunities only across observed sample-to-sample no-buyer streaks.
            traceState.SelectedNoBuyerEstimatedMissedVanillaPasses = string.Equals(currentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal)
                ? stayedSelectedNoBuyer && traceState.HasLastObservedSimulationFrame
                    ? traceState.SelectedNoBuyerEstimatedMissedVanillaPasses + CountVanillaBuyerPassesBetweenFrames(traceState.LastObservedSimulationFrame, currentSimulationFrame, acquisitionState.CompanyUpdateFrame)
                    : 0
                : 0;
            traceState.SelectedRequestNoPathConsecutiveWindows = string.Equals(currentClassification, kTraceSelectedResourceBuyerNoPath, StringComparison.Ordinal)
                ? traceState.SelectedRequestNoPathConsecutiveWindows + 1
                : 0;
            if (!string.Equals(traceState.CurrentClassification, currentClassification, StringComparison.Ordinal))
            {
                traceState.LastTransitionFromLabel = traceState.CurrentClassification;
                traceState.LastTransitionLabel = currentClassification;
                traceState.LastTransitionDay = day;
                traceState.LastTransitionSampleIndex = sampleIndex;
            }

            traceState.CurrentClassification = currentClassification;
            traceState.LastObservedSimulationFrame = currentSimulationFrame;
            traceState.HasLastObservedSimulationFrame = true;
            m_SoftwareConsumerTrace[company] = traceState;
            return traceState;
        }


        private SoftwareConsumerTraceState UpdateVirtualResolutionTrace(Entity company, SoftwareAcquisitionState acquisitionState, SoftwareVirtualResolutionProbeState probeState, SoftwareConsumerTraceState traceState, int sampleIndex)
        {
            if (acquisitionState.VirtualGood && probeState.Eligible && probeState.EvidenceResolvedVirtual)
            {
                traceState.LastVirtualResolutionSampleIndex = sampleIndex;
                traceState.HasLastVirtualResolutionSampleIndex = true;
                m_SoftwareConsumerTrace[company] = traceState;
            }

            return traceState;
        }

        private void EnrichSoftwareAcquisitionState(Entity company, SoftwareNeedState needState, ref SoftwareAcquisitionState state, SoftwareConsumerTraceState traceState, SoftwareVirtualResolutionProbeState probeState, int sampleIndex)
        {
            state.BuyerSeenThisWindow = state.ResourceBuyerPresent;
            state.BuyerOrigin = GetBuyerOriginLabel(state);
            state.LastBuyerSeenSampleAge = GetSampleAge(traceState.HasLastBuyerSeenSampleIndex, traceState.LastBuyerSeenSampleIndex, sampleIndex);
            state.EstimatedMissedVanillaBuyerPasses = string.Equals(traceState.CurrentClassification, kTraceSelectedNoResourceBuyer, StringComparison.Ordinal)
                ? traceState.SelectedNoBuyerEstimatedMissedVanillaPasses
                : 0;
            state.SelectedNoBuyerConsecutiveWindows = traceState.SelectedNoBuyerConsecutiveWindows;
            state.SelectedRequestNoPathConsecutiveWindows = traceState.SelectedRequestNoPathConsecutiveWindows;
            state.BelowThresholdConsecutiveWindows = traceState.BelowThresholdConsecutiveWindows;
            state.PathStage = GetPathStageLabel(state);
            state.LastPathSeenSampleAge = GetSampleAge(traceState.HasLastPathSeenSampleIndex, traceState.LastPathSeenSampleIndex, sampleIndex);
            state.VirtualResolvedThisWindow = state.VirtualGood && probeState.Eligible && probeState.EvidenceResolvedVirtual;
            state.VirtualResolvedAmount = state.VirtualResolvedThisWindow && probeState.HasPreviousSoftwareStock
                ? Math.Max(0, probeState.CurrentSoftwareStock - probeState.PreviousSoftwareStock)
                : 0;
            state.LastVirtualResolutionSampleAge = GetSampleAge(traceState.HasLastVirtualResolutionSampleIndex, traceState.LastVirtualResolutionSampleIndex, sampleIndex);
            state.NoBuyerReason = DetermineNoBuyerReason(needState, state);
            if (!needState.Selected)
            {
                state.PathExpected = false;
            }
        }

        private string DetermineNoBuyerReason(SoftwareNeedState needState, SoftwareAcquisitionState state)
        {
            if (!needState.Selected || state.ResourceBuyerPresent)
            {
                return "none";
            }

            if (state.EstimatedMissedVanillaBuyerPasses > 0)
            {
                return state.VirtualGood && Mod.Settings != null && Mod.Settings.EnableVirtualOfficeResourceBuyerFix
                    ? "missed_vanilla_buyer_pass_awaiting_corrective_pass"
                    : "missed_vanilla_buyer_pass";
            }

            if (state.VirtualGood && state.LastVirtualResolutionSampleAge >= 0 && state.LastVirtualResolutionSampleAge <= 1)
            {
                return "buyer_recently_resolved_virtual";
            }

            if (state.VirtualGood && Mod.Settings != null && Mod.Settings.EnableVirtualOfficeResourceBuyerFix)
            {
                return "awaiting_corrective_pass";
            }

            return "awaiting_vanilla_tick";
        }

        private static int GetSampleAge(bool hasValue, int lastSampleIndex, int currentSampleIndex)
        {
            return hasValue ? Math.Max(0, currentSampleIndex - lastSampleIndex) : -1;
        }

        private bool TryGetCompanyUpdateFrame(Entity company, out int updateFrameIndex)
        {
            if (EntityManager.HasComponent<UpdateFrame>(company))
            {
                updateFrameIndex = (int)EntityManager.GetSharedComponent<UpdateFrame>(company).m_Index;
                return true;
            }

            updateFrameIndex = -1;
            return false;
        }

        private static int GetFramesUntilNextVanillaBuyerPass(uint currentFrame, int companyUpdateFrame)
        {
            if (companyUpdateFrame < 0)
            {
                return -1;
            }

            uint currentBucket = currentFrame / kVanillaBuyerUpdateInterval;
            uint currentGroup = currentBucket % (uint)kVanillaBuyerUpdateGroupCount;
            uint targetGroup = (uint)companyUpdateFrame;
            uint bucketDelta = targetGroup > currentGroup
                ? targetGroup - currentGroup
                : (uint)kVanillaBuyerUpdateGroupCount - (currentGroup - targetGroup);
            if (bucketDelta == 0u)
            {
                bucketDelta = (uint)kVanillaBuyerUpdateGroupCount;
            }

            uint nextBuyerBucket = currentBucket + bucketDelta;
            uint nextBuyerFrame = nextBuyerBucket * kVanillaBuyerUpdateInterval;
            return nextBuyerFrame > currentFrame ? (int)(nextBuyerFrame - currentFrame) : 0;
        }

        private static int CountVanillaBuyerPassesBetweenFrames(uint startFrame, uint endFrame, int companyUpdateFrame)
        {
            if (companyUpdateFrame < 0 || endFrame <= startFrame)
            {
                return 0;
            }

            uint startBucket = startFrame / kVanillaBuyerUpdateInterval;
            uint endBucket = endFrame / kVanillaBuyerUpdateInterval;
            if (endBucket <= startBucket)
            {
                return 0;
            }

            return CountCongruentValuesInInclusiveRange(
                startBucket + 1u,
                endBucket,
                (uint)companyUpdateFrame,
                (uint)kVanillaBuyerUpdateGroupCount);
        }

        private static int CountCongruentValuesInInclusiveRange(uint startValue, uint endValue, uint targetRemainder, uint modulus)
        {
            if (startValue > endValue || modulus == 0u)
            {
                return 0;
            }

            uint first = startValue;
            uint currentRemainder = first % modulus;
            if (currentRemainder != targetRemainder)
            {
                first += (targetRemainder + modulus - currentRemainder) % modulus;
            }

            if (first > endValue)
            {
                return 0;
            }

            return 1 + (int)((endValue - first) / modulus);
        }

        private static string GetBuyerOriginLabel(SoftwareAcquisitionState state)
        {
            if (!state.ResourceBuyerPresent)
            {
                return "none";
            }

            if (state.CorrectiveBuyerTagged || (state.VirtualGood && state.ResourceBuyerAmount > kResourceMinimumRequestAmount))
            {
                return "corrective";
            }

            return "vanilla";
        }

        private static string GetPathStageLabel(SoftwareAcquisitionState state)
        {
            if (!state.PathComponentPresent)
            {
                return state.ResourceBuyerPresent ? "buyer_only" : "none";
            }

            if (state.PathPending)
            {
                return "pending";
            }

            if (state.PathDestination != Entity.Null)
            {
                return "resolved";
            }

            return "found";
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
            return GetResourceWeight(resource) > 0f;
        }

        private bool TryGetLastTradePartner(Entity company, out Entity lastTradePartner)
        {
            if (!TryGetObservedLastTradePartner(company, out lastTradePartner))
            {
                return false;
            }

            return lastTradePartner != Entity.Null;
        }

        private bool TryGetObservedLastTradePartner(Entity company, out Entity lastTradePartner)
        {
            lastTradePartner = Entity.Null;
            if (!EntityManager.HasComponent<BuyingCompany>(company))
            {
                return false;
            }

            lastTradePartner = EntityManager.GetComponentData<BuyingCompany>(company).m_LastTradePartner;
            return true;
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

        private bool TryGetSoftwareOfficePrefabMetadata(Entity prefab, out SoftwareOfficePrefabMetadata prefabMetadata)
        {
            if (m_SoftwareOfficePrefabCache.TryGetValue(prefab, out prefabMetadata))
            {
                return prefabMetadata.IsRelevant;
            }

            prefabMetadata = new SoftwareOfficePrefabMetadata
            {
                StorageLimit = int.MaxValue
            };

            if (!EntityManager.HasComponent<IndustrialProcessData>(prefab))
            {
                m_SoftwareOfficePrefabCache[prefab] = prefabMetadata;
                return false;
            }

            IndustrialProcessData processData = EntityManager.GetComponentData<IndustrialProcessData>(prefab);
            bool isProducer = processData.m_Output.m_Resource == Resource.Software;
            bool isConsumer = processData.m_Input1.m_Resource == Resource.Software ||
                              processData.m_Input2.m_Resource == Resource.Software;
            prefabMetadata.ProcessData = processData;
            prefabMetadata.IsProducer = isProducer;
            prefabMetadata.IsConsumer = isConsumer;
            prefabMetadata.IsRelevant = isProducer || isConsumer;
            prefabMetadata.OutputHasWeight = processData.m_Output.m_Resource != Resource.NoResource &&
                                             ResourceHasWeight(processData.m_Output.m_Resource);
            if (EntityManager.HasComponent<StorageLimitData>(prefab))
            {
                prefabMetadata.StorageLimit = EntityManager.GetComponentData<StorageLimitData>(prefab).m_Limit;
            }

            m_SoftwareOfficePrefabCache[prefab] = prefabMetadata;
            return prefabMetadata.IsRelevant;
        }

        private string CollectFreeSoftwareOfficePropertyDetails()
        {
            StringBuilder details = null;
            int detailCount = 0;
            using NativeArray<Entity> properties = m_FreeOfficePropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length && detailCount < kMaxDetailEntries; i++)
            {
                Entity property = properties[i];
                if (!IsSoftwareCapableOfficeProperty(property))
                {
                    continue;
                }

                AppendDetail(ref details, ref detailCount, DescribeProperty(property));
            }

            return details == null ? string.Empty : details.ToString();
        }

        private string CollectOnMarketOfficePropertyDetails()
        {
            StringBuilder details = null;
            int detailCount = 0;
            using NativeArray<Entity> properties = m_OnMarketPropertyQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < properties.Length && detailCount < kMaxDetailEntries; i++)
            {
                Entity property = properties[i];
                if (!EntityManager.HasComponent<OfficeProperty>(property))
                {
                    continue;
                }

                AppendDetail(ref details, ref detailCount, DescribeProperty(property));
            }

            return details == null ? string.Empty : details.ToString();
        }

        private bool IsSoftwareCapableOfficeProperty(Entity property)
        {
            if (!EntityManager.HasComponent<PrefabRef>(property))
            {
                return false;
            }

            Entity prefab = EntityManager.GetComponentData<PrefabRef>(property).m_Prefab;
            if (!EntityManager.HasComponent<BuildingPropertyData>(prefab))
            {
                return false;
            }

            return (EntityManager.GetComponentData<BuildingPropertyData>(prefab).m_AllowedManufactured & Resource.Software) != Resource.NoResource;
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

        private static bool TryGetDemandFactorValue(NativeArray<int> factors, string factorName, out int value)
        {
            value = 0;
            if (!Enum.TryParse(factorName, ignoreCase: false, out DemandFactor factor))
            {
                return false;
            }

            int index = (int)factor;
            if ((uint)index >= (uint)factors.Length)
            {
                return false;
            }

            value = factors[index];
            return true;
        }

        private static int CountOversupplySignals(DiagnosticSnapshot snapshot)
        {
            int count = 0;
            if (snapshot.EmptyBuildingsFactor != 0)
            {
                count++;
            }

            if (snapshot.LocalDemandFactorKnown && snapshot.LocalDemandFactor != 0)
            {
                count++;
            }

            if (snapshot.FreeOfficeProperties > 0)
            {
                count++;
            }

            if (snapshot.OnMarketOfficeProperties > 0)
            {
                count++;
            }

            if (snapshot.ActivelyVacantOfficeProperties > 0)
            {
                count++;
            }

            if (snapshot.StaleRenterOnMarketOfficeProperties > 0)
            {
                count++;
            }

            return count;
        }

        private static int CountSoftwareTrackSignals(DiagnosticSnapshot snapshot)
        {
            int count = 0;
            if (snapshot.SoftwareProducerOfficeEfficiencyZero > 0)
            {
                count++;
            }

            if (snapshot.SoftwareProducerOfficeLackResourcesZero > 0)
            {
                count++;
            }

            if (snapshot.SoftwareConsumerOfficeEfficiencyZero > 0)
            {
                count++;
            }

            if (snapshot.SoftwareConsumerOfficeLackResourcesZero > 0)
            {
                count++;
            }

            if (snapshot.SoftwareConsumerOfficeSoftwareInputZero > 0)
            {
                count++;
            }

            if (snapshot.SoftwareConsumerSelectedNoResourceBuyer > 0)
            {
                count++;
            }

            if (snapshot.SoftwareConsumerSelectedRequestNoPath > 0)
            {
                count++;
            }

            if (snapshot.SoftwareConsumerPathPending > 0)
            {
                count++;
            }

            return count;
        }

        private static string FormatDemandSignalPattern(DiagnosticSnapshot snapshot)
        {
            int oversupplySignals = CountOversupplySignals(snapshot);
            int softwareTrackSignals = CountSoftwareTrackSignals(snapshot);
            if (oversupplySignals > 0 && softwareTrackSignals > 0)
            {
                return "mixed_oversupply_and_software_track";
            }

            if (oversupplySignals > 0)
            {
                return "oversupply_candidate";
            }

            if (softwareTrackSignals > 0)
            {
                return "software_track_candidate";
            }

            return "none_detected";
        }

        private static string FormatOptionalFactorValue(bool isKnown, int value)
        {
            return isKnown ? value.ToString(CultureInfo.InvariantCulture) : "n/a";
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
                Mod.Settings.EnablePhantomVacancyFix,
                Mod.Settings.EnableOutsideConnectionVirtualSellerFix,
                Mod.Settings.EnableVirtualOfficeResourceBuyerFix,
                Mod.Settings.EnableOfficeDemandDirectPatch,
                Mod.Settings.EnableDemandDiagnostics,
                GetDiagnosticsSamplesPerDay(),
                Mod.Settings.CaptureStableEvidence,
                Mod.Settings.VerboseLogging);
        }

        private static string FormatSettingsSnapshot(DiagnosticsSettingsState settingsState)
        {
            return $"EnablePhantomVacancyFix:{settingsState.EnablePhantomVacancyFix}," +
                   $"EnableOutsideConnectionVirtualSellerFix:{settingsState.EnableOutsideConnectionVirtualSellerFix}," +
                   $"EnableVirtualOfficeResourceBuyerFix:{settingsState.EnableVirtualOfficeResourceBuyerFix}," +
                   $"EnableOfficeDemandDirectPatch:{settingsState.EnableOfficeDemandDirectPatch}," +
                   $"EnableDemandDiagnostics:{settingsState.EnableDemandDiagnostics}," +
                   $"DiagnosticsSamplesPerDay:{settingsState.DiagnosticsSamplesPerDay}," +
                   $"CaptureStableEvidence:{settingsState.CaptureStableEvidence}," +
                   $"VerboseLogging:{settingsState.VerboseLogging}";
        }

        private static string FormatPatchState()
        {
#if DEBUG
            return "debug-build";
#elif RELEASE_BUILD
            return "release-build";
#else
            return "unknown";
#endif
        }

        private static string FormatDiagnosticCounters(DiagnosticSnapshot snapshot)
        {
            int oversupplySignals = CountOversupplySignals(snapshot);
            int softwareTrackSignals = CountSoftwareTrackSignals(snapshot);
            return
                $"officeDemand(building={snapshot.OfficeBuildingDemand}, company={snapshot.OfficeCompanyDemand}, emptyBuildings={snapshot.EmptyBuildingsFactor}, buildingDemand={snapshot.BuildingDemandFactor}); " +
                $"officeDemandSignals(unoccupiedBuildingsFactor={snapshot.EmptyBuildingsFactor}, localDemandFactorKnown={snapshot.LocalDemandFactorKnown}, localDemandFactor={FormatOptionalFactorValue(snapshot.LocalDemandFactorKnown, snapshot.LocalDemandFactor)}, freeProperties={snapshot.FreeOfficeProperties}, onMarket={snapshot.OnMarketOfficeProperties}, activelyVacant={snapshot.ActivelyVacantOfficeProperties}, staleRenterOnly={snapshot.StaleRenterOnMarketOfficeProperties}, oversupplySignalCount={oversupplySignals}, softwareTrackSignalCount={softwareTrackSignals}, pattern={FormatDemandSignalPattern(snapshot)}); " +
                $"freeOfficeProperties(total={snapshot.FreeOfficeProperties}, software={snapshot.FreeSoftwareOfficeProperties}, inOccupiedBuildings={snapshot.FreeOfficePropertiesInOccupiedBuildings}, softwareInOccupiedBuildings={snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings}); " +
                $"onMarketOfficeProperties(total={snapshot.OnMarketOfficeProperties}, activelyVacant={snapshot.ActivelyVacantOfficeProperties}, occupied={snapshot.OccupiedOnMarketOfficeProperties}, staleRenterOnly={snapshot.StaleRenterOnMarketOfficeProperties}); " +
                $"phantomVacancy(signatureOccupiedOnMarketOffice={snapshot.SignatureOccupiedOnMarketOffice}, signatureOccupiedOnMarketIndustrial={snapshot.SignatureOccupiedOnMarketIndustrial}, signatureOccupiedToBeOnMarket={snapshot.SignatureOccupiedToBeOnMarket}, nonSignatureOccupiedOnMarketOffice={snapshot.NonSignatureOccupiedOnMarketOffice}, nonSignatureOccupiedOnMarketIndustrial={snapshot.NonSignatureOccupiedOnMarketIndustrial}, guardCorrections={snapshot.GuardCorrections}); " +
                $"software(resourceProduction={snapshot.SoftwareProduction}, resourceDemand={snapshot.SoftwareDemand}, companies={snapshot.SoftwareProductionCompanies}, propertyless={snapshot.SoftwarePropertylessCompanies}); " +
                $"electronics(resourceProduction={snapshot.ElectronicsProduction}, resourceDemand={snapshot.ElectronicsDemand}, companies={snapshot.ElectronicsProductionCompanies}, propertyless={snapshot.ElectronicsPropertylessCompanies}); " +
                $"softwareProducerOffices(total={snapshot.SoftwareProducerOfficeCompanies}, propertyless={snapshot.SoftwareProducerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareProducerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareProducerOfficeLackResourcesZero}); " +
                $"softwareConsumerOffices(total={snapshot.SoftwareConsumerOfficeCompanies}, propertyless={snapshot.SoftwareConsumerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareConsumerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareConsumerOfficeLackResourcesZero}, softwareInputZero={snapshot.SoftwareConsumerOfficeSoftwareInputZero}); " +
                $"softwareConsumerBuyerState(needSelected={snapshot.SoftwareConsumerNeedSelected}, resourceBuyerPresent={snapshot.SoftwareConsumerResourceBuyerPresent}, correctiveBuyerPresent={snapshot.SoftwareConsumerCorrectiveBuyerPresent}, vanillaBuyerPresent={snapshot.SoftwareConsumerVanillaBuyerPresent}, trackingExpectedSelected={snapshot.SoftwareConsumerTrackingExpectedSelected}, selectedNoResourceBuyer={snapshot.SoftwareConsumerSelectedNoResourceBuyer}, selectedNoBuyerShortGap={snapshot.SoftwareConsumerSelectedNoBuyerShortGap}, selectedNoBuyerPersistent={snapshot.SoftwareConsumerSelectedNoBuyerPersistent}, selectedNoBuyerMissedVanillaPass={snapshot.SoftwareConsumerSelectedNoBuyerMissedVanillaPass}, selectedNoBuyerMissedMultipleVanillaPasses={snapshot.SoftwareConsumerSelectedNoBuyerMissedMultipleVanillaPasses}, selectedNoBuyerMaxMissedVanillaPasses={snapshot.SoftwareConsumerSelectedNoBuyerMaxMissedVanillaPasses}, selectedRequestNoPath={snapshot.SoftwareConsumerSelectedRequestNoPath}, selectedRequestNoPathShortGap={snapshot.SoftwareConsumerSelectedRequestNoPathShortGap}, selectedRequestNoPathPersistent={snapshot.SoftwareConsumerSelectedRequestNoPathPersistent}, pathPending={snapshot.SoftwareConsumerPathPending}, resolvedVirtualNoTrackingExpected={snapshot.SoftwareConsumerResolvedVirtualNoTrackingExpected}, resolvedNoTrackingUnexpected={snapshot.SoftwareConsumerResolvedNoTrackingUnexpected}, tripPresent={snapshot.SoftwareConsumerTripPresent}, currentTradingPresent={snapshot.SoftwareConsumerCurrentTradingPresent}, virtualResolvedThisWindow={snapshot.SoftwareConsumerVirtualResolvedThisWindow}, virtualResolvedAmount={snapshot.SoftwareConsumerVirtualResolvedAmount})";
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

        private void EmitVirtualOfficeBuyerProbeSummary(DiagnosticSnapshot snapshot)
        {
            VirtualOfficeResourceBuyerFixSystem buyerFixSystem = World.GetExistingSystemManaged<VirtualOfficeResourceBuyerFixSystem>();
            if (buyerFixSystem == null)
            {
                return;
            }

            buyerFixSystem.EmitProbeSummaryForObservation(snapshot.Day, snapshot.SampleIndex, snapshot.SampleSlot);
        }

        private void ResetVirtualOfficeBuyerProbeState()
        {
            VirtualOfficeResourceBuyerFixSystem buyerFixSystem = World.GetExistingSystemManaged<VirtualOfficeResourceBuyerFixSystem>();
            if (buyerFixSystem == null)
            {
                return;
            }

            buyerFixSystem.ResetProbeState();
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
            m_SoftwareOfficePrefabCache.Clear();
            m_ResourceWeightCache.Clear();
            ResetVirtualOfficeBuyerProbeState();
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
            ResetVirtualOfficeBuyerProbeState();
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

        private void AppendDetail(ref StringBuilder builder, ref int count, string detail)
        {
            if (count >= kMaxDetailEntries || string.IsNullOrEmpty(detail))
            {
                return;
            }

            if (builder == null)
            {
                builder = new StringBuilder();
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
