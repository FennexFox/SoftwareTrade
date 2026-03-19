using Game.Simulation;
using HarmonyLib;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch(typeof(OfficeAISystem), "OnUpdate")]
    public static class OfficeAIHotfixPatch
    {
        private static bool s_ActivationLogged;

        public static bool Prefix()
        {
            if (!s_ActivationLogged)
            {
                Mod.log.Info("Office AI hotfix active; replacing OfficeAISystem.OnUpdate to avoid aborting chunk iteration on low office stock.");
                s_ActivationLogged = true;
            }

            return false;
        }
    }
}
