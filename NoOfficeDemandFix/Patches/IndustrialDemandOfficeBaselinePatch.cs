using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Game.Simulation;
using HarmonyLib;
using Unity.Collections;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch]
    public static class IndustrialDemandOfficeBaselinePatch
    {
        private static bool s_ActivationLogged;
        private static bool s_DebugSnapshotFailureLogged;
        private static readonly FieldInfo s_FreePropertiesField = AccessTools.Field(typeof(IndustrialDemandSystem), "m_FreeProperties");
        private static readonly FieldInfo s_LastOfficeBuildingDemandField = AccessTools.Field(typeof(IndustrialDemandSystem), "m_LastOfficeBuildingDemand");
        private static readonly FieldInfo s_LastOfficeCompanyDemandField = AccessTools.Field(typeof(IndustrialDemandSystem), "m_LastOfficeCompanyDemand");

        public readonly struct OfficeDemandDebugSnapshot
        {
            public OfficeDemandDebugSnapshot(
                bool hasFreeProperties,
                int[] freePropertiesByResource,
                int lastOfficeCompanyDemand,
                int lastOfficeBuildingDemand)
            {
                HasFreeProperties = hasFreeProperties;
                FreePropertiesByResource = freePropertiesByResource ?? Array.Empty<int>();
                LastOfficeCompanyDemand = lastOfficeCompanyDemand;
                LastOfficeBuildingDemand = lastOfficeBuildingDemand;
            }

            public bool HasFreeProperties { get; }

            public int[] FreePropertiesByResource { get; }

            public int LastOfficeCompanyDemand { get; }

            public int LastOfficeBuildingDemand { get; }
        }

        public static bool Prepare()
        {
            bool patchEnabled = Mod.Settings == null || Mod.Settings.EnableOfficeDemandDirectPatch;
            if (!s_ActivationLogged)
            {
                Mod.log.Info(
                    patchEnabled
                        ? "Industrial demand office baseline direct patch active; replacing the vanilla 1.5f office multiplier inside IndustrialDemandSystem.UpdateIndustrialDemandJob.Execute."
                        : "Industrial demand office baseline direct patch disabled by settings; vanilla 1.5f office multiplier remains active.");
                s_ActivationLogged = true;
            }

            return patchEnabled;
        }

        public static bool TryCaptureDebugSnapshot(IndustrialDemandSystem system, out OfficeDemandDebugSnapshot snapshot)
        {
            snapshot = default;
            if (system == null)
            {
                return false;
            }

            try
            {
                bool hasFreeProperties = false;
                int[] freePropertiesByResource = Array.Empty<int>();
                if (s_FreePropertiesField != null && s_FreePropertiesField.GetValue(system) is NativeArray<int> freeProperties && freeProperties.IsCreated)
                {
                    freePropertiesByResource = freeProperties.ToArray();
                    hasFreeProperties = true;
                }

                snapshot = new OfficeDemandDebugSnapshot(
                    hasFreeProperties,
                    freePropertiesByResource,
                    ReadIntField(s_LastOfficeCompanyDemandField, system, system.officeCompanyDemand),
                    ReadIntField(s_LastOfficeBuildingDemandField, system, system.officeBuildingDemand));
                return true;
            }
            catch (Exception ex)
            {
                if (!s_DebugSnapshotFailureLogged)
                {
                    Mod.log.Error($"Industrial demand office debug snapshot capture failed. Continuing without private demand-field diagnostics. {ex}");
                    s_DebugSnapshotFailureLogged = true;
                }

                return false;
            }
        }

        public static MethodBase TargetMethod()
        {
            Type jobType = typeof(IndustrialDemandSystem).GetNestedType("UpdateIndustrialDemandJob", BindingFlags.NonPublic);
            if (jobType == null)
            {
                throw new MissingMethodException(typeof(IndustrialDemandSystem).FullName, "UpdateIndustrialDemandJob");
            }

            MethodInfo executeMethod = jobType.GetMethod("Execute", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (executeMethod == null)
            {
                throw new MissingMethodException(jobType.FullName, "Execute");
            }

            return executeMethod;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;

            foreach (CodeInstruction instruction in instructions)
            {
                if (!replaced && instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float multiplier && multiplier == 1.5f)
                {
                    instruction.operand = 1f;
                    replaced = true;
                }

                yield return instruction;
            }

            if (!replaced)
            {
                Mod.log.Error("Industrial demand office baseline direct patch did not find the expected 1.5f constant in IndustrialDemandSystem.UpdateIndustrialDemandJob.Execute.");
            }
        }

        private static int ReadIntField(FieldInfo field, object instance, int fallback)
        {
            if (field == null)
            {
                return fallback;
            }

            return field.GetValue(instance) is int value ? value : fallback;
        }
    }

}
