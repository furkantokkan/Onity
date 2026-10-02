#!/usr/bin/env python3
"""Validate OnityTask `framelifecycle` Player reports (plan 16, PERF-3, gates G2/G3).

Usage:
  python validate-framelifecycle.py REPORT.json [REPORT.json ...] [--allow-subset] [--json-out SUMMARY.json]

Each report must come from a non-development Release Player and pass every structural check:
goldens, bracket resolution, required library systems, boundary checklist, A,B,B,A order,
identical frame counts per library, control-subtracted costs recomputed from raw ticks, and the
frame-level sanity bracket within the report's tolerance. A report produced with
-onityTaskFrameLifecycleArms is a subset report and is accepted only with --allow-subset.

With three or more reports (the per-backend process rounds), the G2/G3 arms are also evaluated
against the plan 16 claim rule (median of per-round ratios <= 0.95, worst round <= 1.00). That
evaluation is informational; benchmark-only packets have no timing gate.

Informational output that never fails a report: allocation availability (a report without a calibrated
counter, or with individual slices reported as -1, passes and says so), and the UniTask denominator
coefficient of variation per arm (limit 5% at 128 operations, 10% at 4096).
"""
import argparse
import json
import math
import statistics
import sys

PREDECLARED = [
    "nextframe-1x128", "nextframe-3x128", "yield-1x128", "yield-3x128", "nextframe-typed-1x128",
    "nextframe-1x4096", "nextframe-3x4096", "yield-1x4096", "yield-3x4096",
    "delayframes3-none", "delayframes3-registered", "delayframes3-cancel50",
    "delay50ms-none", "delay50ms-registered", "delay50ms-cancel50",
    "waituntil-none", "waituntil-registered", "waituntil-cancel50",
]
REQUIRED_SYSTEM_COUNT = 14
GATE_MEDIAN = 0.95
GATE_WORST = 1.00
CV_LIMIT_128 = 0.05
CV_LIMIT_4096 = 0.10
NS_TOLERANCE = 1e-6


class Failure(Exception):
    pass


def check(condition, message):
    if not condition:
        raise Failure(message)


def median_ticks(values):
    ordered = sorted(values)
    middle = len(ordered) // 2
    if len(ordered) % 2 == 1:
        return ordered[middle]
    # The Player uses integer (long) division for the even-count median.
    total = ordered[middle - 1] + ordered[middle]
    return int(total / 2) if total >= 0 else -int(-total / 2)


def validate_sample(arm, library, sample, frames, expected_order, frequency, warmup, calibrated):
    tag = "%s %s %s %d" % (arm["id"], library, "warmup" if warmup else "sample", sample["index"])
    ops = arm["cohortSize"]
    check(sample["validated"], tag + ": not validated")
    check(sample["warmup"] == warmup, tag + ": warmup flag mismatch")
    check(sample["operations"] == ops, tag + ": operations != cohort size")
    check(sample["libraryOrder"] == expected_order, tag + ": A,B,B,A order violated")
    expected_canceled = (ops + 1) // 2 if arm["tokenMode"] == "Cancel50" else 0
    check(sample["succeeded"] + sample["canceled"] == ops, tag + ": completions != operations")
    check(sample["canceled"] == expected_canceled, tag + ": canceled count %d != %d" % (sample["canceled"], expected_canceled))
    if arm["shape"] == "Typed int":
        check(sample["resultSum"] == 42 * ops, tag + ": typed result checksum")
    check(sample["queuesEmptyAtEnd"], tag + ": library queues not empty at sample end")
    check(0 <= sample["consumeFrame"] and sample["consumeFrame"] + 1 < sample["frames"],
          tag + ": return frame outside the window")
    if frames is not None:
        check(sample["frames"] == frames, tag + ": frame count differs from the arm frame count")
    for key in ("frameLibraryTicks", "frameInactiveTicks", "frameScheduleTicks", "frameConsumeTicks", "frameWholeTicks"):
        check(len(sample[key]) == sample["frames"], tag + ": " + key + " length")
    for key in ("controlLibraryTicks", "controlAllTicks", "controlWholeTicks"):
        check(len(sample[key]) == 8, tag + ": " + key + " length")
    if arm["tokenMode"] != "None":
        check(sample["registrationCheck"].startswith("passed"), tag + ": registration check")

    # Recompute the control-subtracted cost from raw ticks.
    control = median_ticks(sample["controlLibraryTicks"])
    check(control == sample["controlMedianLibraryTicks"], tag + ": control median mismatch")
    library_ticks = sum(sample["frameLibraryTicks"][f] + sample["frameScheduleTicks"][f] + sample["frameConsumeTicks"][f]
                        for f in range(sample["frames"]))
    check(library_ticks == sample["libraryTicks"], tag + ": library tick sum mismatch")
    net = library_ticks - sample["frames"] * control
    check(net == sample["netLibraryTicks"], tag + ": net tick mismatch")
    ns = net * 1e9 / frequency / ops
    check(math.isclose(ns, sample["nanosecondsPerOperation"], rel_tol=NS_TOLERANCE, abs_tol=1e-6),
          tag + ": ns/op mismatch")
    all_ticks = library_ticks + sum(sample["frameInactiveTicks"])
    check(all_ticks == sample["allTicks"], tag + ": all-bracket tick sum mismatch")
    check(sum(sample["frameWholeTicks"]) == sample["wholeTicks"], tag + ": whole-loop tick sum mismatch")
    net_all = all_ticks - sample["frames"] * median_ticks(sample["controlAllTicks"])
    net_whole = sample["wholeTicks"] - sample["frames"] * median_ticks(sample["controlWholeTicks"])
    check(net_all == sample["netAllTicks"] and net_whole == sample["netWholeTicks"], tag + ": sanity tick mismatch")
    if sample["heapValid"]:
        check(sample["heapBytes"] >= 0 and math.isclose(sample["rawBytesPerOperation"], sample["heapBytes"] / ops),
              tag + ": raw bytes/op mismatch")
    else:
        check(sample["rawBytesPerOperation"] == -1, tag + ": invalid heap slice must report raw -1")
    check(sample["bytesValid"] == (sample["heapValid"] and sample["controlHeapValid"]), tag + ": bytesValid flag")
    if sample["bytesValid"]:
        controlled = (sample["heapBytes"] - sample["controlHeapBytes"] * sample["frames"] / 8) / ops
        check(sample["controlHeapBytes"] >= 0 and math.isclose(sample["bytesPerOperation"], controlled, rel_tol=1e-9,
                                                               abs_tol=1e-9), tag + ": control-subtracted bytes/op mismatch")
    else:
        check(sample["bytesPerOperation"] == -1, tag + ": invalid heap slice must report -1")
    if not calibrated:
        check(not sample["heapValid"] and not sample["controlHeapValid"], tag + ": heap slice valid without a calibrated counter")
    return ns, net_all, net_whole


def coefficient_of_variation(values):
    """Sample standard deviation over the mean; infinity when the mean is not positive."""
    mean = statistics.fmean(values)
    if mean <= 0 or len(values) < 2:
        return float("inf")
    return statistics.stdev(values) / mean


def count_slices(arms):
    """Counts the cohort windows of a report and how many reported no valid control-subtracted bytes."""
    total = 0
    unavailable = 0
    for arm in arms:
        for key in ("onity", "uniTask"):
            for sample in arm[key]["warmups"] + arm[key]["samples"]:
                total += 1
                if not sample["bytesValid"]:
                    unavailable += 1
    return total, unavailable


def validate_report(path, allow_subset):
    with open(path, encoding="utf-8-sig") as handle:
        report = json.load(handle)
    check(report.get("suite") == "framelifecycle" and report.get("schemaVersion") == 1, "not a framelifecycle v1 report")
    check(report["completed"] and not report.get("failure"), "report failed: %s" % (report.get("failure") or "")[:400])
    check(not report.get("loggedError"), "logged error: %s" % (report.get("loggedError") or "")[:400])
    env = report["environment"]
    check(not env["isDevelopment"], "development Player")
    check(env["scriptingBackend"] in ("Mono", "IL2CPP"), "unknown backend")
    check(report["isIl2Cpp"] == (env["scriptingBackend"] == "IL2CPP"), "backend flag mismatch")
    check(report["onityFlowExecutionContext"] is False and env["flowExecutionContext"] is False, "flow must be off")
    check(not env["trackerEnabled"], "tracker enabled")
    check(report["onityRunnerPoolCapacity"] == 128, "runner retention must be the default 128")
    check(report["warmupCohorts"] == 3 and report["sampleCohorts"] == 8 and report["largeCohortSampleCohorts"] == 6
          and report["controlFramesPerSample"] == 8, "cohort protocol changed")
    # Plan 16 allows HeapDelta -1 when no calibrated counter exists; timing evidence stays valid.
    calibrated = bool(report["counterCalibrated"])
    if calibrated:
        check(report["counterKind"] in ("PerThread", "HeapDelta"), "calibrated counter of unknown kind")
    check(report["requiredSystemsValidated"] and len(report["requiredSystems"]) == REQUIRED_SYSTEM_COUNT
          and all(entry.endswith(" x1") for entry in report["requiredSystems"]), "required library systems not bracketed once")
    brackets = report["brackets"]
    owners = {bracket["owner"] for bracket in brackets}
    check(owners == {"OnityTask", "UniTask"}, "brackets must cover both libraries")
    check(all(bracket["totalCalls"] > 0 for bracket in brackets), "a bracket never ran")
    check(report["goldensPassed"] and report["validatedCohorts"] == report["expectedCohorts"] > 0, "goldens failed")
    boundary = report["boundaryChecklist"]
    for key in ("passed", "everyBracketResolved", "returnPassesInsideBrackets", "noLibraryWorkOutsideBrackets",
                "sameFrameCountPerLibrary", "cancellationRegistrationsDisposedInsideMeasuredFrames",
                "harnessInertOutsideBrackets"):
        check(boundary[key] is True, "boundary checklist item failed: " + key)
    check(boundary["unresolvedBracketEvents"] == 0 and boundary["bracketCallAnomalies"] == 0, "unresolved brackets")
    check("harnessCallAnomalies" in boundary and "armStartDiscardedEvents" in report,
          "report lacks the measured harness-call fields (written by the BENCH-INT runner)")
    check(boundary["harnessCallAnomalies"] == 0, "harness systems did not run exactly once per frame")
    check(not report["harnessDeclaresUnityUpdate"]
          and report["harnessSystemsInstalled"] == report["harnessSystemsExpected"] == 3 + 2 * len(brackets),
          "harness PlayerLoop systems or MonoBehaviour updates differ from the bracket design")
    check(report["predeclaredArms"] == PREDECLARED, "predeclared arm set changed")

    arm_ids = [arm["id"] for arm in report["arms"]]
    subset = bool(report["armFilter"])
    if subset:
        check(allow_subset, "subset report (armFilter=%s); pass --allow-subset for development runs" % report["armFilter"])
        check(arm_ids == report["selectedArms"], "arms differ from the selected subset")
    else:
        check(arm_ids == PREDECLARED, "arms differ from the predeclared set")

    tolerance = report["sanityTolerance"]
    frequency = report["stopwatchFrequency"]
    results = {}
    for arm in report["arms"]:
        samples = 6 if arm["cohortSize"] > 128 else 8
        check(arm["warmupCohorts"] == 3 and arm["sampleCohorts"] == samples, arm["id"] + ": cohort counts")
        check(arm["policy"] in ("default", "matched"), arm["id"] + ": policy label")
        if arm["cohortSize"] <= 128:
            check(arm["policy"] == "default", arm["id"] + ": 128 arms run at the default policy")
        frames = arm["framesPerCohort"]
        check(2 <= frames <= 16 and arm["sameFrameCountPerLibrary"] and arm["validated"], arm["id"] + ": frame protocol")
        per_library = {}
        for key, first in (("onity", 0), ("uniTask", 1)):
            library = arm[key]
            check(len(library["warmups"]) == 3 and len(library["samples"]) == samples, arm["id"] + ": sample arrays")
            for index, sample in enumerate(library["warmups"]):
                validate_sample(arm, key, sample, None, (index + first) % 2, frequency, True, calibrated)
            values = []
            net_all = 0
            net_whole = 0
            for index, sample in enumerate(library["samples"]):
                ns, sample_all, sample_whole = validate_sample(
                    arm, key, sample, frames, (index + first) % 2, frequency, False, calibrated)
                values.append(ns)
                net_all += sample_all
                net_whole += sample_whole
            check(math.isclose(statistics.median(values), library["medianNanosecondsPerOperation"], rel_tol=1e-6, abs_tol=1e-6),
                  arm["id"] + " " + key + ": median mismatch")
            deviation = abs(net_whole - net_all) / net_all if net_all > 0 else float("inf")
            check(math.isclose(deviation, library["sanityDeviation"], rel_tol=1e-6, abs_tol=1e-9),
                  arm["id"] + " " + key + ": sanity deviation mismatch")
            check(deviation <= tolerance, "%s %s: sanity bracket disagrees by %.2f%% (> %.0f%%)"
                  % (arm["id"], key, deviation * 100, tolerance * 100))
            per_library[key] = {"values": values, "median": statistics.median(values), "sanity": deviation,
                                "cv": coefficient_of_variation(values),
                                "bytesPerOperation": library["medianBytesPerOperation"],
                                "consumeFrame": library["medianConsumeFrame"]}
        cv_limit = CV_LIMIT_4096 if arm["cohortSize"] > 128 else CV_LIMIT_128
        pairs = [o / u for o, u in zip(per_library["onity"]["values"], per_library["uniTask"]["values"])]
        check(all(u > 0 for u in per_library["uniTask"]["values"]), arm["id"] + ": non-positive UniTask cost")
        results[arm["id"]] = {
            "gate": arm["gate"], "policy": arm["policy"], "frames": frames,
            "onityNs": per_library["onity"]["median"], "uniTaskNs": per_library["uniTask"]["median"],
            "medianPairRatio": statistics.median(pairs), "worstPairRatio": max(pairs),
            "ratioOfMedians": per_library["onity"]["median"] / per_library["uniTask"]["median"],
            "onitySanity": per_library["onity"]["sanity"], "uniTaskSanity": per_library["uniTask"]["sanity"],
            "onityBytesPerOperation": per_library["onity"]["bytesPerOperation"],
            "uniTaskBytesPerOperation": per_library["uniTask"]["bytesPerOperation"],
            "onityConsumeFrame": per_library["onity"]["consumeFrame"],
            "uniTaskConsumeFrame": per_library["uniTask"]["consumeFrame"],
            "uniTaskCv": per_library["uniTask"]["cv"], "uniTaskCvLimit": cv_limit,
            "uniTaskCvWithinLimit": per_library["uniTask"]["cv"] <= cv_limit,
        }
    slices, unavailable = count_slices(report["arms"])
    if not calibrated:
        allocation = "unavailable (counter %s; bytes/op reported as -1)" % report["counterKind"]
    elif unavailable:
        allocation = "partial (%d of %d slices unavailable, reported as -1)" % (unavailable, slices)
    else:
        allocation = "calibrated"
    return {"path": path, "backend": env["scriptingBackend"], "buildGuid": env["buildGuid"], "subset": subset,
            "counterKind": report["counterKind"], "allocation": allocation,
            "installFrameDiscardedEvents": report["installFrameDiscardedEvents"],
            "armStartDiscardedEvents": report["armStartDiscardedEvents"],
            "worstSanityDeviation": boundary["worstSanityDeviation"],
            "arms": results}


def evaluate_rounds(validated):
    backends = {}
    for entry in validated:
        backends.setdefault(entry["backend"], []).append(entry)
    evaluation = {}
    for backend, rounds in backends.items():
        if len(rounds) < 3:
            continue
        arms = {}
        for arm_id in rounds[0]["arms"]:
            if rounds[0]["arms"][arm_id]["gate"] not in ("G2", "G3"):
                continue
            ratios = [entry["arms"][arm_id]["medianPairRatio"] for entry in rounds if arm_id in entry["arms"]]
            if len(ratios) != len(rounds):
                continue
            arms[arm_id] = {"roundRatios": ratios, "median": statistics.median(ratios), "worst": max(ratios),
                            "meetsClaimRule": statistics.median(ratios) <= GATE_MEDIAN and max(ratios) <= GATE_WORST}
        evaluation[backend] = arms
    return evaluation


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("reports", nargs="+")
    parser.add_argument("--allow-subset", action="store_true", help="accept development reports run with an arm filter")
    parser.add_argument("--json-out", help="write the validated summary here")
    args = parser.parse_args()

    validated = []
    failed = False
    for path in args.reports:
        try:
            entry = validate_report(path, args.allow_subset)
        except (Failure, KeyError, TypeError, ValueError, ZeroDivisionError, OSError) as error:
            print("FAIL %s: %s" % (path, error))
            failed = True
            continue
        validated.append(entry)
        print("PASS %s (%s, allocation %s%s, worst sanity %.2f%%, discarded events install %d / pre-arm %d)" % (
            path, entry["backend"], entry["allocation"], ", subset" if entry["subset"] else "",
            entry["worstSanityDeviation"] * 100, entry["installFrameDiscardedEvents"], entry["armStartDiscardedEvents"]))
        print("  %-26s %-8s %6s %10s %10s %8s %8s %8s %8s %9s %9s %8s" % (
            "arm", "gate", "frames", "onity ns", "uni ns", "pairMed", "pairMax", "sanO%", "sanU%", "onityB/op", "uniB/op",
            "uniCV%"))
        noisy = []
        for arm_id, arm in entry["arms"].items():
            print("  %-26s %-8s %6d %10.1f %10.1f %8.3f %8.3f %8.2f %8.2f %9.1f %9.1f %8.2f%s" % (
                arm_id, arm["gate"], arm["frames"], arm["onityNs"], arm["uniTaskNs"], arm["medianPairRatio"],
                arm["worstPairRatio"], arm["onitySanity"] * 100, arm["uniTaskSanity"] * 100,
                arm["onityBytesPerOperation"], arm["uniTaskBytesPerOperation"], arm["uniTaskCv"] * 100,
                "" if arm["uniTaskCvWithinLimit"] else " !"))
            if not arm["uniTaskCvWithinLimit"]:
                noisy.append("%s %.2f%% > %.0f%%" % (arm_id, arm["uniTaskCv"] * 100, arm["uniTaskCvLimit"] * 100))
        if noisy:
            print("  INFO UniTask denominator CV above the limit (informational, not a failure): " + "; ".join(noisy))

    evaluation = evaluate_rounds(validated)
    for backend, arms in evaluation.items():
        print("Claim-rule evaluation (informational), %s, %d rounds:" % (backend, len(next(iter(arms.values()))["roundRatios"]) if arms else 0))
        for arm_id, arm in arms.items():
            print("  %-26s median %.3f worst %.3f %s" % (arm_id, arm["median"], arm["worst"],
                                                         "meets" if arm["meetsClaimRule"] else "misses"))
    if args.json_out:
        with open(args.json_out, "w", encoding="utf-8") as handle:
            json.dump({"passed": not failed, "reports": validated, "claimRuleEvaluation": evaluation}, handle, indent=2)
    return 1 if failed or not validated else 0


if __name__ == "__main__":
    sys.exit(main())
