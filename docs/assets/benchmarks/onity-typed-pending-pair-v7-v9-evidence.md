# Typed pending-pair `WhenAll<int>` experiment evidence (v7–v9)

**Status: HOLD — experimental.** This records a prototype comparison, not a
product promotion or a whole-library superiority claim. The candidate improves
the warm pending pair, but its first observed tracker-off scheduling allocation
regresses, and the intermittent prebridged-fault allocation remains unexplained.

## Revisions and scope

| Role | Product revision | Benchmark branch and final harness revision |
| --- | --- | --- |
| Baseline | `19fa8ab0f9c4cf77552cf964e7730cf201dc3175` | `benchmark/onitytask-pending-typed-whenall`, `71f90ab16fce99232e0afa513d3bfea9b510a77c` |
| Candidate | `adb41418f465363afb5bf65dd060f39d73438c5b` | `benchmark/onitytask-pending-typed-candidate`, `aa853d442885820dc574882bec4fa14909aee4b3` |

The parked product branch is `feature/onitytask-pending-typed-whenall` at
documentation commit `d89eefc6d282c54fe2d38707b84de48d04d3c39e`; its prototype source identity remains parent
commit `adb41418f465363afb5bf65dd060f39d73438c5b`.

The reports used Unity 2022.3.62f3 Editor/Mono and UniTask 2.5.11 at commit
`2e993ff18f28c931602a07292df0b0804eebef99`. The installed Unity CLI
1.0.0-beta.3 was used for preflight; the pinned Editor ran the benchmark
coroutines without `-quit`. `ONITY_TASK_BENCHMARKS` was enabled temporarily in
both isolated benchmark hosts. Product input construction, typed arrays, and
fresh fault exceptions were prepared outside the measured markers. v7 used two
independent timing and two independently calibrated Profiler processes per
revision, each with eight samples per scenario. Measurements include harness
cost without subtraction. Main-thread marker bytes do not cover worker-thread
continuations. Baseline worker/all-thread controls failed, so there is no
matched all-thread comparison even where candidate windows calibrated.

Product source SHA-256 values recorded in every report version:

| Revision | `OnityAsync.cs` | `OnityTaskCompletionSource.cs` |
| --- | --- | --- |
| Baseline | `5d0466c4edd9d99c10cf2858a42db3d8c6cced7841cab920c0056c37022b6e1c` | `7f07b40fce20fa18774e3f23a1a8936a4a96fa1e5c97f5212523c36ddddf334e` |
| Candidate | `c6d2eddf1e1dfb1bd72e289a6874c0177d5a2784311376dd1fa5bc7f9f2962a6` | `1fb3e69d50b284362df5e014aa7e4f174abe5d899e4bad1442b64be4db7f2324` |

The candidate product Git blobs are `c5792ac8ba2afb2315c742a85a706adecc77222e`
and `5eb333d367bc46a2058a5e90ae67e357deac819a`, respectively, and match
the candidate benchmark host. Raw checkout SHA-256 values are reported above;
Git blobs establish source identity across CRLF/LF checkout differences.

| Harness | Baseline / candidate Git blob identity: runner, menu | Raw checkout qualification |
| --- | --- | --- |
| v7 | `6417d7657cbf5d741d5475e0fa2648526856dbd9`, `644f238b1e96b5eeae2b3f6a64e8fa061e91cf56` | Raw runner/menu SHA-256 differs by line endings; normalized content matches. |
| v8 | `872a30774fbbf4d34515f8d24d52683db4f4b890`, `7d04fabbf9139ab9b6b8f12394bc48516520d381` | Raw runner/menu SHA-256 differs by line endings; normalized content matches. |
| v9 | `459d4bd4da515f8c550927b9e2b8b4513d0b717a`, `60c32706f871269f0ae09dd768f3150dbe7c4594` | Raw runner/menu SHA-256 also matches (`d92145d5005f8ac4a11efdbfcd76b95e7a364d57de1883758f82c90c788223b7`, `ebca4231bf73e17a185511e56eceeecb8960e590409110c0176269df6a8901a8`). |

Both hosts have identical benchmark runtime/editor asmdef SHA-256
`e9b85c980c080723b2b996ecc4bbe523ecdc98c15dff990a21aba5c080aede67` /
`3d0c84d3ce30ef56c787d2afe1d03de2d75c65b5176daf02d865f9f914bc3642`,
manifest/lock SHA-256
`c84f866ff341f81979ce8f148dd0f84d31ed574096d39b605de5c667b77bd40c` /
`a3414d2af261076355c557626e31dd6b79691685dcc50680daec8b2032a08d74`,
and manifest/lock Git blobs `b4f180c450b59597db2a96a70ff552ca35909799` /
`2f863e6d228ea9fd67dbf0d9ab62a805993dd220`.

## Measurements

v7 main-thread positive/empty controls were exactly 65,568/0 bytes in all
four Profiler processes; each process had eight zero-byte empty-harness
samples and complete scenario samples. Numbers below are Onity bytes per
operation in both passes unless noted. Timing ranges are the two independent
process medians, in ns/op.

| Case | Baseline → candidate main-thread B/op | Baseline → candidate timing median range (ns/op) |
| --- | --- | --- |
| Warm pending success schedule, tracker on | 1,408 → 1,040 (−26.1%) | 4,060–4,195 → 3,508–3,552 |
| Warm pending success schedule, tracker off | 544 → 176 (−67.6%) | 2,029–2,163 → 1,358–1,392 |
| Pending success lifecycle, tracker on | 1,488.6–1,489.3 → 1,088.3 (about −26.9%) | 12,586–14,013 → 8,271–8,545 |
| Pending success lifecycle, tracker off | 624.6 → 216 (−65.4%) | 10,626–11,357 → 2,522–2,530 |
| Pending fault lifecycle, tracker on / off | 2,152.6 / 1,288.6 → 1,752.3 / 880 | Both candidate timing ranges lower. |
| Pending cancellation lifecycle, tracker on / off | 1,904.6 / 1,040.6 → 1,464.3 / 592 | Both candidate timing ranges lower. |
| Completed two / four inputs | 40 / 48 → 40 / 48 | Timing varies by pass; no stable allocation change. |
| Prebridged pending fault, tracker off | mean 936.9297 → 937.2969 (+0.3672) | 23,422–27,001 → 25,199–25,929; diagnostic only. |

The warm scheduling allocation gate of at least 25% improvement passes in both
tracker modes. Pending success lifecycle allocation also falls in both modes.
Fault and cancellation lifecycle allocations fall. Completed two/four-input
allocations remain 40/48 B/op. Their timing varies across processes, so the
data do not establish a repeatable completed-control timing regression.

The **first observed** pending schedule in the warmed Editor is separate from
the warm steady state: tracker-off Onity grows from 990 to 1,480 B (+49.5%).
For 384 simultaneously outstanding pending outputs, steady samples decrease
from 208,896 to 116,736 B per batch (544 → 304 B/op, −44.1%); the candidate's
first burst sample is 165,888 B (432 B/op). The first-call increase is a
documented pooling tradeoff. Promotion remains deferred because the intermittent
prebridge allocation event is unattributed. v7 prebridge means are affected by
rare 376-byte batch events and are not evidence of a candidate-only route.

v8 isolated the same prebridged fault, tracker-off lifecycle in one fresh
Profiler process per revision: 32 samples × 128 operations, with 65,568/0-byte
main-thread controls and 32 zero-byte empty-harness samples. Both revisions
have **31 samples at 119,880 B and one at 120,256 B**, hence the same
936.6543 B/op mean. The elevated sample occurs at index 0 in the baseline and
index 17 in the candidate. This establishes equal distributions, not the cause
of the additional 376 B.

v9 enabled allocation callstacks in separate fresh processes. Both positive
controls were 65,568 B fully attributed with a resolved
`AllocatePositiveControl` callsite; empty controls and all 32 empty-harness
samples were zero. Callstacks stayed enabled and Deep Profiling stayed off.
All 32 lifecycle samples in each revision are **119,880 B / 2,306 events**,
fully reconciled as 119,880 attributed plus zero unresolved bytes. The ordinary
event sizes per sample match exactly: 32 B × 1,024; 40 B × 641; 48 B × 384;
96 B × 128; 224 B × 128; 2,080 B × 1. Sample 0 has 18 equal resolved-stack
groups, and the 14 first-resolved-callsite groups aggregated over all samples
have equal event counts and bytes. No candidate-specific allocation route was
observed. Neither v9 process captured the elevated 120,256 B case, so it
cannot attribute the intermittent +376 B event. Event-level attributed bytes
may still contain unresolved *individual stack addresses*. Callstack capture
can affect allocation behavior; v9 is diagnostic and has no timing, worker, or
all-thread claim.

## Artifacts and verification

The eight JSON artifacts are in [`typed-pending-pair-v7-v9/`](typed-pending-pair-v7-v9/).
The six v7/v8 JSON files are verbatim copies. The original v9 JSON files remain
outside Git; the two `.json.gz` files are deterministic gzip streams with
`mtime=0` and no embedded filename. Source and copied artifact SHA-256 values
were compared. A scan of all eight raw JSONs found no local paths, URLs, or
common credential markers. SHA-256 values for the small verbatim reports are:

| Report | SHA-256 |
| --- | --- |
| `baseline-pass1.json` | `261c7d90d5062b394a0353f10a829e6765f9967931a519d31a5490b10528a4f9` |
| `baseline-pass2.json` | `8a453aee1556a9ba8b8204ffe48f6e5e1f55fccfc5a00c0e26870ab23954a333` |
| `candidate-pass1.json` | `50b3661aae15eeff16a94875bd73b347a6a5bec7c6495056cf050b33bae05519` |
| `candidate-pass2.json` | `ac8a4f2f3db5393ca343dd02035eeaceb28c5f5fe8b219d4f97c2be1219c51db` |
| `baseline-prebridge-v8.json` | `a1ea5423455815ecc1e80dc8b6a04f9d4f29417388fcaaeadd08ac9ae692fcfa` |
| `candidate-prebridge-v8.json` | `8321f3aecfa26b1be10849a869c25698001b39a01c3266d2f07124f5e053488d` |

The compressed v9 entries are:

| Revision | Raw JSON bytes / SHA-256 | gzip bytes / SHA-256 |
| --- | --- | --- |
| Baseline | 328,073,968 / `636b67e59aaf9e3df2bfd056379d79beff9f049eda6df295375714aa95d82a18` | 3,666,405 / `7a2009cf29ee5c1f9f5dc4539ca99ba9454bb427e1c159f468527671f5b58d2c` |
| Candidate | 328,073,968 / `d0737dc153fdca502123a37495c3c6d3149019adc134e9553675025d16a13c9b` | 3,672,631 / `ea48fa02efd7c66d2d8445965f112728e0e8a9da7e92d6f054e0b7671155950a` |

On the product worktree at `adb41418f465363afb5bf65dd060f39d73438c5b`,
full Unity CLI suites passed: EditMode 605/605, PlayMode 26/26, zero failures
or skips (XML files `onity-typed-full-editmode-20260924-v8.xml` and
`onity-typed-full-playmode-20260924-v8.xml`). Each command used `unity test
<absolute-product-path> --mode <EditMode or PlayMode> --output <XML> --timeout
900 --json --non-interactive --no-banner`. A same-HEAD
`dotnet build Onity.Unity.csproj -c Release --no-restore -nologo` exited 0
with 0 errors and 16 existing generated Unity reference warnings. These checks
support prototype correctness/buildability; promotion remains deferred.
