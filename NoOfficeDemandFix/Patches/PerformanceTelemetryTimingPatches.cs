using Game.Pathfind;
using Game.Simulation;
using HarmonyLib;
using NoOfficeDemandFix.Telemetry;

namespace NoOfficeDemandFix.Patches
{
    [HarmonyPatch(typeof(SimulationSystem), "OnUpdate")]
    public static class SimulationSystemPerformanceTelemetryPatch
    {
        public static void Prefix(out long __state)
        {
            __state = PerformanceTelemetryCollector.BeginTimingScope();
        }

        public static void Postfix(long __state)
        {
            if (__state != 0L)
            {
                PerformanceTelemetryCollector.RecordSimulationUpdateElapsedTicks(System.Diagnostics.Stopwatch.GetTimestamp() - __state);
            }
        }
    }

    [HarmonyPatch(typeof(PathfindSetupSystem), "OnUpdate")]
    public static class PathfindSetupSystemPerformanceTelemetryPatch
    {
        public static void Prefix(out long __state)
        {
            __state = PerformanceTelemetryCollector.BeginTimingScope();
        }

        public static void Postfix(long __state)
        {
            if (__state != 0L)
            {
                PerformanceTelemetryCollector.RecordPathfindUpdateElapsedTicks(System.Diagnostics.Stopwatch.GetTimestamp() - __state);
            }
        }
    }

    [HarmonyPatch(typeof(PathfindQueueSystem), "OnUpdate")]
    public static class PathfindQueueSystemPerformanceTelemetryPatch
    {
        public static void Prefix(PathfindQueueSystem __instance, out long __state)
        {
            __state = PerformanceTelemetryCollector.BeginTimingScope();
            if (__state != 0L)
            {
                PerformanceTelemetryCollector.ObservePathQueueLength(
                    PerformanceTelemetryCollector.GetCurrentPathQueueLength(__instance));
            }
        }

        public static void Postfix(long __state)
        {
            if (__state != 0L)
            {
                PerformanceTelemetryCollector.RecordPathfindUpdateElapsedTicks(System.Diagnostics.Stopwatch.GetTimestamp() - __state);
            }
        }
    }
}
