# Review and verification — quick capture

Reviewed from `26b628a979d5a260a861e588bbe2769f8514d233` against the [acceptance spec](promptcrew-improvement-spec.md). Standards and specification reviews were independent. Findings below describe demonstrated failure cases and their final fixes. Formatting and compiler warnings are checked by tooling.

## Standards axis

No hard violations of the repository's documented standards or additional material design smells were found. Correctness review identified the following:

| Severity | Finding and impact | Fix and location |
| --- | --- | --- |
| P2 | Absence evidence could survive a successful result retry and unlock a later uncertain result, producing duplicate output revisions | Request-specific attempted/rejected flags are persisted and reset for every write in [Editor](../desktop/PromptCrew.Desktop/ViewModels/WorkspaceViewModel.Editor.cs); a 404 cannot unlock an uncertain request |
| P2 | Canceling project navigation left the new project paired with the previous employee/task/draft | [Connection operation coordinator](../desktop/PromptCrew.Desktop/ViewModels/WorkspaceViewModel.Connection.cs) restores a coherent snapshot; queued navigation captures and reapplies its requested project/chain |
| P2 | Offset paging based on deduplicated card count could repeat the oldest page forever when other clients appended records | [Activity query](../src/PromptCrew.Api/Services/CrewService.Activity.cs) uses stable timestamp/ID cursors and explicit continuation; task participation is queried independently |
| P2 | A definitive new-task rejection left a persisted creation ID with no editing recovery | [Quick capture](../desktop/PromptCrew.Desktop/ViewModels/WorkspaceViewModel.QuickCapture.cs) applies the same attempted/rejected policy to task creation |

Standards axis: four correctness findings fixed; no outstanding hard standards violations. The most severe findings on this axis were P2.

## Specification axis

| Severity | Requirement and demonstrated problem | Fix and evidence |
| --- | --- | --- |
| P1 | Preserve IDs/no duplicate retries: releasing a request after a 404 could race a late commit | Unknown outcomes retain their original ID. Only a first definitive rejection permits editing. WPF regressions cover missing requests, lost replies, retries, and later result writes |
| P1 | Recover new tasks: comparing retries with mutable task fields rejected a valid creation after another client renamed the task | An immutable creation fingerprint is stored in an additive migration; unchanged creation requests still identify the task after edits |
| P2 | Ownership/cancellation: incomplete project switches exposed mixed state | Snapshot rollback plus cancellation and stale-response guards; tests cover standalone cancel and three distinct project selections |
| P2 | Connection cancellation: cancellation stopped after session validation and allowed the switch to finish | The caller's token covers refresh; the previous client, configuration, workspace and permission state are restored on failure/cancel |
| P2 | New-task rejection recovery was missing | Task-specific persisted rejection handling; tested by archiving the project through another client before saving |
| P2 | Connection rollback omitted admin permission state | Snapshot includes IsAdmin; cancellation is tested with different credential types |

Specification axis: six findings fixed, including the two P1 recovery cases. No unresolved findings from these reviewed scenarios. The quick-save overlay also exposes Cancel directly; its scrollable body and fixed footer keep controls reachable at the minimum window size.

## Validation record

| Check | Result |
| --- | --- |
| Release build with warnings as errors | Passed, zero warnings/errors |
| API/client tests | 36 passed using SQLite; includes original-schema upgrade, immutable snapshots/versions, task creation replay after rename, stable cursor after concurrent append, scopes, idempotency and version conflicts |
| WPF workflow | 1 extensive STA test passed; actual window/button bindings, double save, employee/task attribution, historical branches, drafts/restart, lost input/result replies, safe rejected-write editing, standalone cancellation, three-project races, connection rollback and permission recovery |
| Real HTTP | Passed using actual Kestrel processes and a fault proxy: success, 401, 403, timeout, caller cancellation, lost response after commit, identical replay, altered-payload 409, offline service and restart with the same database |
| Preserved fitness tests | 49 passed: core 19, API 9, mobile 20, desktop 1 |
| Agent tools | 34 real CLI/HTTP calls passed: 4 employees, 6 handoff/branch rounds, snapshots/context, historical comparison, replay/conflict, employee activity and task-creation replay |
| Migration model | No pending model changes |
| Self-contained Windows x64 package | Passed: demo and personal executables start an authenticated API and real desktop with dotnet absent from PATH, select an available port, render screenshots, and stop the service on exit; missing companion files produce the expected error |
| Skill and documentation | Skill validator passed; root README is English; local links, code fences and function JSON validated |
| Screenshots | Actual fresh demo window plus three quick-capture workflow steps; screenshots are linked in the [desktop guide](promptcrew-desktop-guide.md) |

**86 automated tests** includes one WPF test containing multiple UI/recovery scenarios. Counts do not treat each assertion or HTTP call as another test.

## Delivery batches

| Batch | Commit | Verified result | Remaining scope at that batch |
| --- | --- | --- | --- |
| Client/API foundation | `ebfdf1a` | Cancellable typed calls, remote HTTPS policy, paged previews, stable task creation IDs; 35 API/client tests | Desktop flow and recovery review followed |
| Quick capture/UI | `84e94f9` | Employee-specific +, composer, pending-input feedback, animations, virtual histories; actual WPF flow passed; GitHub Windows/Linux and fitness/mobile workflows passed | Further review exposed recovery edge cases |
| Recovery review fixes | `b05f5df` | Request-specific uncertain/rejected handling, snapshot/permission rollback, immutable creation fingerprints and cursor continuation; 36 API/client + 1 WPF + 49 fitness tests | Public deployment and large-scale performance remain outside this delivery |
| Guides and package | Documentation commit on the same branch | English README, real screenshots, network/review evidence, Skill/contract alignment, self-contained package and executable checks | Limits listed below |

## Practical limits

- Unknown write outcomes remain frozen if the service cannot establish their final status and later returns a conflict; this needs operator reconciliation. The app retains the request rather than silently introducing a new ID.
- First history load is 50 previews per employee with at most three employee loads in flight. Older pages are explicit. The selected task's complete branch tree and compatibility `/activity` endpoint remain unpaged. Very large teams and single-task trees need further pagination/performance work; no large-scale benchmark is claimed.
- Search returns at most 200 full rounds per page. Full-text indexing and output-history paging are not implemented.
- Draft files are local and assume a single desktop window. Keys and database files rely on operating-system permissions; no cloud identity, automatic model execution, or MCP server was added.
- HTTP failure/recovery was exercised on loopback; remote HTTPS policy is enforced by the shared client, but no public deployment was created or tested. Windows x64 is the verified package target.
