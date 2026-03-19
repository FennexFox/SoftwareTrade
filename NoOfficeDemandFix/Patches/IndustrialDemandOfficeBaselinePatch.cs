using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Game.Simulation;
using HarmonyLib;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch]
    public static class IndustrialDemandOfficeBaselinePatch
    {
        private static bool s_ActivationLogged;

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
    }
}
