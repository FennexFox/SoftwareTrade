using System;
using System.Collections.Generic;
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

        private struct FactorEntry
        {
            public int Index;
            public int Weight;
        }

        private struct DiagnosticSnapshot
        {
            public int Day;
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
            public int SoftwareOfficeCompanies;
            public int SoftwareOfficePropertylessCompanies;
            public int SoftwareOfficeEfficiencyZero;
            public int SoftwareOfficeLackResourcesZero;
            public string TopFactors;
            public string FreeSoftwareOfficePropertyDetails;
            public string OnMarketOfficePropertyDetails;
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
        private int m_LastLoggedDay = int.MinValue;

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
            m_LastLoggedDay = int.MinValue;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_LastLoggedDay = int.MinValue;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!IsDiagnosticsEnabled())
            {
                return;
            }

            TimeData timeData = m_TimeDataQuery.GetSingleton<TimeData>();
            int day = TimeSystem.GetDay(m_SimulationSystem.frameIndex, timeData);
            if (day == m_LastLoggedDay)
            {
                return;
            }

            DiagnosticSnapshot snapshot = CaptureSnapshot(day);
            m_LastLoggedDay = day;

            if (!ShouldLog(snapshot))
            {
                return;
            }

            Mod.log.Info(
                $"Office demand diagnostics day {snapshot.Day}: demand(building={snapshot.OfficeBuildingDemand}, company={snapshot.OfficeCompanyDemand}, emptyBuildings={snapshot.EmptyBuildingsFactor}, buildingDemand={snapshot.BuildingDemandFactor}); " +
                $"freeOfficeProperties(total={snapshot.FreeOfficeProperties}, software={snapshot.FreeSoftwareOfficeProperties}, inOccupiedBuildings={snapshot.FreeOfficePropertiesInOccupiedBuildings}, softwareInOccupiedBuildings={snapshot.FreeSoftwareOfficePropertiesInOccupiedBuildings}); " +
                $"onMarketOfficeProperties(total={snapshot.OnMarketOfficeProperties}, activelyVacant={snapshot.ActivelyVacantOfficeProperties}, occupied={snapshot.OccupiedOnMarketOfficeProperties}, staleRenterOnly={snapshot.StaleRenterOnMarketOfficeProperties}); " +
                $"phantomVacancy(signatureOccupiedOnMarketOffice={snapshot.SignatureOccupiedOnMarketOffice}, signatureOccupiedOnMarketIndustrial={snapshot.SignatureOccupiedOnMarketIndustrial}, signatureOccupiedToBeOnMarket={snapshot.SignatureOccupiedToBeOnMarket}, nonSignatureOccupiedOnMarketOffice={snapshot.NonSignatureOccupiedOnMarketOffice}, nonSignatureOccupiedOnMarketIndustrial={snapshot.NonSignatureOccupiedOnMarketIndustrial}, guardCorrections={snapshot.GuardCorrections}); " +
                $"software(resourceProduction={snapshot.SoftwareProduction}, resourceDemand={snapshot.SoftwareDemand}, companies={snapshot.SoftwareProductionCompanies}, propertyless={snapshot.SoftwarePropertylessCompanies}); " +
                $"softwareOffices(total={snapshot.SoftwareOfficeCompanies}, propertyless={snapshot.SoftwareOfficePropertylessCompanies}, efficiencyZero={snapshot.SoftwareOfficeEfficiencyZero}, lackResourcesZero={snapshot.SoftwareOfficeLackResourcesZero}); " +
                $"topFactors=[{snapshot.TopFactors}]");

            if (!string.IsNullOrEmpty(snapshot.FreeSoftwareOfficePropertyDetails))
            {
                Mod.log.Info($"Office demand diagnostics free software properties day {snapshot.Day}: {snapshot.FreeSoftwareOfficePropertyDetails}");
            }

            if (!string.IsNullOrEmpty(snapshot.OnMarketOfficePropertyDetails))
            {
                Mod.log.Info($"Office demand diagnostics on-market office properties day {snapshot.Day}: {snapshot.OnMarketOfficePropertyDetails}");
            }
        }

        private DiagnosticSnapshot CaptureSnapshot(int day)
        {
            JobHandle officeDeps;
            NativeArray<int> officeFactors = m_IndustrialDemandSystem.GetOfficeDemandFactors(out officeDeps);
            officeDeps.Complete();

            JobHandle companyDeps;
            CountCompanyDataSystem.IndustrialCompanyDatas industrialCompanyDatas = m_CountCompanyDataSystem.GetIndustrialCompanyDatas(out companyDeps);
            companyDeps.Complete();

            int softwareIndex = EconomyUtils.GetResourceIndex(Resource.Software);
            DiagnosticSnapshot snapshot = new DiagnosticSnapshot
            {
                Day = day,
                OfficeBuildingDemand = m_IndustrialDemandSystem.officeBuildingDemand,
                OfficeCompanyDemand = m_IndustrialDemandSystem.officeCompanyDemand,
                EmptyBuildingsFactor = officeFactors[(int)DemandFactor.EmptyBuildings],
                BuildingDemandFactor = officeFactors[(int)DemandFactor.BuildingDemand],
                SoftwareProduction = industrialCompanyDatas.m_Production[softwareIndex],
                SoftwareDemand = industrialCompanyDatas.m_Demand[softwareIndex],
                SoftwareProductionCompanies = industrialCompanyDatas.m_ProductionCompanies[softwareIndex],
                SoftwarePropertylessCompanies = industrialCompanyDatas.m_ProductionPropertyless[softwareIndex],
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
                if (processData.m_Output.m_Resource != Resource.Software)
                {
                    continue;
                }

                snapshot.SoftwareOfficeCompanies++;
                if (!EntityManager.HasComponent<PropertyRenter>(company))
                {
                    snapshot.SoftwareOfficePropertylessCompanies++;
                    continue;
                }

                PropertyRenter propertyRenter = EntityManager.GetComponentData<PropertyRenter>(company);
                if (propertyRenter.m_Property == Entity.Null)
                {
                    snapshot.SoftwareOfficePropertylessCompanies++;
                    continue;
                }

                if (!EntityManager.HasBuffer<Efficiency>(propertyRenter.m_Property))
                {
                    continue;
                }

                DynamicBuffer<Efficiency> efficiencyBuffer = EntityManager.GetBuffer<Efficiency>(propertyRenter.m_Property, isReadOnly: true);
                float efficiency = BuildingUtils.GetEfficiency(efficiencyBuffer);
                if (efficiency <= 0f)
                {
                    snapshot.SoftwareOfficeEfficiencyZero++;
                }

                float lackResources = BuildingUtils.GetEfficiencyFactor(efficiencyBuffer, EfficiencyFactor.LackResources);
                if (lackResources <= 0f)
                {
                    snapshot.SoftwareOfficeLackResourcesZero++;
                }
            }
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

        private static bool ShouldLog(DiagnosticSnapshot snapshot)
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
                   snapshot.SoftwareOfficeEfficiencyZero > 0 ||
                   snapshot.SoftwareOfficeLackResourcesZero > 0 ||
                   (Mod.Settings != null && Mod.Settings.VerboseLogging);
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
