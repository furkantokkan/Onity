# OnityTask surpass gate

- Generated: 2026-10-02T03:11:44.635356+00:00
- Mode: gate; primary statistic: mean
- Thresholds: primary synchronous rows median <= 1.00; other gated rows median <= 0.95 and worst process <= 1.00. Gate backend: IL2CPP; Mono is reported only.
- Verdict: **PASS**

## IL2CPP (gate)

- Evidence valid: True; build GUIDs: a1e17c822d2b46799b38e6621ec43969
- builderlifecycle-default: 1 process(es): il2cpp-builderlifecycle-default-p1.json
- builderlifecycle-matched: 3 process(es): il2cpp-builderlifecycle-matched-p1.json, il2cpp-builderlifecycle-matched-p2.json, il2cpp-builderlifecycle-matched-p3.json
- primary-default: 1 process(es): il2cpp-primary-default-p1.json
- primary-matched: 3 process(es): il2cpp-primary-matched-p1.json, il2cpp-primary-matched-p2.json, il2cpp-primary-matched-p3.json
- throughput-matched: 3 process(es): il2cpp-throughput-matched-p1.json, il2cpp-throughput-matched-p2.json, il2cpp-throughput-matched-p3.json

| Suite | Row | N | Per-process Onity/UniTask | Median | Worst | Threshold | Result |
|---|---|---:|---|---:|---:|---|---|
| primary | Completed GetResult | 1 | 0.516, 0.512, 0.515 | 0.515 | 0.516 | median<=1.00 | PASS |
| primary | FromResult<int> GetResult | 1 | 0.780, 0.906, 0.808 | 0.808 | 0.906 | median<=1.00 | PASS |
| primary | NextFrame scheduling | 128 | 0.108, 0.108, 0.110 | 0.108 | 0.110 | median<=0.95, worst<=1.00 | PASS |
| primary | NextFrame GetResult | 128 | 0.124, 0.126, 0.125 | 0.125 | 0.126 | median<=0.95, worst<=1.00 | PASS |
| primary | NextFrame scheduling | 4096 | 0.098, 0.098, 0.098 | 0.098 | 0.098 | median<=0.95, worst<=1.00 | PASS |
| primary | NextFrame GetResult | 4096 | 0.086, 0.084, 0.085 | 0.085 | 0.086 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method completed GetResult | 1 | 0.619, 0.634, 0.629 | 0.629 | 0.634 | median<=1.00 | PASS |
| primary | Async method completed<int> GetResult | 1 | 0.742, 0.750, 0.753 | 0.750 | 0.753 | median<=1.00 | PASS |
| primary | Async method NextFrame scheduling | 128 | 0.415, 0.415, 0.411 | 0.415 | 0.415 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame GetResult | 128 | 0.325, 0.329, 0.326 | 0.326 | 0.329 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame scheduling | 4096 | 0.411, 0.413, 0.409 | 0.411 | 0.413 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame GetResult | 4096 | 0.307, 0.306, 0.317 | 0.307 | 0.317 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> scheduling | 128 | 0.418, 0.401, 0.415 | 0.415 | 0.418 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> GetResult | 128 | 0.341, 0.326, 0.330 | 0.330 | 0.341 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> scheduling | 4096 | 0.399, 0.385, 0.397 | 0.397 | 0.399 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> GetResult | 4096 | 0.311, 0.311, 0.325 | 0.311 | 0.325 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=128 1-susp | 128 | 0.498, 0.511, 0.534 | 0.511 | 0.534 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=128 4-susp | 128 | 0.603, 0.612, 0.595 | 0.603 | 0.612 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=4096 1-susp | 4096 | 0.494, 0.490, 0.485 | 0.490 | 0.494 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=4096 4-susp | 4096 | 0.598, 0.598, 0.596 | 0.598 | 0.598 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Typed int N=128 1-susp | 128 | 0.501, 0.623, 0.501 | 0.501 | 0.623 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Typed int N=128 4-susp | 128 | 0.603, 0.874, 0.605 | 0.605 | 0.874 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Typed int N=4096 1-susp | 4096 | 0.495, 0.523, 0.491 | 0.495 | 0.523 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Typed int N=4096 4-susp | 4096 | 0.587, 0.637, 0.594 | 0.594 | 0.637 | median<=0.95, worst<=1.00 | PASS |
| throughput | NextFrame loops N=1024 K=16 | 1024 | 0.299, 0.293, 0.290 | 0.293 | 0.299 | median<=0.95, worst<=1.00 | PASS |
| throughput | NextFrame loops N=4096 K=16 | 4096 | 0.278, 0.280, 0.280 | 0.280 | 0.280 | median<=0.95, worst<=1.00 | PASS |
| throughput | Yield loops N=1024 K=16 | 1024 | 0.377, 0.352, 0.350 | 0.352 | 0.377 | median<=0.95, worst<=1.00 | PASS |
| throughput | Yield loops N=4096 K=16 | 4096 | 0.353, 0.355, 0.336 | 0.353 | 0.355 | median<=0.95, worst<=1.00 | PASS |
| throughput | NextFrame<int> loops N=1024 K=16 | 1024 | 0.285, 0.275, 0.288 | 0.285 | 0.288 | median<=0.95, worst<=1.00 | PASS |

Report-only rows (flow on, cohort 1, default retention):

| Suite | Retention | Row | N | Per-process Onity/UniTask | Median | Worst |
|---|---|---|---:|---|---:|---:|
| primary | matched | Async method NextFrame scheduling (flow on) | 128 | 0.512, 0.524, 0.526 | 0.524 | 0.526 |
| primary | matched | Async method NextFrame GetResult (flow on) | 128 | 0.321, 0.320, 0.334 | 0.321 | 0.334 |
| primary | matched | Async method NextFrame scheduling (flow on) | 4096 | 0.509, 0.507, 0.514 | 0.509 | 0.514 |
| primary | matched | Async method NextFrame GetResult (flow on) | 4096 | 0.313, 0.301, 0.316 | 0.313 | 0.316 |
| primary | matched | Async method NextFrame<int> scheduling (flow on) | 128 | 0.526, 0.507, 0.525 | 0.525 | 0.526 |
| primary | matched | Async method NextFrame<int> GetResult (flow on) | 128 | 0.326, 0.319, 0.325 | 0.325 | 0.326 |
| primary | matched | Async method NextFrame<int> scheduling (flow on) | 4096 | 0.499, 0.497, 0.501 | 0.499 | 0.501 |
| primary | matched | Async method NextFrame<int> GetResult (flow on) | 4096 | 0.316, 0.312, 0.325 | 0.316 | 0.325 |
| builderlifecycle | matched | Untyped N=1 1-susp | 1 | 0.862, 0.883, 0.857 | 0.862 | 0.883 |
| builderlifecycle | matched | Untyped N=1 4-susp | 1 | 0.901, 0.910, 0.894 | 0.901 | 0.910 |
| builderlifecycle | matched | Typed int N=1 1-susp | 1 | 0.867, 0.858, 0.874 | 0.867 | 0.874 |
| builderlifecycle | matched | Typed int N=1 4-susp | 1 | 0.891, 0.893, 0.886 | 0.891 | 0.893 |
| builderlifecycle | matched | Untyped N=1 1-susp (flow on) | 1 | 0.955, 0.948, 0.970 | 0.955 | 0.970 |
| builderlifecycle | matched | Untyped N=1 4-susp (flow on) | 1 | 1.066, 1.071, 1.072 | 1.071 | 1.072 |
| builderlifecycle | matched | Untyped N=128 1-susp (flow on) | 128 | 0.818, 0.845, 0.836 | 0.836 | 0.845 |
| builderlifecycle | matched | Untyped N=128 4-susp (flow on) | 128 | 1.601, 1.622, 1.597 | 1.601 | 1.622 |
| builderlifecycle | matched | Untyped N=4096 1-susp (flow on) | 4096 | 0.816, 0.838, 0.807 | 0.816 | 0.838 |
| builderlifecycle | matched | Untyped N=4096 4-susp (flow on) | 4096 | 1.518, 1.568, 1.488 | 1.518 | 1.568 |
| builderlifecycle | matched | Typed int N=1 1-susp (flow on) | 1 | 0.959, 0.946, 0.945 | 0.946 | 0.959 |
| builderlifecycle | matched | Typed int N=1 4-susp (flow on) | 1 | 1.087, 1.075, 1.070 | 1.075 | 1.087 |
| builderlifecycle | matched | Typed int N=128 1-susp (flow on) | 128 | 0.810, 0.826, 0.828 | 0.826 | 0.828 |
| builderlifecycle | matched | Typed int N=128 4-susp (flow on) | 128 | 1.571, 1.548, 1.572 | 1.571 | 1.572 |
| builderlifecycle | matched | Typed int N=4096 1-susp (flow on) | 4096 | 0.815, 0.864, 0.823 | 0.823 | 0.864 |
| builderlifecycle | matched | Typed int N=4096 4-susp (flow on) | 4096 | 1.514, 1.570, 1.472 | 1.514 | 1.570 |
| primary | default | Completed GetResult | 1 | 0.538 | 0.538 | 0.538 |
| primary | default | FromResult<int> GetResult | 1 | 0.765 | 0.765 | 0.765 |
| primary | default | NextFrame scheduling | 128 | 0.107 | 0.107 | 0.107 |
| primary | default | NextFrame GetResult | 128 | 0.123 | 0.123 | 0.123 |
| primary | default | NextFrame scheduling | 4096 | 0.096 | 0.096 | 0.096 |
| primary | default | NextFrame GetResult | 4096 | 0.083 | 0.083 | 0.083 |
| primary | default | Async method completed GetResult | 1 | 0.623 | 0.623 | 0.623 |
| primary | default | Async method completed<int> GetResult | 1 | 0.690 | 0.690 | 0.690 |
| primary | default | Async method NextFrame scheduling | 128 | 0.408 | 0.408 | 0.408 |
| primary | default | Async method NextFrame GetResult | 128 | 0.333 | 0.333 | 0.333 |
| primary | default | Async method NextFrame scheduling | 4096 | 0.715 | 0.715 | 0.715 |
| primary | default | Async method NextFrame GetResult | 4096 | 0.269 | 0.269 | 0.269 |
| primary | default | Async method NextFrame<int> scheduling | 128 | 0.402 | 0.402 | 0.402 |
| primary | default | Async method NextFrame<int> GetResult | 128 | 0.320 | 0.320 | 0.320 |
| primary | default | Async method NextFrame<int> scheduling | 4096 | 0.710 | 0.710 | 0.710 |
| primary | default | Async method NextFrame<int> GetResult | 4096 | 0.276 | 0.276 | 0.276 |
| primary | default | Async method NextFrame scheduling (flow on) | 128 | 0.510 | 0.510 | 0.510 |
| primary | default | Async method NextFrame GetResult (flow on) | 128 | 0.319 | 0.319 | 0.319 |
| primary | default | Async method NextFrame scheduling (flow on) | 4096 | 0.894 | 0.894 | 0.894 |
| primary | default | Async method NextFrame GetResult (flow on) | 4096 | 0.293 | 0.293 | 0.293 |
| primary | default | Async method NextFrame<int> scheduling (flow on) | 128 | 0.506 | 0.506 | 0.506 |
| primary | default | Async method NextFrame<int> GetResult (flow on) | 128 | 0.323 | 0.323 | 0.323 |
| primary | default | Async method NextFrame<int> scheduling (flow on) | 4096 | 0.801 | 0.801 | 0.801 |
| primary | default | Async method NextFrame<int> GetResult (flow on) | 4096 | 0.271 | 0.271 | 0.271 |
| builderlifecycle | default | Untyped N=1 1-susp | 1 | 0.853 | 0.853 | 0.853 |
| builderlifecycle | default | Untyped N=1 4-susp | 1 | 0.897 | 0.897 | 0.897 |
| builderlifecycle | default | Untyped N=128 1-susp | 128 | 0.511 | 0.511 | 0.511 |
| builderlifecycle | default | Untyped N=128 4-susp | 128 | 0.611 | 0.611 | 0.611 |
| builderlifecycle | default | Untyped N=4096 1-susp | 4096 | 2.575 | 2.575 | 2.575 |
| builderlifecycle | default | Untyped N=4096 4-susp | 4096 | 1.820 | 1.820 | 1.820 |
| builderlifecycle | default | Typed int N=1 1-susp | 1 | 0.927 | 0.927 | 0.927 |
| builderlifecycle | default | Typed int N=1 4-susp | 1 | 0.893 | 0.893 | 0.893 |
| builderlifecycle | default | Typed int N=128 1-susp | 128 | 0.499 | 0.499 | 0.499 |
| builderlifecycle | default | Typed int N=128 4-susp | 128 | 0.608 | 0.608 | 0.608 |
| builderlifecycle | default | Typed int N=4096 1-susp | 4096 | 1.472 | 1.472 | 1.472 |
| builderlifecycle | default | Typed int N=4096 4-susp | 4096 | 0.946 | 0.946 | 0.946 |
| builderlifecycle | default | Untyped N=1 1-susp (flow on) | 1 | 0.927 | 0.927 | 0.927 |
| builderlifecycle | default | Untyped N=1 4-susp (flow on) | 1 | 1.073 | 1.073 | 1.073 |
| builderlifecycle | default | Untyped N=128 1-susp (flow on) | 128 | 0.819 | 0.819 | 0.819 |
| builderlifecycle | default | Untyped N=128 4-susp (flow on) | 128 | 1.586 | 1.586 | 1.586 |
| builderlifecycle | default | Untyped N=4096 1-susp (flow on) | 4096 | 1.801 | 1.801 | 1.801 |
| builderlifecycle | default | Untyped N=4096 4-susp (flow on) | 4096 | 1.879 | 1.879 | 1.879 |
| builderlifecycle | default | Typed int N=1 1-susp (flow on) | 1 | 0.937 | 0.937 | 0.937 |
| builderlifecycle | default | Typed int N=1 4-susp (flow on) | 1 | 1.080 | 1.080 | 1.080 |
| builderlifecycle | default | Typed int N=128 1-susp (flow on) | 128 | 0.821 | 0.821 | 0.821 |
| builderlifecycle | default | Typed int N=128 4-susp (flow on) | 128 | 1.579 | 1.579 | 1.579 |
| builderlifecycle | default | Typed int N=4096 1-susp (flow on) | 4096 | 1.579 | 1.579 | 1.579 |
| builderlifecycle | default | Typed int N=4096 4-susp (flow on) | 4096 | 2.395 | 2.395 | 2.395 |

## Mono (reported, not gating)

- Evidence valid: True; build GUIDs: ad8bc783e0d34baab4bf1afb000e3141
- builderlifecycle-default: 1 process(es): mono-builderlifecycle-default-p1.json
- builderlifecycle-matched: 3 process(es): mono-builderlifecycle-matched-p1.json, mono-builderlifecycle-matched-p2.json, mono-builderlifecycle-matched-p3.json
- primary-default: 1 process(es): mono-primary-default-p1.json
- primary-matched: 3 process(es): mono-primary-matched-p1.json, mono-primary-matched-p2.json, mono-primary-matched-p3.json
- throughput-matched: 3 process(es): mono-throughput-matched-p1.json, mono-throughput-matched-p2.json, mono-throughput-matched-p3.json

| Suite | Row | N | Per-process Onity/UniTask | Median | Worst | Threshold | Result |
|---|---|---:|---|---:|---:|---|---|
| primary | Completed GetResult | 1 | 1.319, 1.273, 1.291 | 1.291 | 1.319 | median<=1.00 | FAIL |
| primary | FromResult<int> GetResult | 1 | 1.924, 1.923, 1.925 | 1.924 | 1.925 | median<=1.00 | FAIL |
| primary | NextFrame scheduling | 128 | 0.311, 0.314, 0.320 | 0.314 | 0.320 | median<=0.95, worst<=1.00 | PASS |
| primary | NextFrame GetResult | 128 | 0.133, 0.133, 0.138 | 0.133 | 0.138 | median<=0.95, worst<=1.00 | PASS |
| primary | NextFrame scheduling | 4096 | 0.318, 0.321, 0.319 | 0.319 | 0.321 | median<=0.95, worst<=1.00 | PASS |
| primary | NextFrame GetResult | 4096 | 0.116, 0.120, 0.125 | 0.120 | 0.125 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method completed GetResult | 1 | 1.087, 1.104, 1.035 | 1.087 | 1.104 | median<=1.00 | FAIL |
| primary | Async method completed<int> GetResult | 1 | 1.218, 1.192, 1.196 | 1.196 | 1.218 | median<=1.00 | FAIL |
| primary | Async method NextFrame scheduling | 128 | 0.581, 0.581, 0.587 | 0.581 | 0.587 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame GetResult | 128 | 0.784, 0.756, 0.783 | 0.783 | 0.784 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame scheduling | 4096 | 0.581, 0.588, 0.586 | 0.586 | 0.588 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame GetResult | 4096 | 0.811, 0.826, 0.814 | 0.814 | 0.826 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> scheduling | 128 | 0.564, 0.570, 0.559 | 0.564 | 0.570 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> GetResult | 128 | 0.789, 0.766, 0.745 | 0.766 | 0.789 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> scheduling | 4096 | 0.559, 0.572, 0.566 | 0.566 | 0.572 | median<=0.95, worst<=1.00 | PASS |
| primary | Async method NextFrame<int> GetResult | 4096 | 0.709, 0.758, 0.754 | 0.754 | 0.758 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=128 1-susp | 128 | 0.873, 0.885, 0.887 | 0.885 | 0.887 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=128 4-susp | 128 | 0.943, 0.932, 0.924 | 0.932 | 0.943 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=4096 1-susp | 4096 | 0.874, 0.889, 0.894 | 0.889 | 0.894 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Untyped N=4096 4-susp | 4096 | 1.009, 0.926, 0.935 | 0.935 | 1.009 | median<=0.95, worst<=1.00 | FAIL |
| builderlifecycle | Typed int N=128 1-susp | 128 | 0.895, 0.912, 0.886 | 0.895 | 0.912 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Typed int N=128 4-susp | 128 | 0.973, 0.976, 0.962 | 0.973 | 0.976 | median<=0.95, worst<=1.00 | FAIL |
| builderlifecycle | Typed int N=4096 1-susp | 4096 | 0.909, 0.907, 0.916 | 0.909 | 0.916 | median<=0.95, worst<=1.00 | PASS |
| builderlifecycle | Typed int N=4096 4-susp | 4096 | 0.971, 0.966, 0.977 | 0.971 | 0.977 | median<=0.95, worst<=1.00 | FAIL |
| throughput | NextFrame loops N=1024 K=16 | 1024 | 0.361, 0.365, 0.366 | 0.365 | 0.366 | median<=0.95, worst<=1.00 | PASS |
| throughput | NextFrame loops N=4096 K=16 | 4096 | 0.366, 0.361, 0.354 | 0.361 | 0.366 | median<=0.95, worst<=1.00 | PASS |
| throughput | Yield loops N=1024 K=16 | 1024 | 0.674, 0.709, 0.666 | 0.674 | 0.709 | median<=0.95, worst<=1.00 | PASS |
| throughput | Yield loops N=4096 K=16 | 4096 | 0.721, 0.730, 0.674 | 0.721 | 0.730 | median<=0.95, worst<=1.00 | PASS |
| throughput | NextFrame<int> loops N=1024 K=16 | 1024 | 0.370, 0.384, 0.361 | 0.370 | 0.384 | median<=0.95, worst<=1.00 | PASS |

Report-only rows (flow on, cohort 1, default retention):

| Suite | Retention | Row | N | Per-process Onity/UniTask | Median | Worst |
|---|---|---|---:|---|---:|---:|
| primary | matched | Async method NextFrame scheduling (flow on) | 128 | 0.763, 0.778, 0.769 | 0.769 | 0.778 |
| primary | matched | Async method NextFrame GetResult (flow on) | 128 | 0.777, 0.768, 0.759 | 0.768 | 0.777 |
| primary | matched | Async method NextFrame scheduling (flow on) | 4096 | 0.761, 0.769, 0.759 | 0.761 | 0.769 |
| primary | matched | Async method NextFrame GetResult (flow on) | 4096 | 0.819, 0.821, 0.809 | 0.819 | 0.821 |
| primary | matched | Async method NextFrame<int> scheduling (flow on) | 128 | 0.755, 0.783, 0.758 | 0.758 | 0.783 |
| primary | matched | Async method NextFrame<int> GetResult (flow on) | 128 | 0.780, 0.724, 0.685 | 0.724 | 0.780 |
| primary | matched | Async method NextFrame<int> scheduling (flow on) | 4096 | 0.734, 0.761, 0.753 | 0.753 | 0.761 |
| primary | matched | Async method NextFrame<int> GetResult (flow on) | 4096 | 0.701, 0.753, 0.753 | 0.753 | 0.753 |
| builderlifecycle | matched | Untyped N=1 1-susp | 1 | 0.983, 0.969, 0.986 | 0.983 | 0.986 |
| builderlifecycle | matched | Untyped N=1 4-susp | 1 | 1.003, 1.011, 0.992 | 1.003 | 1.011 |
| builderlifecycle | matched | Typed int N=1 1-susp | 1 | 0.961, 0.970, 1.015 | 0.970 | 1.015 |
| builderlifecycle | matched | Typed int N=1 4-susp | 1 | 0.987, 0.992, 1.020 | 0.992 | 1.020 |
| builderlifecycle | matched | Untyped N=1 1-susp (flow on) | 1 | 1.336, 1.336, 1.382 | 1.336 | 1.382 |
| builderlifecycle | matched | Untyped N=1 4-susp (flow on) | 1 | 1.923, 1.871, 1.911 | 1.911 | 1.923 |
| builderlifecycle | matched | Untyped N=128 1-susp (flow on) | 128 | 1.627, 1.626, 1.624 | 1.626 | 1.627 |
| builderlifecycle | matched | Untyped N=128 4-susp (flow on) | 128 | 2.738, 2.721, 2.720 | 2.721 | 2.738 |
| builderlifecycle | matched | Untyped N=4096 1-susp (flow on) | 4096 | 1.612, 1.629, 1.655 | 1.629 | 1.655 |
| builderlifecycle | matched | Untyped N=4096 4-susp (flow on) | 4096 | 2.657, 2.646, 2.633 | 2.646 | 2.657 |
| builderlifecycle | matched | Typed int N=1 1-susp (flow on) | 1 | 1.355, 1.340, 1.397 | 1.355 | 1.397 |
| builderlifecycle | matched | Typed int N=1 4-susp (flow on) | 1 | 1.875, 1.873, 1.970 | 1.875 | 1.970 |
| builderlifecycle | matched | Typed int N=128 1-susp (flow on) | 128 | 1.641, 1.648, 1.645 | 1.645 | 1.648 |
| builderlifecycle | matched | Typed int N=128 4-susp (flow on) | 128 | 2.716, 2.699, 2.689 | 2.699 | 2.716 |
| builderlifecycle | matched | Typed int N=4096 1-susp (flow on) | 4096 | 1.651, 1.634, 1.653 | 1.651 | 1.653 |
| builderlifecycle | matched | Typed int N=4096 4-susp (flow on) | 4096 | 2.602, 2.660, 2.675 | 2.660 | 2.675 |
| primary | default | Completed GetResult | 1 | 1.307 | 1.307 | 1.307 |
| primary | default | FromResult<int> GetResult | 1 | 1.935 | 1.935 | 1.935 |
| primary | default | NextFrame scheduling | 128 | 0.321 | 0.321 | 0.321 |
| primary | default | NextFrame GetResult | 128 | 0.121 | 0.121 | 0.121 |
| primary | default | NextFrame scheduling | 4096 | 0.315 | 0.315 | 0.315 |
| primary | default | NextFrame GetResult | 4096 | 0.101 | 0.101 | 0.101 |
| primary | default | Async method completed GetResult | 1 | 1.082 | 1.082 | 1.082 |
| primary | default | Async method completed<int> GetResult | 1 | 1.242 | 1.242 | 1.242 |
| primary | default | Async method NextFrame scheduling | 128 | 0.609 | 0.609 | 0.609 |
| primary | default | Async method NextFrame GetResult | 128 | 0.745 | 0.745 | 0.745 |
| primary | default | Async method NextFrame scheduling | 4096 | 0.882 | 0.882 | 0.882 |
| primary | default | Async method NextFrame GetResult | 4096 | 0.584 | 0.584 | 0.584 |
| primary | default | Async method NextFrame<int> scheduling | 128 | 0.573 | 0.573 | 0.573 |
| primary | default | Async method NextFrame<int> GetResult | 128 | 0.737 | 0.737 | 0.737 |
| primary | default | Async method NextFrame<int> scheduling | 4096 | 0.891 | 0.891 | 0.891 |
| primary | default | Async method NextFrame<int> GetResult | 4096 | 0.537 | 0.537 | 0.537 |
| primary | default | Async method NextFrame scheduling (flow on) | 128 | 0.776 | 0.776 | 0.776 |
| primary | default | Async method NextFrame GetResult (flow on) | 128 | 0.763 | 0.763 | 0.763 |
| primary | default | Async method NextFrame scheduling (flow on) | 4096 | 1.044 | 1.044 | 1.044 |
| primary | default | Async method NextFrame GetResult (flow on) | 4096 | 0.569 | 0.569 | 0.569 |
| primary | default | Async method NextFrame<int> scheduling (flow on) | 128 | 0.771 | 0.771 | 0.771 |
| primary | default | Async method NextFrame<int> GetResult (flow on) | 128 | 0.758 | 0.758 | 0.758 |
| primary | default | Async method NextFrame<int> scheduling (flow on) | 4096 | 1.023 | 1.023 | 1.023 |
| primary | default | Async method NextFrame<int> GetResult (flow on) | 4096 | 0.525 | 0.525 | 0.525 |
| builderlifecycle | default | Untyped N=1 1-susp | 1 | 0.968 | 0.968 | 0.968 |
| builderlifecycle | default | Untyped N=1 4-susp | 1 | 0.981 | 0.981 | 0.981 |
| builderlifecycle | default | Untyped N=128 1-susp | 128 | 0.884 | 0.884 | 0.884 |
| builderlifecycle | default | Untyped N=128 4-susp | 128 | 0.931 | 0.931 | 0.931 |
| builderlifecycle | default | Untyped N=4096 1-susp | 4096 | 2.495 | 2.495 | 2.495 |
| builderlifecycle | default | Untyped N=4096 4-susp | 4096 | 1.805 | 1.805 | 1.805 |
| builderlifecycle | default | Typed int N=1 1-susp | 1 | 0.990 | 0.990 | 0.990 |
| builderlifecycle | default | Typed int N=1 4-susp | 1 | 1.001 | 1.001 | 1.001 |
| builderlifecycle | default | Typed int N=128 1-susp | 128 | 0.910 | 0.910 | 0.910 |
| builderlifecycle | default | Typed int N=128 4-susp | 128 | 0.990 | 0.990 | 0.990 |
| builderlifecycle | default | Typed int N=4096 1-susp | 4096 | 2.370 | 2.370 | 2.370 |
| builderlifecycle | default | Typed int N=4096 4-susp | 4096 | 1.632 | 1.632 | 1.632 |
| builderlifecycle | default | Untyped N=1 1-susp (flow on) | 1 | 1.342 | 1.342 | 1.342 |
| builderlifecycle | default | Untyped N=1 4-susp (flow on) | 1 | 1.907 | 1.907 | 1.907 |
| builderlifecycle | default | Untyped N=128 1-susp (flow on) | 128 | 1.637 | 1.637 | 1.637 |
| builderlifecycle | default | Untyped N=128 4-susp (flow on) | 128 | 2.774 | 2.774 | 2.774 |
| builderlifecycle | default | Untyped N=4096 1-susp (flow on) | 4096 | 2.677 | 2.677 | 2.677 |
| builderlifecycle | default | Untyped N=4096 4-susp (flow on) | 4096 | 3.403 | 3.403 | 3.403 |
| builderlifecycle | default | Typed int N=1 1-susp (flow on) | 1 | 1.314 | 1.314 | 1.314 |
| builderlifecycle | default | Typed int N=1 4-susp (flow on) | 1 | 1.913 | 1.913 | 1.913 |
| builderlifecycle | default | Typed int N=128 1-susp (flow on) | 128 | 1.677 | 1.677 | 1.677 |
| builderlifecycle | default | Typed int N=128 4-susp (flow on) | 128 | 2.712 | 2.712 | 2.712 |
| builderlifecycle | default | Typed int N=4096 1-susp (flow on) | 4096 | 3.176 | 3.176 | 3.176 |
| builderlifecycle | default | Typed int N=4096 4-susp (flow on) | 4096 | 3.755 | 3.755 | 3.755 |

Noise screen (informational): il2cpp-builderlifecycle-default-p1.json=flagged, il2cpp-builderlifecycle-matched-p1.json=flagged, il2cpp-builderlifecycle-matched-p2.json=flagged, il2cpp-builderlifecycle-matched-p3.json=flagged, il2cpp-primary-default-p1.json=flagged, il2cpp-primary-matched-p1.json=flagged, il2cpp-primary-matched-p2.json=flagged, il2cpp-primary-matched-p3.json=flagged, il2cpp-throughput-matched-p1.json=flagged, il2cpp-throughput-matched-p2.json=flagged, il2cpp-throughput-matched-p3.json=flagged, mono-builderlifecycle-default-p1.json=flagged, mono-builderlifecycle-matched-p1.json=flagged, mono-builderlifecycle-matched-p2.json=flagged, mono-builderlifecycle-matched-p3.json=flagged, mono-primary-default-p1.json=flagged, mono-primary-matched-p1.json=flagged, mono-primary-matched-p2.json=flagged, mono-primary-matched-p3.json=flagged, mono-throughput-matched-p1.json=flagged, mono-throughput-matched-p2.json=flagged, mono-throughput-matched-p3.json=flagged
