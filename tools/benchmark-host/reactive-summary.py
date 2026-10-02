#!/usr/bin/env python3
"""Validate and summarize reactive comparison reports (Onity.Reactive vs R3 vs UniRx).

Reads every Player report under <evidence-root>/runs (all *.json except *.observation.json), as written
by tools/benchmark-host/run-reactive-comparison.ps1, validates them and writes reactive-summary.md and
reactive-summary.json (to --output-dir, default the evidence root). Existing outputs are never
overwritten without --force.

Validation (any failure: exit 1, nothing written):
- schema and benchmark id, completed run, no failure text, overall and per-scenario golden passed,
  no checksum mismatch in any sample, identical checksums across the three libraries;
- Release Player: not Development, not the Editor, build sidecar found and matching the Player;
- self-test reports only with --allow-self-test, and never mixed with measurement reports;
- exactly --processes reports per backend (IL2CPP and Mono by default) and one build GUID per backend;
- the same scenarios, operation counts and sample counts in every report;
- ns/op recomputed from the raw Stopwatch ticks matches the report, and so do the reported medians.

Summary per backend and scenario (IL2CPP is the headline; Mono is reported the same way):
- process median = median of that process's measured samples (recomputed from ticks);
- library ns/op = median over processes of the process medians;
- per-process ratio Onity/R3 and Onity/UniRx = Onity process median / other library process median;
- median ratio and worst (largest) ratio over processes, and the pre-registered class:
  faster when median <= 0.95 and worst <= 1.00; slower when median >= 1.05; otherwise on par.

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
BENCHMARK = 'onity-reactive-comparison'
LIBRARIES = ('Onity', 'R3', 'UniRx')
OTHERS = ('R3', 'UniRx')
DEFAULT_BACKENDS = ('IL2CPP', 'Mono')
FASTER_MEDIAN = 0.95
FASTER_WORST = 1.00
SLOWER_MEDIAN = 1.05
RULE = ('ratio = Onity process median / other library process median (time per operation, so below 1 means '
        'Onity is faster); faster when the median ratio over processes <= 0.95 and the worst process ratio '
        '<= 1.00; slower when the median ratio >= 1.05; otherwise on par')
OUTPUT_MD = 'reactive-summary.md'
OUTPUT_JSON = 'reactive-summary.json'


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


def load_reports(runs_dir):
    reports = []
    for path in sorted(runs_dir.glob('*.json')):
        if path.name.endswith('.observation.json'):
            continue
        with path.open(encoding='utf-8-sig') as handle:
            reports.append((path, json.load(handle)))
    return reports


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
        'sourceHead': (first.get('build') or {}).get('sourceHead'),
        'processorType': (first.get('environment') or {}).get('processorType'),
        'allocationAvailable': allocation_available,
        'allocationDetail': sorted({(report.get('allocation') or {}).get('detail', '') for report in reports}),
        'scenarios': [],
    }
    for index, scenario in enumerate(first.get('scenarios') or []):
        process_medians = {library: [] for library in LIBRARIES}
        allocations = {library: [] for library in LIBRARIES}
        for path, report in items:
            current = report['scenarios'][index]
            for library in current['libraries']:
                label = path.name + ' ' + current['id'] + ' ' + library['library']
                values = sample_values(report, current, library, errors, label)
                process_medians[library['library']].append(statistics.median(values) if values else 0.0)
                if library.get('allocationMeasured'):
                    allocations[library['library']].append(library.get('allocatedBytesPerOp'))
        row = {
            'id': scenario['id'],
            'operationsPerSample': scenario['operationsPerSample'],
            'processMediansNsPerOp': process_medians,
            'medianNsPerOp': {library: statistics.median(values) for library, values in process_medians.items()},
            'ratios': {},
            'allocatedBytesPerOp': ({library: statistics.median(values) for library, values in allocations.items()}
                                    if allocation_available and all(allocations.values()) else None),
        }
        for other in OTHERS:
            per_process = []
            for onity, value in zip(process_medians['Onity'], process_medians[other]):
                per_process.append(onity / value if value > 0 else None)
            valid = all(ratio is not None for ratio in per_process)
            median_ratio = statistics.median(per_process) if valid else None
            worst_ratio = max(per_process) if valid else None
            row['ratios'][other] = {
                'perProcess': per_process,
                'median': median_ratio,
                'worst': worst_ratio,
                'class': classify(median_ratio, worst_ratio),
            }
        result['scenarios'].append(row)
    return result


def fmt(value, digits=2):
    return 'n/a' if value is None else ('%.' + str(digits) + 'f') % value


def render_markdown(summary):
    lines = ['# Reactive comparison summary', '']
    if summary['selfTest']:
        lines += ['> **SELF-TEST.** Tiny counts that check the harness end to end. These numbers are not '
                  'performance evidence and must not be quoted.', '']
    lines += [
        '- Evidence root: `' + summary['evidenceRoot'] + '`',
        '- Generated (UTC): ' + summary['generatedUtc'],
        '- Reports: ' + str(summary['reportCount']) + ' (' + str(summary['processesPerBackend']) + ' per backend), '
        + 'measured samples per process: ' + str(summary['measuredSamples']) + ', warmups: ' + str(summary['warmupSamples']),
        '- Libraries: ' + ', '.join(name + ' ' + version for name, version in summary['libraryVersions'].items()),
        '- Rule (pre-registered): ' + RULE + '.',
        '- IL2CPP is the headline; Mono is reported the same way.',
        '',
    ]
    for backend in summary['backends']:
        title = backend['backend'] + (' (headline)' if backend['backend'] == 'IL2CPP' else '')
        lines += ['## ' + title, '',
                  '- Build GUID `' + str(backend['buildGuid']) + '`, Unity ' + str(backend['unityVersion'])
                  + ', Onity ' + str(backend['onityVersion']) + ', source ' + str(backend['sourceHead']),
                  '- CPU: ' + str(backend['processorType']),
                  '- Processes: ' + ', '.join(backend['processes']),
                  '',
                  '| Scenario | Ops/sample | Onity ns/op | R3 ns/op | UniRx ns/op | Onity/R3 median (worst) | vs R3 | Onity/UniRx median (worst) | vs UniRx |',
                  '| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |']
        for row in backend['scenarios']:
            medians = row['medianNsPerOp']
            r3 = row['ratios']['R3']
            unirx = row['ratios']['UniRx']
            lines.append('| ' + ' | '.join([
                row['id'], str(row['operationsPerSample']),
                fmt(medians['Onity']), fmt(medians['R3']), fmt(medians['UniRx']),
                fmt(r3['median'], 3) + ' (' + fmt(r3['worst'], 3) + ')', r3['class'],
                fmt(unirx['median'], 3) + ' (' + fmt(unirx['worst'], 3) + ')', unirx['class']]) + ' |')
        lines.append('')
        if backend['allocationAvailable']:
            lines += ['Allocated bytes per operation (separate untimed pass, median over processes):', '',
                      '| Scenario | Onity | R3 | UniRx |', '| --- | ---: | ---: | ---: |']
            for row in backend['scenarios']:
                allocated = row['allocatedBytesPerOp'] or {}
                lines.append('| ' + row['id'] + ' | ' + ' | '.join(fmt(allocated.get(library)) for library in LIBRARIES) + ' |')
        else:
            lines.append('Allocation: unavailable on this backend (' + '; '.join(backend['allocationDetail']) + ').')
        lines += ['', 'Per-process ratios:', '', '| Scenario | Onity/R3 per process | Onity/UniRx per process |',
                  '| --- | --- | --- |']
        for row in backend['scenarios']:
            lines.append('| ' + row['id'] + ' | ' + ', '.join(fmt(value, 3) for value in row['ratios']['R3']['perProcess'])
                         + ' | ' + ', '.join(fmt(value, 3) for value in row['ratios']['UniRx']['perProcess']) + ' |')
        lines.append('')
    return '\n'.join(lines) + '\n'


def main(argv=None):
    parser = argparse.ArgumentParser(description='Validate and summarize reactive comparison reports.')
    parser.add_argument('--evidence-root', required=True, help='Directory with runs/<stem>.json reports.')
    parser.add_argument('--output-dir', help='Where to write the summary (default: the evidence root).')
    parser.add_argument('--processes', type=int, default=3, help='Required reports per backend (default 3).')
    parser.add_argument('--backends', default=','.join(DEFAULT_BACKENDS), help='Required backends (default IL2CPP,Mono).')
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
            'rule': RULE,
            'thresholds': {'fasterMedian': FASTER_MEDIAN, 'fasterWorst': FASTER_WORST, 'slowerMedian': SLOWER_MEDIAN},
            'backends': [summarize_backend(backend, grouped[backend], errors) for backend in backends],
        }

    if errors:
        print('INVALID: ' + str(len(errors)) + ' problem(s)')
        for error in errors[:60]:
            print('  - ' + error)
        return 1

    for backend in summary['backends']:
        print(backend['backend'] + ': ' + str(len(backend['processes'])) + ' process(es), build ' + str(backend['buildGuid']))
        for row in backend['scenarios']:
            print('  %-26s Onity/R3 %s (%s)  Onity/UniRx %s (%s)' % (
                row['id'], fmt(row['ratios']['R3']['median'], 3), row['ratios']['R3']['class'],
                fmt(row['ratios']['UniRx']['median'], 3), row['ratios']['UniRx']['class']))
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
