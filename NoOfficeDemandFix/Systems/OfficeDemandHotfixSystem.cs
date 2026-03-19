using Colossal.Collections;
using Game;
using Game.City;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class OfficeDemandHotfixSystem : GameSystemBase
    {
        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, NativeArray<int>> s_FreePropertiesRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, NativeArray<int>>("m_FreeProperties");

        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, NativeValue<int>> s_OfficeCompanyDemandRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, NativeValue<int>>("m_OfficeCompanyDemand");

        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, NativeValue<int>> s_OfficeBuildingDemandRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, NativeValue<int>>("m_OfficeBuildingDemand");

        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, float> s_IndustrialOfficeTaxEffectDemandOffsetRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, float>("m_IndustrialOfficeTaxEffectDemandOffset");

        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, bool> s_UnlimitedDemandRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, bool>("m_UnlimitedDemand");

        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, int> s_LastOfficeCompanyDemandRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, int>("m_LastOfficeCompanyDemand");

        private static readonly AccessTools.FieldRef<IndustrialDemandSystem, int> s_LastOfficeBuildingDemandRef =
            AccessTools.FieldRefAccess<IndustrialDemandSystem, int>("m_LastOfficeBuildingDemand");

        private CountCompanyDataSystem m_CountCompanyDataSystem;
        private CountHouseholdDataSystem m_CountHouseholdDataSystem;
        private CountWorkplacesSystem m_CountWorkplacesSystem;
        private CitySystem m_CitySystem;
        private EntityQuery m_DemandParameterQuery;
        private IndustrialDemandSystem m_IndustrialDemandSystem;
        private EntityQuery m_OfficePropertyQuery;
        private ResourceSystem m_ResourceSystem;
        private TaxSystem m_TaxSystem;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_IndustrialDemandSystem = World.GetOrCreateSystemManaged<IndustrialDemandSystem>();
            m_CountCompanyDataSystem = World.GetOrCreateSystemManaged<CountCompanyDataSystem>();
            m_CountHouseholdDataSystem = World.GetOrCreateSystemManaged<CountHouseholdDataSystem>();
            m_CountWorkplacesSystem = World.GetOrCreateSystemManaged<CountWorkplacesSystem>();
            m_TaxSystem = World.GetOrCreateSystemManaged<TaxSystem>();
            m_ResourceSystem = World.GetOrCreateSystemManaged<ResourceSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();

            m_DemandParameterQuery = GetEntityQuery(ComponentType.ReadOnly<DemandParameterData>());
            m_OfficePropertyQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Buildings.OfficeProperty>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Game.Buildings.Abandoned>(),
                ComponentType.Exclude<Game.Common.Destroyed>(),
                ComponentType.Exclude<Game.Common.Deleted>(),
                ComponentType.Exclude<Game.Tools.Temp>(),
                ComponentType.Exclude<Game.Buildings.Condemned>());

            RequireForUpdate(m_DemandParameterQuery);
        }

        [Preserve]
        protected override void OnUpdate()
        {
            NativeArray<int> officeFactors = m_IndustrialDemandSystem.GetOfficeDemandFactors(out JobHandle officeDeps);
            NativeArray<int> companyDemands = m_IndustrialDemandSystem.GetResourceDemands(out JobHandle companyDemandDeps);
            NativeArray<int> buildingDemands = m_IndustrialDemandSystem.GetBuildingDemands(out JobHandle buildingDemandDeps);
            NativeArray<int> resourceDemands = m_IndustrialDemandSystem.GetIndustrialResourceDemands(out JobHandle resourceDemandDeps);
            CountCompanyDataSystem.IndustrialCompanyDatas industrialCompanyDatas = m_CountCompanyDataSystem.GetIndustrialCompanyDatas(out JobHandle companyDataDeps);
            NativeArray<int> householdDemands = m_CountHouseholdDataSystem.GetResourceNeeds(out JobHandle householdDemandDeps);

            JobHandle combinedDeps = JobHandle.CombineDependencies(
                JobHandle.CombineDependencies(officeDeps, companyDemandDeps),
                JobHandle.CombineDependencies(buildingDemandDeps, resourceDemandDeps),
                JobHandle.CombineDependencies(companyDataDeps, householdDemandDeps));
            combinedDeps.Complete();

            DemandParameterData demandParameters = m_DemandParameterQuery.GetSingleton<DemandParameterData>();
            NativeArray<int> employableByEducation = m_CountHouseholdDataSystem.GetEmployables();
            Workplaces freeWorkplaces = m_CountWorkplacesSystem.GetFreeWorkplaces();
            NativeArray<int> taxRates = m_TaxSystem.GetTaxRates();
            ResourcePrefabs resourcePrefabs = m_ResourceSystem.GetPrefabs();
            ComponentLookup<ResourceData> resourceDatas = GetComponentLookup<ResourceData>(isReadOnly: true);
            BufferLookup<CityModifier> cityModifiers = GetBufferLookup<CityModifier>(isReadOnly: true);
            ComponentLookup<Population> populations = GetComponentLookup<Population>(isReadOnly: true);

            ref NativeArray<int> freeProperties = ref s_FreePropertiesRef(m_IndustrialDemandSystem);
            ref NativeValue<int> officeCompanyDemand = ref s_OfficeCompanyDemandRef(m_IndustrialDemandSystem);
            ref NativeValue<int> officeBuildingDemand = ref s_OfficeBuildingDemandRef(m_IndustrialDemandSystem);
            float taxEffectOffset = s_IndustrialOfficeTaxEffectDemandOffsetRef(m_IndustrialDemandSystem);
            bool unlimitedDemand = s_UnlimitedDemandRef(m_IndustrialDemandSystem);
            bool previousOfficeBuildingDemandPositive = s_LastOfficeBuildingDemandRef(m_IndustrialDemandSystem) > 0;

            DynamicBuffer<CityModifier> modifiers = default;
            bool hasModifiers = cityModifiers.HasBuffer(m_CitySystem.City);
            if (hasModifiers)
            {
                modifiers = cityModifiers[m_CitySystem.City];
            }

            officeFactors.Fill(0);
            officeCompanyDemand.value = 0;
            officeBuildingDemand.value = 0;

            int officeResourceCount = 0;
            ResourceIterator iterator = ResourceIterator.GetIterator();
            while (iterator.Next())
            {
                if (!EconomyUtils.IsOfficeResource(iterator.resource))
                {
                    continue;
                }

                int resourceIndex = EconomyUtils.GetResourceIndex(iterator.resource);
                int baseDemandDelta = householdDemands[resourceIndex] + industrialCompanyDatas.m_Demand[resourceIndex];
                resourceDemands[resourceIndex] = math.max(0, resourceDemands[resourceIndex] - baseDemandDelta);

                if (!resourceDatas.HasComponent(resourcePrefabs[iterator.resource]))
                {
                    companyDemands[resourceIndex] = 0;
                    buildingDemands[resourceIndex] = 0;
                    continue;
                }

                ResourceData resourceData = resourceDatas[resourcePrefabs[iterator.resource]];
                if (!resourceData.m_IsProduceable || resourceData.m_Weight != 0f)
                {
                    continue;
                }

                float value = demandParameters.m_IndustrialBaseDemand;
                if (iterator.resource == Resource.Software && hasModifiers)
                {
                    CityUtils.ApplyModifier(ref value, modifiers, CityModifierType.OfficeSoftwareDemand);
                }

                float demandGap = (1f + (float)resourceDemands[resourceIndex] - (float)industrialCompanyDatas.m_Production[resourceIndex]) / ((float)resourceDemands[resourceIndex] + 1f);
                int officeTaxRate = TaxSystem.GetOfficeTaxRate(iterator.resource, taxRates);
                float officeTaxEffect = demandParameters.m_TaxEffect.z * -0.05f * ((float)officeTaxRate - 10f);
                officeTaxEffect += taxEffectOffset;
                float taxContribution = 100f * officeTaxEffect;

                int educatedWorkforceEffect = 0;
                float neutralUnemployment = demandParameters.m_NeutralUnemployment / 100f;
                for (int educationLevel = 2; educationLevel < 5; educationLevel++)
                {
                    educatedWorkforceEffect += (int)((float)employableByEducation[educationLevel] * (1f - neutralUnemployment)) - freeWorkplaces[educationLevel];
                }

                float localDemandContribution = 50f * math.max(0f, value * demandGap);
                if (taxContribution > 0f)
                {
                    educatedWorkforceEffect = (int)MapAndClaimWorkforceEffect(
                        educatedWorkforceEffect,
                        0f - math.max(10f + taxContribution, 10f),
                        10f);
                }
                else
                {
                    educatedWorkforceEffect = math.clamp(educatedWorkforceEffect, -10, 10);
                }

                int correctedCompanyDemand = localDemandContribution > 0f
                    ? Mathf.RoundToInt(localDemandContribution + taxContribution + (float)educatedWorkforceEffect)
                    : 0;

                correctedCompanyDemand = math.clamp(correctedCompanyDemand, 0, 100);
                companyDemands[resourceIndex] = correctedCompanyDemand;
                officeCompanyDemand.value += correctedCompanyDemand;
                officeResourceCount++;

                if (resourceDemands[resourceIndex] > 0 && correctedCompanyDemand > 0)
                {
                    buildingDemands[resourceIndex] = (freeProperties[resourceIndex] - industrialCompanyDatas.m_ProductionPropertyless[resourceIndex] <= 0) ? 50 : 0;
                    officeBuildingDemand.value += correctedCompanyDemand;
                }
                else
                {
                    buildingDemands[resourceIndex] = 0;
                }

                if (!previousOfficeBuildingDemandPositive || (buildingDemands[resourceIndex] > 0 && correctedCompanyDemand > 0))
                {
                    officeFactors[(int)DemandFactor.EducatedWorkforce] += educatedWorkforceEffect;
                    officeFactors[(int)DemandFactor.LocalDemand] += (int)localDemandContribution;
                    officeFactors[(int)DemandFactor.Taxes] += (int)taxContribution;
                    officeFactors[(int)DemandFactor.EmptyBuildings] += buildingDemands[resourceIndex];
                }
            }

            officeFactors[(int)DemandFactor.LocalDemand] = officeFactors[(int)DemandFactor.LocalDemand] == 0 ? -1 : officeFactors[(int)DemandFactor.LocalDemand];
            officeFactors[(int)DemandFactor.EmptyBuildings] = officeFactors[(int)DemandFactor.EmptyBuildings] == 0 ? -1 : officeFactors[(int)DemandFactor.EmptyBuildings];

            if (populations.HasComponent(m_CitySystem.City) && populations[m_CitySystem.City].m_Population <= 0)
            {
                officeFactors[(int)DemandFactor.LocalDemand] = 0;
            }

            if (m_OfficePropertyQuery.IsEmptyIgnoreFilter)
            {
                officeFactors[(int)DemandFactor.BuildingDemand] = officeFactors[(int)DemandFactor.EmptyBuildings];
                officeFactors[(int)DemandFactor.EmptyBuildings] = 0;
            }

            officeCompanyDemand.value = officeResourceCount > 0
                ? officeCompanyDemand.value * (2 * officeCompanyDemand.value / officeResourceCount)
                : 0;
            officeBuildingDemand.value = math.clamp(officeBuildingDemand.value, 0, 100);
            if (unlimitedDemand)
            {
                officeBuildingDemand.value = 100;
            }

            s_LastOfficeCompanyDemandRef(m_IndustrialDemandSystem) = officeCompanyDemand.value;
            s_LastOfficeBuildingDemandRef(m_IndustrialDemandSystem) = officeBuildingDemand.value;
        }

        private static float MapAndClaimWorkforceEffect(float value, float min, float max)
        {
            if (value < 0f)
            {
                float clamped = math.unlerp(-2000f, 0f, value);
                clamped = math.clamp(clamped, 0f, 1f);
                return math.lerp(min, 0f, clamped);
            }

            float clampedPositive = math.unlerp(0f, 20f, value);
            clampedPositive = math.clamp(clampedPositive, 0f, 1f);
            return math.lerp(0f, max, clampedPositive);
        }
    }
}
