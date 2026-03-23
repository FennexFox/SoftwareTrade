using System;
using System.Diagnostics;
using System.Globalization;
using Colossal.Serialization.Entities;
using Game;
using Game.Pathfind;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using NoOfficeDemandFix.Telemetry;
using UnityEngine.Scripting;

namespace NoOfficeDemandFix.Systems
{
    [Preserve]
    public partial class PerformanceTelemetrySystem : GameSystemBase
    {
        private const int kDisabledPollInterval = 8;
        private const string kUnknownScenarioId = "unknown";

        private SimulationSystem m_SimulationSystem;
        private PathfindQueueSystem m_PathfindQueueSystem;
        private PathfindSetupSystem m_PathfindSetupSystem;
        private MapMetadataSystem m_MapMetadataSystem;
        private TelemetrySettingsState m_LastSettingsState;
        private bool m_LastTelemetryEnabled;
        private bool m_HasFrameTimestamp;
        private long m_LastFrameTimestamp;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            if (phase == SystemUpdatePhase.GameSimulation)
            {
                return 1;
            }

            return base.GetUpdateInterval(phase);
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PathfindSetupSystem = World.GetOrCreateSystemManaged<PathfindSetupSystem>();
            m_PathfindQueueSystem = World.GetOrCreateSystemManaged<PathfindQueueSystem>();
            m_MapMetadataSystem = World.GetExistingSystemManaged<MapMetadataSystem>();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            // Session-scoped telemetry should flush here instead of waiting for
            // IMod.OnDispose(), which only runs on full mod-manager shutdown.
            PerformanceTelemetryCollector.FlushActiveRun();
            // New or unsaved sessions must not inherit the previous save label.
            PerformanceTelemetryCollector.ResetKnownSaveName();
            m_HasFrameTimestamp = false;
            m_LastTelemetryEnabled = false;
            m_LastSettingsState = default;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            m_HasFrameTimestamp = false;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            bool telemetryEnabled = IsTelemetryEnabled();
            if (!telemetryEnabled)
            {
                if (!m_LastTelemetryEnabled &&
                    (m_SimulationSystem.frameIndex % (uint)kDisabledPollInterval) != 0u)
                {
                    return;
                }

                if (m_LastTelemetryEnabled)
                {
                    PerformanceTelemetryCollector.FlushActiveRun();
                }

                m_LastTelemetryEnabled = false;
                m_LastSettingsState = default;
                m_HasFrameTimestamp = false;
                return;
            }

            TelemetrySettingsState settingsState = CaptureSettingsState();
            bool runStateChanged = !m_LastTelemetryEnabled || !settingsState.Equals(m_LastSettingsState);
            if (runStateChanged)
            {
                PerformanceTelemetryCollector.FlushActiveRun();
                PerformanceTelemetryCollector.BeginRun(CreateRunMetadata(settingsState));
                PerformanceTelemetryCollector.UpdateRunContext(
                    PerformanceTelemetryCollector.KnownSaveName,
                    GetScenarioId());
                m_HasFrameTimestamp = false;
            }
            else
            {
                PerformanceTelemetryCollector.UpdateRunContext(
                    PerformanceTelemetryCollector.KnownSaveName,
                    GetScenarioId());
            }

            long now = Stopwatch.GetTimestamp();
            if (!m_HasFrameTimestamp)
            {
                m_LastFrameTimestamp = now;
                m_HasFrameTimestamp = true;
                m_LastTelemetryEnabled = true;
                m_LastSettingsState = settingsState;
                return;
            }

            float renderLatencyMs = (float)((now - m_LastFrameTimestamp) * 1000d / Stopwatch.Frequency);
            m_LastFrameTimestamp = now;

            int pendingRequestCount = m_PathfindSetupSystem != null ? m_PathfindSetupSystem.pendingRequestCount : 0;
            int pathQueueLength = PerformanceTelemetryCollector.GetCurrentPathQueueLength(m_PathfindQueueSystem);
            PerformanceTelemetryCollector.RecordFrame(
                renderLatencyMs,
                m_SimulationSystem.frameIndex,
                pendingRequestCount,
                pathQueueLength);

            m_LastTelemetryEnabled = true;
            m_LastSettingsState = settingsState;
        }

        private static bool IsTelemetryEnabled()
        {
            return Mod.Settings != null && Mod.Settings.EnablePerformanceTelemetry;
        }

        private string GetScenarioId()
        {
            if (m_MapMetadataSystem == null)
            {
                m_MapMetadataSystem = World.GetExistingSystemManaged<MapMetadataSystem>();
            }

            string mapName = m_MapMetadataSystem != null ? m_MapMetadataSystem.mapName : null;
            return string.IsNullOrWhiteSpace(mapName) ? kUnknownScenarioId : mapName.Trim();
        }

        private static PerformanceRunMetadata CreateRunMetadata(TelemetrySettingsState settingsState)
        {
            return new PerformanceRunMetadata
            {
                RunId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture),
                RunStartUtc = DateTime.UtcNow,
                GameBuildVersion = Game.Version.current.fullVersion,
                ModVersion = Mod.ModVersion,
                SaveName = PerformanceTelemetryCollector.KnownSaveName,
                ScenarioId = kUnknownScenarioId,
                SamplingIntervalSec = settingsState.PerformanceTelemetrySamplingIntervalSec,
                StallThresholdMs = settingsState.PerformanceTelemetryStallThresholdMs,
                EnablePhantomVacancyFix = settingsState.EnablePhantomVacancyFix,
                EnableOutsideConnectionVirtualSellerFix = settingsState.EnableOutsideConnectionVirtualSellerFix,
                EnableVirtualOfficeResourceBuyerFix = settingsState.EnableVirtualOfficeResourceBuyerFix,
                EnableOfficeDemandDirectPatch = settingsState.EnableOfficeDemandDirectPatch
            };
        }

        private static TelemetrySettingsState CaptureSettingsState()
        {
            return new TelemetrySettingsState(
                Mod.Settings.EnablePhantomVacancyFix,
                Mod.Settings.EnableOutsideConnectionVirtualSellerFix,
                Mod.Settings.EnableVirtualOfficeResourceBuyerFix,
                Mod.Settings.EnableOfficeDemandDirectPatch,
                Mod.Settings.EnablePerformanceTelemetry,
                Mod.Settings.PerformanceTelemetrySamplingIntervalSec,
                Mod.Settings.PerformanceTelemetryStallThresholdMs);
        }

        private readonly struct TelemetrySettingsState : IEquatable<TelemetrySettingsState>
        {
            public TelemetrySettingsState(
                bool enablePhantomVacancyFix,
                bool enableOutsideConnectionVirtualSellerFix,
                bool enableVirtualOfficeResourceBuyerFix,
                bool enableOfficeDemandDirectPatch,
                bool enablePerformanceTelemetry,
                float performanceTelemetrySamplingIntervalSec,
                int performanceTelemetryStallThresholdMs)
            {
                EnablePhantomVacancyFix = enablePhantomVacancyFix;
                EnableOutsideConnectionVirtualSellerFix = enableOutsideConnectionVirtualSellerFix;
                EnableVirtualOfficeResourceBuyerFix = enableVirtualOfficeResourceBuyerFix;
                EnableOfficeDemandDirectPatch = enableOfficeDemandDirectPatch;
                EnablePerformanceTelemetry = enablePerformanceTelemetry;
                PerformanceTelemetrySamplingIntervalSec = performanceTelemetrySamplingIntervalSec;
                PerformanceTelemetryStallThresholdMs = performanceTelemetryStallThresholdMs;
            }

            public bool EnablePhantomVacancyFix { get; }
            public bool EnableOutsideConnectionVirtualSellerFix { get; }
            public bool EnableVirtualOfficeResourceBuyerFix { get; }
            public bool EnableOfficeDemandDirectPatch { get; }
            public bool EnablePerformanceTelemetry { get; }
            public float PerformanceTelemetrySamplingIntervalSec { get; }
            public int PerformanceTelemetryStallThresholdMs { get; }

            public bool Equals(TelemetrySettingsState other)
            {
                return EnablePhantomVacancyFix == other.EnablePhantomVacancyFix &&
                       EnableOutsideConnectionVirtualSellerFix == other.EnableOutsideConnectionVirtualSellerFix &&
                       EnableVirtualOfficeResourceBuyerFix == other.EnableVirtualOfficeResourceBuyerFix &&
                       EnableOfficeDemandDirectPatch == other.EnableOfficeDemandDirectPatch &&
                       EnablePerformanceTelemetry == other.EnablePerformanceTelemetry &&
                       Math.Abs(PerformanceTelemetrySamplingIntervalSec - other.PerformanceTelemetrySamplingIntervalSec) < 0.0001f &&
                       PerformanceTelemetryStallThresholdMs == other.PerformanceTelemetryStallThresholdMs;
            }

            public override bool Equals(object obj)
            {
                return obj is TelemetrySettingsState other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    EnablePhantomVacancyFix,
                    EnableOutsideConnectionVirtualSellerFix,
                    EnableVirtualOfficeResourceBuyerFix,
                    EnableOfficeDemandDirectPatch,
                    EnablePerformanceTelemetry,
                    PerformanceTelemetrySamplingIntervalSec,
                    PerformanceTelemetryStallThresholdMs);
            }
        }
    }
}
