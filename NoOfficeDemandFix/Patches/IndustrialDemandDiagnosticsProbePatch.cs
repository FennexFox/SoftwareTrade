using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Economy;
using Game.Simulation;
using HarmonyLib;
using Unity.Collections;
using Unity.Jobs;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch(typeof(IndustrialDemandSystem), "OnUpdate")]
    internal static class IndustrialDemandDiagnosticsProbePatch
    {
        internal readonly struct OfficeResourceDemandEntry
        {
            public OfficeResourceDemandEntry(
                Resource resource,
                int resourceDemand,
                int buildingDemand,
                int companyDemand,
                bool companyDemandKnown,
                int freeProperties,
                bool freePropertiesKnown)
            {
                Resource = resource;
                ResourceDemand = resourceDemand;
                BuildingDemand = buildingDemand;
                CompanyDemand = companyDemand;
                CompanyDemandKnown = companyDemandKnown;
                FreeProperties = freeProperties;
                FreePropertiesKnown = freePropertiesKnown;
            }

            public Resource Resource { get; }
            public int ResourceDemand { get; }
            public int BuildingDemand { get; }
            public int CompanyDemand { get; }
            public bool CompanyDemandKnown { get; }
            public int FreeProperties { get; }
            public bool FreePropertiesKnown { get; }
        }

        internal readonly struct OfficeDemandProbeSnapshot
        {
            public OfficeDemandProbeSnapshot(
                bool captureAvailable,
                bool captureComplete,
                string captureStatus,
                int simulationFrame,
                OfficeResourceDemandEntry[] officeResources)
            {
                CaptureAvailable = captureAvailable;
                CaptureComplete = captureComplete;
                CaptureStatus = captureStatus ?? string.Empty;
                SimulationFrame = simulationFrame;
                OfficeResources = officeResources ?? Array.Empty<OfficeResourceDemandEntry>();
            }

            public bool CaptureAvailable { get; }
            public bool CaptureComplete { get; }
            public string CaptureStatus { get; }
            public int SimulationFrame { get; }
            public OfficeResourceDemandEntry[] OfficeResources { get; }
        }

        private static readonly object s_SnapshotLock = new object();
        private static readonly FieldInfo s_IndustrialCompanyDemandsField =
            AccessTools.Field(typeof(IndustrialDemandSystem), "m_IndustrialCompanyDemands");
        private static readonly FieldInfo s_FreePropertiesField =
            AccessTools.Field(typeof(IndustrialDemandSystem), "m_FreeProperties");

        private static OfficeDemandProbeSnapshot s_LastSnapshot;
        private static bool s_RuntimeFailureLogged;
        private static Resource[] s_OfficeResources;

        internal static bool TryGetLatestSnapshot(out OfficeDemandProbeSnapshot snapshot)
        {
            lock (s_SnapshotLock)
            {
                snapshot = s_LastSnapshot;
                return snapshot.CaptureAvailable;
            }
        }

        internal static void Reset()
        {
            lock (s_SnapshotLock)
            {
                s_LastSnapshot = default;
            }

            s_RuntimeFailureLogged = false;
        }

        private static void Postfix(IndustrialDemandSystem __instance)
        {
            if (Mod.Settings == null || !Mod.Settings.EnableDemandDiagnostics || !Mod.Settings.VerboseLogging)
            {
                return;
            }

            try
            {
                CaptureSnapshot(__instance);
            }
            catch (Exception ex)
            {
                if (s_RuntimeFailureLogged)
                {
                    return;
                }

                Mod.log.Error($"Industrial demand diagnostics probe failed while capturing office-demand internals. Continuing without the supplemental office-demand detail. {ex}");
                s_RuntimeFailureLogged = true;
            }
        }

        private static void CaptureSnapshot(IndustrialDemandSystem system)
        {
            NativeArray<int> resourceDemands = system.GetResourceDemands(out JobHandle resourceDemandDeps);
            resourceDemandDeps.Complete();

            NativeArray<int> buildingDemands = system.GetBuildingDemands(out JobHandle buildingDemandDeps);
            buildingDemandDeps.Complete();

            bool hasCompanyDemands = TryGetNativeArray(system, s_IndustrialCompanyDemandsField, out NativeArray<int> companyDemands);
            bool hasFreeProperties = TryGetNativeArray(system, s_FreePropertiesField, out NativeArray<int> freeProperties);

            Resource[] officeResources = GetOfficeResources();
            OfficeResourceDemandEntry[] officeResourceEntries = new OfficeResourceDemandEntry[officeResources.Length];
            for (int i = 0; i < officeResources.Length; i++)
            {
                Resource resource = officeResources[i];
                int resourceIndex = EconomyUtils.GetResourceIndex(resource);
                int resourceDemand = TryGetArrayValue(resourceDemands, resourceIndex, out int resourceDemandValue)
                    ? resourceDemandValue
                    : 0;
                int buildingDemand = TryGetArrayValue(buildingDemands, resourceIndex, out int buildingDemandValue)
                    ? buildingDemandValue
                    : 0;
                int companyDemandValue = 0;
                bool companyDemandKnown = hasCompanyDemands && TryGetArrayValue(companyDemands, resourceIndex, out companyDemandValue);
                int freePropertiesValue = 0;
                bool freePropertiesKnown = hasFreeProperties && TryGetArrayValue(freeProperties, resourceIndex, out freePropertiesValue);
                officeResourceEntries[i] = new OfficeResourceDemandEntry(
                    resource,
                    resourceDemand,
                    buildingDemand,
                    companyDemandKnown ? companyDemandValue : 0,
                    companyDemandKnown,
                    freePropertiesKnown ? freePropertiesValue : 0,
                    freePropertiesKnown);
            }

            SimulationSystem simulationSystem = system.World?.GetExistingSystemManaged<SimulationSystem>();
            int simulationFrame = simulationSystem != null ? (int)simulationSystem.frameIndex : -1;
            bool captureComplete = hasCompanyDemands && hasFreeProperties;
            string captureStatus = captureComplete
                ? "ok"
                : hasCompanyDemands || hasFreeProperties
                    ? "partial_missing_private_field"
                    : "missing_private_fields";

            lock (s_SnapshotLock)
            {
                s_LastSnapshot = new OfficeDemandProbeSnapshot(
                    captureAvailable: true,
                    captureComplete: captureComplete,
                    captureStatus: captureStatus,
                    simulationFrame: simulationFrame,
                    officeResources: officeResourceEntries);
            }
        }

        private static bool TryGetNativeArray(IndustrialDemandSystem system, FieldInfo field, out NativeArray<int> value)
        {
            value = default;
            if (field == null)
            {
                return false;
            }

            object fieldValue = field.GetValue(system);
            if (fieldValue is not NativeArray<int> nativeArray)
            {
                return false;
            }

            value = nativeArray;
            return nativeArray.IsCreated;
        }

        private static bool TryGetArrayValue(NativeArray<int> values, int index, out int value)
        {
            value = 0;
            if (!values.IsCreated || index < 0 || index >= values.Length)
            {
                return false;
            }

            value = values[index];
            return true;
        }

        private static Resource[] GetOfficeResources()
        {
            if (s_OfficeResources != null)
            {
                return s_OfficeResources;
            }

            List<Resource> officeResources = new List<Resource>();
            Array resources = Enum.GetValues(typeof(Resource));
            for (int i = 0; i < resources.Length; i++)
            {
                if (resources.GetValue(i) is not Resource resource || resource == Resource.NoResource)
                {
                    continue;
                }

                if (EconomyUtils.IsOfficeResource(resource))
                {
                    officeResources.Add(resource);
                }
            }

            officeResources.Sort();
            s_OfficeResources = officeResources.ToArray();
            return s_OfficeResources;
        }
    }
}
