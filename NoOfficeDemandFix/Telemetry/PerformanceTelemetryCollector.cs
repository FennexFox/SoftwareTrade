using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Pathfind;
using UnityEngine;

namespace NoOfficeDemandFix.Telemetry
{
    internal static class PerformanceTelemetryCollector
    {
        private const int kStallDebounceFrames = 1;
        private const string kUnknownScenarioId = "unknown";
        private const string kUnsavedName = "unsaved";

        private static readonly double s_TicksToMilliseconds = 1000d / Stopwatch.Frequency;
        private static readonly string[] s_PathfindActionFieldNames =
        {
            "m_AvailabilityActions",
            "m_CoverageActions",
            "m_CreateActions",
            "m_DeleteActions",
            "m_DensityActions",
            "m_FlowActions",
            "m_PathfindActions",
            "m_TimeActions",
            "m_UpdateActions"
        };

        private static readonly List<PerformanceSummaryRow> s_SummaryRows = new List<PerformanceSummaryRow>();
        private static readonly List<PerformanceStallRow> s_StallRows = new List<PerformanceStallRow>();
        private static readonly List<float> s_WindowLatencySamplesMs = new List<float>(128);
        private static readonly List<float> s_StallLatencySamplesMs = new List<float>(512);

        private static PerformanceRunMetadata s_RunMetadata;
        private static SummaryAccumulator s_Window;
        private static ActiveStallAccumulator s_ActiveStall;
        private static PendingStallCandidate s_PendingStallCandidate;

        private static bool s_RunActive;
        private static bool s_RunFlushed;
        private static double s_ElapsedSec;
        private static string s_KnownSaveName = kUnsavedName;
        private static string s_PendingLoadedSaveName;
        private static int s_ConsecutiveAboveThreshold;
        private static int s_ConsecutiveBelowThreshold;
        private static int s_NextStallId;

        private static long s_FrameSimulationTicks;
        private static long s_FramePathfindTicks;
        private static long s_FrameModTicks;
        private static int s_FrameModEntitiesInspected;
        private static int s_FrameModRepathRequested;
        private static int s_FrameObservedPathQueueLenMax;

        private static FieldInfo[] s_PathfindActionFields;
        private static FieldInfo[] s_PathfindActionItemsFields;
        private static FieldInfo[] s_PathfindActionNextIndexFields;
        private static bool s_PathfindReflectionInitialized;
        private static bool s_PathfindReflectionUnavailableLogged;

        public static bool IsCollecting => s_RunActive && !s_RunFlushed;

        public static string KnownSaveName => string.IsNullOrWhiteSpace(s_KnownSaveName) ? kUnsavedName : s_KnownSaveName;

        public static long BeginTimingScope()
        {
            return IsCollecting ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void SetPendingLoadedSaveName(string saveName)
        {
            if (!string.IsNullOrWhiteSpace(saveName))
            {
                s_PendingLoadedSaveName = saveName.Trim();
            }
        }

        public static void ClearPendingLoadedSaveName()
        {
            s_PendingLoadedSaveName = null;
        }

        public static void PromotePendingLoadedSaveName()
        {
            s_KnownSaveName = string.IsNullOrWhiteSpace(s_PendingLoadedSaveName)
                ? kUnsavedName
                : s_PendingLoadedSaveName;
            s_PendingLoadedSaveName = null;
        }

        public static void BeginRun(PerformanceRunMetadata metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            if (s_RunActive && !s_RunFlushed)
            {
                FlushActiveRun();
            }

            ResetRunState();
            s_RunMetadata = metadata;
            s_RunActive = true;
            s_RunFlushed = false;
        }

        public static void UpdateRunContext(string saveName, string scenarioId)
        {
            if (!IsCollecting || s_RunMetadata == null)
            {
                return;
            }

            if ((string.IsNullOrWhiteSpace(s_RunMetadata.SaveName) || s_RunMetadata.SaveName == kUnsavedName) &&
                !string.IsNullOrWhiteSpace(saveName))
            {
                s_RunMetadata.SaveName = saveName.Trim();
            }

            if ((string.IsNullOrWhiteSpace(s_RunMetadata.ScenarioId) || s_RunMetadata.ScenarioId == kUnknownScenarioId) &&
                !string.IsNullOrWhiteSpace(scenarioId))
            {
                s_RunMetadata.ScenarioId = scenarioId.Trim();
            }
        }

        public static void FlushActiveRun()
        {
            if (!s_RunActive || s_RunFlushed || s_RunMetadata == null)
            {
                return;
            }

            if (s_ActiveStall.IsActive)
            {
                FinalizeActiveStall(s_ElapsedSec);
            }

            // Keep the trailing partial window so session-end flushes do not
            // silently drop the last seconds of a run.
            if (ShouldEmitTrailingSummaryRow())
            {
                EmitSummaryRow(s_ElapsedSec);
            }

            try
            {
                WriteCsvOutputs();
            }
            catch (Exception ex)
            {
                Mod.log.Error($"Performance telemetry flush failed. {ex}");
            }
            finally
            {
                s_RunFlushed = true;
                s_RunActive = false;
                s_RunMetadata = null;
                ResetRuntimeAccumulators();
            }
        }

        public static void RecordSimulationUpdateElapsedTicks(long elapsedTicks)
        {
            if (IsCollecting && elapsedTicks > 0)
            {
                s_FrameSimulationTicks += elapsedTicks;
            }
        }

        public static void RecordPathfindUpdateElapsedTicks(long elapsedTicks)
        {
            if (IsCollecting && elapsedTicks > 0)
            {
                s_FramePathfindTicks += elapsedTicks;
            }
        }

        public static void RecordModUpdateElapsedTicks(long elapsedTicks)
        {
            if (IsCollecting && elapsedTicks > 0)
            {
                s_FrameModTicks += elapsedTicks;
            }
        }

        public static void RecordModActivity(int entitiesInspected, int repathRequested)
        {
            if (!IsCollecting)
            {
                return;
            }

            if (entitiesInspected > 0)
            {
                s_FrameModEntitiesInspected += entitiesInspected;
            }

            if (repathRequested > 0)
            {
                s_FrameModRepathRequested += repathRequested;
            }
        }

        public static void ObservePathQueueLength(int pathQueueLength)
        {
            if (IsCollecting && pathQueueLength > s_FrameObservedPathQueueLenMax)
            {
                s_FrameObservedPathQueueLenMax = pathQueueLength;
            }
        }

        public static int GetCurrentPathQueueLength(PathfindQueueSystem pathfindQueueSystem)
        {
            if (!IsCollecting || pathfindQueueSystem == null || !TryEnsurePathfindReflection())
            {
                return 0;
            }

            try
            {
                int total = 0;
                for (int i = 0; i < s_PathfindActionFields.Length; i++)
                {
                    object actionList = s_PathfindActionFields[i].GetValue(pathfindQueueSystem);
                    if (actionList == null)
                    {
                        continue;
                    }

                    total += GetActionListDepth(actionList, i);
                }

                return total;
            }
            catch (Exception ex)
            {
                DisablePathfindReflectionSampling();
                if (!s_PathfindReflectionUnavailableLogged)
                {
                    Mod.log.Error($"Performance telemetry could not read PathfindQueueSystem internals. Path queue metrics will stay at 0. {ex}");
                    s_PathfindReflectionUnavailableLogged = true;
                }

                return 0;
            }
        }

        public static void RecordFrame(float renderLatencyMs, uint simulationTick, int pathRequestsPendingCount, int currentPathQueueLength)
        {
            if (!IsCollecting)
            {
                ResetFrameInstrumentation();
                return;
            }

            float clampedRenderLatencyMs = Math.Max(0f, renderLatencyMs);
            double frameDurationSec = clampedRenderLatencyMs / 1000d;
            double frameStartSec = s_ElapsedSec;
            double frameEndSec = frameStartSec + frameDurationSec;
            double simulationStepMs = s_FrameSimulationTicks * s_TicksToMilliseconds;
            double pathfindUpdateMs = s_FramePathfindTicks * s_TicksToMilliseconds;
            double modUpdateMs = s_FrameModTicks * s_TicksToMilliseconds;
            int pathQueueLength = Math.Max(currentPathQueueLength, s_FrameObservedPathQueueLenMax);
            bool frameInConfirmedStall = UpdateStallTracking(
                frameStartSec,
                frameEndSec,
                clampedRenderLatencyMs,
                pathQueueLength,
                s_FrameModRepathRequested,
                s_FrameModEntitiesInspected);

            s_Window.FrameCount++;
            s_Window.TotalDurationSec += frameDurationSec;
            s_Window.TotalRenderLatencyMs += clampedRenderLatencyMs;
            s_Window.TotalSimulationStepMs += simulationStepMs;
            s_Window.TotalPathfindUpdateMs += pathfindUpdateMs;
            s_Window.TotalModUpdateMs += modUpdateMs;
            s_Window.ModEntitiesInspectedCount += s_FrameModEntitiesInspected;
            s_Window.ModRepathRequestedCount += s_FrameModRepathRequested;
            s_Window.LastSimulationTick = simulationTick;
            s_Window.LastPathRequestsPendingCount = pathRequestsPendingCount;
            s_Window.PathQueueLenMax = Math.Max(s_Window.PathQueueLenMax, pathQueueLength);
            s_Window.IsStallWindow |= frameInConfirmedStall;
            s_WindowLatencySamplesMs.Add(clampedRenderLatencyMs);

            s_ElapsedSec = frameEndSec;
            if (s_Window.TotalDurationSec >= Math.Max(0.1f, s_RunMetadata.SamplingIntervalSec))
            {
                EmitSummaryRow(frameEndSec);
            }

            ResetFrameInstrumentation();
        }

        private static bool UpdateStallTracking(
            double frameStartSec,
            double frameEndSec,
            float renderLatencyMs,
            int pathQueueLength,
            int modRepathRequested,
            int modEntitiesInspected)
        {
            bool isAboveThreshold = renderLatencyMs >= s_RunMetadata.StallThresholdMs;

            if (s_ActiveStall.IsActive)
            {
                if (isAboveThreshold)
                {
                    AddFrameToActiveStall(renderLatencyMs, pathQueueLength, modRepathRequested, modEntitiesInspected);
                    s_ConsecutiveBelowThreshold = 0;
                    s_ConsecutiveAboveThreshold++;
                    return true;
                }

                s_ConsecutiveBelowThreshold++;
                if (s_ConsecutiveBelowThreshold >= kStallDebounceFrames)
                {
                    FinalizeActiveStall(frameStartSec);
                    return false;
                }

                s_ConsecutiveAboveThreshold = 0;
                return true;
            }

            if (!isAboveThreshold)
            {
                s_ConsecutiveAboveThreshold = 0;
                s_ConsecutiveBelowThreshold = 0;
                s_PendingStallCandidate = default;
                return false;
            }

            s_ConsecutiveAboveThreshold++;
            if (s_ConsecutiveAboveThreshold == 1)
            {
                s_PendingStallCandidate = new PendingStallCandidate
                {
                    HasValue = true,
                    FrameStartSec = frameStartSec,
                    FrameEndSec = frameEndSec,
                    RenderLatencyMs = renderLatencyMs,
                    PathQueueLength = pathQueueLength,
                    ModRepathRequested = modRepathRequested,
                    ModEntitiesInspected = modEntitiesInspected
                };
            }

            if (s_ConsecutiveAboveThreshold >= kStallDebounceFrames)
            {
                StartActiveStallFromPendingCandidate();
                if (s_ConsecutiveAboveThreshold > 1)
                {
                    AddFrameToActiveStall(renderLatencyMs, pathQueueLength, modRepathRequested, modEntitiesInspected);
                }

                return true;
            }

            return false;
        }

        private static void StartActiveStallFromPendingCandidate()
        {
            s_NextStallId++;
            s_ActiveStall = new ActiveStallAccumulator
            {
                IsActive = true,
                StallId = s_NextStallId,
                StallStartSec = s_PendingStallCandidate.HasValue ? s_PendingStallCandidate.FrameStartSec : s_ElapsedSec
            };
            s_StallLatencySamplesMs.Clear();
            s_ConsecutiveBelowThreshold = 0;

            if (s_PendingStallCandidate.HasValue)
            {
                AddFrameToActiveStall(
                    s_PendingStallCandidate.RenderLatencyMs,
                    s_PendingStallCandidate.PathQueueLength,
                    s_PendingStallCandidate.ModRepathRequested,
                    s_PendingStallCandidate.ModEntitiesInspected);
            }

            s_PendingStallCandidate = default;
        }

        private static void AddFrameToActiveStall(float renderLatencyMs, int pathQueueLength, int modRepathRequested, int modEntitiesInspected)
        {
            s_ActiveStall.PeakRenderLatencyMs = Math.Max(s_ActiveStall.PeakRenderLatencyMs, renderLatencyMs);
            s_ActiveStall.PeakPathQueueLen = Math.Max(s_ActiveStall.PeakPathQueueLen, pathQueueLength);
            s_ActiveStall.ModRepathRequestedCount += modRepathRequested;
            s_ActiveStall.ModEntitiesInspectedCount += modEntitiesInspected;
            s_StallLatencySamplesMs.Add(renderLatencyMs);
        }

        private static void FinalizeActiveStall(double stallEndSec)
        {
            if (!s_ActiveStall.IsActive)
            {
                return;
            }

            s_StallRows.Add(new PerformanceStallRow
            {
                RunId = s_RunMetadata.RunId,
                StallId = s_ActiveStall.StallId,
                StallStartSec = s_ActiveStall.StallStartSec,
                StallEndSec = stallEndSec,
                StallDurationSec = Math.Max(0d, stallEndSec - s_ActiveStall.StallStartSec),
                StallPeakRenderLatencyMs = s_ActiveStall.PeakRenderLatencyMs,
                StallP95RenderLatencyMs = CalculatePercentile(s_StallLatencySamplesMs, 0.95d),
                StallPeakPathQueueLen = s_ActiveStall.PeakPathQueueLen,
                StallModRepathRequestedCount = s_ActiveStall.ModRepathRequestedCount,
                StallModEntitiesInspectedCount = s_ActiveStall.ModEntitiesInspectedCount
            });

            s_ActiveStall = default;
            s_StallLatencySamplesMs.Clear();
            s_ConsecutiveAboveThreshold = 0;
            s_ConsecutiveBelowThreshold = 0;
        }

        private static void EmitSummaryRow(double elapsedSec)
        {
            if (s_Window.FrameCount <= 0)
            {
                return;
            }

            double fpsMean = s_Window.TotalRenderLatencyMs > 0d
                ? (s_Window.FrameCount * 1000d) / s_Window.TotalRenderLatencyMs
                : 0d;

            s_SummaryRows.Add(new PerformanceSummaryRow
            {
                RunId = s_RunMetadata.RunId,
                ElapsedSec = elapsedSec,
                SimulationTick = s_Window.LastSimulationTick,
                FpsMean = fpsMean,
                RenderLatencyMeanMs = s_Window.TotalRenderLatencyMs / s_Window.FrameCount,
                RenderLatencyP95Ms = CalculatePercentile(s_WindowLatencySamplesMs, 0.95d),
                SimulationStepMeanMs = s_Window.TotalSimulationStepMs / s_Window.FrameCount,
                PathfindUpdateMeanMs = s_Window.TotalPathfindUpdateMs / s_Window.FrameCount,
                ModUpdateMeanMs = s_Window.TotalModUpdateMs / s_Window.FrameCount,
                ModEntitiesInspectedCount = s_Window.ModEntitiesInspectedCount,
                ModRepathRequestedCount = s_Window.ModRepathRequestedCount,
                // TODO(perf-telemetry): Revisit whether pending backlog should
                // be emitted as a window rollup or demoted to a snapshot-only
                // diagnostic; queue maxima have been the stronger KPI so far.
                PathRequestsPendingCount = s_Window.LastPathRequestsPendingCount,
                PathQueueLenMax = s_Window.PathQueueLenMax,
                IsStallWindow = s_Window.IsStallWindow
            });

            s_Window = default;
            s_WindowLatencySamplesMs.Clear();
        }

        private static bool ShouldEmitTrailingSummaryRow()
        {
            if (s_Window.FrameCount <= 0)
            {
                return false;
            }

            if (s_SummaryRows.Count <= 0)
            {
                return true;
            }

            PerformanceSummaryRow previousRow = s_SummaryRows[s_SummaryRows.Count - 1];
            bool duplicateSimulationTick = s_Window.LastSimulationTick == previousRow.SimulationTick;
            bool noSimulationOrPathfindWork = s_Window.TotalSimulationStepMs <= 0d && s_Window.TotalPathfindUpdateMs <= 0d;
            bool tinyTrailingWindow = s_Window.TotalDurationSec <= 0.01d;
            return !(duplicateSimulationTick && noSimulationOrPathfindWork && tinyTrailingWindow);
        }

        private static double CalculatePercentile(List<float> samples, double percentile)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0d;
            }

            samples.Sort();
            int rawIndex = (int)Math.Ceiling(samples.Count * percentile) - 1;
            int percentileIndex = Math.Max(0, Math.Min(samples.Count - 1, rawIndex));
            return samples[percentileIndex];
        }

        private static void WriteCsvOutputs()
        {
            string outputDirectory = Path.Combine(Application.persistentDataPath, nameof(NoOfficeDemandFix), "perf", s_RunMetadata.RunId);
            Directory.CreateDirectory(outputDirectory);

            string summaryPath = Path.Combine(outputDirectory, "perf_summary.csv");
            string stallPath = Path.Combine(outputDirectory, "perf_stalls.csv");

            using (StreamWriter writer = CreateWriter(summaryPath))
            {
                WriteMetadataBlock(writer, "summary");
                writer.WriteLine("run_id,elapsed_sec,simulation_tick,fps_mean,render_latency_mean_ms,render_latency_p95_ms,simulation_step_mean_ms,pathfind_update_mean_ms,mod_update_mean_ms,mod_entities_inspected_count,mod_repath_requested_count,path_requests_pending_count,path_queue_len_max,is_stall_window");
                for (int i = 0; i < s_SummaryRows.Count; i++)
                {
                    PerformanceSummaryRow row = s_SummaryRows[i];
                    writer.Write(EscapeCsv(row.RunId));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.ElapsedSec));
                    writer.Write(',');
                    writer.Write(row.SimulationTick.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.FpsMean));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.RenderLatencyMeanMs));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.RenderLatencyP95Ms));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.SimulationStepMeanMs));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.PathfindUpdateMeanMs));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.ModUpdateMeanMs));
                    writer.Write(',');
                    writer.Write(row.ModEntitiesInspectedCount.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(row.ModRepathRequestedCount.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(row.PathRequestsPendingCount.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(row.PathQueueLenMax.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(row.IsStallWindow ? "true" : "false");
                    writer.WriteLine();
                }
            }

            using (StreamWriter writer = CreateWriter(stallPath))
            {
                WriteMetadataBlock(writer, "stalls");
                writer.WriteLine("run_id,stall_id,stall_start_sec,stall_end_sec,stall_duration_sec,stall_peak_render_latency_ms,stall_p95_render_latency_ms,stall_peak_path_queue_len,stall_mod_repath_requested_count,stall_mod_entities_inspected_count");
                for (int i = 0; i < s_StallRows.Count; i++)
                {
                    PerformanceStallRow row = s_StallRows[i];
                    writer.Write(EscapeCsv(row.RunId));
                    writer.Write(',');
                    writer.Write(row.StallId.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.StallStartSec));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.StallEndSec));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.StallDurationSec));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.StallPeakRenderLatencyMs));
                    writer.Write(',');
                    writer.Write(FormatDouble(row.StallP95RenderLatencyMs));
                    writer.Write(',');
                    writer.Write(row.StallPeakPathQueueLen.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(row.StallModRepathRequestedCount.ToString(CultureInfo.InvariantCulture));
                    writer.Write(',');
                    writer.Write(row.StallModEntitiesInspectedCount.ToString(CultureInfo.InvariantCulture));
                    writer.WriteLine();
                }
            }
        }

        private static StreamWriter CreateWriter(string path)
        {
            return new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 65536);
        }

        private static void WriteMetadataBlock(TextWriter writer, string fileKind)
        {
            WriteMetadataLine(writer, "telemetry_schema_version", "1");
            WriteMetadataLine(writer, "telemetry_file_kind", SanitizeMetadataValue(fileKind));
            WriteMetadataLine(writer, "run_id", SanitizeMetadataValue(s_RunMetadata.RunId));
            WriteMetadataLine(writer, "run_start_utc", s_RunMetadata.RunStartUtc.ToString("O", CultureInfo.InvariantCulture));
            WriteMetadataLine(writer, "game_build_version", SanitizeMetadataValue(s_RunMetadata.GameBuildVersion));
            WriteMetadataLine(writer, "mod_version", SanitizeMetadataValue(s_RunMetadata.ModVersion));
            WriteMetadataLine(writer, "save_name", SanitizeMetadataValue(s_RunMetadata.SaveName));
            WriteMetadataLine(writer, "scenario_id", SanitizeMetadataValue(s_RunMetadata.ScenarioId));
            WriteMetadataLine(writer, "sampling_interval_sec", FormatDouble(s_RunMetadata.SamplingIntervalSec));
            WriteMetadataLine(writer, "stall_threshold_ms", s_RunMetadata.StallThresholdMs.ToString(CultureInfo.InvariantCulture));
            WriteMetadataLine(writer, "enable_phantom_vacancy_fix", s_RunMetadata.EnablePhantomVacancyFix ? "true" : "false");
            WriteMetadataLine(writer, "enable_outside_connection_virtual_seller_fix", s_RunMetadata.EnableOutsideConnectionVirtualSellerFix ? "true" : "false");
            WriteMetadataLine(writer, "enable_virtual_office_resource_buyer_fix", s_RunMetadata.EnableVirtualOfficeResourceBuyerFix ? "true" : "false");
            WriteMetadataLine(writer, "enable_office_demand_direct_patch", s_RunMetadata.EnableOfficeDemandDirectPatch ? "true" : "false");
        }

        private static void WriteMetadataLine(TextWriter writer, string key, string value)
        {
            writer.Write("# ");
            writer.Write(key);
            writer.Write('=');
            writer.WriteLine(value);
        }

        private static bool TryEnsurePathfindReflection()
        {
            if (s_PathfindReflectionInitialized)
            {
                return s_PathfindActionFields != null &&
                    s_PathfindActionItemsFields != null &&
                    s_PathfindActionNextIndexFields != null;
            }

            s_PathfindReflectionInitialized = true;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type pathfindQueueType = typeof(PathfindQueueSystem);
            // Queue depth is not exposed publicly on this build, so cache the
            // private action-list fields once and reuse them on each sample.
            s_PathfindActionFields = new FieldInfo[s_PathfindActionFieldNames.Length];
            s_PathfindActionItemsFields = new FieldInfo[s_PathfindActionFieldNames.Length];
            s_PathfindActionNextIndexFields = new FieldInfo[s_PathfindActionFieldNames.Length];
            for (int i = 0; i < s_PathfindActionFieldNames.Length; i++)
            {
                s_PathfindActionFields[i] = pathfindQueueType.GetField(s_PathfindActionFieldNames[i], flags);
                if (s_PathfindActionFields[i] != null)
                {
                    s_PathfindActionItemsFields[i] = s_PathfindActionFields[i].FieldType.GetField("m_Items", flags);
                    s_PathfindActionNextIndexFields[i] = s_PathfindActionFields[i].FieldType.GetField("m_NextIndex", flags);
                }
            }

            bool valid = true;
            for (int i = 0; i < s_PathfindActionFields.Length; i++)
            {
                valid &= s_PathfindActionFields[i] != null &&
                    (s_PathfindActionItemsFields[i] != null || s_PathfindActionNextIndexFields[i] != null);
            }

            if (!valid && !s_PathfindReflectionUnavailableLogged)
            {
                DisablePathfindReflectionSampling();
                Mod.log.Error("Performance telemetry could not bind PathfindQueueSystem fields. Path queue metrics will stay at 0.");
                s_PathfindReflectionUnavailableLogged = true;
            }

            return valid;
        }

        private static void DisablePathfindReflectionSampling()
        {
            s_PathfindActionFields = null;
            s_PathfindActionItemsFields = null;
            s_PathfindActionNextIndexFields = null;
            s_PathfindReflectionInitialized = true;
        }

        private static int GetActionListDepth(object actionList, int index)
        {
            FieldInfo nextIndexField = s_PathfindActionNextIndexFields[index];
            if (nextIndexField != null)
            {
                return Math.Max(0, (int)nextIndexField.GetValue(actionList));
            }

            FieldInfo itemsField = s_PathfindActionItemsFields[index];
            if (itemsField != null)
            {
                object items = itemsField.GetValue(actionList);
                if (items is ICollection collection)
                {
                    return Math.Max(0, collection.Count);
                }
            }

            return 0;
        }

        private static void ResetRunState()
        {
            s_SummaryRows.Clear();
            s_StallRows.Clear();
            ResetRuntimeAccumulators();
            s_NextStallId = 0;
            s_ElapsedSec = 0d;
            s_RunActive = false;
            s_RunFlushed = false;
            s_RunMetadata = null;
        }

        private static void ResetRuntimeAccumulators()
        {
            s_Window = default;
            s_WindowLatencySamplesMs.Clear();
            s_ActiveStall = default;
            s_StallLatencySamplesMs.Clear();
            s_PendingStallCandidate = default;
            s_ConsecutiveAboveThreshold = 0;
            s_ConsecutiveBelowThreshold = 0;
            ResetFrameInstrumentation();
        }

        private static void ResetFrameInstrumentation()
        {
            s_FrameSimulationTicks = 0L;
            s_FramePathfindTicks = 0L;
            s_FrameModTicks = 0L;
            s_FrameModEntitiesInspected = 0;
            s_FrameModRepathRequested = 0;
            s_FrameObservedPathQueueLenMax = 0;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string SanitizeMetadataValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private struct SummaryAccumulator
        {
            public int FrameCount;
            public double TotalDurationSec;
            public double TotalRenderLatencyMs;
            public double TotalSimulationStepMs;
            public double TotalPathfindUpdateMs;
            public double TotalModUpdateMs;
            public long ModEntitiesInspectedCount;
            public long ModRepathRequestedCount;
            public uint LastSimulationTick;
            public int LastPathRequestsPendingCount;
            public int PathQueueLenMax;
            public bool IsStallWindow;
        }

        private struct ActiveStallAccumulator
        {
            public bool IsActive;
            public int StallId;
            public double StallStartSec;
            public float PeakRenderLatencyMs;
            public int PeakPathQueueLen;
            public long ModRepathRequestedCount;
            public long ModEntitiesInspectedCount;
        }

        private struct PendingStallCandidate
        {
            public bool HasValue;
            public double FrameStartSec;
            public double FrameEndSec;
            public float RenderLatencyMs;
            public int PathQueueLength;
            public int ModRepathRequested;
            public int ModEntitiesInspected;
        }
    }
}
