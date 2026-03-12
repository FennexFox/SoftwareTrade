from pathlib import Path
import sys
import textwrap
import unittest
from unittest import mock


SCRIPTS_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = SCRIPTS_ROOT.parents[1]
sys.path.insert(0, str(SCRIPTS_ROOT))

import raw_log_automation as automation  # noqa: E402


CURRENT_BRANCH_LOG = textwrap.dedent(
    """
    [2026-03-10 14:28:15,189] [INFO]  Office resource storage patch applied. Outside connections: 6, cargo stations: 28.
    [2026-03-10 14:30:41,511] [INFO]  Signature phantom vacancy guard corrected office property 394316:1 prefab="EE_OfficeSignature02" (36377:1) removed=[PropertyOnMarket]
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T052953590Z, run_id=1, start_day=20, end_day=22, start_sample_index=145, end_sample_index=153, sample_day=22, sample_index=153, sample_slot=1, samples_per_day=2, sample_count=9, trigger=suspicious_state); environment(settings=EnableTradePatch:True,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=14196, emptyBuildings=150, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=925211, resourceDemand=411328, companies=27, propertyless=1); electronics(resourceProduction=109125, resourceDemand=351810, companies=11, propertyless=2); softwareProducerOffices(total=27, propertyless=1, efficiencyZero=10, lackResourcesZero=10); softwareConsumerOffices(total=28, propertyless=3, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0)); diagnostic_context(topFactors=[EmptyBuildings=150, Taxes=100, LocalDemand=58, EducatedWorkforce=30])
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T052953590Z, run_id=1, observation_end_day=22, observation_end_sample_index=153, detail_type=softwareOfficeStates, values=role=producer, company=524397:1, prefab="Office_SoftwareCompany" (364:1), property=276428:1, output=Software, outputStock=0, input1=Electronics(stock=0, buyCost=0.89), efficiency=0, lackResources=0)
    """
).strip()

MULTI_OBSERVATION_LOG = textwrap.dedent(
    """
    [2026-03-10 14:28:15,189] [INFO]  Office resource storage patch applied. Outside connections: 6, cargo stations: 28.
    [2026-03-10 14:30:41,511] [INFO]  Signature phantom vacancy guard corrected office property 394316:1 prefab="EE_OfficeSignature02" (36377:1) removed=[PropertyOnMarket]
    [2026-03-10 15:10:00,000] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T130639590Z, run_id=1, start_day=20, end_day=20, start_sample_index=145, end_sample_index=145, sample_day=20, sample_index=145, sample_slot=1, samples_per_day=7, sample_count=1, trigger=capture_stable_evidence); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:7,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=57, company=1596, emptyBuildings=50, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=925211, resourceDemand=411328, companies=27, propertyless=1); electronics(resourceProduction=109125, resourceDemand=351810, companies=11, propertyless=2); softwareProducerOffices(total=27, propertyless=1, efficiencyZero=0, lackResourcesZero=0); softwareConsumerOffices(total=28, propertyless=3, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0)); diagnostic_context(topFactors=[EmptyBuildings=50, Taxes=100, LocalDemand=58, EducatedWorkforce=30])
    [2026-03-10 15:20:00,000] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T130639590Z, run_id=1, start_day=20, end_day=21, start_sample_index=145, end_sample_index=152, sample_day=21, sample_index=152, sample_slot=6, samples_per_day=7, sample_count=8, trigger=suspicious_state); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:7,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=15225, emptyBuildings=100, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=1044875, resourceDemand=471676, companies=26, propertyless=1); electronics(resourceProduction=226375, resourceDemand=391863, companies=11, propertyless=2); softwareProducerOffices(total=26, propertyless=1, efficiencyZero=2, lackResourcesZero=2); softwareConsumerOffices(total=29, propertyless=3, efficiencyZero=24, lackResourcesZero=0, softwareInputZero=24)); diagnostic_context(topFactors=[EmptyBuildings=100, Taxes=100, LocalDemand=58, EducatedWorkforce=30])
    [2026-03-10 15:20:00,001] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T130639590Z, run_id=1, observation_end_day=21, observation_end_sample_index=152, detail_type=softwareOfficeStates, values=role=consumer, company=276439:1, prefab="Office_MediaCompany" (420:1), property=71688:1, output=Media, outputStock=0, input1=Software(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0), softwareInputZero=True, efficiency=0, lackResources=0)
    [2026-03-10 15:20:00,001] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T130639590Z, run_id=1, observation_end_day=21, observation_end_sample_index=152, detail_type=softwareOfficeStates, values=role=consumer, company=276447:1, prefab="Office_Bank" (419:1), property=71715:1, output=Financial, outputStock=0, input1=Software(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0), softwareInputZero=True, efficiency=0, lackResources=0)
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T130639590Z, run_id=1, start_day=20, end_day=22, start_sample_index=145, end_sample_index=160, sample_day=22, sample_index=160, sample_slot=6, samples_per_day=7, sample_count=16, trigger=suspicious_state); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:7,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=12482, emptyBuildings=100, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=1044875, resourceDemand=471676, companies=26, propertyless=1); electronics(resourceProduction=226375, resourceDemand=391863, companies=11, propertyless=2); softwareProducerOffices(total=26, propertyless=1, efficiencyZero=8, lackResourcesZero=8); softwareConsumerOffices(total=29, propertyless=3, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0)); diagnostic_context(topFactors=[EmptyBuildings=100, Taxes=100, LocalDemand=58, EducatedWorkforce=30])
    [2026-03-10 15:28:36,543] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T130639590Z, run_id=1, observation_end_day=22, observation_end_sample_index=160, detail_type=softwareOfficeStates, values=role=producer, company=524396:1, prefab="Office_SoftwareCompany" (364:1), property=116821:1, output=Software, outputStock=0, input1=Electronics(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0.577), efficiency=0, lackResources=0)
    [2026-03-10 15:28:36,543] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T130639590Z, run_id=1, observation_end_day=22, observation_end_sample_index=160, detail_type=softwareOfficeStates, values=role=producer, company=524398:1, prefab="Office_SoftwareCompany" (364:1), property=116825:1, output=Software, outputStock=0, input1=Electronics(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0.998), efficiency=0, lackResources=0)
    """
).strip()


BUYER_STATE_ONLY_LOG = textwrap.dedent(
    """
    [2026-03-10 16:00:00,000] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T160000000Z, run_id=1, start_day=22, end_day=22, start_sample_index=200, end_sample_index=200, sample_day=22, sample_index=200, sample_slot=1, samples_per_day=2, sample_count=1, trigger=suspicious_state); environment(settings=EnableTradePatch:True,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=12000, emptyBuildings=120, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=900000, resourceDemand=450000, companies=26, propertyless=0); electronics(resourceProduction=150000, resourceDemand=350000, companies=11, propertyless=0); softwareProducerOffices(total=26, propertyless=0, efficiencyZero=0, lackResourcesZero=0); softwareConsumerOffices(total=28, propertyless=0, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0); softwareConsumerBuyerState(selectedNeed=4, noBuyerDespiteNeed=4, tradeCostOnly=4, buyerActive=0)); diagnostic_context(topFactors=[EmptyBuildings=120, Taxes=100, LocalDemand=58])
    [2026-03-10 16:00:00,001] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T160000000Z, run_id=1, observation_end_day=22, observation_end_sample_index=200, detail_type=softwareOfficeStates, values=role=consumer, company=276439:1, prefab="Office_MediaCompany" (420:1), property=71688:1, output=Media, outputStock=0, input1=Software(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0), softwareNeed(selected=Software, amount=16), softwareBuyerState(selectedNeed=True, buyerActive=False, noBuyerDespiteNeed=True), softwareTrace(tripNeededCount=1, currentTradingCount=0))
    """
).strip()


RECENT_CONSUMER_HISTORY_LOG = textwrap.dedent(
    """
    [2026-03-10 15:20:00,000] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T170000000Z, run_id=1, start_day=21, end_day=21, start_sample_index=152, end_sample_index=152, sample_day=21, sample_index=152, sample_slot=1, samples_per_day=2, sample_count=1, trigger=suspicious_state); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=90, company=12000, emptyBuildings=90, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=925211, resourceDemand=411328, companies=27, propertyless=0); electronics(resourceProduction=109125, resourceDemand=351810, companies=11, propertyless=0); softwareProducerOffices(total=27, propertyless=0, efficiencyZero=0, lackResourcesZero=0); softwareConsumerOffices(total=28, propertyless=0, efficiencyZero=8, lackResourcesZero=0, softwareInputZero=8)); diagnostic_context(topFactors=[EmptyBuildings=90, Taxes=100, LocalDemand=58])
    [2026-03-10 15:20:00,001] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T170000000Z, run_id=1, observation_end_day=21, observation_end_sample_index=152, detail_type=softwareOfficeStates, values=role=consumer, company=276439:1, prefab="Office_MediaCompany" (420:1), property=71688:1, output=Media, outputStock=0, input1=Software(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0), softwareInputZero=True, efficiency=0, lackResources=0)
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T170000000Z, run_id=1, start_day=21, end_day=22, start_sample_index=152, end_sample_index=160, sample_day=22, sample_index=160, sample_slot=2, samples_per_day=2, sample_count=2, trigger=suspicious_state); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=12482, emptyBuildings=100, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=1044875, resourceDemand=471676, companies=26, propertyless=0); electronics(resourceProduction=226375, resourceDemand=391863, companies=11, propertyless=0); softwareProducerOffices(total=26, propertyless=0, efficiencyZero=0, lackResourcesZero=0); softwareConsumerOffices(total=29, propertyless=0, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0); softwareConsumerBuyerState(selectedNeed=3, noBuyerDespiteNeed=3, tradeCostOnly=3, buyerActive=0)); diagnostic_context(topFactors=[EmptyBuildings=100, Taxes=100, LocalDemand=58])
    [2026-03-10 15:28:36,543] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T170000000Z, run_id=1, observation_end_day=22, observation_end_sample_index=160, detail_type=softwareOfficeStates, values=role=consumer, company=276447:1, prefab="Office_Bank" (419:1), property=71715:1, output=Financial, outputStock=0, input1=Software(stock=0, tradeCostBuffer=True, tradeCostEntry=True, buyCost=0), softwareNeed(selected=Software, amount=16), softwareBuyerState(selectedNeed=True, buyerActive=False, noBuyerDespiteNeed=True), softwareTrace(tripNeededCount=1, currentTradingCount=0))
    """
).strip()


RECENT_MIXED_HISTORY_LOG = textwrap.dedent(
    """
    [2026-03-10 15:20:00,000] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T180000000Z, run_id=1, start_day=21, end_day=21, start_sample_index=152, end_sample_index=152, sample_day=21, sample_index=152, sample_slot=1, samples_per_day=2, sample_count=1, trigger=suspicious_state); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=90, company=12000, emptyBuildings=90, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=925211, resourceDemand=411328, companies=27, propertyless=0); electronics(resourceProduction=109125, resourceDemand=351810, companies=11, propertyless=0); softwareProducerOffices(total=27, propertyless=0, efficiencyZero=4, lackResourcesZero=4); softwareConsumerOffices(total=28, propertyless=0, efficiencyZero=8, lackResourcesZero=0, softwareInputZero=8)); diagnostic_context(topFactors=[EmptyBuildings=90, Taxes=100, LocalDemand=58])
    [2026-03-10 15:20:00,001] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T180000000Z, run_id=1, observation_end_day=21, observation_end_sample_index=152, detail_type=softwareOfficeStates, values=role=consumer, company=1, softwareInputZero=True)
    [2026-03-10 15:20:00,002] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T180000000Z, run_id=1, observation_end_day=21, observation_end_sample_index=152, detail_type=softwareOfficeStates, values=role=producer, company=2, lackResources=0)
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(session_id=20260310T180000000Z, run_id=1, start_day=21, end_day=22, start_sample_index=152, end_sample_index=160, sample_day=22, sample_index=160, sample_slot=2, samples_per_day=2, sample_count=2, trigger=suspicious_state); environment(settings=EnableTradePatch:False,EnablePhantomVacancyFix:True,EnableDemandDiagnostics:True,DiagnosticsSamplesPerDay:2,CaptureStableEvidence:True,VerboseLogging:True, patch_state=debug-build); diagnostic_counters(officeDemand(building=100, company=12482, emptyBuildings=100, buildingDemand=0); freeOfficeProperties(total=0, software=0, inOccupiedBuildings=0, softwareInOccupiedBuildings=0); onMarketOfficeProperties(total=0, activelyVacant=0, occupied=0, staleRenterOnly=0); phantomVacancy(signatureOccupiedOnMarketOffice=0, signatureOccupiedOnMarketIndustrial=0, signatureOccupiedToBeOnMarket=0, nonSignatureOccupiedOnMarketOffice=0, nonSignatureOccupiedOnMarketIndustrial=0, guardCorrections=0); software(resourceProduction=1044875, resourceDemand=471676, companies=26, propertyless=0); electronics(resourceProduction=226375, resourceDemand=391863, companies=11, propertyless=0); softwareProducerOffices(total=26, propertyless=0, efficiencyZero=3, lackResourcesZero=3); softwareConsumerOffices(total=29, propertyless=0, efficiencyZero=0, lackResourcesZero=0, softwareInputZero=0); softwareConsumerBuyerState(selectedNeed=3, noBuyerDespiteNeed=3, tradeCostOnly=3, buyerActive=0)); diagnostic_context(topFactors=[EmptyBuildings=100, Taxes=100, LocalDemand=58])
    [2026-03-10 15:28:36,543] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T180000000Z, run_id=1, observation_end_day=22, observation_end_sample_index=160, detail_type=softwareOfficeStates, values=role=consumer, company=3, noBuyerDespiteNeed=True)
    [2026-03-10 15:28:36,544] [INFO]  softwareEvidenceDiagnostics detail(session_id=20260310T180000000Z, run_id=1, observation_end_day=22, observation_end_sample_index=160, detail_type=softwareOfficeStates, values=role=producer, company=4, tradeCostEntry=True)
    """
).strip()


RAW_ISSUE_BODY = textwrap.dedent(
    """
    <!-- raw-log-report -->
    ### Game version
    1.5.xf1

    ### Mod version
    0.1.1

    ### Save or city label
    New Seoul

    ### What happened
    Loaded the save, enabled diagnostics, and waited 3 in-game days.

    ### Platform notes
    Windows release build

    ### Other mods
    none known

    ### Raw log
    ```text
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(...)
    ```
    """
).strip()

RAW_ISSUE_BODY_WITHOUT_MARKER = textwrap.dedent(
    """
    ### Game version
    1.5.xf1

    ### Mod version
    0.1.1

    ### Save or city label
    New Seoul

    ### What happened
    Loaded the save, enabled diagnostics, and waited 3 in-game days.

    ### Platform notes
    Windows release build

    ### Other mods
    none known

    ### Raw log
    ```text
    [2026-03-10 15:28:36,542] [INFO]  softwareEvidenceDiagnostics observation_window(...)
    ```
    """
).strip()

RAW_ISSUE_BODY_WITHOUT_MARKER_MISSING_REQUIRED = textwrap.dedent(
    """
    ### Game version
    1.5.xf1

    ### Mod version
    0.1.1

    ### Save or city label
    New Seoul

    ### What happened

    ### Platform notes
    Windows release build

    ### Other mods
    none known

    ### Raw log
    """
).strip()


class RawLogAutomationTests(unittest.TestCase):
    def test_parse_issue_form_sections(self) -> None:
        parsed = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        self.assertEqual(parsed["game_version"], "1.5.xf1")
        self.assertEqual(parsed["mod_version"], "0.1.1")
        self.assertEqual(parsed["save_or_city_label"], "New Seoul")
        self.assertEqual(parsed["platform_notes"], "Windows release build")
        self.assertIn("Loaded the save", parsed["what_happened"])
        self.assertIn("softwareEvidenceDiagnostics", parsed["raw_log"])

    def test_is_raw_log_issue_accepts_github_issue_form_output_without_markdown_marker(self) -> None:
        self.assertTrue(automation.is_raw_log_issue(RAW_ISSUE_BODY_WITHOUT_MARKER, "[Raw Log] test"))
        self.assertFalse(automation.is_raw_log_issue(RAW_ISSUE_BODY_WITHOUT_MARKER, "[Bug] test"))

    def test_is_raw_log_issue_rejects_missing_required_fields_without_marker(self) -> None:
        self.assertFalse(
            automation.is_raw_log_issue(RAW_ISSUE_BODY_WITHOUT_MARKER_MISSING_REQUIRED, "[Raw Log] test")
        )

    def test_is_raw_log_issue_rejects_prefix_only_false_positive(self) -> None:
        self.assertFalse(automation.is_raw_log_issue("not issue-form output", "[Raw Log] test"))

    def test_select_raw_log_source_prefers_attachment(self) -> None:
        issue_fields = {
            "raw_log": "[NoOfficeDemandFix.Mod.log](https://github.com/user-attachments/files/12345/NoOfficeDemandFix.Mod.log)"
        }
        with mock.patch.object(automation, "download_attachment", return_value="attachment-body") as download_mock:
            source = automation.select_raw_log_source(issue_fields)
        self.assertEqual(source["mode"], "attachment")
        self.assertEqual(source["text"], "attachment-body")
        self.assertEqual(download_mock.call_count, 1)

    def test_select_raw_log_source_rejects_non_github_attachment_hosts(self) -> None:
        issue_fields = {"raw_log": "https://example.com/NoOfficeDemandFix.Mod.log"}
        with self.assertRaisesRegex(automation.AttachmentDownloadError, "host is not allowed"):
            automation.select_raw_log_source(issue_fields)

    def test_download_attachment_does_not_forward_authorization_header(self) -> None:
        request_headers: dict[str, str] = {}

        class FakeResponse:
            status = 200

            def __enter__(self) -> "FakeResponse":
                return self

            def __exit__(self, exc_type, exc, tb) -> None:
                return None

            def read(self) -> bytes:
                return b"attachment-body"

        def fake_urlopen(request: object) -> FakeResponse:
            nonlocal request_headers
            request_headers = dict(request.header_items())
            return FakeResponse()

        with mock.patch("urllib.request.urlopen", side_effect=fake_urlopen):
            body = automation.download_attachment(
                "https://github.com/user-attachments/files/12345/NoOfficeDemandFix.Mod.log"
            )

        self.assertEqual(body, "attachment-body")
        self.assertNotIn("Authorization", request_headers)

    def test_redact_log_text_removes_local_paths_and_query_strings(self) -> None:
        redacted, notes = automation.redact_log_text(
            "Current mod asset at C:/Users/techn/AppData/LocalLow/Thing.dll\n"
            "Attachment https://example.com/log.txt?download=1"
        )
        self.assertIn("<redacted-path>", redacted)
        self.assertNotIn("techn", redacted)
        self.assertIn("https://example.com/log.txt", redacted)
        self.assertNotIn("?download=1", redacted)
        self.assertTrue(notes)

    def test_parse_log_uses_current_branch_observation_shape(self) -> None:
        parsed = automation.parse_log(CURRENT_BRANCH_LOG)
        latest = parsed["latest_observation"]
        self.assertIsNotNone(latest)
        self.assertEqual(latest["observation_window"]["sample_slot"], 1)
        self.assertEqual(latest["observation_window"]["samples_per_day"], 2)
        self.assertEqual(latest["settings"]["DiagnosticsSamplesPerDay"], 2)
        self.assertEqual(latest["patch_state"], "debug-build")
        self.assertEqual(parsed["latest_patch_summary"], "Office resource storage patch applied. Outside connections: 6, cargo stations: 28.")
        self.assertEqual(
            automation.derive_symptom_classification(latest["diagnostic_counters"]),
            "software_track_unclear",
        )
        self.assertIn("role=producer", parsed["latest_software_office_detail"]["values"])
        self.assertGreaterEqual(len(parsed["anchors"]), 4)
        self.assertEqual(parsed["anchor_index"]["observation"], 1)
        self.assertTrue(parsed["selected_snippets"])

    def test_parse_log_retains_latest_run_candidates_across_multiple_observations(self) -> None:
        parsed = automation.parse_log(MULTI_OBSERVATION_LOG)
        self.assertEqual(len(parsed["latest_run_observations"]), 3)
        self.assertEqual(automation.observation_day(parsed["consumer_peak_observation"]), 21)
        self.assertEqual(automation.observation_day(parsed["producer_peak_observation"]), 22)
        self.assertEqual([candidate["label"] for candidate in parsed["log_excerpt_candidates"]], ["consumer_latest", "producer_latest"])
        self.assertIn("role=consumer", parsed["log_excerpt_candidates"][0]["markdown"])
        self.assertIn("role=producer", parsed["log_excerpt_candidates"][1]["markdown"])

    def test_parse_log_keeps_latest_consumer_detail_without_distress_peak(self) -> None:
        parsed = automation.parse_log(BUYER_STATE_ONLY_LOG)
        self.assertEqual(automation.observation_day(parsed["consumer_peak_observation"]), 22)
        self.assertIsNone(parsed["producer_peak_observation"])
        self.assertEqual([candidate["label"] for candidate in parsed["log_excerpt_candidates"]], ["consumer_latest"])
        self.assertIn("softwareNeed(selected=Software", parsed["log_excerpt_candidates"][0]["markdown"])
        self.assertTrue(parsed["selected_snippets"])

    def test_parse_log_keeps_recent_consumer_detail_history(self) -> None:
        parsed = automation.parse_log(RECENT_CONSUMER_HISTORY_LOG)
        self.assertEqual(
            [candidate["label"] for candidate in parsed["log_excerpt_candidates"]],
            ["consumer_previous", "consumer_latest"],
        )
        self.assertIn("softwareInputZero=True", parsed["log_excerpt_candidates"][0]["markdown"])
        self.assertIn("softwareNeed(selected=Software", parsed["log_excerpt_candidates"][1]["markdown"])
        self.assertEqual(len([snippet for snippet in parsed["selected_snippets"] if snippet["kind"] == "detail_excerpt"]), 2)

    def test_parse_log_exposes_neutral_detail_observation_aliases(self) -> None:
        parsed = automation.parse_log(MULTI_OBSERVATION_LOG)
        self.assertEqual(
            automation.observation_day(parsed["latest_consumer_detail_observation"]),
            automation.observation_day(parsed["consumer_peak_observation"]),
        )
        self.assertEqual(
            automation.observation_day(parsed["latest_producer_detail_observation"]),
            automation.observation_day(parsed["producer_peak_observation"]),
        )

    def test_build_deterministic_draft_includes_checklist_confounders(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        draft = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        self.assertIn("patch_state=debug-build", draft["confounders"])
        self.assertIn("trade patch enabled during capture", draft["confounders"])
        self.assertIn("no explicit comparison baseline in raw intake", draft["confounders"])
        self.assertEqual(draft["platform_notes"], "Windows release build")
        self.assertEqual(draft["symptom_classification"], "software_track_unclear")
        self.assertEqual(draft["title"], "[Software Evidence] New Seoul evidence by day 22")

    def test_build_deterministic_draft_marks_disabled_trade_patch_in_confounders(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(MULTI_OBSERVATION_LOG)
        draft = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": MULTI_OBSERVATION_LOG},
            [],
        )
        self.assertIn("trade patch disabled during capture", draft["confounders"])
        self.assertIn("no explicit comparison baseline in raw intake", draft["confounders"])

    def test_build_deterministic_summary_prefers_buyer_state_pressure(self) -> None:
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        final_observation = parsed_log["final_observation"] or parsed_log["latest_observation"]
        final_observation["diagnostic_counters"]["softwareConsumerBuyerState"] = {
            "noBuyerDespiteNeed": 24,
            "buyerActive": 0,
        }
        summary = automation.build_deterministic_summary(parsed_log, "software_demand_mismatch")
        self.assertIn("softwareConsumerBuyerState(noBuyerDespiteNeed=24, buyerActive=0)", summary)
        self.assertIn("officeDemand(building=100", summary)

    def test_managed_comment_round_trip_preserves_override_block(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        reply_fields = {
            "scenario_label": "New Seoul",
            "scenario_type": "existing save",
            "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
            "mod_ref": "track/software-instability @ abc1234",
            "symptom_classification": "software_office_propertyless",
            "evidence_summary": "Maintainer-edited summary.",
            "confounders": "none known",
            "notes": "Maintainer note line 1\nMaintainer note line 2",
        }
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            reply_fields,
            [],
            "skipped",
            "no eligible observation",
        )
        parsed = automation.parse_managed_comment(body)
        self.assertEqual(parsed["reply_template"]["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("Maintainer note line 2", parsed["reply_template"]["notes"])

    def test_render_managed_comment_formats_markdown_at_column_zero(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            {
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "",
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "summary",
                "confounders": "none known",
                "notes": "note",
            },
            [],
            "failed",
            "http_403: models access denied",
        )
        self.assertIn("\n## Draft Evidence Issue Preview\n", body)
        self.assertIn("Draft title: `", body)
        self.assertIn("\n## Game version\n", body)
        self.assertIn("\n### Maintainer reply template\n", body)
        self.assertIn("\n```yaml\n", body)
        self.assertIn(
            f"\n{automation.PAYLOAD_START_MARKER}\n<details>\n<summary>Machine payload</summary>\n",
            body,
        )
        self.assertIn("\n```json\n", body)
        self.assertNotIn("\n        ## Draft Evidence Issue Preview\n", body)
        self.assertNotIn("\n        ```yaml\n", body)
        self.assertIn("- LLM status: `failed`", body)
        self.assertIn("- LLM detail: `http_403: models access denied`", body)
        self.assertIn("/promote-evidence", body)

    def test_render_managed_comment_keeps_preview_and_reply_yaml_full(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        long_notes = "A" * 800
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            {
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "",
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "summary",
                "confounders": "none known",
                "notes": long_notes,
            },
            [],
            "skipped",
            "no eligible observation",
        )
        self.assertIn(long_notes, body)
        self.assertIn("## Notes", body)

    def test_render_managed_comment_shows_full_reasoning_and_plain_yaml_guidance(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        full_reasoning = "B" * 460
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            {
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "summary",
                "confounders": "none known",
                "notes": "note",
                "missing_user_input": ["mod_ref"],
                "reasoning_summary": full_reasoning,
            },
            {
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "",
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "summary",
                "confounders": "none known",
                "notes": "note",
            },
            [],
            "enabled",
            automation.DEFAULT_GITHUB_MODELS_MODEL,
        )
        self.assertIn(full_reasoning, body)
        self.assertIn("plain YAML is accepted", body)
        self.assertIn("code fences are optional", body)

    def test_render_managed_comment_compact_fallback_keeps_reasoning_untruncated(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        long_reasoning = "C" * 450
        with mock.patch.object(automation, "COMMENT_BODY_LIMIT", 2500):
            body, _ = automation.render_managed_comment(
                21,
                issue_fields,
                log_source,
                parsed_log,
                deterministic,
                {
                    "symptom_classification": "software_office_propertyless",
                    "evidence_summary": "summary",
                    "confounders": "none known",
                    "notes": "note",
                    "missing_user_input": ["mod_ref"],
                    "reasoning_summary": long_reasoning,
                },
                {
                    "scenario_label": "New Seoul",
                    "scenario_type": "existing save",
                    "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                    "mod_ref": "",
                    "symptom_classification": "software_office_propertyless",
                    "evidence_summary": "summary",
                    "confounders": "none known",
                    "notes": "note",
                },
                [],
                "enabled",
                automation.DEFAULT_GITHUB_MODELS_MODEL,
            )
        self.assertIn("### Draft provenance", body)
        self.assertIn("- LLM reasoning: `see machine payload`", body)
        self.assertNotIn(f"`{'C' * 397}...`", body)
        self.assertIn("<summary>Machine payload</summary>", body)
        self.assertIn("Do not edit this block manually.", body)
        self.assertIn("```json", body)

    def test_render_managed_comment_compact_fallback_shows_short_llm_reasoning(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        short_reasoning = "Buyer inactivity persisted while office demand stayed high."
        with mock.patch.object(automation, "COMMENT_BODY_LIMIT", 2500):
            body, _ = automation.render_managed_comment(
                21,
                issue_fields,
                log_source,
                parsed_log,
                deterministic,
                {
                    "symptom_classification": "software_demand_mismatch",
                    "evidence_summary": "summary",
                    "confounders": "EnableTradePatch=False; debug-build patch state",
                    "notes": "note",
                    "missing_user_input": [],
                    "reasoning_summary": short_reasoning,
                },
                {
                    "scenario_label": "New Seoul",
                    "scenario_type": "existing save",
                    "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                    "mod_ref": "",
                    "symptom_classification": "software_demand_mismatch",
                    "evidence_summary": "summary",
                    "confounders": deterministic["confounders"],
                    "notes": "note",
                },
                [],
                "enabled",
                automation.DEFAULT_GITHUB_MODELS_MODEL,
            )
        self.assertIn(f"- LLM reasoning: `{short_reasoning}`", body)
        self.assertNotIn("see machine payload", body)

    def test_merge_evidence_fields_and_required_gate(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        body, payload = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            {
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "track/software-instability @ abc1234",
                "symptom_classification": "software_office_propertyless",
                "evidence_summary": "Maintainer-edited summary.",
                "confounders": "none known",
                "notes": "Maintainer note.",
            },
            [],
            "skipped",
            "no eligible observation",
        )
        parsed = automation.parse_managed_comment(body)
        fields = automation.merge_evidence_fields(
            parsed["payload"],
            parsed["reply_template"],
            "https://github.com/example/repo/issues/21#issuecomment-1",
            "https://github.com/example/repo/issues/21",
            "https://github.com/example/repo/issues/21#issuecomment-2",
        )
        required = automation.extract_required_issue_fields(
            str(REPO_ROOT / ".github" / "ISSUE_TEMPLATE" / "software_evidence.yml")
        )
        missing = automation.find_missing_required_fields(fields, required)
        self.assertEqual(missing, [])
        issue_body = automation.render_evidence_issue_body(21, 1, fields)
        self.assertIn("<!-- source-raw-log-issue:21 -->", issue_body)
        self.assertIn("<!-- source-raw-log-comment:1 -->", issue_body)
        self.assertIn("## Observation window", issue_body)
        self.assertIn("session_id=20260310T052953590Z", issue_body)
        self.assertIn("## Settings", issue_body)
        self.assertIn("EnableTradePatch:True", issue_body)
        self.assertIn("## Diagnostic counters", issue_body)
        self.assertIn("softwareProducerOffices(total=27, propertyless=1, efficiencyZero=10, lackResourcesZero=10)", issue_body)
        self.assertIn("## Log excerpt\n### Day 22 producer-side detail", issue_body)
        self.assertNotIn("Readable view", issue_body)
        self.assertNotIn("Raw string", issue_body)
        self.assertNotIn("Custom symptom classification", issue_body)
        self.assertIn("## Mod ref", issue_body)
        self.assertIn("track/software-instability @ abc1234", issue_body)
        self.assertIn("- maintainer promote reply: https://github.com/example/repo/issues/21#issuecomment-2", fields["artifacts"])

    def test_merge_evidence_fields_keeps_mod_ref_optional_and_hides_blank_section(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG}
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        body, _ = automation.render_managed_comment(
            21,
            issue_fields,
            log_source,
            parsed_log,
            deterministic,
            None,
            {
                "title": deterministic["title"],
                "scenario_label": "New Seoul",
                "scenario_type": "existing save",
                "reproduction_conditions": "Loaded the same save and waited 3 in-game days.",
                "mod_ref": "",
                "symptom_classification": deterministic["symptom_classification"],
                "evidence_summary": deterministic["evidence_summary"],
                "confounders": deterministic["confounders"],
                "notes": deterministic["notes"],
            },
            [],
            "skipped",
            "no eligible observation",
        )
        parsed = automation.parse_managed_comment(body)
        fields = automation.merge_evidence_fields(
            parsed["payload"],
            parsed["reply_template"],
            "https://github.com/example/repo/issues/21#issuecomment-1",
            "https://github.com/example/repo/issues/21",
            "https://github.com/example/repo/issues/21#issuecomment-2",
        )
        required = automation.extract_required_issue_fields(
            str(REPO_ROOT / ".github" / "ISSUE_TEMPLATE" / "software_evidence.yml")
        )
        missing = automation.find_missing_required_fields(fields, required)
        self.assertEqual(missing, [])
        issue_body = automation.render_evidence_issue_body(21, 1, fields)
        self.assertNotIn("## Mod ref", issue_body)
        self.assertIn("## Platform notes", issue_body)
        self.assertIn("Windows release build", issue_body)

    def test_merge_evidence_fields_prefers_deterministic_confounders_format(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        log_source = {"mode": "inline", "url": "", "attachment_urls": [], "text": MULTI_OBSERVATION_LOG}
        parsed_log = automation.parse_log(MULTI_OBSERVATION_LOG)
        deterministic = automation.build_deterministic_draft(21, issue_fields, parsed_log, log_source, [])
        payload = {
            "raw_issue": {"fields": issue_fields},
            "log_source": log_source,
            "parsed_log": parsed_log,
            "deterministic_draft": deterministic,
            "llm_draft": {
                "title": deterministic["title"],
                "symptom_classification": deterministic["symptom_classification"],
                "evidence_summary": deterministic["evidence_summary"],
                "confidence": "medium",
                "confounders": "EnableTradePatch=False; Realistic Trips mod active; debug-build patch state; no baseline comparison provided",
                "notes": deterministic["notes"],
            },
        }
        fields = automation.merge_evidence_fields(
            payload,
            overrides={},
            raw_issue_url="https://example.test/raw/21",
            triage_comment_url="https://example.test/comment/1",
        )
        self.assertIn("- trade patch disabled during capture", fields["confounders"])
        self.assertNotIn("EnableTradePatch=False;", fields["confounders"])

    def test_generate_llm_suggestions_returns_none_without_token(self) -> None:
        self.assertIsNone(automation.generate_llm_suggestions({"foo": "bar"}, None))

    def test_build_llm_request_payload_uses_github_models_chat_completions_shape(self) -> None:
        payload = automation.build_llm_request_payload({"foo": "bar"})
        schema_properties = payload["response_format"]["json_schema"]["schema"]["properties"]
        self.assertEqual(payload["model"], automation.DEFAULT_GITHUB_MODELS_MODEL)
        self.assertEqual(payload["messages"][0]["role"], "system")
        self.assertEqual(payload["messages"][1]["role"], "user")
        self.assertEqual(payload["messages"][1]["content"], '{"foo":"bar"}')
        self.assertEqual(payload["response_format"]["type"], "json_schema")
        self.assertEqual(
            payload["response_format"]["json_schema"]["name"],
            "raw_log_triage_suggestions",
        )
        self.assertIn("title", schema_properties)
        self.assertIn("log_excerpt", schema_properties)
        self.assertEqual(schema_properties["title"]["maxLength"], automation.LLM_TITLE_MAX_LENGTH)
        self.assertEqual(
            schema_properties["evidence_summary"]["maxLength"],
            automation.LLM_EVIDENCE_SUMMARY_MAX_LENGTH,
        )
        self.assertEqual(
            schema_properties["reasoning_summary"]["maxLength"],
            automation.LLM_REASONING_SUMMARY_MAX_LENGTH,
        )
        self.assertEqual(schema_properties["missing_user_input"]["maxItems"], 3)
        self.assertIn("lackResourcesZero", payload["messages"][0]["content"])
        self.assertIn("zero resources", payload["messages"][0]["content"])
        self.assertIn("#25 and #26", payload["messages"][0]["content"])
        self.assertIn("Do not mention the chosen symptom label", payload["messages"][0]["content"])
        self.assertIn("Put label-selection rationale and interpretation only in `reasoning_summary`", payload["messages"][0]["content"])
        self.assertIn("do not speculate about root cause", payload["messages"][0]["content"])
        self.assertIn("no indication of phantom vacancies", payload["messages"][0]["content"])
        self.assertIn("softwareInputZero=False", payload["messages"][0]["content"])
        self.assertIn("Do not use ellipses", payload["messages"][0]["content"])
        self.assertIn("allowed_missing_user_input", payload["messages"][0]["content"])
        self.assertIn("under 220 characters", payload["messages"][0]["content"])

    def test_build_summary_refinement_request_payload_uses_gpt_4_1(self) -> None:
        payload = automation.build_summary_refinement_request_payload({"foo": "bar"})
        self.assertEqual(payload["model"], automation.DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL)
        self.assertEqual(payload["messages"][0]["role"], "system")
        self.assertEqual(payload["messages"][1]["content"], '{"foo":"bar"}')
        self.assertEqual(
            payload["response_format"]["json_schema"]["schema"]["required"],
            ["evidence_summary"],
        )

    def test_build_llm_context_excludes_raw_log_and_caps_excerpt(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        issue_fields["raw_log"] = "X" * 5000
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        self.assertNotIn("raw_log", context["raw_issue"])
        self.assertIn("anchors", context)
        self.assertIn("selected_snippets", context)
        self.assertIn("excerpt_candidates", context)
        self.assertTrue(context["excerpt_candidates"])
        self.assertLessEqual(
            len(context["excerpt_candidates"][0]["lines"][0]),
            automation.LLM_DETAIL_EXCERPT_LIMIT,
        )
        self.assertTrue(context["anchors"])
        self.assertTrue(context["selected_snippets"])
        self.assertIn("fallback_hints", context)
        self.assertIn("semantic_facts", context)
        self.assertTrue(any("lackResourcesZero" in fact for fact in context["semantic_facts"]))

    def test_build_llm_context_size_stays_small_even_with_huge_raw_issue_body(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        issue_fields["raw_log"] = "Y" * 50000
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        serialized = automation.json.dumps(context, ensure_ascii=True)
        self.assertLess(len(serialized), 12000)

    def test_build_llm_context_variants_preserve_allowed_missing_user_input(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        issue_fields["save_or_city_label"] = ""
        issue_fields["what_happened"] = ""
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        variants = automation.build_llm_context_variants(context)
        self.assertEqual(deterministic["missing_user_input"], ["scenario_label", "reproduction_conditions"])
        self.assertTrue(variants)
        self.assertTrue(all("allowed_missing_user_input" in variant for variant in variants))
        self.assertEqual(
            variants[-1]["allowed_missing_user_input"],
            ["scenario_label", "reproduction_conditions"],
        )

    def test_build_llm_context_variants_keep_latest_excerpt_candidates(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(RECENT_MIXED_HISTORY_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": RECENT_MIXED_HISTORY_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        variants = automation.build_llm_context_variants(context)
        self.assertEqual(
            [candidate["label"] for candidate in variants[0]["excerpt_candidates"]],
            ["consumer_previous", "producer_previous", "consumer_latest", "producer_latest"],
        )
        self.assertEqual(
            [candidate["label"] for candidate in variants[1]["excerpt_candidates"]],
            ["consumer_latest", "producer_latest"],
        )
        self.assertEqual(
            [candidate["label"] for candidate in variants[2]["excerpt_candidates"]],
            ["producer_latest"],
        )

    def test_has_unsupported_reasoning_summary_format_rejects_double_question_mark(self) -> None:
        self.assertTrue(automation.has_unsupported_reasoning_summary_format("Why this label??"))
        self.assertFalse(automation.has_unsupported_reasoning_summary_format("Buyer inactivity persisted."))

    def test_generate_llm_suggestions_parses_github_models_response(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"title":"[Software Evidence] test title",'
                            '"symptom_classification":"software_track_unclear",'
                            '"custom_symptom_classification":"",'
                            '"evidence_summary":"summary","comparison_baseline":"",'
                            '"confidence":"medium","confounders":"none","analysis_basis":"",'
                            '"log_excerpt":"### Day 22 producer-side detail\\n```text\\nrole=producer\\n```",'
                            '"notes":"note","missing_user_input":[],'
                            '"reasoning_summary":"reason"}'
                        )
                    }
                }
            ]
        }
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, response_payload, ""),
        ) as request_mock:
            suggestions = automation.generate_llm_suggestions({"foo": "bar"}, "gh-token")
        self.assertEqual(suggestions["symptom_classification"], "software_track_unclear")
        self.assertEqual(request_mock.call_args.args[1], automation.GITHUB_MODELS_CHAT_COMPLETIONS_URL)

    def test_refine_evidence_summary_uses_gpt_4_1_and_returns_refined_text(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        draft = {
            "title": deterministic["title"],
            "evidence_summary": "draft summary",
        }
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"evidence_summary":"The final day-22 sample kept '
                            '`softwareConsumerBuyerState.noBuyerDespiteNeed=24` with '
                            '`buyerActive=0` while `officeDemand.building=100` remained high."}'
                        )
                    }
                }
            ]
        }
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, response_payload, ""),
        ) as request_mock:
            refined_summary, refinement_model = automation.refine_evidence_summary(
                issue_fields,
                parsed_log,
                deterministic,
                draft,
                "gh-token",
            )
        self.assertEqual(refinement_model, automation.DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL)
        self.assertIn("officeDemand.building=100", refined_summary)
        self.assertEqual(
            request_mock.call_args.kwargs["payload"]["model"],
            automation.DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL,
        )

    def test_generate_llm_suggestions_rewrites_unsupported_zero_resources_wording(self) -> None:
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"title":"[Software Evidence] test title",'
                            '"symptom_classification":"software_office_propertyless",'
                            '"custom_symptom_classification":"",'
                            '"evidence_summary":"8 producer offices at zero efficiency and zero resources.",'
                            '"comparison_baseline":"","confidence":"medium","confounders":"none","analysis_basis":"",'
                            '"log_excerpt":"","notes":"note","missing_user_input":[],'
                            '"reasoning_summary":"reason"}'
                        )
                    }
                }
            ]
        }
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, response_payload, ""),
        ):
            suggestions = automation.generate_llm_suggestions({"foo": "bar"}, "gh-token")
        self.assertNotIn("zero resources", suggestions["evidence_summary"].lower())
        self.assertIn("lackResources=0", suggestions["evidence_summary"])

    def test_generate_llm_suggestions_retries_with_compact_context_after_413(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(MULTI_OBSERVATION_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": MULTI_OBSERVATION_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        response_payload = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"title":"[Software Evidence] compact retry title",'
                            '"symptom_classification":"software_demand_mismatch",'
                            '"custom_symptom_classification":"",'
                            '"evidence_summary":"summary","comparison_baseline":"",'
                            '"confidence":"medium","confounders":"none","analysis_basis":"",'
                            '"log_excerpt":"","notes":"note","missing_user_input":[],' 
                            '"reasoning_summary":"reason"}'
                        )
                    }
                }
            ]
        }
        with mock.patch.object(
            automation,
            "http_request",
            side_effect=[
                (413, {}, "payload too large"),
                (200, response_payload, ""),
            ],
        ) as request_mock:
            suggestions = automation.generate_llm_suggestions(context, "gh-token")
        self.assertEqual(suggestions["title"], "[Software Evidence] compact retry title")
        self.assertEqual(request_mock.call_count, 2)
        first_payload = request_mock.call_args_list[0].kwargs["payload"]
        second_payload = request_mock.call_args_list[1].kwargs["payload"]
        self.assertLess(
            len(second_payload["messages"][1]["content"]),
            len(first_payload["messages"][1]["content"]),
        )

    def test_generate_validated_llm_draft_accepts_primary_model_when_valid(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        valid_draft = {
            "title": "[Software Evidence] EnableTradePatch-enabled run still shows software-track distress by day 22",
            "symptom_classification": "software_demand_mismatch",
            "custom_symptom_classification": "",
            "evidence_summary": "The final day-22 sample still showed producer-side lackResources=0 distress while officeDemand.building=100 remained high.",
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": "none known",
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": "note",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=valid_draft):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=("Refined summary", automation.DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertEqual(result["draft"]["symptom_classification"], "software_demand_mismatch")
        self.assertEqual(result["draft"]["evidence_summary"], "Refined summary")
        self.assertIn("summary_refinement=openai/gpt-4.1", result["detail"])

    def test_generate_validated_llm_draft_escalates_after_validator_failure(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        parsed_log["observation_count"] = 4
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        primary_draft = {
            "title": "[Software Evidence] automation test",
            "symptom_classification": "software_office_propertyless",
            "custom_symptom_classification": "",
            "evidence_summary": "summary",
            "comparison_baseline": "Compare against #25.",
            "confidence": "medium",
            "confounders": "none",
            "analysis_basis": "",
            "log_excerpt": "",
            "notes": "note",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        escalation_draft = {
            "title": "[Software Evidence] EnableTradePatch-enabled run still shows software-track distress by day 22",
            "symptom_classification": "software_demand_mismatch",
            "custom_symptom_classification": "",
            "evidence_summary": "The final day-22 sample still showed producer-side lackResources=0 distress while officeDemand.building=100 remained high.",
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": "none known",
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": "note",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.dict(
            automation.os.environ,
            {
                automation.ESCALATION_GITHUB_MODELS_MODEL_ENV: "openai/gpt-5.4"
            },
            clear=False,
        ):
            with mock.patch.object(
                automation,
                "generate_llm_suggestions",
                side_effect=[primary_draft, escalation_draft],
            ):
                with mock.patch.object(
                    automation,
                    "refine_evidence_summary",
                    return_value=("Refined escalation summary", automation.DEFAULT_SUMMARY_REFINEMENT_GITHUB_MODELS_MODEL),
                ):
                    result = automation.generate_validated_llm_draft(
                        context,
                        issue_fields,
                        parsed_log,
                        deterministic,
                        "gh-token",
                    )
        self.assertEqual(result["status"], "escalated")
        self.assertEqual(result["model"], "openai/gpt-5.4")
        self.assertEqual(result["draft"]["symptom_classification"], "software_demand_mismatch")
        self.assertEqual(result["draft"]["evidence_summary"], "Refined escalation summary")
        self.assertIn("summary_refinement=openai/gpt-4.1", result["detail"])

    def test_generate_validated_llm_draft_keeps_sanitized_excerpt_without_fallback(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft_with_bad_excerpt = {
            "title": "[Software Evidence] EnableTradePatch-enabled run still shows software-track distress by day 22",
            "symptom_classification": "software_demand_mismatch",
            "custom_symptom_classification": "",
            "evidence_summary": "The final day-22 sample still showed producer-side lackResources=0 distress while officeDemand.building=100 remained high.",
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": "none known",
            "analysis_basis": "",
            "log_excerpt": "### Day 22 producer-side detail\n```text\nrole=producer, rewritten wording\n```",
            "notes": "note",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft_with_bad_excerpt):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(draft_with_bad_excerpt["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertEqual(result["detail"], automation.DEFAULT_GITHUB_MODELS_MODEL)
        self.assertIn("unsupported_excerpt_line", result["validation_errors"])
        self.assertEqual(result["draft"]["log_excerpt"], deterministic["log_excerpt"])

    def test_generate_validated_llm_draft_replaces_unsupported_summary_and_notes(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft_with_bad_interpretation = {
            "title": "[Software Evidence] EnableTradePatch-enabled run still shows software-track distress by day 22",
            "symptom_classification": "software_demand_mismatch",
            "custom_symptom_classification": "",
            "evidence_summary": "There is no indication of phantom vacancies and office demand increased significantly during the window.",
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": "none known",
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": "This suggests unresolved software demand despite stable office market conditions.",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft_with_bad_interpretation):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(deterministic["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertIn("unsupported_evidence_summary_interpretation", result["validation_errors"])
        self.assertIn("unsupported_notes_interpretation", result["validation_errors"])
        self.assertEqual(result["draft"]["evidence_summary"], deterministic["evidence_summary"])
        self.assertEqual(result["draft"]["notes"], deterministic["notes"])

    def test_generate_validated_llm_draft_drops_unsupported_missing_user_input(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft = {
            "title": deterministic["title"],
            "symptom_classification": deterministic["symptom_classification"],
            "custom_symptom_classification": "",
            "evidence_summary": deterministic["evidence_summary"],
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": deterministic["confounders"],
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": deterministic["notes"],
            "missing_user_input": ["specific save file or city label", "comparison_baseline"],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(deterministic["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertIn("unsupported_missing_user_input", result["validation_errors"])
        self.assertEqual(result["draft"]["missing_user_input"], [])
        self.assertNotIn("unsupported_issue_ref:notes", result["validation_errors"])

    def test_generate_validated_llm_draft_replaces_unsupported_phantom_zero_claim(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft = {
            "title": deterministic["title"],
            "symptom_classification": deterministic["symptom_classification"],
            "custom_symptom_classification": "",
            "evidence_summary": deterministic["evidence_summary"],
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": deterministic["confounders"],
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": "Phantom vacancy counters and guard corrections remain zero throughout the run.",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(deterministic["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertIn("unsupported_notes_interpretation", result["validation_errors"])
        self.assertEqual(result["draft"]["notes"], deterministic["notes"])

    def test_generate_validated_llm_draft_replaces_phantom_absence_claims(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft = {
            "title": deterministic["title"],
            "symptom_classification": deterministic["symptom_classification"],
            "custom_symptom_classification": "",
            "evidence_summary": "PhantomVacancy counters and guardCorrections were zero during the window.",
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": deterministic["confounders"],
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": "No free office properties existed, and no phantom vacancies or guard corrections were detected during the run.",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(deterministic["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertIn("unsupported_evidence_summary_interpretation", result["validation_errors"])
        self.assertIn("unsupported_notes_interpretation", result["validation_errors"])
        self.assertEqual(result["draft"]["evidence_summary"], deterministic["evidence_summary"])
        self.assertEqual(result["draft"]["notes"], deterministic["notes"])

    def test_generate_validated_llm_draft_replaces_phantom_zero_and_no_detected_claims(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft = {
            "title": deterministic["title"],
            "symptom_classification": deterministic["symptom_classification"],
            "custom_symptom_classification": "",
            "evidence_summary": deterministic["evidence_summary"],
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": deterministic["confounders"],
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": "Phantom vacancy counters stayed at zero indicating no detected phantom vacancies during the window.",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(deterministic["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertIn("unsupported_notes_interpretation", result["validation_errors"])
        self.assertEqual(result["draft"]["notes"], deterministic["notes"])

    def test_generate_validated_llm_draft_replaces_ellipsis_reasoning_summary(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        draft = {
            "title": deterministic["title"],
            "symptom_classification": deterministic["symptom_classification"],
            "custom_symptom_classification": "",
            "evidence_summary": deterministic["evidence_summary"],
            "comparison_baseline": "",
            "confidence": "medium",
            "confounders": deterministic["confounders"],
            "analysis_basis": "",
            "log_excerpt": deterministic["log_excerpt"],
            "notes": deterministic["notes"],
            "missing_user_input": [],
            "reasoning_summary": "This is incomplete...",
        }
        with mock.patch.object(automation, "generate_llm_suggestions", return_value=draft):
            with mock.patch.object(
                automation,
                "refine_evidence_summary",
                return_value=(deterministic["evidence_summary"], ""),
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "enabled")
        self.assertIn("unsupported_reasoning_summary_format", result["validation_errors"])
        self.assertEqual(result["draft"]["reasoning_summary"], deterministic["reasoning_summary"])

    def test_generate_validated_llm_draft_falls_back_when_both_drafts_fail_validation(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(CURRENT_BRANCH_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": CURRENT_BRANCH_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        invalid_draft = {
            "title": "[Software Evidence] automation test",
            "symptom_classification": "software_office_propertyless",
            "custom_symptom_classification": "",
            "evidence_summary": "summary referencing day-99",
            "comparison_baseline": "Compare against #25.",
            "confidence": "low",
            "confounders": "none",
            "analysis_basis": "",
            "log_excerpt": "### Day 99 detail\n```text\ninvented line\n```",
            "notes": "note",
            "missing_user_input": [],
            "reasoning_summary": "reason",
        }
        with mock.patch.dict(
            automation.os.environ,
            {
                automation.ESCALATION_GITHUB_MODELS_MODEL_ENV: "openai/gpt-5.4"
            },
            clear=False,
        ):
            with mock.patch.object(
                automation,
                "generate_llm_suggestions",
                side_effect=[invalid_draft, invalid_draft],
            ):
                result = automation.generate_validated_llm_draft(
                    context,
                    issue_fields,
                    parsed_log,
                    deterministic,
                    "gh-token",
                )
        self.assertEqual(result["status"], "fallback")
        self.assertIsNone(result["draft"])

    def test_generate_validated_llm_draft_falls_back_after_payload_too_large(self) -> None:
        issue_fields = automation.parse_issue_form_sections(RAW_ISSUE_BODY)
        parsed_log = automation.parse_log(MULTI_OBSERVATION_LOG)
        deterministic = automation.build_deterministic_draft(
            21,
            issue_fields,
            parsed_log,
            {"mode": "inline", "url": "", "attachment_urls": [], "text": MULTI_OBSERVATION_LOG},
            [],
        )
        context = automation.build_llm_context(issue_fields, parsed_log, deterministic, [])
        with mock.patch.object(
            automation,
            "generate_llm_suggestions",
            side_effect=automation.AutomationError("GitHub Models request failed (413): payload too large"),
        ):
            result = automation.generate_validated_llm_draft(
                context,
                issue_fields,
                parsed_log,
                deterministic,
                "gh-token",
            )
        self.assertEqual(result["status"], "fallback")
        self.assertEqual(result["detail"], "context fallback: payload too large")
        self.assertIsNone(result["draft"])

    def test_build_evidence_issue_title_prefers_merged_title(self) -> None:
        title = automation.build_evidence_issue_title(
            {"title": "[Software Evidence] EnableTradePatch-enabled run still shows software-track distress by day 22"},
            "[Raw Log] automation test",
            21,
        )
        self.assertEqual(
            title,
            "[Software Evidence] EnableTradePatch-enabled run still shows software-track distress by day 22",
        )

    def test_parse_reply_comment_and_command(self) -> None:
        comment_body = textwrap.dedent(
            """
            Ready to promote.

            /promote-evidence

            ```yaml
            maintainer_reply:
              scenario_label: "New Seoul"
              scenario_type: "existing save"
              reproduction_conditions: |
                Loaded the same save.
                Waited 3 in-game days.
              mod_ref: "track/software-instability @ abc1234"
              symptom_classification: "software_office_propertyless"
              evidence_summary: "summary"
              confounders: |
                - patch_state=debug-build
                - trade patch enabled during capture
              notes: "note"
            ```
            """
        ).strip()
        parsed = automation.parse_reply_comment(comment_body)
        self.assertTrue(automation.comment_has_promote_command(comment_body))
        self.assertEqual(parsed["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("trade patch enabled", parsed["confounders"])

    def test_parse_reply_comment_accepts_plain_copied_yaml(self) -> None:
        comment_body = textwrap.dedent(
            """
            maintainer_reply:
              scenario_label: "New Seoul"
              scenario_type: "existing save"
              reproduction_conditions: |
                Loaded the same save.
                Waited 3 in-game days.
              mod_ref: "track/software-instability @ abc1234"
              symptom_classification: "software_office_propertyless"
              evidence_summary: "summary"
              confounders: |
                - patch_state=debug-build
                - trade patch enabled during capture
              notes: "note"

            /promote-evidence
            """
        ).strip()
        parsed = automation.parse_reply_comment(comment_body)
        self.assertTrue(automation.comment_has_promote_command(comment_body))
        self.assertEqual(parsed["mod_ref"], "track/software-instability @ abc1234")
        self.assertIn("trade patch enabled", parsed["confounders"])

    def test_parse_reply_comment_returns_blank_for_missing_or_malformed_yaml(self) -> None:
        missing = automation.parse_reply_comment("/promote-evidence")
        malformed = automation.parse_reply_comment(
            "maintainer_reply:\nmod_ref: \"track/software-instability @ abc1234\"\n/promote-evidence"
        )
        self.assertFalse(automation.has_nonempty_reply_fields(missing))
        self.assertFalse(automation.has_nonempty_reply_fields(malformed))

    def test_find_latest_reply_comment_ignores_bot_and_nonmaintainer(self) -> None:
        comments = [
            {
                "id": 1,
                "body": "```yaml\nmaintainer_reply:\n  mod_ref: \"one\"\n```",
                "user": {"login": "github-actions[bot]"},
                "author_association": "NONE",
                "updated_at": "2026-03-10T09:00:00Z",
                "created_at": "2026-03-10T09:00:00Z",
            },
            {
                "id": 2,
                "body": "```yaml\nmaintainer_reply:\n  mod_ref: \"two\"\n```",
                "user": {"login": "random-user"},
                "author_association": "CONTRIBUTOR",
                "updated_at": "2026-03-10T09:01:00Z",
                "created_at": "2026-03-10T09:01:00Z",
            },
            {
                "id": 3,
                "body": "```yaml\nmaintainer_reply:\n  mod_ref: \"three\"\n```",
                "user": {"login": "repo-owner"},
                "author_association": "OWNER",
                "updated_at": "2026-03-10T09:02:00Z",
                "created_at": "2026-03-10T09:02:00Z",
            },
        ]
        latest = automation.find_latest_reply_comment(comments)
        self.assertIsNotNone(latest)
        self.assertEqual(latest["id"], 3)

    def test_find_latest_reply_comment_accepts_plain_yaml_reply(self) -> None:
        comments = [
            {
                "id": 1,
                "body": "maintainer_reply:\n  mod_ref: \"one\"\n\n/promote-evidence",
                "user": {"login": "repo-owner"},
                "author_association": "OWNER",
                "updated_at": "2026-03-10T09:00:00Z",
                "created_at": "2026-03-10T09:00:00Z",
            }
        ]
        latest = automation.find_latest_reply_comment(comments)
        self.assertIsNotNone(latest)
        self.assertEqual(latest["id"], 1)

    def test_get_issue_comments_paginates_until_short_page(self) -> None:
        first_page = [{"id": index} for index in range(1, 101)]
        second_page = [{"id": 101}]
        with mock.patch.object(
            automation,
            "http_request",
            side_effect=[(200, first_page, ""), (200, second_page, "")],
        ) as request_mock:
            comments = automation.get_issue_comments("FennexFox/NoOfficeDemandFix", 30, "token")
        self.assertEqual(len(comments), 101)
        self.assertEqual(comments[-1]["id"], 101)
        self.assertEqual(request_mock.call_count, 2)
        self.assertTrue(request_mock.call_args_list[0].args[1].endswith("/issues/30/comments?per_page=100&page=1"))
        self.assertTrue(request_mock.call_args_list[1].args[1].endswith("/issues/30/comments?per_page=100&page=2"))

    def test_get_issue_comments_stops_after_first_short_page(self) -> None:
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, [{"id": 1}, {"id": 2}], ""),
        ) as request_mock:
            comments = automation.get_issue_comments("FennexFox/NoOfficeDemandFix", 30, "token")
        self.assertEqual([comment["id"] for comment in comments], [1, 2])
        self.assertEqual(request_mock.call_count, 1)

    def test_get_issue_comments_raises_on_http_error(self) -> None:
        with mock.patch.object(automation, "http_request", return_value=(500, {"message": "boom"}, "boom")):
            with self.assertRaisesRegex(automation.AutomationError, r"Failed to fetch issue comments \(500\): boom"):
                automation.get_issue_comments("FennexFox/NoOfficeDemandFix", 30, "token")

    def test_find_existing_promoted_issue_uses_search_api_and_returns_match(self) -> None:
        marker = automation.SOURCE_RAW_ISSUE_MARKER.format(issue_number=123)
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, {"items": [{"number": 44, "body": marker}]}, ""),
        ) as request_mock:
            issue = automation.find_existing_promoted_issue("FennexFox/NoOfficeDemandFix", 123, "token")
        self.assertIsNotNone(issue)
        self.assertEqual(issue["number"], 44)
        request_url = request_mock.call_args.args[1]
        self.assertIn("/search/issues?", request_url)
        search_query = automation.urllib.parse.parse_qs(automation.urllib.parse.urlsplit(request_url).query)["q"][0]
        self.assertIn(
            f'repo:FennexFox/NoOfficeDemandFix is:issue "{marker}"',
            search_query,
        )

    def test_find_existing_promoted_issue_ignores_pull_request_results(self) -> None:
        marker = automation.SOURCE_RAW_ISSUE_MARKER.format(issue_number=123)
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(
                200,
                {
                    "items": [
                        {"number": 44, "body": marker, "pull_request": {"url": "https://example.invalid/pr/44"}},
                        {"number": 45, "body": marker},
                    ]
                },
                "",
            ),
        ):
            issue = automation.find_existing_promoted_issue("FennexFox/NoOfficeDemandFix", 123, "token")
        self.assertIsNotNone(issue)
        self.assertEqual(issue["number"], 45)

    def test_find_existing_promoted_issue_returns_none_when_search_has_no_match(self) -> None:
        with mock.patch.object(automation, "http_request", return_value=(200, {"items": []}, "")):
            issue = automation.find_existing_promoted_issue("FennexFox/NoOfficeDemandFix", 123, "token")
        self.assertIsNone(issue)

    def test_find_existing_promoted_issue_ignores_empty_body_search_hit(self) -> None:
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, {"items": [{"number": 44, "body": ""}]}, ""),
        ):
            issue = automation.find_existing_promoted_issue("FennexFox/NoOfficeDemandFix", 123, "token")
        self.assertIsNone(issue)

    def test_find_existing_promoted_issue_ignores_comment_only_search_hit(self) -> None:
        with mock.patch.object(
            automation,
            "http_request",
            return_value=(200, {"items": [{"number": 44, "body": "search matched a comment, not the marker"}]}, ""),
        ):
            issue = automation.find_existing_promoted_issue("FennexFox/NoOfficeDemandFix", 123, "token")
        self.assertIsNone(issue)

    def test_sanitize_llm_detail_maps_common_failures(self) -> None:
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (403): access denied"),
            "http_403: models access denied",
        )
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (429): rate limited"),
            "http_429: rate limited",
        )
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (413): payload too large"),
            "http_413: payload too large",
        )
        self.assertEqual(
            automation.sanitize_llm_detail("GitHub Models request failed (500): unexpected upstream error"),
            "http_500: request failed",
        )


if __name__ == "__main__":
    unittest.main()
