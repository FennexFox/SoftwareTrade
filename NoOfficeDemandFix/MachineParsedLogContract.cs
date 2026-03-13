using System.Collections.Generic;

namespace NoOfficeDemandFix
{
    internal static class MachineParsedLogContract
    {
        // raw_log_automation.py depends on these stable prefixes.
        // If one changes, update the Python parser constants and fixtures in the same change.
        // observation_window(...) is reserved for scheduled sample emissions.
        // TODO(next PR): add a separate anomaly log contract instead of overloading observation_window(...)
        // if we need ad-hoc anomaly emissions outside the scheduled cadence.
        // TODO(next PR): add generic observed-rate time-scaling metadata if maintainers need
        // more provenance than the runtime TimeSystem clock source; do not couple this contract
        // to one specific time-scaling mod name.
        public const string DiagnosticsObservationPrefix = "softwareEvidenceDiagnostics observation_window(";
        public const string DiagnosticsDetailPrefix = "softwareEvidenceDiagnostics detail(";
        public const string PhantomVacancyCorrectionPrefix = "Signature phantom vacancy guard corrected";
        public const string OfficeResourcePatchAppliedPrefix = "Office resource storage patch applied for the current load.";

        public const string ScheduledObservationKind = "scheduled";
        public const string RuntimeTimeSystemClockSource = "runtime_time_system";
        public const string DisplayedClockSource = "displayed_clock";
        public const string SimulationFallbackClockSource = "simulation_fallback";
        public const string FreeSoftwareOfficePropertiesDetailType = "freeSoftwareOfficeProperties";
        public const string OnMarketOfficePropertiesDetailType = "onMarketOfficeProperties";
        public const string SoftwareOfficeStatesDetailType = "softwareOfficeStates";
        public const string SoftwareTradeLifecycleDetailType = "softwareTradeLifecycle";
        public const string SoftwareVirtualResolutionProbeDetailType = "softwareVirtualResolutionProbe";

        public static string FormatObservationWindow(
            string sessionId,
            int runId,
            int startDay,
            int endDay,
            int startSampleIndex,
            int endSampleIndex,
            int sampleDay,
            int sampleIndex,
            int sampleSlot,
            int samplesPerDay,
            int sampleCount,
            string observationKind,
            int skippedSampleSlots,
            string clockSource,
            string trigger,
            string settingsSnapshot,
            string patchState,
            string diagnosticCounters,
            string topFactors)
        {
            return
                $"{DiagnosticsObservationPrefix}session_id={sessionId}, run_id={runId}, start_day={startDay}, end_day={endDay}, start_sample_index={startSampleIndex}, end_sample_index={endSampleIndex}, sample_day={sampleDay}, sample_index={sampleIndex}, sample_slot={sampleSlot}, samples_per_day={samplesPerDay}, sample_count={sampleCount}, observation_kind={observationKind}, skipped_sample_slots={skippedSampleSlots}, clock_source={clockSource}, trigger={trigger}); " +
                $"environment(settings={settingsSnapshot}, patch_state={patchState}); " +
                $"diagnostic_counters({diagnosticCounters}); " +
                $"diagnostic_context(topFactors=[{topFactors}])";
        }

        public static string FormatDetail(
            string sessionId,
            int runId,
            int observationEndDay,
            int observationEndSampleIndex,
            string detailType,
            string values)
        {
            return
                $"{DiagnosticsDetailPrefix}session_id={sessionId}, run_id={runId}, observation_end_day={observationEndDay}, observation_end_sample_index={observationEndSampleIndex}, detail_type={detailType}, values={values})";
        }

        public static string FormatOfficeResourcePatchApplied(int patchedOutsideConnections, int patchedCargoStations)
        {
            return $"{OfficeResourcePatchAppliedPrefix} Outside connections: {patchedOutsideConnections}, cargo stations: {patchedCargoStations}.";
        }

        public static string FormatPhantomVacancyCorrection(
            string propertyType,
            string propertyEntity,
            string prefabLabel,
            IEnumerable<string> removedComponents)
        {
            return
                $"{PhantomVacancyCorrectionPrefix} {propertyType} property {propertyEntity} " +
                $"prefab={prefabLabel} removed=[{string.Join(", ", removedComponents)}]";
        }
    }
}
