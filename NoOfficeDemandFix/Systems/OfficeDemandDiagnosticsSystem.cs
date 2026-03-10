using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Objects;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
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
        private const int kSamplesPerDay = 2;
        private const int kTicksPerSample = TimeSystem.kTicksPerDay / kSamplesPerDay;

        private struct FactorEntry
        {
            public int Index;
            public int Weight;
        }

        private struct DiagnosticSnapshot
        {
            public int Day;
            public int SampleIndex;
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
            public string TopFactors;
            public string FreeSoftwareOfficePropertyDetails;
            public string OnMarketOfficePropertyDetails;
            public string SoftwareOfficeDetails;
        }

        private SimulationSystem m_SimulationSystem;
        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private CountCompanyDataSystem m_CountCompanyDataSystem;
        private SignaturePropertyMarketGuardSystem m_SignaturePropertyMarketGuardSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_TimeDataQuery;
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

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_IndustrialDemandSystem = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            m_CountCompanyDataSystem = World.GetOrCreateSystemManaged<CountCompanyDataSystem>();
            m_SignaturePropertyMarketGuardSystem = World.GetOrCreateSystemManaged<SignaturePropertyMarketGuardSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());
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
            int day = TimeSystem.GetDay(m_SimulationSystem.frameIndex, timeData);
            int sampleIndex = GetSampleIndex(m_SimulationSystem.frameIndex, timeData);
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

            DiagnosticSnapshot snapshot = CaptureSnapshot(day, sampleIndex);
            m_LastLoggedSampleIndex = sampleIndex;

            if (!TryGetObservationTrigger(snapshot, out string trigger))
            {
                return;
            }

            Mod.log.Info(
                $"softwareEvidenceDiagnostics observation_window(session_id={m_SessionId}, run_id={m_RunSequence}, start_day={m_RunStartDay}, end_day={snapshot.Day}, start_sample_index={m_RunStartSampleIndex}, end_sample_index={snapshot.SampleIndex}, sample_day={snapshot.Day}, sample_index={snapshot.SampleIndex}, sample_count={GetObservationSampleCount(snapshot.SampleIndex)}, trigger={trigger}); " +
                $"environment(settings={settingsSnapshot}, patch_state={patchState}); " +
                $"diagnostic_counters(" +
                $"officeDemand(building={snapshot.OfficeBuildingDemand}, company={snapshot.OfficeCompanyDemand}, emptyBuildings={snapshot.EmptyBuildingsFactor}, buildingDemand={snapshot.BuildingDemandFactor}); " +
                $"freeOfficeProperties(total={snapshot.FreeOfficeProperties}, software={snapshot.FreeSoftwareOfficeProperties}, inOccupiedBuildings={snapshot.FreeOfficePropertiesInOccupiedBuildings}, softwareInOccupiedBuildings={snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings}); " +
                $"onMarketOfficeProperties(total={snapshot.OnMarketOfficeProperties}, activelyVacant={snapshot.ActivelyVacantOfficeProperties}, occupied={snapshot.OccupiedOnMarketOfficeProperties}, staleRenterOnly={snapshot.StaleRenterOnMarketOfficeProperties}); " +
                $"phantomVacancy(signatureOccupiedOnMarketOffice={snapshot.SignatureOccupiedOnMarketOffice}, signatureOccupiedOnMarketIndustrial={snapshot.SignatureOccupiedOnMarketIndustrial}, signatureOccupiedToBeOnMarket={snapshot.SignatureOccupiedToBeOnMarket}, nonSignatureOccupiedOnMarketOffice={snapshot.NonSignatureOccupiedOnMarketOffice}, nonSignatureOccupiedOnMarketIndustrial={snapshot.NonSignatureOccupiedOnMarketIndustrial}, guardCorrections={snapshot.GuardCorrections}); " +
                $"software(resourceProduction={snapshot.SoftwareProduction}, resourceDemand={snapshot.SoftwareDemand}, companies={snapshot.SoftwareProductionCompanies}, propertyless={snapshot.SoftwarePropertylessCompanies}); " +
                $"electronics(resourceProduction={snapshot.ElectronicsProduction}, resourceDemand={snapshot.ElectronicsDemand}, companies={snapshot.ElectronicsProductionCompanies}, propertyless={snapshot.ElectronicsPropertylessCompanies}); " +
                $"softwareProducerOffices(total={snapshot.SoftwareProducerOfficeCompanies}, propertyless={snapshot.SoftwareProducerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareProducerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareProducerOfficeLackResourcesZero}); " +
                $"softwareConsumerOffices(total={snapshot.SoftwareConsumerOfficeCompanies}, propertyless={snapshot.SoftwareConsumerOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareConsumerOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareConsumerOfficeLackResourcesZero}, softwareInputZero={snapshot.SoftwareConsumerOfficeSoftwareInputZero})" +
                $"); " +
                $"diagnostic_context(topFactors=[{snapshot.TopFactors}])");

            if (!string.IsNullOrEmpty(snapshot.FreeSoftwareOfficePropertyDetails))
            {
                Mod.log.Info($"softwareEvidenceDiagnostics detail(session_id={m_SessionId}, run_id={m_RunSequence}, observation_end_day={snapshot.Day}, detail_type=freeSoftwareOfficeProperties, values={snapshot.FreeSoftwareOfficePropertyDetails})");
            }

            if (!string.IsNullOrEmpty(snapshot.OnMarketOfficePropertyDetails))
            {
                Mod.log.Info($"softwareEvidenceDiagnostics detail(session_id={m_SessionId}, run_id={m_RunSequence}, observation_end_day={snapshot.Day}, detail_type=onMarketOfficeProperties, values={snapshot.OnMarketOfficePropertyDetails})");
            }

            if (!string.IsNullOrEmpty(snapshot.SoftwareOfficeDetails))
            {
                Mod.log.Info($"softwareEvidenceDiagnostics detail(session_id={m_SessionId}, run_id={m_RunSequence}, observation_end_day={snapshot.Day}, detail_type=softwareOfficeStates, values={snapshot.SoftwareOfficeDetails})");
            }
        }

        private DiagnosticSnapshot CaptureSnapshot(int day, int sampleIndex)
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

                if (efficiencyZero || lackResourcesZero || softwareInputZero)
                {
                    AppendDetail(details, ref detailCount, DescribeSoftwareOffice(company, prefabRef.m_Prefab, propertyRenter.m_Property, processData, isProducer, isConsumer, softwareInputZero, hasEfficiency, efficiency, lackResources));
                }
            }

            snapshot.SoftwareOfficeDetails = details.ToString();
        }

        private string DescribeSoftwareOffice(Entity company, Entity companyPrefab, Entity property, IndustrialProcessData processData, bool isProducer, bool isConsumer, bool softwareInputZero, bool hasEfficiency, float efficiency, float lackResources)
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
            builder.Append("(stock=").Append(GetCompanyResourceAmount(company, resource));
            if (TryGetCompanyBuyCost(company, resource, out float buyCost))
            {
                builder.Append(", buyCost=").Append(buyCost.ToString("0.###", CultureInfo.InvariantCulture));
            }

            builder.Append(')');
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

        private bool TryGetCompanyBuyCost(Entity company, Resource resource, out float buyCost)
        {
            buyCost = 0f;
            if (resource == Resource.NoResource || !EntityManager.HasBuffer<TradeCost>(company))
            {
                return false;
            }

            DynamicBuffer<TradeCost> costs = EntityManager.GetBuffer<TradeCost>(company, isReadOnly: true);
            buyCost = EconomyUtils.GetTradeCost(resource, costs).m_BuyCost;
            return true;
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
                   snapshot.SoftwareConsumerOfficeSoftwareInputZero > 0;
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
        }

        private int GetObservationSampleCount(int endSampleIndex)
        {
            return Math.Max(1, endSampleIndex - m_RunStartSampleIndex + 1);
        }

        private static int GetSampleIndex(uint frameIndex, TimeData timeData)
        {
            return (int)Math.Floor((double)(frameIndex - timeData.m_FirstFrame) / kTicksPerSample + timeData.TimeOffset * kSamplesPerDay);
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
