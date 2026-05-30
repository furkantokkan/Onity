#!/usr/bin/env python3
"""
Render DI benchmark charts from Onity benchmark JSON output.

Usage:
  python Assets/Onity.Benchmarks/Tools/render_di_benchmark_charts.py \
      --input Assets/Onity.Benchmarks/Results/di-benchmark-latest.json \
      --output-dir Assets/Onity.Benchmarks/Results
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from typing import Dict, List, Tuple


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Render Onity DI benchmark charts.")
    parser.add_argument(
        "--input",
        default="Assets/Onity.Benchmarks/Results/di-benchmark-latest.json",
        help="Path to benchmark JSON report.",
    )
    parser.add_argument(
        "--output-dir",
        default="Assets/Onity.Benchmarks/Results",
        help="Directory where charts will be written.",
    )
    return parser.parse_args()


def load_report(path: str) -> Dict:
    with open(path, "r", encoding="utf-8-sig") as file:
        return json.load(file)


def get_repo_root() -> str:
    script_directory = os.path.dirname(os.path.abspath(__file__))
    return os.path.abspath(os.path.join(script_directory, "..", "..", ".."))


def resolve_path(path: str, prefer_repo_root: bool) -> str:
    if os.path.isabs(path):
        return path

    cwd_candidate = os.path.abspath(path)
    repo_candidate = os.path.abspath(os.path.join(get_repo_root(), path))

    candidates = [repo_candidate, cwd_candidate] if prefer_repo_root else [cwd_candidate, repo_candidate]

    for candidate in candidates:
        if os.path.exists(candidate):
            return candidate

    return candidates[0]


def container_sort_key(container: str) -> int:
    ordering = {
        "Onity": 0,
        "VContainer": 1,
        "Zenject": 2,
    }
    return ordering.get(container, 99)


def collect_containers(report: Dict) -> List[str]:
    names = set()

    for scenario in report.get("scenarios", []):
        for result in scenario.get("results", []):
            names.add(result.get("container", "Unknown"))

    return sorted(names, key=container_sort_key)


def collect_scenario_rows(report: Dict, containers: List[str]) -> List[Tuple[str, Dict[str, float], Dict[str, float]]]:
    rows = []

    for scenario in report.get("scenarios", []):
        name = scenario.get("displayName", scenario.get("scenario", "Unknown"))
        ms_map = {container: math.nan for container in containers}
        alloc_map = {container: math.nan for container in containers}

        for result in scenario.get("results", []):
            container = result.get("container", "Unknown")
            ms_map[container] = float(result.get("meanMilliseconds", math.nan))
            alloc_map[container] = float(result.get("allocBytesPerSampleMean", math.nan))

        rows.append((name, ms_map, alloc_map))

    return rows


def render_runtime_chart(
    report: Dict,
    rows: List[Tuple[str, Dict[str, float], Dict[str, float]]],
    containers: List[str],
    output_path: str,
) -> None:
    try:
        import matplotlib.pyplot as plt
        import numpy as np
    except ImportError:
        raise RuntimeError("matplotlib and numpy are required. Install with: pip install matplotlib numpy")

    colors = {
        "Onity": "#22c55e",
        "VContainer": "#3b82f6",
        "Zenject": "#ef4444",
    }

    scenario_names = [row[0] for row in rows]
    x = np.arange(len(scenario_names))
    width = 0.8 / max(1, len(containers))

    fig, ax = plt.subplots(figsize=(14, 7))

    for index, container in enumerate(containers):
        values = [row[1][container] for row in rows]
        offset = (index - (len(containers) - 1) / 2) * width
        ax.bar(
            x + offset,
            values,
            width=width,
            label=container,
            color=colors.get(container, "#888888"),
        )

    iterations = report.get("scenarios", [{}])[0].get("iterationsPerSample", 0)
    samples = report.get("samplesPerCase", 0)
    unity_version = report.get("unityVersion", "Unknown")
    platform = report.get("platform", "Unknown")

    ax.set_title(
        f"DI Benchmark Runtime (mean ms per sample)\n"
        f"{iterations:,} iterations x {samples} samples ({unity_version} / {platform})"
    )
    ax.set_xticks(x)
    ax.set_xticklabels(scenario_names, rotation=18, ha="right")
    ax.set_ylabel("Milliseconds (lower is better)")
    ax.grid(axis="y", alpha=0.25)
    ax.legend(loc="upper left")
    fig.tight_layout()
    fig.savefig(output_path, dpi=160)
    plt.close(fig)


def render_gc_chart(
    rows: List[Tuple[str, Dict[str, float], Dict[str, float]]],
    containers: List[str],
    output_path: str,
) -> None:
    try:
        import matplotlib.pyplot as plt
        import numpy as np
    except ImportError:
        raise RuntimeError("matplotlib and numpy are required. Install with: pip install matplotlib numpy")

    selected_row = None

    for row in rows:
        if "Resolve (Complex)" in row[0]:
            selected_row = row
            break

    if selected_row is None and rows:
        selected_row = rows[0]

    if selected_row is None:
        raise RuntimeError("No scenario data found in report.")

    scenario_name = selected_row[0]
    values_kb = [selected_row[2][container] / 1024.0 for container in containers]
    colors = {
        "Onity": "#22c55e",
        "VContainer": "#3b82f6",
        "Zenject": "#ef4444",
    }

    x = np.arange(len(containers))

    fig, ax = plt.subplots(figsize=(10, 6))
    ax.bar(x, values_kb, color=[colors.get(name, "#888888") for name in containers], width=0.6)
    ax.set_xticks(x)
    ax.set_xticklabels(containers)
    ax.set_ylabel("Allocated KB per sample (lower is better)")
    ax.set_title(f"GC Allocation Benchmark ({scenario_name})")
    ax.grid(axis="y", alpha=0.25)
    fig.tight_layout()
    fig.savefig(output_path, dpi=160)
    plt.close(fig)


def render_summary_markdown(
    rows: List[Tuple[str, Dict[str, float], Dict[str, float]]],
    containers: List[str],
    output_path: str,
) -> None:
    lines: List[str] = []
    lines.append("# DI Benchmark Summary")
    lines.append("")
    lines.append("| Scenario | Container | Mean (ms) | Alloc/sample (B) |")
    lines.append("|---|---|---:|---:|")

    for scenario_name, ms_map, alloc_map in rows:
        for container in containers:
            lines.append(
                f"| {scenario_name} | {container} | {ms_map[container]:.4f} | {alloc_map[container]:.2f} |"
            )

    lines.append("")
    lines.append("## Relative Speedup vs VContainer")
    lines.append("")
    lines.append("| Scenario | Onity speedup |")
    lines.append("|---|---:|")

    for scenario_name, ms_map, _ in rows:
        onity = ms_map.get("Onity", math.nan)
        vcontainer = ms_map.get("VContainer", math.nan)

        if math.isnan(onity) or math.isnan(vcontainer) or vcontainer <= 0:
            lines.append(f"| {scenario_name} | N/A |")
            continue

        speedup = (vcontainer - onity) / vcontainer * 100.0
        lines.append(f"| {scenario_name} | {speedup:+.2f}% |")

    with open(output_path, "w", encoding="utf-8") as file:
        file.write("\n".join(lines))


def main() -> int:
    args = parse_args()
    input_path = resolve_path(args.input, prefer_repo_root=True)
    output_directory = resolve_path(args.output_dir, prefer_repo_root=True)

    if os.path.exists(input_path) is False:
        raise FileNotFoundError(
            f"Benchmark report not found at '{input_path}'. "
            "Run Unity menu 'Onity/Benchmarks/Run DI Benchmarks (Editor)' first."
        )

    os.makedirs(output_directory, exist_ok=True)

    report = load_report(input_path)
    containers = collect_containers(report)
    rows = collect_scenario_rows(report, containers)

    runtime_chart = os.path.join(output_directory, "di-runtime-comparison.png")
    gc_chart = os.path.join(output_directory, "di-gc-alloc-comparison.png")
    summary_md = os.path.join(output_directory, "di-benchmark-summary.md")

    render_runtime_chart(report, rows, containers, runtime_chart)
    render_gc_chart(rows, containers, gc_chart)
    render_summary_markdown(rows, containers, summary_md)

    print("Rendered benchmark artifacts:")
    print(f"- {runtime_chart}")
    print(f"- {gc_chart}")
    print(f"- {summary_md}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # pylint: disable=broad-except
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)
