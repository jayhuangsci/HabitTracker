# PromptCrew · AI Employee Prompt Manager

**A C# workspace for your AI team's prompts, results, and development history.**

[![Build and test](https://github.com/jayhuangsci/HabitTracker/actions/workflows/promptcrew.yml/badge.svg?branch=codex%2Fpromptcrew-quick-capture)](https://github.com/jayhuangsci/HabitTracker/actions/workflows/promptcrew.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![WPF](https://img.shields.io/badge/Desktop-WPF-24523C)
![ASP.NET Core](https://img.shields.io/badge/API-ASP.NET_Core-193729)
![SQLite](https://img.shields.io/badge/Storage-SQLite-517A4C)
![Project age](https://img.shields.io/date/1777390533?label=Project%20age&color=193729&cacheSeconds=86400 "Repository created April 28, 2026")

[Run on Windows](#windows) · [Three-minute walkthrough](#demo) · [Connect an Agent](#agent) · [Implementation and validation](#engineering)

![Project overview with a separate development history for each AI employee](docs/images/promptcrew/workspace.png)

PromptCrew organizes AI work by project, employee, and task. Define roles such as gameplay designer, programmer, artist, or balance designer, then keep the actual prompts, model outputs, and artifact references from each round of work.

Click **+ beside an employee** to save a new prompt immediately. Task, employee, and parent context stay visible; advanced fields are collapsed. Input-only rounds appear as **Pending output**, ready for results later.

The overview gives **each employee a separate vertical history**. Select an employee to open their workspace, or select a history card to inspect the full input, role snapshot, output revisions, and context.

The application handles recording and organization. The Agent host runs models and coordinates execution; PromptCrew receives records through its HTTP API and companion Skill.

**Language:** the desktop UI and demo prompts currently use Simplified Chinese. This README uses English translations of the UI labels. Linked implementation guides and the Skill are currently in Chinese.

<a id="windows"></a>
## Run the Windows app

**The portable package runs on Windows 10/11 x64. No .NET installation, SDK, or PowerShell 7 is required.**

1. Open [GitHub Actions](https://github.com/jayhuangsci/HabitTracker/actions/workflows/promptcrew.yml) and select the latest successful build of `codex/promptcrew-quick-capture`.
2. Under **Artifacts**, download `PromptCrew-win-x64` and extract it. If the download contains another ZIP with the same name, extract that inner ZIP too.
3. Open the folder containing the following executables and double-click one:

| Executable | Purpose |
| --- | --- |
| **`PromptCrew-Demo.exe`** | Game project demo with four employees and three rounds per employee |
| **`PromptCrew.exe`** | Your own workspace; create a project on first launch |

The launcher starts the local service and desktop app, then stops its service when the desktop closes. Keep the `api`, `desktop`, and `tools` folders beside the executables.

```text
PromptCrew-win-x64/
├─ PromptCrew.exe
├─ PromptCrew-Demo.exe
├─ api/
├─ desktop/
├─ tools/
├─ promptcrew-journal/
└─ promptcrew.function.json
```

A local build produces `artifacts/PromptCrew-win-x64/` and `artifacts/PromptCrew-win-x64.zip`. Downloading Actions artifacts generally requires a GitHub account.

<a id="demo"></a>
## Three-minute walkthrough

A fresh demo creates a spaceport game project with **4 employees, 12 recorded rounds, output corrections, and gameplay branches**. The screenshots use an isolated demo database. Existing workspaces do not receive additional sample records automatically.

**Lines in the overview connect successive records from the same employee.** Each card comes from persisted data and shows a prompt or result excerpt, timestamp, task, and status:

```mermaid
flowchart LR
    subgraph G["Gameplay designer"]
        direction TB
        G1["Core loop proposal"] --> G2["Real-time combat proposal"] --> G3["Combo rules refined"]
    end
    subgraph P["Programmer"]
        direction TB
        P1["Turn-based prototype"] --> P2["Energy cap corrected"] --> P3["Combo and protection implemented"]
    end
    subgraph A["Artist"]
        direction TB
        A1["Asset prompts"] --> A2["Visual guidelines revised"] --> A3["Small-size readability improved"]
    end
    subgraph B["Balance designer"]
        direction TB
        B1["Shield and energy review"] --> B2["Attack pacing reviewed"] --> B3["Recovery parameters revised"]
    end
```

1. **Browse Overview.** Read what each employee worked on and how their work changed. Excerpts come directly from the original text.
2. **Select an employee.** Open Work History, Tasks & Prompts, or Responsibilities & Skills, and see the tasks they contributed to.
3. **Open a card.** Read the complete prompt and output. Expand the saved base prompt snapshot or record metadata.
4. **Continue the work.** Continue from a historical node, or revise an input by creating a new round. Output corrections append a revision.
5. **Inspect context.** Retrieve the selected record's ancestors and explicit references, or compare two output revisions from the same round.
6. **Return and resume.** Overview restores your browsing position. Employees have separate drafts, and saved work remains available after a restart.

![Employee workspace showing continuous work history and task participation](docs/images/promptcrew/employee-history.png)

<details>
<summary>More screenshots: complete records, employee configuration, and revision comparison</summary>

![Complete original prompt and output history](docs/images/promptcrew/history.png)

![Employee responsibilities, base prompt, and linked Skills](docs/images/promptcrew/employee-profile.png)

![Comparison of rounds and historical output revisions](docs/images/promptcrew/comparison.png)

</details>

The overview is a chronological reading view. Actual task parents, branches, input revisions, and references between tasks remain intact and are available in task details. See the [full demo walkthrough](docs/promptcrew-demo.md).

## Quick capture: click, write, save

1. Click **+** beside the intended employee. **Add employee** is a separate action.
2. Enter the prompt. Reuse the current task, choose another, or create one inline. The header shows the actual task and parent.
3. Save with the button or **Ctrl+Enter**. The employee history updates and confirms **Pending output**. **Esc** closes the panel while preserving the draft.

![Enter a prompt directly for the selected employee](docs/images/promptcrew/quick-02-input.png)

![Saved input immediately appears in that employee's history](docs/images/promptcrew/quick-03-saved.png)

[Full walkthrough, connection settings, recovery and screenshots](docs/promptcrew-desktop-guide.md) · [Review findings and verification](docs/promptcrew-quality-review.md)

## Run from source

Requires Windows 10/11 and the **.NET 10 SDK**. The source launcher works with Windows PowerShell 5.1:

```powershell
git clone --branch codex/promptcrew-quick-capture https://github.com/jayhuangsci/HabitTracker.git
cd HabitTracker
.\Start-PromptCrew-Demo.cmd
```

For your own workspace, double-click `Start-PromptCrew.cmd`. To choose a port:

```powershell
.\Start-PromptCrew.cmd -Port 5285
```

The portable executable tries port 5283 and selects an available port if it is occupied. The source launcher accepts `-Port`. Maintenance scripts for Agent verification and packaging use PowerShell 7.

## Implemented features

| Capability | Behavior |
| --- | --- |
| Projects and employees | Project background, constraints, and state; employee responsibilities, base prompts, Skills, and role templates |
| Employee history | Separate chronological card chains, expandable previews, 50-record cursor pages, and recycled WPF containers |
| Quick capture | Employee-specific +, inline tasks, visible parent, optional fields, input-only saves, keyboard shortcuts, and saved feedback |
| Connections | Connection tests, configurable timeout, cancel, recovery, stale-response protection, and HTTPS outside loopback |
| Motion and sizing | 180 ms transitions, reduced motion preference and OS settings, 920×660 minimum window, scrollable forms |
| Task chains | One parent per round, branches, employee handoffs, and references between tasks in the same project |
| Input preservation | Original inputs and employee definition snapshots remain available; input edits create linked rounds |
| Output revisions | Results and corrections append history; compare v2/v3 of one round or text from different rounds |
| Search and context | Search by employee, task, tag, status, and text; retrieve ancestors and explicitly referenced work |
| Safe retries | Request IDs deduplicate input and result writes; reusing an ID with different content returns HTTP 409 |
| Concurrency and isolation | Version checks prevent silent overwrites; project-scoped API Keys, ownership validation, and key revocation |
| Desktop recovery | Separate employee drafts, persisted pending requests, legacy draft migration, and restart recovery |
| Agent integration | Executable C# HTTP tools, function definitions, a Skill, OpenAPI, and runnable examples |

<a id="agent"></a>
## Let an Agent record its work

1. Keep the app open. Under **Connection & Permissions → API Key & Project Permissions**, issue a project-scoped key and save it outside the repository.
2. Configure the API address and key file in the Agent host. The portable app writes its current address to the workspace's `connection.json`.
3. Load the [companion Skill](skills/promptcrew-journal/SKILL.md), or register the [function definition](tools/promptcrew.function.json).

A recorded task follows this sequence:

```text
Resolve project, employee, and task → retrieve ancestors and references → save actual input
→ execute the task → append visible output and artifact references → return the record ID
```

After building from source, run this example from the repository root in PowerShell 7:

```powershell
$env:PROMPTCREW_API_URL = 'http://127.0.0.1:5283/'
$env:PROMPTCREW_KEY_FILE = 'C:/my-secrets/promptcrew-agent.key'
$env:PROMPTCREW_TOOLS_DLL = (Resolve-Path 'src/PromptCrew.Tools/bin/Release/net10.0/PromptCrew.Tools.dll').Path

New-Item -ItemType Directory .local -Force | Out-Null
'{"operation":"list_projects"}' | Set-Content .local/list-projects.json -Encoding utf8NoBOM
dotnet $env:PROMPTCREW_TOOLS_DLL invoke .local/list-projects.json
```

The portable package also includes `tools/PromptCrew.Tools.exe invoke <request-file>`. The tool returns `ok: true` only after the service confirms success. Failures retain the request ID and file location. Retry with the original file; Agent writes also include the employee definition version actually used.

- [Agent setup, Skill installation, and remote access](docs/promptcrew-agent.md)
- [API routes, payloads, and working examples](docs/promptcrew-api.md)
- [Skill tool contract](skills/promptcrew-journal/references/tool-contract.md)

The API can run independently on Windows, Linux, or macOS:

```powershell
dotnet run --project src/PromptCrew.Api
```

Development-mode documentation is available at [local Swagger](http://127.0.0.1:5283/swagger). A cloud Agent needs a reachable HTTPS endpoint and project credentials supplied by its host; the default service listens locally. Linked Skills store configuration names, and execution depends on the host's tools and permissions.

<a id="engineering"></a>
## C# implementation

```mermaid
flowchart LR
    Launcher["C# executable launcher"] --> WPF["WPF / MVVM"]
    Launcher --> API["ASP.NET Core API"]
    WPF --> Client["Shared C# HTTP client"]
    Agent["Agent host + Skill"] --> Tools["C# JSON tool adapter"]
    Tools --> Client
    Client --> API
    API --> DB[("Dedicated SQLite database")]
```

| Project | Responsibility |
| --- | --- |
| `PromptCrew.Core` | Record contracts, enums, validation, and bounded text comparison, without WPF or EF dependencies |
| `PromptCrew.Api` | Project permissions, EF Core migrations, input snapshots, appended results, idempotency, and optimistic concurrency |
| `PromptCrew.Client` | Typed HTTP calls and error handling shared by the desktop and tools |
| `PromptCrew.Desktop` | Employee histories, task details, role configuration, drafts, and recovery |
| `PromptCrew.Launcher` | Service startup, port selection, authenticated readiness checks, and shutdown |
| `PromptCrew.Tools` | JSON arguments mapped to real HTTP calls, with credentials supplied by the host |

Overview initially requests 50 lightweight previews per employee with at most three employee loads in flight. Stable timestamp/ID cursors load older records, an indexed SQLite query bounds each page, and WPF recycles offscreen cards. Complete records load on demand; participation queries include older tasks outside the loaded pages. Composite foreign keys enforce project ownership for employees, tasks, parents, and references. Inputs and parent links are fixed after creation; revised inputs create records, and corrected outputs append revisions. The service stores UTC timestamps, while the desktop displays local time.

```mermaid
erDiagram
    PROJECT ||--o{ EMPLOYEE : owns
    PROJECT ||--o{ TASK_CHAIN : owns
    TASK_CHAIN ||--o{ PROMPT_ROUND : contains
    EMPLOYEE ||--o{ PROMPT_ROUND : performs
    PROMPT_ROUND o|--o{ PROMPT_ROUND : parent
    PROMPT_ROUND ||--o{ OUTPUT_REVISION : preserves
    PROMPT_ROUND ||--o{ ROUND_REFERENCE : cites
    PROMPT_ROUND ||--o{ ROUND_REFERENCE : referenced_by
```

See the [implementation notes](docs/promptcrew-architecture.md) for code reuse, write transactions, version conflicts, and context handling.

## Validation and packaging

**86 automated tests passed: 36 PromptCrew API/client tests, 1 extensive WPF workflow test, and 49 tests for the preserved fitness application.**

| Verification | Coverage |
| --- | --- |
| API with real SQLite | Project isolation, valid chains, duplicate requests, concurrency conflicts, role snapshots, output history, and service restarts |
| Actual WPF button bindings | Employee + → prompt → save → history, double clicks, pending ID recovery, output conflicts, standalone cancellation, three-project races, and connection rollback |
| Agent tool demo | 34 CLI/HTTP calls creating 4 employees and 6 handoff rounds, checking branches, input/task replay, cursor paging, conflicts, and historical comparisons |
| Portable package checks | Personal and demo executables, occupied ports, authentication, desktop rendering, service shutdown, and incomplete-package errors |

```powershell
dotnet test PromptCrew.slnx -c Release
dotnet test HabitTracker.slnx -c Release
./scripts/Test-PromptCrewAgent.ps1
./scripts/Publish-PromptCrew.ps1 -SelfContained
./scripts/Test-PromptCrewPackage.ps1 -PackageDirectory artifacts/PromptCrew-win-x64
```

[GitHub Actions](https://github.com/jayhuangsci/HabitTracker/actions/workflows/promptcrew.yml) checks PromptCrew on Windows and Linux and produces a self-contained Windows package. The [delivery log](docs/promptcrew-delivery.md) records completed batches and their verification.

## Storage and current limits

| Data | Default location |
| --- | --- |
| Personal records | `%LOCALAPPDATA%/PromptCrew/personal/` |
| Portable demo records | `%LOCALAPPDATA%/PromptCrew/demo/` |
| Source demo records | `.local/promptcrew-demo/` |
| Drafts, keys, and connection information | The launcher's corresponding data directory |
| Drafts when starting the desktop alone | `%LOCALAPPDATA%/PromptCrew/desktop-settings.json` |

The current design targets personal use on one machine. Employee histories load 50 records at a time, with a maximum API page of 200. The compatibility `/activity` endpoint and selected-task branch tree remain unpaged; search pages contain up to 200 full records. Context is limited to 256 ancestors and 500,000 compiled characters. Text comparison shows shared context and the replaced section without guaranteeing the shortest edit sequence. Project keys allow reads and writes within their scope; read-only permissions are not yet available. Local files rely on operating-system access controls. Pending requests with an uncertain outcome retain their original IDs even after a 404; the editor unlocks only after confirmed success or a first definitive rejection. A later conflict after an uncertain write requires server-side reconciliation, rather than silently minting another ID.

## Repository history

This repository evolved from **HabitTracker / FORM**, a strength-training journal. PromptCrew reuses its MVVM commands, base theme, and testing approach. The original WPF and OpenSilver fitness apps, source, and data remain available, while the new business uses separate storage. See the [HabitTracker / FORM documentation](docs/legacy-fitness.md).
