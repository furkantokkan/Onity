#!/usr/bin/env python3
"""Validate and summarize messaging comparison reports (Onity.Messaging vs MessagePipe).

Reads every Player report under <evidence-root>/runs (all *.json except *.observation.json), as written
by tools/benchmark-host/run-messaging-comparison.ps1, validates them and writes messaging-summary.md and
messaging-summary.json (to --output-dir, default the evidence root). Existing outputs are never
overwritten without --force.

Validation (any failure: exit 1, nothing written):
- schema and benchmark id, completed run, no failure text, overall and per-scenario golden passed,
  no checksum mismatch in any sample, identical checksums for both libraries;
- Release Player: not Development, not the Editor, build sidecar found and matching the Player;
- self-test reports only with --allow-self-test, and never mixed with measurement reports;
- exactly --processes reports per backend (IL2CPP and Mono by default) and one build GUID per backend;
- the same scenarios, operation counts and sample counts in every report;
- one MessagePipe flavor across all reports: nuget-netstandard2.0 (the precompiled NuGet dll, gate A) or
  unity-package (the Unity package's sources compiled against UniTask, gate B); the flavor each Player was
  compiled with equals its build sidecar's; a unity-package build verified its package entries and records
  a UniTask version. Reports from before the flavor field are the NuGet flavor;
- one source HEAD, Onity version, MessagePipe version, package hash and UniTask version across all reports,
  and the package hash (the dll for the NuGet flavor, the .unitypackage for the Unity package flavor) equals
  --messagepipe-sha256, by default the official MessagePipe 1.8.1 artifact of the recorded flavor;
- ns/op recomputed from the raw Stopwatch ticks matches the report, and so do the reported medians.

Summary per backend and scenario (IL2CPP gates; Mono is reported the same way):
- process median = median of that process's measured samples (recomputed from ticks);
- library ns/op = median over processes of the process medians;
- per-process ratio Onity/MessagePipe = Onity process median / MessagePipe process median;
- median ratio and worst (largest) ratio over processes, and the pre-registered class:
  faster when median <= 0.95 and worst <= 1.00; slower when median >= 1.05; otherwise on par.
- Gate (pre-registered): IL2CPP, 3 processes, every row faster. The rows are the nine scenarios, or the
  seven synchronous ones when the async rows were dropped. Any other row set, process count or a
  self-test is reported as not evaluated.

The observation files of the runner (other Unity Editor CPU during each process) are listed for
information; they do not change validity or the gate.

Exit codes: 0 valid (outputs written, or --check-only), 1 invalid evidence, 2 refused or usage error.
"""
from __future__ import annotations

import argparse
import json
import math
import statistics
import sys
from datetime import datetime, timezone
from pathlib import Path

SCHEMA_VERSION = 1
BENCHMARK = 'onity-messaging-comparison'
LIBRARIES = ('Onity', 'MessagePipe')
OTHER = 'MessagePipe'
DEFAULT_BACKENDS = ('IL2CPP', 'Mono')
GATE_BACKEND = 'IL2CPP'
GATE_PROCESSES = 3
FASTER_MEDIAN = 0.95
FASTER_WORST = 1.00
SLOWER_MEDIAN = 1.05
SYNC_ROWS = ('PublishNoSubscribers', 'Publish1', 'Publish8', 'SubscribeDispose', 'SubscribeDispose64',
             'KeyedPublish', 'KeyedSubscribeDispose')
ALL_ROWS = SYNC_ROWS + ('AsyncPublish1', 'AsyncPublish8')
NUGET_FLAVOR = 'nuget-netstandard2.0'
UNITY_PACKAGE_FLAVOR = 'unity-package'
# Official MessagePipe 1.8.1 artifacts: lib/netstandard2.0/MessagePipe.dll of the NuGet package, and
# MessagePipe.1.8.1.unitypackage (41,788 bytes) from the Cysharp GitHub release 1.8.1.
OFFICIAL_MESSAGEPIPE_SHA256 = {
    NUGET_FLAVOR: 'cf8a702ab31bbb7da9ea6de4349f6dec9e214be396234f7e527d6ebe06a94f1b',
    UNITY_PACKAGE_FLAVOR: '36ff7aa0611272e22c0c596616f2b01dfb42507a206194d9da2a11d8976544b0',
}
PACKAGE_KIND = {NUGET_FLAVOR: 'dll', UNITY_PACKAGE_FLAVOR: '.unitypackage'}
RULE = ('ratio = Onity process median / MessagePipe process median (time per operation, so below 1 means '
        'Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio '
        '<= 1.00; slower when the median ratio >= 1.05; otherwise on par')
GATE_RULE = ('IL2CPP, 3 processes, every row faster (9 rows, or the 7 synchronous rows if the async rows were '
             'dropped); Mono is classified the same way and reported, not gated')
OUTPUT_MD = 'messaging-summary.md'
OUTPUT_JSON = 'messaging-summary.json'


def classify(median_ratio, worst_ratio):
    """Pre-registered class of one ratio row."""
    if median_ratio is None or worst_ratio is None:
        return 'n/a'
    if median_ratio <= FASTER_MEDIAN and worst_ratio <= FASTER_WORST:
        return 'faster'
    if median_ratio >= SLOWER_MEDIAN:
        return 'slower'
    return 'on par'


def close(first, second):
    return math.isclose(first, second, rel_tol=1e-9, abs_tol=1e-9)


def flavor_of(report):
    """MessagePipe flavor of the build; sidecars from before the field are the NuGet build (gate A)."""
    return (report.get('build') or {}).get('messagePipeFlavor') or NUGET_FLAVOR


def package_sha256_of(report):
    """SHA-256 of the MessagePipe package: the dll (NuGet flavor) or the .unitypackage (Unity package flavor)."""
    build = report.get('build') or {}
    return str(build.get('messagePipePackageSha256') or build.get('messagePipeDllSha256') or '').lower()


def load_reports(runs_dir):
    reports = []
    for path in sorted(runs_dir.glob('*.json')):
        if path.name.endswith('.observation.json'):
            continue
        with path.open(encoding='utf-8-sig') as handle:
            reports.append((path, json.load(handle)))
    return reports


def load_observations(runs_dir):
    observations = []
    for path in sorted(runs_dir.glob('*.observation.json')):
        with path.open(encoding='utf-8-sig') as handle:
            observations.append(json.load(handle))
    return observations


def sample_values(report, scenario, library, errors, label):
    """ns/op samples recomputed from ticks; records an error when they disagree with the report."""
    environment = report.get('environment') or {}
    frequency = environment.get('stopwatchFrequency') or 0
    operations = scenario.get('operationsPerSample') or 0
    ticks = library.get('sampleTicks') or []
    reported = library.get('samplesNsPerOp') or []
    if frequency <= 0 or operations <= 0:
        errors.append(label + ': missing stopwatch frequency or operation count')
        return []
    values = [tick * 1e9 / frequency / operations for tick in ticks]
    if len(values) != len(reported) or any(not close(a, b) for a, b in zip(values, reported)):
        errors.append(label + ': samplesNsPerOp do not match the raw ticks')
    if values and not close(statistics.median(values), library.get('medianNsPerOp', -1.0)):
        errors.append(label + ': medianNsPerOp does not match the samples')
    return values


def validate_report(path, report, allow_self_test, errors):
    name = path.name
    if report.get('schemaVersion') != SCHEMA_VERSION or report.get('benchmark') != BENCHMARK:
        errors.append(name + ': not a schema-1 ' + BENCHMARK + ' report')
        return False
    if not report.get('completed') or report.get('failure'):
        errors.append(name + ': run did not complete: ' + str(report.get('failure', ''))[:300])
    if not report.get('goldenPassed'):
        errors.append(name + ': golden checks failed')
    if report.get('selfTest') and not allow_self_test:
        errors.append(name + ': self-test report (pass --allow-self-test to parse self-tests)')
    environment = report.get('environment') or {}
    if environment.get('isDevelopment') or environment.get('isEditor'):
        errors.append(name + ': not a Release Player (development or Editor)')
    if environment.get('selfTest') != report.get('selfTest'):
        errors.append(name + ': selfTest flags disagree')
    build = report.get('build') or {}
    if not report.get('buildMetadataFound') or not report.get('buildMetadataMatchesPlayer'):
        errors.append(name + ': build sidecar missing or not matching the Player')
    if build.get('development'):
        errors.append(name + ': build sidecar says Development')
    if not report.get('messagePipeSetup'):
        errors.append(name + ': the MessagePipe setup method is not recorded')
    flavor = flavor_of(report)
    if flavor not in OFFICIAL_MESSAGEPIPE_SHA256:
        errors.append(name + ': unknown MessagePipe flavor ' + str(flavor))
    if (report.get('messagePipeFlavor') or NUGET_FLAVOR) != flavor:
        errors.append(name + ': the Player was compiled for MessagePipe flavor ' + str(report.get('messagePipeFlavor'))
                      + ' but its build sidecar records ' + flavor)
    if flavor == UNITY_PACKAGE_FLAVOR and (not build.get('messagePipePackageEntries') or not build.get('uniTaskVersion')):
        errors.append(name + ': the Unity package build records no verified package entries or no UniTask version')
    scenarios = report.get('scenarios') or []
    if not scenarios:
        errors.append(name + ': no scenarios')
    samples = (report.get('settings') or {}).get('measuredSamples', 0)
    for scenario in scenarios:
        label = name + ' ' + str(scenario.get('id'))
        if not scenario.get('goldenPassed') or scenario.get('failure'):
            errors.append(label + ': golden failed ' + str(scenario.get('failure', ''))[:200])
        libraries = scenario.get('libraries') or []
        if tuple(library.get('library') for library in libraries) != LIBRARIES:
            errors.append(label + ': libraries are not ' + ', '.join(LIBRARIES))
            continue
        expected = (scenario.get('expectedChecksum'), scenario.get('expectedNotifications'),
                    scenario.get('expectedProbeChecksum'), scenario.get('expectedProbeNotifications'))
        for library in libraries:
            observed = (library.get('checksum'), library.get('notifications'),
                        library.get('probeChecksum'), library.get('probeNotifications'))
            if observed != expected or library.get('mismatchedRuns') != 0 or library.get('checkedRuns', 0) <= 0:
                errors.append(label + ' ' + library.get('library') + ': checksum mismatch ' + str(library.get('firstMismatch', '')))
            if len(library.get('samplesNsPerOp') or []) != samples or len(library.get('sampleTicks') or []) != samples:
                errors.append(label + ' ' + library.get('library') + ': sample count differs from settings.measuredSamples')
    return True


def shape(report):
    settings = report.get('settings') or {}
    return (settings.get('warmupSamples'), settings.get('measuredSamples'),
            tuple((scenario.get('id'), scenario.get('operationsPerSample')) for scenario in report.get('scenarios') or []))


def provenance(report):
    build = report.get('build') or {}
    return (build.get('sourceHead'), build.get('onityVersion'), build.get('messagePipeVersion'), flavor_of(report),
            package_sha256_of(report), build.get('uniTaskVersion'), build.get('uniTaskTreeSha256'))


def summarize_backend(backend, items, errors):
    reports = [report for _, report in items]
    first = reports[0]
    allocation_available = all((report.get('allocation') or {}).get('available') for report in reports)
    result = {
        'backend': backend,
        'processes': [path.stem for path, _ in items],
        'buildGuid': (first.get('environment') or {}).get('buildGuid'),
        'unityVersion': (first.get('environment') or {}).get('unityVersion'),
        'onityVersion': (first.get('build') or {}).get('onityVersion'),
        'messagePipeVersion': (first.get('build') or {}).get('messagePipeVersion'),
        'messagePipeFlavor': flavor_of(first),
        'sourceHead': (first.get('build') or {}).get('sourceHead'),
        'processorType': (first.get('environment') or {}).get('processorType'),
        'gcMode': (first.get('environment') or {}).get('gcMode'),
        'allocationAvailable': allocation_available,
        'allocationDetail': sorted({(report.get('allocation') or {}).get('detail', '') for report in reports}),
        'scenarios': [],
    }
    for index, scenario in enumerate(first.get('scenarios') or []):
        process_medians = {library: [] for library in LIBRARIES}
        allocations = {library: [] for library in LIBRARIES}
        collections = {library: 0 for library in LIBRARIES}
        for path, report in items:
            current = report['scenarios'][index]
            for library in current['libraries']:
                label = path.name + ' ' + current['id'] + ' ' + library['library']
                values = sample_values(report, current, library, errors, label)
                process_medians[library['library']].append(statistics.median(values) if values else 0.0)
                collections[library['library']] += library.get('gcCollectionsDuringSamples', 0)
                if library.get('allocationMeasured'):
                    allocations[library['library']].append(library.get('allocatedBytesPerOp'))
        per_process = []
        for onity, other in zip(process_medians['Onity'], process_medians[OTHER]):
            per_process.append(onity / other if other > 0 else None)
        valid = all(ratio is not None for ratio in per_process)
        median_ratio = statistics.median(per_process) if valid else None
        worst_ratio = max(per_process) if valid else None
        result['scenarios'].append({
            'id': scenario['id'],
            'operationsPerSample': scenario['operationsPerSample'],
            'processMediansNsPerOp': process_medians,
            'medianNsPerOp': {library: statistics.median(values) for library, values in process_medians.items()},
            'gcCollectionsDuringSamples': collections,
            'allocatedBytesPerOp': ({library: statistics.median(values) for library, values in allocations.items()}
                                    if allocation_available and all(allocations.values()) else None),
            'ratio': {
                'perProcess': per_process,
                'median': median_ratio,
                'worst': worst_ratio,
                'class': classify(median_ratio, worst_ratio),
            },
        })
    return result


def evaluate_gate(summary):
    """The pre-registered gate over the IL2CPP rows; not evaluated for self-tests or partial runs."""
    gate = {'backend': GATE_BACKEND, 'rule': GATE_RULE, 'evaluated': False, 'pass': None, 'rows': [],
            'failingRows': [], 'reason': ''}
    if summary['selfTest']:
        gate['reason'] = 'self-test: harness check only'
        return gate
    if summary['processesPerBackend'] != GATE_PROCESSES:
        gate['reason'] = 'the gate needs exactly ' + str(GATE_PROCESSES) + ' processes per backend'
        return gate
    backend = next((item for item in summary['backends'] if item['backend'] == GATE_BACKEND), None)
    if backend is None:
        gate['reason'] = 'no ' + GATE_BACKEND + ' reports'
        return gate
    rows = tuple(row['id'] for row in backend['scenarios'])
    if rows not in (ALL_ROWS, SYNC_ROWS):
        gate['reason'] = 'the row set is neither the nine pre-registered rows nor the seven synchronous rows'
        return gate
    gate['evaluated'] = True
    gate['rows'] = list(rows)
    gate['asyncRowsDropped'] = rows == SYNC_ROWS
    gate['failingRows'] = [row['id'] + ' (' + row['ratio']['class'] + ')' for row in backend['scenarios']
                           if row['ratio']['class'] != 'faster']
    gate['pass'] = not gate['failingRows']
    return gate


def fmt(value, digits=2):
    return 'n/a' if value is None else ('%.' + str(digits) + 'f') % value


def render_markdown(summary):
    lines = ['# Messaging comparison summary', '']
    if summary['selfTest']:
        lines += ['> **SELF-TEST.** Tiny counts that check the harness end to end. These numbers are not '
                  'performance evidence and must not be quoted.', '']
    gate = summary['gate']
    if gate['evaluated']:
        verdict = 'PASS' if gate['pass'] else 'FAIL'
        gate_line = '- Gate (' + gate['backend'] + '): **' + verdict + '**'
        if gate['failingRows']:
            gate_line += '; not faster: ' + ', '.join(gate['failingRows'])
        if gate.get('asyncRowsDropped'):
            gate_line += '; async rows dropped (7 rows)'
    else:
        gate_line = '- Gate: not evaluated (' + gate['reason'] + ')'
    lines += [
        '- Evidence root: `' + summary['evidenceRoot'] + '`',
        '- Generated (UTC): ' + summary['generatedUtc'],
        '- Reports: ' + str(summary['reportCount']) + ' (' + str(summary['processesPerBackend']) + ' per backend), '
        + 'measured samples per process: ' + str(summary['measuredSamples']) + ', warmups: ' + str(summary['warmupSamples']),
        '- Libraries: ' + ', '.join(name + ' ' + version for name, version in summary['libraryVersions'].items()),
        '- MessagePipe flavor: `' + str(summary['messagePipeFlavor']) + '`; '
        + PACKAGE_KIND.get(summary['messagePipeFlavor'], 'package') + ' SHA-256 `'
        + str(summary['messagePipePackageSha256']) + '`'
        + ('; ' + str(summary['messagePipePackageEntries']) + ' package entries verified in the project; UniTask '
           + str(summary['uniTaskVersion']) + ' (tree SHA-256 `' + str(summary['uniTaskTreeSha256']) + '`)'
           if summary['messagePipeFlavor'] == UNITY_PACKAGE_FLAVOR else ''),
        '- Rule (pre-registered): ' + RULE + '.',
        '- Gate rule (pre-registered): ' + GATE_RULE + '.',
        gate_line,
        '',
    ]
    for backend in summary['backends']:
        title = backend['backend'] + (' (gated)' if backend['backend'] == GATE_BACKEND else ' (reported)')
        lines += ['## ' + title, '',
                  '- Build GUID `' + str(backend['buildGuid']) + '`, Unity ' + str(backend['unityVersion'])
                  + ', Onity ' + str(backend['onityVersion']) + ', MessagePipe ' + str(backend['messagePipeVersion'])
                  + ' (' + str(backend['messagePipeFlavor']) + '), source ' + str(backend['sourceHead']),
                  '- CPU: ' + str(backend['processorType']) + '; GC mode ' + str(backend['gcMode']),
                  '- Processes: ' + ', '.join(backend['processes']),
                  '',
                  '| Scenario | Ops/sample | Onity ns/op | MessagePipe ns/op | Onity/MessagePipe per process | Median ratio | Worst ratio | Class |',
                  '| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |']
        for row in backend['scenarios']:
            medians = row['medianNsPerOp']
            ratio = row['ratio']
            lines.append('| ' + ' | '.join([
                row['id'], str(row['operationsPerSample']),
                fmt(medians['Onity']), fmt(medians[OTHER]),
                ', '.join(fmt(value, 3) for value in ratio['perProcess']),
                fmt(ratio['median'], 3), fmt(ratio['worst'], 3), ratio['class']]) + ' |')
        lines += ['', 'Process medians (ns/op) and gen-0 collections during the measured samples (sum over processes):', '',
                  '| Scenario | Onity per process | MessagePipe per process | Onity GCs | MessagePipe GCs |',
                  '| --- | --- | --- | ---: | ---: |']
        for row in backend['scenarios']:
            lines.append('| ' + row['id'] + ' | '
                         + ', '.join(fmt(value, 3) for value in row['processMediansNsPerOp']['Onity']) + ' | '
                         + ', '.join(fmt(value, 3) for value in row['processMediansNsPerOp'][OTHER]) + ' | '
                         + str(row['gcCollectionsDuringSamples']['Onity']) + ' | '
                         + str(row['gcCollectionsDuringSamples'][OTHER]) + ' |')
        lines.append('')
        if backend['allocationAvailable']:
            lines += ['Allocated bytes per operation (separate untimed pass, median over processes):', '',
                      '| Scenario | Onity | MessagePipe |', '| --- | ---: | ---: |']
            for row in backend['scenarios']:
                allocated = row['allocatedBytesPerOp'] or {}
                lines.append('| ' + row['id'] + ' | ' + ' | '.join(fmt(allocated.get(library)) for library in LIBRARIES) + ' |')
        else:
            lines.append('Allocation: unavailable on this backend (' + '; '.join(backend['allocationDetail']) + ').')
        lines.append('')
    observations = summary['observations']
    lines += ['## Process observations (informational)', '']
    if observations:
        lines += ['| Process | Exit | Wall s | Priority | Affinity | Other Unity CPU s | Fraction of wall | Noise |',
                  '| --- | ---: | ---: | --- | --- | ---: | ---: | --- |']
        for item in observations:
            affinity = item.get('affinityApplied')
            lines.append('| ' + ' | '.join([
                str(item.get('stem')), str(item.get('exitCode')), fmt(item.get('elapsedSeconds'), 1),
                str(item.get('priorityApplied')), hex(affinity) if isinstance(affinity, int) else str(affinity),
                fmt(item.get('otherUnityCpuSeconds'), 2), fmt(item.get('otherUnityWallFraction'), 3),
                str(item.get('noiseStatus'))]) + ' |')
        lines.append('')
        lines.append('Noise rule: ' + str(observations[0].get('noiseRule', '')))
    else:
        lines.append('No observation files.')
    return '\n'.join(lines) + '\n'


def main(argv=None):
    parser = argparse.ArgumentParser(description='Validate and summarize messaging comparison reports.')
    parser.add_argument('--evidence-root', required=True, help='Directory with runs/<stem>.json reports.')
    parser.add_argument('--output-dir', help='Where to write the summary (default: the evidence root).')
    parser.add_argument('--processes', type=int, default=3, help='Required reports per backend (default 3).')
    parser.add_argument('--backends', default=','.join(DEFAULT_BACKENDS), help='Required backends (default IL2CPP,Mono).')
    parser.add_argument('--messagepipe-sha256',
                        help='Required SHA-256 of the MessagePipe package in every build: the dll for the NuGet flavor, '
                             'the .unitypackage for the Unity package flavor (default: the official 1.8.1 artifact '
                             'of the recorded flavor).')
    parser.add_argument('--allow-self-test', action='store_true', help='Accept self-test reports (never evidence).')
    parser.add_argument('--check-only', action='store_true', help='Validate and print; write nothing.')
    parser.add_argument('--force', action='store_true', help='Overwrite existing summary files.')
    args = parser.parse_args(argv)

    root = Path(args.evidence_root).resolve()
    runs = root / 'runs'
    output_dir = Path(args.output_dir).resolve() if args.output_dir else root
    backends = [backend.strip() for backend in args.backends.split(',') if backend.strip()]
    if not runs.is_dir():
        print('INVALID: no runs directory under ' + str(root))
        return 1
    if not args.check_only and not args.force:
        for name in (OUTPUT_MD, OUTPUT_JSON):
            if (output_dir / name).exists():
                print('REFUSED: ' + str(output_dir / name) + ' exists (pass --force to overwrite)')
                return 2

    errors = []
    reports = load_reports(runs)
    if not reports:
        errors.append('no reports under ' + str(runs))
    valid = [(path, report) for path, report in reports if validate_report(path, report, args.allow_self_test, errors)]
    if len({bool(report.get('selfTest')) for _, report in valid}) > 1:
        errors.append('self-test and measurement reports are mixed')
    if len({shape(report) for _, report in valid}) > 1:
        errors.append('reports differ in scenarios, operation counts or sample counts')
    flavors = {flavor_of(report) for _, report in valid}
    if len(flavors) > 1:
        errors.append('reports mix MessagePipe flavors: ' + ', '.join(sorted(flavors)))
    provenances = {provenance(report) for _, report in valid}
    if len(provenances) > 1:
        errors.append('reports differ in source HEAD, Onity version, MessagePipe version, flavor, package hash or UniTask '
                      'version: ' + '; '.join(sorted(str(item) for item in provenances)))
    for path, report in valid:
        flavor = flavor_of(report)
        required = (args.messagepipe_sha256 or OFFICIAL_MESSAGEPIPE_SHA256.get(flavor, '')).lower()
        package_hash = package_sha256_of(report)
        if package_hash != required:
            errors.append(path.name + ': MessagePipe ' + PACKAGE_KIND.get(flavor, 'package') + ' SHA-256 '
                          + (package_hash or 'missing') + ' is not the required ' + (required or 'hash'))

    grouped = {}
    for path, report in valid:
        grouped.setdefault((report.get('environment') or {}).get('scriptingBackend'), []).append((path, report))
    for backend in backends:
        items = grouped.get(backend, [])
        if len(items) != args.processes:
            errors.append(backend + ': ' + str(len(items)) + ' report(s), expected exactly ' + str(args.processes))
        guids = {(report.get('environment') or {}).get('buildGuid') for _, report in items}
        sidecars = {(report.get('build') or {}).get('buildGuid') for _, report in items}
        if len(guids) > 1 or len(sidecars) > 1:
            errors.append(backend + ': more than one build (' + ', '.join(sorted(str(guid) for guid in guids)) + ')')
    for backend in grouped:
        if backend not in backends:
            errors.append('unexpected backend ' + str(backend))

    summary = None
    if not errors:
        first = valid[0][1]
        settings = first.get('settings') or {}
        summary = {
            'schemaVersion': 1,
            'generatedUtc': datetime.now(timezone.utc).isoformat(),
            'evidenceRoot': str(root),
            'selfTest': bool(first.get('selfTest')),
            'reportCount': len(valid),
            'processesPerBackend': args.processes,
            'warmupSamples': settings.get('warmupSamples'),
            'measuredSamples': settings.get('measuredSamples'),
            'libraryVersions': {info.get('name'): info.get('version') for info in first.get('libraries') or []},
            'messagePipeFlavor': flavor_of(first),
            'messagePipePackageSha256': package_sha256_of(first),
            'messagePipePackageEntries': (first.get('build') or {}).get('messagePipePackageEntries'),
            'messagePipeDllSha256': (first.get('build') or {}).get('messagePipeDllSha256'),
            'uniTaskVersion': (first.get('build') or {}).get('uniTaskVersion'),
            'uniTaskTreeSha256': (first.get('build') or {}).get('uniTaskTreeSha256'),
            'messagePipeSetup': first.get('messagePipeSetup'),
            'messagePipeAsyncApi': (first.get('build') or {}).get('messagePipeAsyncApi'),
            'rule': RULE,
            'thresholds': {'fasterMedian': FASTER_MEDIAN, 'fasterWorst': FASTER_WORST, 'slowerMedian': SLOWER_MEDIAN},
            'backends': [summarize_backend(backend, grouped[backend], errors) for backend in backends],
            'observations': load_observations(runs),
        }
        summary['gate'] = evaluate_gate(summary)

    if errors:
        print('INVALID: ' + str(len(errors)) + ' problem(s)')
        for error in errors[:60]:
            print('  - ' + error)
        return 1

    for backend in summary['backends']:
        print(backend['backend'] + ': ' + str(len(backend['processes'])) + ' process(es), build ' + str(backend['buildGuid']))
        for row in backend['scenarios']:
            print('  %-24s Onity %8s ns/op  MessagePipe %8s ns/op  ratio %s (worst %s)  %s' % (
                row['id'], fmt(row['medianNsPerOp']['Onity']), fmt(row['medianNsPerOp'][OTHER]),
                fmt(row['ratio']['median'], 3), fmt(row['ratio']['worst'], 3), row['ratio']['class']))
    gate = summary['gate']
    if gate['evaluated']:
        print('gate (' + gate['backend'] + '): ' + ('PASS' if gate['pass'] else 'FAIL')
              + ('' if gate['pass'] else ' - not faster: ' + ', '.join(gate['failingRows'])))
    else:
        print('gate: not evaluated (' + gate['reason'] + ')')
    if summary['selfTest']:
        print('SELF-TEST parsed: harness check only, not performance evidence.')
    if args.check_only:
        print('VALID (check only; nothing written)')
        return 0
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / OUTPUT_JSON).write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    (output_dir / OUTPUT_MD).write_text(render_markdown(summary), encoding='utf-8')
    print('VALID: wrote ' + str(output_dir / OUTPUT_MD) + ' and ' + str(output_dir / OUTPUT_JSON))
    return 0


if __name__ == '__main__':
    sys.exit(main())
