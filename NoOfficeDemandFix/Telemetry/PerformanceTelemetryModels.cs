using System;

namespace NoOfficeDemandFix.Telemetry
{
    internal sealed class PerformanceRunMetadata
    {
        public string RunId;
        public DateTime RunStartUtc;
        public string GameBuildVersion;
        public string ModVersion;
        public string SaveName;
        public string ScenarioId;
        public float SamplingIntervalSec;
        public int StallThresholdMs;
        public bool EnablePhantomVacancyFix;
        public bool EnableOutsideConnectionVirtualSellerFix;
        public bool EnableVirtualOfficeResourceBuyerFix;
        public bool EnableOfficeDemandDirectPatch;
    }

    internal struct PerformanceSummaryRow
    {
        public string RunId;
        public double ElapsedSec;
        public uint SimulationTick;
        public double FpsMean;
        public double RenderLatencyMeanMs;
        public double RenderLatencyP95Ms;
        public double SimulationUpdateRateMean;
        public double SimulationUpdateIntervalMeanMs;
        public double SimulationUpdateIntervalP95Ms;
        public double SimulationStepMeanMs;
        public double PathfindUpdateMeanMs;
        public double ModUpdateMeanMs;
        public long ModEntitiesInspectedCount;
        public long ModRepathRequestedCount;
        public int PathRequestsPendingCount;
        public int PathQueueLenMax;
        public bool IsStallWindow;
    }

    internal struct PerformanceStallRow
    {
        public string RunId;
        public int StallId;
        public double StallStartSec;
        public double StallEndSec;
        public double StallDurationSec;
        public double StallPeakRenderLatencyMs;
        public double StallP95RenderLatencyMs;
        public int StallPeakPathQueueLen;
        public long StallModRepathRequestedCount;
        public long StallModEntitiesInspectedCount;
    }
}
