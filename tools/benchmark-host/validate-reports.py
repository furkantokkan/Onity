"""Validate fixed builder-lifecycle evidence without launching Players or selecting favorable results.

Promoted from the returnbuffer-20260930 packet helper (plan 16 PERF-0). Adds the UniTask denominator
stability check, --gate claim|packet evaluation (plan 1.2 / 1.3), published-style ratio ranges, a
phase table, generated-summary.json and summary.md. Never launches Unity or a Player.
"""
import argparse
from datetime import datetime
import hashlib
import itertools
import json
import math
from pathlib import Path
import re
import statistics
import sys


def require(condition, message):
    if not condition:
        raise ValueError(message)


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def close(actual, expected, label):
    require(isinstance(actual, (int, float)) and math.isfinite(actual), label + ": nonfinite/missing value")
    require(math.isclose(actual, expected, rel_tol=1e-9, abs_tol=1e-7), label + ": arithmetic mismatch")


def integer(value, label, minimum=0):
    require(type(value) is int and value >= minimum, label + ": invalid integer")
    return value


def validate_environment(env, backend, flow, build_guid=None, capacity=128):
    require(env["scriptingBackend"] == backend, "backend mismatch")
    require(env["isDevelopment"] is False, "development build")
    require(env.get("isEditor", False) is False, "Editor report")
    if flow is not None:
        require(env["flowExecutionContext"] is flow, "environment flow mismatch")
    require(isinstance(env["flowExecutionContext"], bool), "environment flow missing")
    if capacity is not None:
        require(env["runnerPoolCapacity"] == capacity, "capacity changed")
    require(env["trackerEnabled"] is False and env["trackerStackTraceEnabled"] is False, "tracker enabled")
    require(env["executionContextPath"] in ("InternalPair", "PublicFallback"), "unknown context path")
    require(bool(env["executionContextEvidence"]), "missing context evidence")
    require(bool(env["buildGuid"]), "missing build GUID")
    if build_guid is not None:
        require(env["buildGuid"] == build_guid, "metric build GUID mismatch")
    build = env["build"]
    require("Release" in build["codeOptimization"] and "BuildOptions.None" in build["codeOptimization"], "not Release")
    require(build["il2CppCompilerConfiguration"] == "Release", "IL2CPP configuration changed")
    require(build["uniTaskVersion"] == "2.5.11", "pinned UniTask version changed")
    require("2e993ff18f28c931602a07292df0b0804eebef99" in build["uniTaskPackageId"], "UniTask pin changed")


def validate_startup(path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    rows = [re.split(r"\t|\\t", line) for line in lines if line.strip()]
    stages = [row[1] for row in rows if len(row) >= 2]
    expected = ["early-initialization", "before-scene-load", "after-scene-load", "arguments-detected",
                "benchmark-entry", "benchmark-completed"]
    require(stages == expected, "startup stages missing/out of order/failed")
    require(rows[4][2] == "builderlifecycle", "startup suite mismatch")


def validate_control(control, steps, brackets, checksum):
    require(len(control["completionTicks"]) == steps, "control completion shape")
    ticks = [control["scheduleTicks"], *control["completionTicks"], control["consumptionTicks"],
             control["drainFirstTicks"], control["drainSecondTicks"]]
    for tick in ticks:
        integer(tick, "control ticks")
    require(control["totalTicks"] == sum(ticks), "control total mismatch")
    require(control["phaseBracketCount"] == brackets and control["checksum"] == checksum, "control brackets/checksum")


def validate_sample(sample, metric, index, frequency, il2cpp):
    count = metric["cohortSize"]
    steps = metric["sequentialSuspensions"]
    invocations = metric["invocations"]
    operations = count * invocations
    onity = metric["library"] == "OnityTask"
    typed = metric["shape"] == "Typed int"
    brackets = (steps + 4) * invocations
    require(sample["index"] == index and sample["libraryOrder"] == ((index & 1) if onity else 1 - (index & 1)),
            "sample index/library order")
    require(sample["operations"] == operations and sample["phaseBracketCount"] == brackets, "sample work/brackets")
    require(sample["validated"] is True and sample["safetyFrames"] >= 2, "validation/safety frames")
    require(sample["safetyEndFrame"] - sample["safetyStartFrame"] == sample["safetyFrames"], "frame arithmetic")
    require(len(sample["completionTicks"]) == steps and len(sample["completionNanosecondsPerOperation"]) == steps,
            "completion phase shape")
    ticks = [sample["scheduleTicks"], *sample["completionTicks"], sample["consumptionTicks"],
             sample["drainFirstTicks"], sample["drainSecondTicks"]]
    for tick in ticks:
        integer(tick, "phase ticks")
    require(sample["totalTicks"] == sum(ticks), "phase total mismatch")
    factor = 1e9 / frequency / operations
    for field, tick in (("scheduleNanosecondsPerOperation", sample["scheduleTicks"]),
                        ("consumptionNanosecondsPerOperation", sample["consumptionTicks"]),
                        ("drainFirstNanosecondsPerOperation", sample["drainFirstTicks"]),
                        ("drainSecondNanosecondsPerOperation", sample["drainSecondTicks"]),
                        ("totalNanosecondsPerOperation", sample["totalTicks"])):
        close(sample[field], tick * factor, field)
    for actual, tick in zip(sample["completionNanosecondsPerOperation"], sample["completionTicks"]):
        close(actual, tick * factor, "completion ns/op")
    for field in ("registrations", "callbacks", "completions", "gateResults"):
        require(sample[field] == operations * steps, field + " count")
    require(sample["nativeConsumptions"] == operations, "native single consumption count")
    require(sample["resultChecksum"] == operations * (42 if typed else 1), "native result checksum")
    expected_onity = count if il2cpp and onity else 0
    expected_uni = count if il2cpp and not onity else 0
    if sample["expectedOnityBeforeFirstPassPerInvocation"] == -1:
        # Current protocol: Onity's pre-pass count is recorded, not asserted (return-on-unwind leaves it empty).
        integer(sample["onityBeforeFirstPassSum"], "Onity observed queue sum")
    else:
        require(sample["expectedOnityBeforeFirstPassPerInvocation"] == expected_onity, "Onity expected queue count")
        require(sample["onityBeforeFirstPassSum"] == expected_onity * invocations, "Onity observed queue sum")
    require(sample["expectedUniBeforeFirstPassPerInvocation"] == expected_uni, "UniTask expected queue count")
    require(sample["uniBeforeFirstPassSum"] == expected_uni * invocations, "UniTask observed queue sum")
    for field in ("expectedBeforeFirstPassChecks", "zeroBeforeInvocationCount", "zeroBetweenDrainsCount", "zeroAfterDrainsCount"):
        require(sample[field] == invocations, field + " proof count")
    validate_control(sample["timestampControl"], steps, brackets, 0)
    validate_control(sample["arrayLoopControl"], steps, brackets, operations * (1 + steps))


def phase_medians(metric):
    samples = metric["samples"]
    fields = {"schedule": "scheduleNanosecondsPerOperation", "consume": "consumptionNanosecondsPerOperation",
              "returnFirst": "drainFirstNanosecondsPerOperation", "returnSecond": "drainSecondNanosecondsPerOperation",
              "total": "totalNanosecondsPerOperation"}
    result = {name: statistics.median(sample[field] for sample in samples) for name, field in fields.items()}
    for step in range(metric["sequentialSuspensions"]):
        result["completion" + str(step + 1)] = statistics.median(
            sample["completionNanosecondsPerOperation"][step] for sample in samples)
    result["completionSum"] = statistics.median(sum(sample["completionNanosecondsPerOperation"]) for sample in samples)
    result["returnSum"] = statistics.median(sample["drainFirstNanosecondsPerOperation"]
                                            + sample["drainSecondNanosecondsPerOperation"] for sample in samples)
    return result


def validate_report(path, expected_backend=None):
    data = load(path)
    require(data["schemaVersion"] == 1 and data["completed"] is True, "report schema/completion")
    require(not data["failure"] and not data["loggedError"], "report failure/logged error")
    require(data.get("isEditor", False) is False, "Editor report")
    backend = data["environment"]["scriptingBackend"]
    require(backend in ("Mono", "IL2CPP") and (expected_backend is None or backend == expected_backend), "report backend")
    validate_environment(data["environment"], backend, None)
    require(data["isIl2Cpp"] is (backend == "IL2CPP"), "backend compile flag")
    require(data["onityRunnerPoolCapacity"] == 128 and data["uniTaskMaxPoolSize"] == 2147483647, "pool policy")
    require(data["warmupsPerLibrary"] == 2 and data["samplesPerLibrary"] == 8, "sample policy")
    require(data["goldensPassed"] is True and data["multiSuspensionGoldensPassed"] == 8, "fixed goldens")
    require(data["preallocatedGateCapacity"] == 16384 and data["nativeTaskArrayCapacity"] == 4096, "buffer capacity")
    frequency = integer(data["stopwatchFrequency"], "Stopwatch frequency", 1)
    require("ELAPSED" in data["measurementScope"] and "both libraries" in data["measurementScope"], "elapsed scope")
    require("unsubtracted" in data["controls"].lower() and "unsubtracted" in data["returnBoundary"].lower(), "control subtraction")
    require(data["allocation"].startswith("UNAVAILABLE:") and "no zero-allocation" in data["allocation"], "allocation claim")
    require("Onity.Unity.Async.OnityTaskMainThreadDispatcher.Drain" in data["drainBinding"]
            or data["drainBinding"].startswith("absent"), "Onity binding")
    # BENCH-INT made flow off the primary arm; earlier reports used flow on as primary.
    primary_flow = not data.get("flowPolicy", "").startswith("Flow=false")
    require("ContinuationQueue.Run" in data["uniDrainBinding"] and "LastPostLateUpdate" in data["uniDrainBinding"], "UniTask binding")
    validate_startup(path.with_suffix(".startup.log"))
    # Base matrix (default retention) is mandatory; PERF-2 may add "matched" 4096 arms (retentionPolicy field).
    expected = set(itertools.product((True, False), ("Untyped", "Typed int"), (1, 128, 4096), (1, 4), ("OnityTask", "UniTask")))
    actual = set()
    matched_keys = set()
    cases = {}
    for metric in data["metrics"]:
        retention = metric.get("retentionPolicy", "default")
        require(retention in ("default", "matched"), "unknown retention policy")
        key = (metric["onityFlowExecutionContext"], metric["shape"], metric["cohortSize"],
               metric["sequentialSuspensions"], metric["library"])
        target = actual if retention == "default" else matched_keys
        require(key in expected and key not in target, "metric matrix/duplicate")
        require(retention == "default" or key[2] == 4096, "matched policy only valid at 4096")
        target.add(key)
        flow, shape, count, steps, library = key
        invocations = max(1, 4096 // count)
        require(metric["invocations"] == invocations and metric["operations"] == count * invocations, "fixed invocation policy")
        require(metric["diagnosticOnly"] is (count == 1 or flow != primary_flow), "diagnostic classification")
        validate_environment(metric["environment"], backend, flow, data["environment"]["buildGuid"],
                             128 if retention == "default" else None)
        lengths = [(2, 8)]
        if count == 4096:
            lengths.append((3, 12))
        require((len(metric["warmups"]), len(metric["samples"])) in lengths, "warmup/sample count")
        for field in ("warmups", "samples"):
            for index, sample in enumerate(metric[field]):
                validate_sample(sample, metric, index, frequency, backend == "IL2CPP")
        case_key = f"flow={flow};shape={shape};N={count};steps={steps}"
        if retention != "default":
            case_key += ";retention=matched"
        case = cases.setdefault(case_key, {"flow": flow, "shape": shape, "N": count, "steps": steps, "retention": retention,
                                           "diagnosticOnly": count == 1 or not flow, "rawMedianPhaseNs": {},
                                           "sampleCv": {}})
        case["rawMedianPhaseNs"][library] = phase_medians(metric)
        totals = [sample["totalNanosecondsPerOperation"] for sample in metric["samples"]]
        case["sampleCv"][library] = (statistics.stdev(totals) / statistics.mean(totals)) if len(totals) > 1 and statistics.mean(totals) else 0.0
    require(actual == expected, "incomplete metric matrix")
    require(not matched_keys or all(k[2] == 4096 for k in matched_keys), "matched arms outside 4096")
    for case in cases.values():
        onity = case["rawMedianPhaseNs"]["OnityTask"]
        uni = case["rawMedianPhaseNs"]["UniTask"]
        case["onityOverUniRatios"] = {phase: onity[phase] / uni[phase] if uni[phase] else None for phase in onity}
    return {"valid": True, "backend": backend, "sha256": sha(path), "metrics": len(data["metrics"]),
            "warmups": sum(len(m["warmups"]) for m in data["metrics"]),
            "measuredSamples": sum(len(m["samples"]) for m in data["metrics"]), "goldens": 8, "cases": cases,
            "nonEditorEvidence": "Successful known Release Player harness guard; schema has no explicit isEditor field."}


def noise_from_snapshots(before, after, wall):
    require(wall > 0 and math.isfinite(wall), "observation elapsed time")
    unknown = False
    cpu = 0.0
    def known(item):
        return (item.get("observed") is True and type(item.get("pid")) is int and bool(item.get("startUtc"))
                and isinstance(item.get("cpuSeconds"), (int, float)) and math.isfinite(item["cpuSeconds"]))
    for collection in (before, after):
        identities = [(item.get("pid"), item.get("startUtc")) for item in collection]
        require(len(set(identities)) == len(identities), "duplicate Unity process identity")
        for item in collection:
            require(set(item) <= {"pid", "startUtc", "cpuSeconds", "observed"}, "unsanitized Unity snapshot")
    for item in before:
        matched = [other for other in after if other.get("pid") == item.get("pid") and other.get("startUtc") == item.get("startUtc")]
        if not known(item) or len(matched) != 1 or not known(matched[0]):
            unknown = True
        else:
            delta = matched[0]["cpuSeconds"] - item["cpuSeconds"]
            if delta < 0 or not math.isfinite(delta):
                unknown = True
            else:
                cpu += delta
    for item in after:
        if sum(other.get("pid") == item.get("pid") and other.get("startUtc") == item.get("startUtc") for other in before) != 1:
            unknown = True
    fraction = cpu / wall
    status = "unknown" if unknown else "accepted" if cpu <= 0.5 and fraction <= 0.05 else "flagged"
    return unknown, cpu, fraction, status


def replacement_eligible(initial_observations, backend):
    return any(observation["backend"] == backend and observation["noiseStatus"] == "flagged"
               for observation in initial_observations)


def validate_observation(root, path, revision, backend, round_number, initial_rounds=3):
    observation_path = path.with_suffix(".observation.json")
    obs = load(observation_path)
    require(obs["schemaVersion"] == 1 and obs["suite"] == "builderlifecycle", "observation schema/suite")
    require((obs["revision"], obs["backend"], obs["round"]) == (revision, backend, round_number), "observation identity")
    require(obs["replacement"] is (round_number > initial_rounds), "replacement flag")
    require(obs["exitCode"] == 0 and obs["timedOut"] is False, "Player failure/timeout")
    require(obs["binaryStable"] is True and obs["buildMetadataStable"] is True, "build changed during process")
    binary = root / "Players" / (revision + "-" + backend) / "OnityTaskBenchmark.exe"
    require(Path(obs["executable"]).resolve() == binary.resolve(), "wrong executable path")
    require(Path(obs["report"]).resolve() == path.resolve(), "wrong report path")
    for artifact, field in ((binary, "binarySha256"), (Path(str(binary) + ".build.json"), "buildMetadataSha256"),
                            (path, "reportSha256"), (path.with_suffix(".startup.log"), "startupSha256"),
                            (path.with_suffix(".player.log"), "playerLogSha256")):
        require(sha(artifact) == obs[field].upper(), field + " changed")
    sidecar = load(Path(str(binary) + ".build.json"))
    report_build = load(path)["environment"]["build"]
    require(sidecar == report_build, "report build metadata differs from sidecar")
    unknown, cpu, fraction, status = noise_from_snapshots(obs["otherUnityBefore"], obs["otherUnityAfter"], obs["elapsedSeconds"])
    require(obs["unknown"] is unknown and obs["noiseStatus"] == status, "noise classification")
    close(obs["otherUnityCpuSeconds"], cpu, "noise CPU")
    close(obs["otherUnityWallFraction"], fraction, "noise wall fraction")
    if round_number > initial_rounds and obs.get("quietRecheckSha256"):
        validate_legacy_replacement(root, obs, backend, initial_rounds)
    elif round_number > initial_rounds:
        pregates = sorted(root.glob("quiet-pregate-*.json"))
        matches = [item for item in pregates if sha(item) == str(obs.get("quietCheckSha256")).upper()]
        require(len(matches) == 1 and load(matches[0])["quiet"] is True, "replacement round lacks a passing quiet pre-gate")
    return {"noiseStatus": status, "otherUnityCpuSeconds": cpu, "otherUnityWallFraction": fraction,
            "observationSha256": sha(observation_path), "revision": revision, "round": round_number,
            "sequence": obs["sequence"], "priorityApplied": obs.get("priorityApplied"),
            "affinityApplied": obs.get("affinityApplied")}


def validate_legacy_replacement(root, obs, backend, initial_rounds):
    quiet_path = root / "quiet-recheck.json"
    quiet = load(quiet_path)
    require(quiet["unknown"] is False and 0 <= quiet["otherUnityCpuSeconds"] <= 0.5
            and 0 <= quiet["otherUnityWallFraction"] <= 0.05, "replacement quiet prerequisite")
    require(sha(quiet_path) == obs["quietRecheckSha256"].upper(), "replacement quiet identity")
    quiet_unknown, quiet_cpu, quiet_fraction, quiet_status = noise_from_snapshots(
        quiet["before"], quiet["after"], quiet["elapsedSeconds"])
    require(not quiet_unknown and quiet_status == "accepted", "replacement quiet snapshots")
    close(quiet["otherUnityCpuSeconds"], quiet_cpu, "quiet CPU")
    close(quiet["otherUnityWallFraction"], quiet_fraction, "quiet wall fraction")
    rounds = range(1, initial_rounds + 1)
    initial_observations = [load(root / f"{rev}-{be.lower()}-r{r}.observation.json")
                            for be in ("Mono", "IL2CPP") for r in rounds for rev in ("baseline", "candidate")]
    quiet_started = datetime.fromisoformat(quiet["startedUtc"].replace("Z", "+00:00"))
    require(all(quiet_started > datetime.fromisoformat(initial["endedUtc"].replace("Z", "+00:00"))
                for initial in initial_observations), "quiet recheck predates initial rounds")
    backend_initial = [load(root / f"{rev}-{backend.lower()}-r{r}.observation.json")
                       for r in rounds for rev in ("baseline", "candidate")]
    require(replacement_eligible(backend_initial, backend), "unjustified replacement round")



def compare_pair(baseline, candidate):
    result = {}
    for key, first in baseline["cases"].items():
        second = candidate["cases"][key]
        result[key] = {"baselineOnityOverUni": first["onityOverUniRatios"],
                       "candidateOnityOverUni": second["onityOverUniRatios"],
                       "candidateOverBaselineOnity": {}, "candidateOverBaselineUni": {}}
        for library, target in (("OnityTask", "candidateOverBaselineOnity"), ("UniTask", "candidateOverBaselineUni")):
            for phase, value in first["rawMedianPhaseNs"][library].items():
                result[key][target][phase] = second["rawMedianPhaseNs"][library][phase] / value if value else None
    return result


CASE_PATTERN = re.compile(r"flow=(True|False);shape=([^;]+);N=(\d+);steps=(\d+)(;retention=matched)?")
REPORT_PATTERN = re.compile(r"^(baseline|candidate)-(mono|il2cpp)-r([1-9])\.json$")
PHASES = ("schedule", "completionSum", "consume", "returnSum", "total")


def label_case(case_key):
    match = CASE_PATTERN.fullmatch(case_key)
    flow, shape, count, steps, matched = match.groups()
    return {"flow": flow == "True", "shape": shape, "N": int(count), "steps": int(steps), "matched": bool(matched)}


def ratio_ranges(records, selections):
    """Onity/UniTask total ratio range across selected rounds and both shapes (the published table form)."""
    grouped = {}
    for backend, rounds in selections.items():
        for revision in ("baseline", "candidate"):
            for round_number in rounds:
                record = records[f"{revision}-{backend.lower()}-r{round_number}.json"]
                for case_key, case in record["cases"].items():
                    info = label_case(case_key)
                    group = (revision, backend, info["N"], info["steps"], info["flow"], info["matched"])
                    grouped.setdefault(group, []).append(case["onityOverUniRatios"]["total"])
    result = {}
    for (revision, backend, count, steps, flow, matched), values in sorted(grouped.items()):
        name = f"{revision} {backend} N={count} {steps}-susp flow={'on' if flow else 'off'}" + (" matched" if matched else "")
        result[name] = {"min": min(values), "max": max(values), "samples": len(values)}
    return result


def denominator_cv(records, selections):
    """UniTask total ns/op across the (up to six) selected processes per backend and arm."""
    rows = []
    for backend, rounds in selections.items():
        series = {}
        for revision in ("baseline", "candidate"):
            for round_number in rounds:
                record = records[f"{revision}-{backend.lower()}-r{round_number}.json"]
                for case_key, case in record["cases"].items():
                    series.setdefault(case_key, []).append((revision, round_number, case["rawMedianPhaseNs"]["UniTask"]["total"]))
        for case_key, points in sorted(series.items()):
            info = label_case(case_key)
            values = [value for _, _, value in points]
            if len(values) < 2:
                continue
            mean = statistics.mean(values)
            cv = statistics.stdev(values) / mean if mean else 0.0
            limit = 0.05 if info["N"] == 128 else 0.10 if info["N"] == 4096 else None
            median = statistics.median(values)
            worst = max(points, key=lambda point: abs(point[2] - median))
            rows.append({"backend": backend, "case": case_key, "processes": len(values), "cv": cv, "limit": limit,
                         "ok": True if limit is None else cv <= limit,
                         "farthestProcess": f"{worst[0]}-r{worst[1]}"})
    return rows


def claim_gate(records, selections, revision, flow):
    """G1 on IL2CPP: median(per-round Onity/UniTask) <= 0.95 and worst <= 1.00 at 128 and matched 4096."""
    backend = "IL2CPP"
    rounds = selections.get(backend, [])
    arms = []
    if len(rounds) < 3:
        return {"gate": "G1", "verdict": "INCONCLUSIVE", "reason": "fewer than three accepted IL2CPP pairs", "arms": arms}
    for shape in ("Untyped", "Typed int"):
        for steps in (1, 4):
            for count, matched in ((128, False), (4096, True), (4096, False)):
                key = f"flow={flow};shape={shape};N={count};steps={steps}" + (";retention=matched" if matched else "")
                ratios = []
                for round_number in rounds:
                    record = records[f"{revision}-{backend.lower()}-r{round_number}.json"]
                    case = record["cases"].get(key)
                    if case:
                        ratios.append(case["onityOverUniRatios"]["total"])
                gated = not (count == 4096 and not matched)
                arm = {"arm": key, "gated": gated, "ratios": ratios}
                if len(ratios) != len(rounds):
                    arm.update({"status": "MISSING", "pass": False if gated else None})
                else:
                    median, worst = statistics.median(ratios), max(ratios)
                    arm.update({"median": median, "worst": worst,
                                "pass": (median <= 0.95 and worst <= 1.00) if gated else None,
                                "status": "gated" if gated else "reported (default-128 retention at 4096)"})
                arms.append(arm)
    gated_arms = [arm for arm in arms if arm["gated"]]
    verdict = "PASS" if gated_arms and all(arm["pass"] for arm in gated_arms) else "FAIL"
    return {"gate": "G1", "backend": backend, "revision": revision, "flowExecutionContext": flow, "verdict": verdict,
            "thresholds": {"median": 0.95, "worstRound": 1.00}, "arms": arms,
            "otherGates": "G2-G6 come from their own suites (framelifecycle, primary, whenany, threadpool); allocation is not in this suite."}


def packet_gate(records, selections, targets, target_phase, no_timing, flow_filter):
    result = {"verdict": "PASS", "checks": [], "targetPhase": target_phase}

    def add(name, ok, detail=""):
        result["checks"].append({"check": name, "ok": ok, "detail": detail})
        if not ok:
            result["verdict"] = "FAIL"

    bad = [row for row in denominator_cv(records, selections) if not row["ok"]]
    add("UniTask denominator CV (<=5% at 128, <=10% at 4096)", not bad,
        "; ".join(f"{r['backend']} {r['case']} cv={r['cv']:.3f} limit={r['limit']} farthest={r['farthestProcess']}" for r in bad))
    for backend in selections:
        add(f"{backend}: three accepted pairs", len(selections[backend]) >= 3, f"selected rounds {selections[backend]}")
    if no_timing:
        result["note"] = "benchmark-only packet: no timing gate; arms ran, validation passed."
        return result
    target_re = re.compile(targets) if targets else None
    for backend, rounds in selections.items():
        if len(rounds) < 3:
            continue
        for key in sorted(records[f"baseline-{backend.lower()}-r{rounds[0]}.json"]["cases"]):
            info = label_case(key)
            if info["N"] == 1 or (flow_filter is not None and info["flow"] != flow_filter):
                continue
            ratios, phase_ratios, cvs = [], [], []
            for round_number in rounds:
                base = records[f"baseline-{backend.lower()}-r{round_number}.json"]["cases"][key]
                cand = records[f"candidate-{backend.lower()}-r{round_number}.json"]["cases"][key]
                ratios.append(cand["rawMedianPhaseNs"]["OnityTask"]["total"] / base["rawMedianPhaseNs"]["OnityTask"]["total"])
                phase_ratios.append(cand["rawMedianPhaseNs"]["OnityTask"][target_phase] / base["rawMedianPhaseNs"]["OnityTask"][target_phase])
                cvs.extend([base["sampleCv"]["OnityTask"], cand["sampleCv"]["OnityTask"]])
            pooled = math.sqrt(sum(value * value for value in cvs) / len(cvs))
            median = statistics.median(ratios)
            name = f"{backend} {key}"
            if backend == "IL2CPP" and target_re is not None and target_re.search(name):
                need = max(0.02, 1.5 * pooled)
                add(f"target improves: {name}", (1 - median) > need,
                    f"improvement={1 - median:.4f} need>{need:.4f} (pooled CV {pooled:.4f})")
                add(f"target phase {target_phase} moves down: {name}", statistics.median(phase_ratios) < 1.0,
                    f"phase ratio median={statistics.median(phase_ratios):.4f}")
            add(f"no regression beyond repeat spread: {name}", median <= 1 + pooled,
                f"candidate/baseline={median:.4f} spread={pooled:.4f}")
    if target_re is not None and not any(c["check"].startswith("target improves") for c in result["checks"]):
        add("target arms matched by --target-arms", False, "pattern matched no IL2CPP arm")
    return result


def phase_table(records, selections):
    lines = []
    for backend, rounds in selections.items():
        if not rounds:
            continue
        for revision in ("baseline", "candidate"):
            lines.append(f"\n### {backend} / {revision} (median across rounds {rounds}; ns per operation)\n")
            lines.append("| Arm | Onity sched | Onity completion | Onity consume | Onity return | Onity total | UniTask total | Ratio |")
            lines.append("|---|---:|---:|---:|---:|---:|---:|---:|")
            for key in sorted(records[f"{revision}-{backend.lower()}-r{rounds[0]}.json"]["cases"]):
                values = {phase: [] for phase in PHASES}
                uni, ratio = [], []
                for round_number in rounds:
                    case = records[f"{revision}-{backend.lower()}-r{round_number}.json"]["cases"][key]
                    for phase in PHASES:
                        values[phase].append(case["rawMedianPhaseNs"]["OnityTask"][phase])
                    uni.append(case["rawMedianPhaseNs"]["UniTask"]["total"])
                    ratio.append(case["onityOverUniRatios"]["total"])
                med = {phase: statistics.median(vals) for phase, vals in values.items()}
                lines.append(f"| {key} | {med['schedule']:.1f} | {med['completionSum']:.1f} | {med['consume']:.1f} | "
                             f"{med['returnSum']:.1f} | {med['total']:.1f} | {statistics.median(uni):.1f} | {statistics.median(ratio):.3f} |")
    return "\n".join(lines)


def build_markdown(summary, table):
    out = ["# Benchmark validation summary", "", f"Suite: {summary['suite']}", "", "## Round selection", ""]
    for backend, item in summary["selection"].items():
        out.append(f"- {backend}: {item['status']}, rounds {item['selectedRounds']}")
    out += ["", "## Onity/UniTask total ratio ranges (selected rounds, both shapes)", "",
            "| Group | Min | Max | Values |", "|---|---:|---:|---:|"]
    for name, row in summary["ratioRanges"].items():
        out.append(f"| {name} | {row['min']:.3f} | {row['max']:.3f} | {row['samples']} |")
    bad = [row for row in summary["denominatorCv"] if not row["ok"]]
    out += ["", "## UniTask denominator stability", "", f"{len(summary['denominatorCv'])} arms checked, {len(bad)} above limit."]
    for row in bad:
        out.append(f"- {row['backend']} {row['case']}: CV {row['cv']:.3f} > {row['limit']} (farthest {row['farthestProcess']})")
    if summary.get("gate"):
        out += ["", "## Gate", "", "```json", json.dumps(summary["gate"], indent=2), "```"]
    out += ["", "## Phase table", table, "",
            "_No automatic general superiority claim: review against the claim contract (plan 16 section 1)._"]
    return "\n".join(out) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-root", type=Path, default=Path(__file__).parent)
    parser.add_argument("--reports", nargs="*", type=Path)
    parser.add_argument("--check-only", action="store_true", help="Validate/read only; write nothing.")
    parser.add_argument("--output-dir", type=Path, help="Where generated-summary.json and summary.md go (default evidence root).")
    parser.add_argument("--force", action="store_true", help="Overwrite existing generated files.")
    parser.add_argument("--initial-rounds", type=int, default=3)
    parser.add_argument("--gate", choices=("claim", "packet"))
    parser.add_argument("--revision", default="candidate", help="claim gate: revision whose ratios are gated")
    parser.add_argument("--flow", choices=("on", "off", "all"), default=None,
                        help="flow arms to gate (claim: off unless 'on'; packet: all unless on/off)")
    parser.add_argument("--target-arms", help="packet gate: regex over '<backend> <arm key>' naming the target arms")
    parser.add_argument("--target-phase", choices=PHASES, default="total")
    parser.add_argument("--no-timing-gate", action="store_true", help="packet gate for benchmark-only packets")
    args = parser.parse_args()
    root = args.evidence_root.resolve()
    paths = args.reports if args.reports is not None else sorted(root.glob("*.json"))
    records = {}
    errors = []
    for supplied in paths:
        path = supplied if supplied.is_absolute() else root / supplied
        matched = REPORT_PATTERN.match(path.name)
        if args.reports is None and not matched and not path.name.endswith("-build-validation.json"):
            continue
        try:
            backend = ("Mono" if matched[2] == "mono" else "IL2CPP") if matched else None
            record = validate_report(path, backend)
            record["report"] = path.name
            if matched:
                record.update(validate_observation(root, path, matched[1], backend, int(matched[3]), args.initial_rounds))
            else:
                record["noiseStatus"] = "validation-only"
            records[path.name] = record
            print(f"PASS {path.name}: {record['metrics']} metrics, {record['warmups']} warmups, "
                  f"{record['measuredSamples']} measured samples, 8 goldens; {record['noiseStatus']}")
        except (ValueError, KeyError, TypeError, IndexError, OSError, json.JSONDecodeError) as exception:
            records[path.name] = {"valid": False, "report": path.name, "error": str(exception)}
            errors.append(path.name + ": " + str(exception))
            print("FAIL " + errors[-1])
    require(bool(records), "no lifecycle reports selected")
    pairs = {"accepted": [], "flagged": [], "invalidOrMissing": []}
    selections = {}
    selection_status = {}
    for backend in ("Mono", "IL2CPP"):
        eligible = []
        for round_number in range(1, args.initial_rounds + 3):
            names = [f"{revision}-{backend.lower()}-r{round_number}.json" for revision in ("baseline", "candidate")]
            entries = [records.get(name) for name in names]
            if round_number > args.initial_rounds and not any(entry is not None for entry in entries):
                continue
            identity = {"backend": backend, "round": round_number, "reports": names}
            if not all(entry and entry.get("valid") for entry in entries):
                pairs["invalidOrMissing"].append(identity)
                continue
            identity["cases"] = compare_pair(*entries)
            if all(entry["noiseStatus"] == "accepted" for entry in entries):
                pairs["accepted"].append(identity)
                eligible.append(round_number)
            else:
                pairs["flagged"].append(identity)
        selections[backend] = eligible[:args.initial_rounds]
        selection_status[backend] = {"selectedRounds": selections[backend],
                                     "status": "SUFFICIENT_PAIR_COUNT" if len(eligible) >= args.initial_rounds else "INCONCLUSIVE"}
    usable = {backend: rounds for backend, rounds in selections.items() if rounds}
    ranges = ratio_ranges(records, usable) if usable else {}
    cv_rows = denominator_cv(records, usable) if usable else []
    gate = None
    if args.gate == "claim":
        gate = claim_gate(records, selections, args.revision, args.flow == "on")
    elif args.gate == "packet":
        flow_filter = {"on": True, "off": False}.get(args.flow)
        gate = packet_gate(records, selections, args.target_arms, args.target_phase, args.no_timing_gate, flow_filter)
    summary = {"schemaVersion": 2, "suite": "builderlifecycle", "reports": records, "pairs": pairs,
               "selection": selection_status, "ratioRanges": ranges, "denominatorCv": cv_rows, "gate": gate,
               "reportGroups": {name: [key for key, value in records.items() if value.get("valid") and value.get("noiseStatus") in states]
                                for name, states in (("accepted", ("accepted",)), ("flagged", ("flagged", "unknown")),
                                                     ("validationOnly", ("validation-only",)))},
               "selectionRule": "First three VALID PAIRS ascending round, requiring BOTH revisions accepted by the fixed Unity-noise screen. "
                                "Flagged/unknown/invalid evidence retained; never select favorable timings. Maximum two replacement rounds per backend.",
               "noiseScope": "Only known competing Unity processes screened; <=0.5 CPU seconds AND <=5% wall, no core normalization. "
                             "This does not establish an idle workstation.",
               "decision": "No automatic general superiority claim; all phases, default-flow rows, diagnostics and flagged evidence require independent review.",
               "errors": errors}
    for backend, item in selection_status.items():
        print(f"{backend}: {item['status']} rounds {item['selectedRounds']}")
    for name, row in ranges.items():
        print(f"RANGE {name}: {row['min']:.3f}-{row['max']:.3f}")
    bad_cv = [row for row in cv_rows if not row["ok"]]
    print(f"UniTask denominator CV: {len(cv_rows)} arms, {len(bad_cv)} above limit")
    for row in bad_cv:
        print(f"  CV {row['backend']} {row['case']}: {row['cv']:.3f} > {row['limit']} (farthest {row['farthestProcess']})")
    if gate:
        print("GATE " + args.gate + ": " + gate["verdict"])
        for check in gate.get("checks", []):
            if not check["ok"]:
                print("  FAIL " + check["check"] + " " + check["detail"])
        for arm in gate.get("arms", []):
            if arm.get("pass") is False:
                print("  FAIL " + arm["arm"] + " " + str({k: v for k, v in arm.items() if k in ("median", "worst", "status")}))
    if not args.check_only:
        out_dir = (args.output_dir or root).resolve()
        out_dir.mkdir(parents=True, exist_ok=True)
        table = phase_table(records, usable) if usable else ""
        for name, text in (("generated-summary.json", json.dumps(summary, indent=2, allow_nan=False) + "\n"),
                           ("summary.md", build_markdown(summary, table))):
            target = out_dir / name
            if target.exists() and not args.force:
                print(f"REFUSED to overwrite {target} (use --force or --output-dir)")
                return 1
            target.write_text(text, encoding="utf-8")
            print("Generated " + str(target))
    if errors:
        return 1
    if gate and gate["verdict"] != "PASS":
        return 3
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError) as exception:
        print("FAIL " + str(exception))
        sys.exit(1)
