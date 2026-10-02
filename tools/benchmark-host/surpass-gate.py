"""Surpass-UniTask gate over primary, builderlifecycle and throughput Release Player reports.

Inputs are report JSON files or directories (searched recursively; other JSON such as observation files,
staging manifests and build sidecars is ignored, and byte-identical copies count once). Per backend the
gate expects exactly three processes each of primary (retention matched), builderlifecycle (retention
matched) and throughput; default-retention primary/builderlifecycle processes are reported only.

Per process the Onity/UniTask ratio of every row is computed:
  primary          ns/op per scenario row (the report's mean-based nanosecondsPerOperation; --primary-statistic
                   median uses the median of the raw sample times instead)
  builderlifecycle total complete-cycle ns/op per arm, median of samples per library
  throughput       ns per await per arm, median of samples per library (recomputed from raw ticks)
Thresholds over the three per-process ratios: primary synchronous rows pass when the median <= 1.00; every
other primary row except "(flow on)" rows, every flow-off builderlifecycle arm at 128 and 4096 and every
throughput arm pass when the median <= 0.95 AND the worst process <= 1.00. Flow-on rows, cohort-1 arms and
default-retention processes are report-only. IL2CPP is the gate backend; Mono gets the same table but never
gates.

Evidence is rejected (INVALID) for selfTest reports (unless --allow-self-test), incomplete or failed runs,
failed goldens, mismatched throughput frame counts, missing rows, a wrong process count, non-Release or
Editor reports, inconsistent arithmetic, or mixed build GUIDs within a backend.

Writes <output-dir>/surpass-gate.md and gate-summary.json (refuses to overwrite without --force).
Exit codes: 0 PASS (or self-test parse OK), 1 INVALID evidence, 2 usage error, 3 gate FAIL.
--self-check runs the gate on synthetic reports and exits 0 when every expectation holds.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import statistics
import sys
import tempfile

SUITES = ("primary", "builderlifecycle", "throughput")
GATE_GROUPS = (("primary", "matched"), ("builderlifecycle", "matched"), ("throughput", "matched"))
REPORT_ONLY_GROUPS = (("primary", "default"), ("builderlifecycle", "default"))
PROCESSES_PER_GROUP = 3
GATE_BACKEND = "IL2CPP"
BACKENDS = ("IL2CPP", "Mono")
SYNC_ROWS = ("Completed GetResult", "FromResult<int> GetResult", "Async method completed GetResult",
             "Async method completed<int> GetResult")
FLOW_ON_SUFFIX = " (flow on)"
SYNC_MEDIAN_LIMIT = 1.00
MEDIAN_LIMIT = 0.95
WORST_LIMIT = 1.00
THROUGHPUT_ARMS = ("nextframe-n1024", "nextframe-n4096", "yield-n1024", "yield-n4096", "nextframe-typed-n1024")
LIFECYCLE_TITLE = "Manual-awaitable builder lifecycle elapsed phases"


class Invalid(Exception):
    """Evidence that cannot be used for a verdict."""


def require(condition, message):
    if not condition:
        raise Invalid(message)


def finite(value, label):
    require(isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value),
            label + ": missing or non-finite value")
    return float(value)


def close(actual, expected, label, rel=1e-6, absolute=1e-6):
    finite(actual, label)
    require(math.isclose(actual, expected, rel_tol=rel, abs_tol=absolute),
            f"{label}: reported {actual} but raw data gives {expected}")


def sha256(data):
    return hashlib.sha256(data).hexdigest().upper()


def expected_primary_rows():
    rows = [(name, 1) for name in SYNC_ROWS]
    for concurrency in (128, 4096):
        rows += [("NextFrame scheduling", concurrency), ("NextFrame GetResult", concurrency)]
    for suffix in ("", FLOW_ON_SUFFIX):
        for name in ("Async method NextFrame", "Async method NextFrame<int>"):
            for concurrency in (128, 4096):
                rows += [(name + " scheduling" + suffix, concurrency), (name + " GetResult" + suffix, concurrency)]
    return rows


def classify(data):
    """Return the suite name of a report JSON, or None for any other JSON."""
    if not isinstance(data, dict):
        return None
    suite = data.get("suite")
    if suite in SUITES:
        return suite
    if data.get("title") == LIFECYCLE_TITLE and "metrics" in data:
        return "builderlifecycle"
    if "scenarios" in data and "synchronousHarnessBaseline" in data:
        return "primary"
    return None


def backend_of(data):
    environment = data.get("environment") if isinstance(data, dict) else None
    backend = environment.get("scriptingBackend") if isinstance(environment, dict) else None
    return backend if backend in BACKENDS else None


def validate_environment(environment, label):
    require(isinstance(environment, dict), label + ": missing environment")
    backend = environment.get("scriptingBackend")
    require(backend in BACKENDS, label + ": unknown scripting backend " + repr(backend))
    require(environment.get("isDevelopment") is False, label + ": development build")
    require(bool(environment.get("buildGuid")), label + ": missing build GUID")
    build = environment.get("build") or {}
    optimization = str(build.get("codeOptimization", ""))
    require("Release" in optimization and "BuildOptions.None" in optimization,
            label + ": not a non-development Release Player build (" + optimization + ")")
    return backend


def sample_median(values):
    require(len(values) > 0, "no samples")
    return statistics.median(values)


def long_median(values):
    """The harness's Median(long[]): middle element, or the integer mean of the two middle elements."""
    ordered = sorted(int(value) for value in values)
    middle = len(ordered) // 2
    return ordered[middle] if len(ordered) % 2 == 1 else (ordered[middle - 1] + ordered[middle]) // 2


def evaluate_primary(data, label, statistic):
    require(data.get("schemaVersion", 0) >= 7, label + ": primary schema < 7 (no completion/retention fields)")
    require(data.get("completed") is True, label + ": primary run not completed")
    require(data.get("isEditor") is False, label + ": Editor report")
    frequency = finite(data.get("stopwatchFrequency"), label + ": stopwatchFrequency")
    require(frequency > 0, label + ": stopwatchFrequency")
    scenarios = data.get("scenarios") or []
    rows = {}
    for scenario in scenarios:
        require(isinstance(scenario, dict) and scenario.get("results"), label + ": malformed scenario")
        key = (scenario.get("displayName"), scenario.get("concurrency"))
        require(key not in rows, label + ": duplicate primary row " + str(key))
        libraries = {metric.get("library"): metric for metric in scenario["results"]}
        require(set(libraries) == {"OnityTask", "UniTask"}, label + ": row without both libraries " + str(key))
        values = {}
        operations = finite(scenario.get("iterationsPerSample"), label + ": iterationsPerSample")
        require(operations > 0, label + ": iterationsPerSample")
        for library, metric in libraries.items():
            samples = metric.get("sampleMilliseconds") or []
            require(len(samples) == data.get("samplesPerCase"), label + f": {key} {library} sample count")
            mean_ns = statistics.fmean(samples) * 1e6 / operations
            close(metric.get("nanosecondsPerOperation"), mean_ns, label + f": {key} {library} ns/op", rel=1e-6, absolute=1e-3)
            values[library] = mean_ns if statistic == "mean" else statistics.median(samples) * 1e6 / operations
        require(values["UniTask"] > 0, label + f": {key} non-positive UniTask ns/op")
        rows[key] = {"onity": values["OnityTask"], "uniTask": values["UniTask"],
                     "ratio": values["OnityTask"] / values["UniTask"],
                     "retention": scenario.get("retentionPolicy", "default")}
    missing = [row for row in expected_primary_rows() if row not in rows]
    require(not missing, label + ": missing primary rows " + str(missing))
    return rows


def evaluate_lifecycle(data, label):
    require(data.get("schemaVersion") == 1, label + ": builderlifecycle schema")
    require(data.get("completed") is True and not data.get("failure") and not data.get("loggedError"),
            label + ": builderlifecycle run failed: " + str(data.get("failure") or data.get("loggedError")))
    require(data.get("goldensPassed") is True and data.get("multiSuspensionGoldensPassed") == 8,
            label + ": builderlifecycle goldens failed")
    frequency = finite(data.get("stopwatchFrequency"), label + ": stopwatchFrequency")
    require(frequency > 0, label + ": stopwatchFrequency")
    metrics = {}
    for metric in data.get("metrics") or []:
        require(isinstance(metric, dict), label + ": malformed metric")
        key = (bool(metric.get("onityFlowExecutionContext")), metric.get("shape"), metric.get("cohortSize"),
               metric.get("sequentialSuspensions"), metric.get("library"))
        require(key not in metrics, label + ": duplicate builderlifecycle metric " + str(key))
        samples = metric.get("samples") or []
        require(len(samples) > 0, label + f": {key} has no samples")
        totals = []
        for sample in samples:
            require(sample.get("validated") is True, label + f": {key} unvalidated sample")
            operations = finite(sample.get("operations"), label + ": operations")
            require(operations > 0, label + ": operations")
            expected = finite(sample.get("totalTicks"), label + ": totalTicks") * 1e9 / frequency / operations
            close(sample.get("totalNanosecondsPerOperation"), expected, label + f": {key} total ns/op")
            totals.append(expected)
        metrics[key] = {"median": sample_median(totals), "retention": metric.get("retentionPolicy", "default"),
                        "runnerPoolCapacity": metric.get("runnerPoolCapacity")}
    for flow in (False, True):
        for shape in ("Untyped", "Typed int"):
            for count in (1, 128, 4096):
                for steps in (1, 4):
                    for library in ("OnityTask", "UniTask"):
                        require((flow, shape, count, steps, library) in metrics,
                                label + f": missing builderlifecycle arm flow={flow} {shape} N={count} steps={steps} {library}")
    rows = {}
    for (flow, shape, count, steps, library), value in metrics.items():
        if library != "OnityTask":
            continue
        uni = metrics[(flow, shape, count, steps, "UniTask")]["median"]
        require(uni > 0, label + ": non-positive UniTask builderlifecycle total")
        name = f"{shape} N={count} {steps}-susp" + (" (flow on)" if flow else "")
        rows[(name, count)] = {"onity": value["median"], "uniTask": uni, "ratio": value["median"] / uni,
                               "flow": flow, "count": count, "retention": value["retention"],
                               "runnerPoolCapacity": value["runnerPoolCapacity"]}
    return rows


def evaluate_throughput(data, label):
    require(data.get("schemaVersion") == 1, label + ": throughput schema")
    require(data.get("completed") is True and not data.get("failure") and not data.get("loggedError"),
            label + ": throughput run failed: " + str(data.get("failure") or data.get("loggedError")))
    require(data.get("goldensPassed") is True, label + ": throughput goldens failed")
    require(data.get("validatedCohorts") == data.get("expectedCohorts") and data.get("expectedCohorts", 0) > 0,
            label + ": throughput cohorts not all validated")
    require(data.get("framesMatched") is True, label + ": mismatched throughput frame counts")
    require(data.get("frameStartAnomalies") == 0, label + ": frame-start system skipped or repeated frames")
    frequency = finite(data.get("stopwatchFrequency"), label + ": stopwatchFrequency")
    require(frequency > 0, label + ": stopwatchFrequency")
    rows = {}
    notes = []
    arms = {arm.get("id"): arm for arm in data.get("arms") or []}
    for arm_id in THROUGHPUT_ARMS:
        require(arm_id in arms, label + ": missing throughput arm " + arm_id)
        arm = arms[arm_id]
        require(arm.get("validated") is True and arm.get("framesMatched") is True,
                label + f": {arm_id} not validated or frame counts differ")
        require(arm.get("retentionPolicy") == "matched", label + f": {arm_id} retention is not matched")
        if not arm.get("sourcePoolCapacityMatched"):
            notes.append(f"{arm_id}: source pool capacity not configurable in this runtime (runner capacity matched only)")
        medians = {}
        frames = {}
        for key in ("onity", "uniTask"):
            library = arm.get(key) or {}
            samples = library.get("samples") or []
            require(len(samples) == data.get("samplesPerLibrary"), label + f": {arm_id} {key} sample count")
            values = []
            sample_frames = set()
            for sample in samples:
                require(sample.get("validated") is True and sample.get("framesMatchArm") is True,
                        label + f": {arm_id} {key} unvalidated sample or frame mismatch")
                require(sample.get("loopsCompleted") == arm.get("loops") and sample.get("consumed") == arm.get("loops")
                        and sample.get("exceptions") == 0 and sample.get("uniTaskIdle") is True,
                        label + f": {arm_id} {key} sample goldens failed")
                if arm.get("shape") == "Typed int":
                    require(sample.get("resultSum") == 42 * arm.get("loops"), label + f": {arm_id} {key} typed results")
                controls = sample.get("controlWindowTicks") or []
                require(len(controls) == data.get("controlWindowsPerSample") and len(controls) >= 3,
                        label + f": {arm_id} {key} control windows")
                control_frames = finite(sample.get("controlFrames"), label + ": controlFrames")
                require(control_frames > 0, label + ": controlFrames")
                per_frame = long_median(controls) / control_frames
                net = finite(sample.get("elapsedTicks"), label + ": elapsedTicks") - per_frame * sample.get("frames")
                value = net * 1e9 / frequency / finite(sample.get("awaits"), label + ": awaits")
                close(sample.get("nanosecondsPerAwait"), value, label + f": {arm_id} {key} ns/await", absolute=1e-3)
                values.append(value)
                sample_frames.add(sample.get("frames"))
            medians[key] = statistics.median(values)
            frames[key] = sorted(sample_frames)
        require(frames["onity"] == frames["uniTask"] == [arm.get("framesPerCohort")],
                label + f": {arm_id} frames-to-complete differ (Onity {frames['onity']}, UniTask {frames['uniTask']})")
        require(medians["uniTask"] > 0, label + f": {arm_id} non-positive UniTask ns/await (control exceeds work)")
        rows[(arm.get("displayName") or arm_id, arm.get("loops"))] = {
            "id": arm_id, "onity": medians["onity"], "uniTask": medians["uniTask"],
            "ratio": medians["onity"] / medians["uniTask"], "frames": arm.get("framesPerCohort")}
    return rows, notes


def retention_of(suite, data):
    if suite == "throughput":
        return "matched"
    value = data.get("retentionArgument") or "default"
    return value if value in ("default", "matched") else "unknown"


def load_inputs(inputs):
    candidates = []
    for item in inputs:
        path = Path(item)
        if path.is_dir():
            candidates += sorted(p for p in path.rglob("*.json") if p.is_file())
        elif path.is_file():
            candidates.append(path)
        else:
            raise SystemExit(f"USAGE: input not found: {item}")
    reports = []
    ignored = []
    observations = {}
    seen = {}
    for path in candidates:
        raw = path.read_bytes()
        try:
            data = json.loads(raw.decode("utf-8-sig"))
        except (UnicodeDecodeError, ValueError) as error:
            ignored.append({"path": str(path), "reason": "not JSON: " + str(error)[:80]})
            continue
        if path.name.endswith(".observation.json") and isinstance(data, dict) and "noiseStatus" in data:
            report = data.get("report")
            if report:
                observations[str(Path(report).resolve()).lower()] = data.get("noiseStatus")
            continue
        suite = classify(data)
        if suite is None:
            ignored.append({"path": str(path), "reason": "not a primary/builderlifecycle/throughput report"})
            continue
        digest = sha256(raw)
        if digest in seen:
            seen[digest]["copies"].append(str(path))
            continue
        record = {"path": str(path), "sha256": digest, "suite": suite, "data": data, "copies": []}
        seen[digest] = record
        reports.append(record)
    for record in reports:
        record["noise"] = observations.get(str(Path(record["path"]).resolve()).lower(), "n/a")
    return reports, ignored


def evaluate(inputs, allow_self_test=False, primary_statistic="mean"):
    reports, ignored = load_inputs(inputs)
    summary = {"schemaVersion": 1, "generatedUtc": datetime.now(timezone.utc).isoformat(),
               "mode": "self-test parse" if allow_self_test else "gate", "primaryStatistic": primary_statistic,
               "thresholds": {"primarySyncMedian": SYNC_MEDIAN_LIMIT, "median": MEDIAN_LIMIT, "worstProcess": WORST_LIMIT},
               "gateBackend": GATE_BACKEND, "inputs": [], "ignored": ignored, "backends": {}}
    by_backend = {backend: [] for backend in BACKENDS}
    unattributed = []
    for record in reports:
        data = record["data"]
        label = Path(record["path"]).name
        entry = {"path": record["path"], "sha256": record["sha256"], "suite": record["suite"],
                 "copies": record["copies"], "noise": record["noise"], "selfTest": bool(data.get("selfTest"))}
        summary["inputs"].append(entry)
        backend = backend_of(data)
        if backend is None:
            entry["problem"] = label + ": report without a known scripting backend"
            unattributed.append(entry["problem"])
            continue
        entry["backend"] = backend
        entry["buildGuid"] = (data.get("environment") or {}).get("buildGuid")
        entry["retention"] = retention_of(record["suite"], data)
        by_backend[backend].append((record, entry))

    verdict_problems = list(unattributed)
    for backend in BACKENDS:
        result = {"valid": True, "problems": [], "notes": [], "rows": [], "reportOnlyRows": [], "processes": {}}
        summary["backends"][backend] = result
        items = by_backend.get(backend, [])
        if not items:
            result["valid"] = False
            result["problems"].append("no reports for this backend")
            if backend == GATE_BACKEND:
                verdict_problems += result["problems"]
            continue
        guids = sorted({str(entry["buildGuid"]) for _, entry in items})
        result["buildGuids"] = guids
        if len(guids) != 1:
            result["problems"].append("mixed build GUIDs within the backend: " + ", ".join(guids))
        groups = {}
        for record, entry in items:
            data = record["data"]
            label = Path(record["path"]).name
            try:
                validate_environment(data.get("environment"), label)
            except Invalid as error:
                result["problems"].append(str(error))
                continue
            if data.get("selfTest") and not allow_self_test:
                result["problems"].append(label + ": selfTest report (never usable for ratios)")
                continue
            if not data.get("selfTest") and allow_self_test:
                result["notes"].append(label + ": not a selfTest report (self-test parse mode)")
            flow_default = data.get("flowExecutionContextDefault")
            if record["suite"] == "primary" and flow_default is True:
                result["notes"].append(label + ": WARNING library default FlowExecutionContext read true at run start")
            try:
                if record["suite"] == "primary":
                    rows = evaluate_primary(data, label, primary_statistic)
                    notes = []
                elif record["suite"] == "builderlifecycle":
                    rows = evaluate_lifecycle(data, label)
                    notes = []
                else:
                    rows, notes = evaluate_throughput(data, label)
            except Invalid as error:
                result["problems"].append(str(error))
                continue
            for note in notes:
                if note not in result["notes"]:
                    result["notes"].append(note)
            groups.setdefault((record["suite"], entry["retention"]), []).append((record, rows))
        for (suite, retention), members in sorted(groups.items()):
            result["processes"][f"{suite}-{retention}"] = [Path(record["path"]).name for record, _ in members]
        for suite, retention in GATE_GROUPS:
            members = groups.get((suite, retention), [])
            if allow_self_test:
                if not members:
                    result["problems"].append(f"no valid {suite} ({retention}) report")
            elif len(members) != PROCESSES_PER_GROUP:
                result["problems"].append(f"{suite} ({retention}) has {len(members)} valid process(es), expected {PROCESSES_PER_GROUP}")
            if members:
                build_rows(result, suite, retention, members, gated=True)
        for suite, retention in REPORT_ONLY_GROUPS:
            members = groups.get((suite, retention), [])
            if members:
                build_rows(result, suite, retention, members, gated=False)
        for (suite, retention), members in groups.items():
            if (suite, retention) not in GATE_GROUPS and (suite, retention) not in REPORT_ONLY_GROUPS:
                result["problems"].append(f"unexpected {suite} report with retention {retention}")
        result["valid"] = not result["problems"]
        if backend == GATE_BACKEND and not result["valid"]:
            verdict_problems += result["problems"]
    summary["unattributedProblems"] = unattributed

    gate = summary["backends"][GATE_BACKEND]
    if allow_self_test:
        summary["verdict"] = "NO VERDICT (self-test)" if not verdict_problems and all(
            summary["backends"][b]["valid"] for b in BACKENDS) else "INVALID"
    elif verdict_problems:
        summary["verdict"] = "INVALID"
    else:
        failed = [row for row in gate["rows"] if row["result"] == "FAIL"]
        summary["verdict"] = "FAIL" if failed else "PASS"
        summary["failedRows"] = [f"{row['suite']}: {row['row']}" for row in failed]
    return summary


def build_rows(result, suite, retention, members, gated):
    keys = []
    for _, rows in members:
        for key in rows:
            if key not in keys:
                keys.append(key)
    for key in keys:
        ratios = []
        for record, rows in members:
            if key in rows:
                ratios.append(rows[key]["ratio"])
        name, count = key
        sample = members[0][1].get(key, {})
        flow_on = name.endswith(FLOW_ON_SUFFIX)
        threshold = None
        if gated and len(ratios) == len(members):
            if suite == "primary" and not flow_on:
                threshold = ("median", SYNC_MEDIAN_LIMIT) if name in SYNC_ROWS else ("median+worst", MEDIAN_LIMIT)
            elif suite == "builderlifecycle" and not sample.get("flow") and count in (128, 4096):
                threshold = ("median+worst", MEDIAN_LIMIT)
            elif suite == "throughput":
                threshold = ("median+worst", MEDIAN_LIMIT)
        median = statistics.median(ratios) if ratios else float("nan")
        worst = max(ratios) if ratios else float("nan")
        row = {"suite": suite, "retention": retention, "row": name, "count": count,
               "processRatios": ratios, "median": median, "worst": worst,
               "onityMedianNs": statistics.median(rows[key]["onity"] for _, rows in members if key in rows),
               "uniTaskMedianNs": statistics.median(rows[key]["uniTask"] for _, rows in members if key in rows)}
        if threshold is None:
            row.update({"threshold": "report-only", "result": "REPORT"})
            result["reportOnlyRows"].append(row)
            continue
        kind, limit = threshold
        passed = median <= limit and (kind == "median" or worst <= WORST_LIMIT)
        row.update({"threshold": f"median<={limit:.2f}" + ("" if kind == "median" else f", worst<={WORST_LIMIT:.2f}"),
                    "result": "PASS" if passed else "FAIL"})
        result["rows"].append(row)


def fmt(value):
    return "n/a" if value is None or (isinstance(value, float) and math.isnan(value)) else f"{value:.3f}"


def markdown(summary):
    lines = ["# OnityTask surpass gate", "",
             f"- Generated: {summary['generatedUtc']}",
             f"- Mode: {summary['mode']}; primary statistic: {summary['primaryStatistic']}",
             f"- Thresholds: primary synchronous rows median <= {SYNC_MEDIAN_LIMIT:.2f}; other gated rows median <= "
             f"{MEDIAN_LIMIT:.2f} and worst process <= {WORST_LIMIT:.2f}. Gate backend: {GATE_BACKEND}; Mono is reported only.",
             f"- Verdict: **{summary['verdict']}**", ""]
    for backend in BACKENDS:
        result = summary["backends"].get(backend, {})
        gating = " (gate)" if backend == GATE_BACKEND else " (reported, not gating)"
        lines += [f"## {backend}{gating}", ""]
        lines.append(f"- Evidence valid: {result.get('valid')}; build GUIDs: {', '.join(result.get('buildGuids', [])) or 'n/a'}")
        for group, names in result.get("processes", {}).items():
            lines.append(f"- {group}: {len(names)} process(es): {', '.join(names)}")
        for problem in result.get("problems", []):
            lines.append(f"- PROBLEM: {problem}")
        for note in result.get("notes", []):
            lines.append(f"- Note: {note}")
        lines.append("")
        if result.get("rows"):
            lines += ["| Suite | Row | N | Per-process Onity/UniTask | Median | Worst | Threshold | Result |",
                      "|---|---|---:|---|---:|---:|---|---|"]
            for row in result["rows"]:
                lines.append(f"| {row['suite']} | {row['row']} | {row['count']} | "
                             f"{', '.join(fmt(r) for r in row['processRatios'])} | {fmt(row['median'])} | "
                             f"{fmt(row['worst'])} | {row['threshold']} | {row['result']} |")
            lines.append("")
        if result.get("reportOnlyRows"):
            lines += ["Report-only rows (flow on, cohort 1, default retention):", "",
                      "| Suite | Retention | Row | N | Per-process Onity/UniTask | Median | Worst |",
                      "|---|---|---|---:|---|---:|---:|"]
            for row in result["reportOnlyRows"]:
                lines.append(f"| {row['suite']} | {row['retention']} | {row['row']} | {row['count']} | "
                             f"{', '.join(fmt(r) for r in row['processRatios'])} | {fmt(row['median'])} | {fmt(row['worst'])} |")
            lines.append("")
    if summary.get("ignored"):
        lines.append(f"Ignored {len(summary['ignored'])} non-report JSON file(s).")
    noisy = [item for item in summary["inputs"] if item.get("noise") not in ("accepted", "n/a")]
    if noisy:
        lines.append("Noise screen (informational): " + ", ".join(f"{Path(i['path']).name}={i['noise']}" for i in noisy))
    return "\n".join(lines) + "\n"


def write_outputs(summary, output_dir, force):
    output_dir.mkdir(parents=True, exist_ok=True)
    md_path = output_dir / "surpass-gate.md"
    json_path = output_dir / "gate-summary.json"
    for path in (md_path, json_path):
        if path.exists() and not force:
            raise SystemExit(f"USAGE: refusing to overwrite {path} (use --force)")
    text = markdown(summary)
    md_path.write_text(text, encoding="utf-8")
    json_path.write_text(json.dumps(summary, indent=2, allow_nan=True), encoding="utf-8")
    return md_path, json_path, text


# ---------------------------------------------------------------- synthetic self-check

def _environment(backend, guid):
    return {"buildGuid": guid, "isDevelopment": False, "scriptingBackend": backend, "flowExecutionContext": False,
            "runnerPoolCapacity": 128, "build": {"codeOptimization": "Release (non-development Player; BuildOptions.None)"}}


def synthetic_primary(backend, guid, ratio_for, self_test=False):
    samples = 2 if self_test else 8
    scenarios = []
    for name, concurrency in expected_primary_rows():
        operations = 1000 if concurrency == 1 else concurrency * 32
        uni_ms = [0.05 + 0.001 * i for i in range(samples)]
        ratio = ratio_for("primary", name, concurrency)
        onity_ms = [value * ratio for value in uni_ms]
        results = []
        for library, values in (("OnityTask", onity_ms), ("UniTask", uni_ms)):
            results.append({"library": library, "sampleMilliseconds": values,
                            "nanosecondsPerOperation": statistics.fmean(values) * 1e6 / operations})
        scenarios.append({"displayName": name, "concurrency": concurrency, "iterationsPerSample": operations,
                          "retentionPolicy": "matched" if concurrency == 4096 else "default", "results": results})
    return {"schemaVersion": 7, "suite": "primary", "selfTest": self_test, "completed": True, "isEditor": False,
            "retentionArgument": "matched", "flowExecutionContextDefault": False, "stopwatchFrequency": 10000000,
            "samplesPerCase": samples, "environment": _environment(backend, guid), "synchronousHarnessBaseline": {},
            "scenarios": scenarios}


def synthetic_lifecycle(backend, guid, ratio_for, self_test=False):
    metrics = []
    samples = 2 if self_test else 8
    for flow in (False, True):
        for shape in ("Untyped", "Typed int"):
            for count in (1, 128, 4096):
                for steps in (1, 4):
                    name = f"{shape} N={count} {steps}-susp" + (" (flow on)" if flow else "")
                    ratio = ratio_for("builderlifecycle", name, count)
                    for library in ("OnityTask", "UniTask"):
                        operations = count * max(1, 4096 // count)
                        scale = ratio if library == "OnityTask" else 1.0
                        rows = []
                        for index in range(samples):
                            ticks = int(round((2000000 + 1000 * index) * scale))
                            rows.append({"validated": True, "operations": operations, "totalTicks": ticks,
                                         "totalNanosecondsPerOperation": ticks * 1e9 / 10000000 / operations})
                        metrics.append({"onityFlowExecutionContext": flow, "shape": shape, "cohortSize": count,
                                        "sequentialSuspensions": steps, "library": library, "samples": rows,
                                        "retentionPolicy": "matched" if count == 4096 else "default",
                                        "runnerPoolCapacity": 4096 if count == 4096 else 128})
    return {"schemaVersion": 1, "suite": "builderlifecycle", "title": LIFECYCLE_TITLE, "selfTest": self_test,
            "completed": True, "failure": "", "loggedError": "", "goldensPassed": True,
            "multiSuspensionGoldensPassed": 8, "retentionArgument": "matched", "stopwatchFrequency": 10000000,
            "environment": _environment(backend, guid), "metrics": metrics}


def synthetic_throughput(backend, guid, ratio_for, self_test=False, frames_mismatch=False, goldens=True):
    samples = 2 if self_test else 8
    arms = []
    for arm_id in THROUGHPUT_ARMS:
        loops = 4096 if "4096" in arm_id else 1024
        typed = "typed" in arm_id
        frames = 16 if arm_id.startswith("yield") else 17
        name = ("Yield" if arm_id.startswith("yield") else "NextFrame") + ("<int>" if typed else "") + f" loops N={loops} K=16"
        ratio = ratio_for("throughput", name, loops)
        libraries = {}
        for key, scale in (("onity", ratio), ("uniTask", 1.0)):
            rows = []
            for index in range(samples):
                controls = [170000 + 100 * index, 171000, 169000]
                per_frame = statistics.median(controls) / frames
                net = (400000 + 1000 * index) * scale
                elapsed = int(round(per_frame * frames + net))
                actual = (elapsed - per_frame * frames) * 1e9 / 10000000 / (loops * 16)
                sample_frames = frames + 1 if (frames_mismatch and key == "onity" and index == 0) else frames
                rows.append({"validated": True, "framesMatchArm": sample_frames == frames, "frames": sample_frames,
                             "loopsCompleted": loops, "consumed": loops, "exceptions": 0, "uniTaskIdle": True,
                             "resultSum": 42 * loops if typed else 0, "controlWindowTicks": controls,
                             "controlFrames": frames, "elapsedTicks": elapsed, "awaits": loops * 16,
                             "nanosecondsPerAwait": (elapsed - statistics.median(controls) / frames * sample_frames)
                             * 1e9 / 10000000 / (loops * 16) if sample_frames != frames else actual})
            libraries[key] = {"samples": rows}
        arms.append({"id": arm_id, "displayName": name, "loops": loops, "shape": "Typed int" if typed else "Untyped",
                     "retentionPolicy": "matched", "sourcePoolCapacityMatched": False, "validated": True,
                     "framesMatched": not frames_mismatch, "framesPerCohort": frames, **libraries})
    return {"schemaVersion": 1, "suite": "throughput", "selfTest": self_test, "completed": True, "failure": "",
            "loggedError": "", "goldensPassed": goldens, "validatedCohorts": 10, "expectedCohorts": 10,
            "framesMatched": not frames_mismatch, "frameStartAnomalies": 0, "stopwatchFrequency": 10000000,
            "samplesPerLibrary": samples, "controlWindowsPerSample": 3, "environment": _environment(backend, guid),
            "arms": arms}


def write_set(root, ratio_for, self_test=False, mutate=None):
    """ratio_for(suite, row name, count, process) gives each synthetic process's Onity/UniTask ratio."""
    root.mkdir(parents=True, exist_ok=True)
    for backend, guid in (("IL2CPP", "11111111111111111111111111111111"), ("Mono", "22222222222222222222222222222222")):
        for process in range(1, (2 if self_test else PROCESSES_PER_GROUP + 1)):
            def jitter(suite, name, count, process=process):
                return ratio_for(suite, name, count, process)
            reports = {"primary": synthetic_primary(backend, guid, jitter, self_test),
                       "builderlifecycle": synthetic_lifecycle(backend, guid, jitter, self_test),
                       "throughput": synthetic_throughput(backend, guid, jitter, self_test)}
            for suite, data in reports.items():
                if mutate:
                    mutate(backend, process, suite, data)
                (root / f"{backend.lower()}-{suite}-matched-p{process}.json").write_text(json.dumps(data), encoding="utf-8")


def self_check():
    good = lambda suite, name, count, process: 0.80 * (1.0 + 0.01 * (process - 2))
    checks = []

    def run(name, ratio_for, expected, allow_self_test=False, self_test=False, mutate=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            write_set(root, ratio_for, self_test=self_test, mutate=mutate)
            summary = evaluate([str(root)], allow_self_test=allow_self_test)
            verdict = summary["verdict"]
            ok = verdict == expected
            checks.append(ok)
            detail = "" if ok else " problems=" + str(summary["backends"][GATE_BACKEND]["problems"][:3])
            print(f"  {'ok  ' if ok else 'FAIL'} {name}: verdict {verdict} (expected {expected}){detail}")

    def one(suite_name, row_name, ratios):
        def ratio_for(suite, name, count, process):
            if suite == suite_name and name == row_name:
                return ratios[process - 1]
            return good(suite, name, count, process)
        return ratio_for

    print("surpass-gate self-check (synthetic data, no Player evidence):")
    run("all rows at 0.80", good, "PASS")
    run("throughput arm median 0.90 but worst process 1.02", one("throughput", "Yield loops N=4096 K=16", (0.88, 0.90, 1.02)), "FAIL")
    run("throughput arm worst process exactly 1.00 passes", one("throughput", "Yield loops N=4096 K=16", (0.88, 0.90, 1.00)), "PASS")
    run("throughput arm median 0.96", one("throughput", "NextFrame loops N=1024 K=16", (0.96, 0.96, 0.97)), "FAIL")
    run("primary sync row median 0.99 passes despite worst 1.04", one("primary", "Completed GetResult", (0.97, 0.99, 1.04)), "PASS")
    run("primary sync row median 1.01 fails", one("primary", "FromResult<int> GetResult", (0.97, 1.01, 1.04)), "FAIL")
    run("primary async row median 0.98 fails", one("primary", "Async method NextFrame scheduling", (0.98, 0.98, 0.98)), "FAIL")
    run("primary async row worst 1.01 fails", one("primary", "Async method NextFrame<int> GetResult", (0.90, 0.91, 1.01)), "FAIL")
    run("flow-on primary row at 1.50 is report-only", one("primary", "Async method NextFrame scheduling (flow on)", (1.5, 1.5, 1.5)), "PASS")
    run("builderlifecycle cohort-1 arm at 2.0 is report-only", one("builderlifecycle", "Untyped N=1 1-susp", (2.0, 2.0, 2.0)), "PASS")
    run("builderlifecycle flow-on arm at 1.3 is report-only", one("builderlifecycle", "Typed int N=128 4-susp (flow on)", (1.3, 1.3, 1.3)), "PASS")
    run("builderlifecycle 4096 arm median 0.97 fails", one("builderlifecycle", "Typed int N=4096 4-susp", (0.97, 0.97, 0.97)), "FAIL")
    run("selfTest reports rejected in gate mode", good, "INVALID", self_test=True)
    run("selfTest reports parse in self-test mode", good, "NO VERDICT (self-test)", allow_self_test=True, self_test=True)

    def frames(backend, process, suite, data):
        if suite == "throughput" and backend == "IL2CPP" and process == 2:
            data.update(synthetic_throughput(backend, data["environment"]["buildGuid"],
                                             lambda s, n, c: good(s, n, c, process), frames_mismatch=True))
    run("mismatched throughput frames rejected", good, "INVALID", mutate=frames)

    def hidden_frames(backend, process, suite, data):
        # Flags claim a match, but one UniTask sample took an extra frame (arithmetic kept consistent).
        if suite == "throughput" and backend == "IL2CPP" and process == 1:
            sample = data["arms"][1]["uniTask"]["samples"][0]
            sample["frames"] += 1
            per_frame = long_median(sample["controlWindowTicks"]) / sample["controlFrames"]
            sample["nanosecondsPerAwait"] = (sample["elapsedTicks"] - per_frame * sample["frames"]) * 1e9 / 10000000 / sample["awaits"]
    run("frame mismatch hidden behind matching flags rejected", good, "INVALID", mutate=hidden_frames)

    def goldens(backend, process, suite, data):
        if suite == "builderlifecycle" and backend == "IL2CPP" and process == 1:
            data["goldensPassed"] = False
    run("failed goldens rejected", good, "INVALID", mutate=goldens)

    def missing(backend, process, suite, data):
        if suite == "primary" and backend == "IL2CPP" and process == 3:
            data["scenarios"] = [s for s in data["scenarios"] if s["displayName"] != "NextFrame GetResult"]
    run("missing primary row rejected", good, "INVALID", mutate=missing)

    def guids(backend, process, suite, data):
        if suite == "throughput" and backend == "IL2CPP" and process == 3:
            data["environment"]["buildGuid"] = "33333333333333333333333333333333"
    run("mixed build GUIDs rejected", good, "INVALID", mutate=guids)

    def arithmetic(backend, process, suite, data):
        if suite == "throughput" and backend == "IL2CPP" and process == 1:
            data["arms"][0]["onity"]["samples"][0]["nanosecondsPerAwait"] *= 0.5
    run("inconsistent throughput arithmetic rejected", good, "INVALID", mutate=arithmetic)

    def development(backend, process, suite, data):
        if suite == "primary" and backend == "IL2CPP" and process == 2:
            data["environment"]["isDevelopment"] = True
    run("development build rejected", good, "INVALID", mutate=development)

    def mono_only(backend, process, suite, data):
        if backend == "Mono" and suite == "throughput":
            data["goldensPassed"] = False
    run("invalid Mono evidence does not change the IL2CPP verdict", good, "PASS", mutate=mono_only)

    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        write_set(root, good)
        first = next(root.glob("il2cpp-primary-matched-p1.json"))
        (root / "copy-of-primary.json").write_bytes(first.read_bytes())
        (root / "unrelated.json").write_text(json.dumps({"schemaVersion": 1, "noiseStatus": "accepted"}), encoding="utf-8")
        summary = evaluate([str(root)])
        ok = summary["verdict"] == "PASS" and len(summary["ignored"]) == 1
        checks.append(ok)
        print(f"  {'ok  ' if ok else 'FAIL'} byte-identical copy counted once, unrelated JSON ignored: verdict {summary['verdict']}")
        output = root / "out"
        md_path, json_path, _ = write_outputs(summary, output, force=False)
        ok = md_path.is_file() and json.loads(json_path.read_text(encoding="utf-8"))["verdict"] == "PASS"
        try:
            write_outputs(summary, output, force=False)
            ok = False
        except SystemExit:
            pass
        checks.append(ok)
        print(f"  {'ok  ' if ok else 'FAIL'} outputs written once and never overwritten without --force")

    passed = all(checks)
    print(f"self-check: {sum(checks)}/{len(checks)} expectations held -> {'OK' if passed else 'FAILED'}")
    return 0 if passed else 1


def main(argv=None):
    parser = argparse.ArgumentParser(description="OnityTask surpass-UniTask gate over Release Player reports.")
    parser.add_argument("inputs", nargs="*", help="Report JSON files or directories.")
    parser.add_argument("--output-dir", help="Where surpass-gate.md and gate-summary.json go (default: the single "
                                             "input directory, else the current directory).")
    parser.add_argument("--allow-self-test", action="store_true",
                        help="Self-test parse mode: accept selfTest reports and at least one process per group; "
                             "no verdict is given.")
    parser.add_argument("--primary-statistic", choices=("mean", "median"), default="mean",
                        help="Per-library primary ns/op: report mean (default) or median of raw samples.")
    parser.add_argument("--force", action="store_true", help="Overwrite existing gate outputs.")
    parser.add_argument("--self-check", action="store_true", help="Run the gate on synthetic data and exit.")
    args = parser.parse_args(argv)
    if args.self_check:
        return self_check()
    if not args.inputs:
        parser.print_usage()
        return 2
    summary = evaluate(args.inputs, allow_self_test=args.allow_self_test, primary_statistic=args.primary_statistic)
    if args.output_dir:
        output_dir = Path(args.output_dir)
    elif len(args.inputs) == 1 and Path(args.inputs[0]).is_dir():
        output_dir = Path(args.inputs[0])
    else:
        output_dir = Path.cwd()
    md_path, json_path, text = write_outputs(summary, output_dir, args.force)
    print(text)
    print(f"verdict: {summary['verdict']}")
    print(f"gate report: {md_path}")
    print(f"gate summary: {json_path}")
    if summary["verdict"] == "PASS" or summary["verdict"].startswith("NO VERDICT"):
        return 0
    return 3 if summary["verdict"] == "FAIL" else 1


if __name__ == "__main__":
    sys.exit(main())
