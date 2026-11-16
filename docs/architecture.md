# Engineering decisions

## Objective

Deliver a complete strength-training journal around a concrete personal need: preserve the physical plates used on each side of a bar. A reviewer should be able to launch synthetic data, log a set, restart, recover that set, inspect progress, and export the original plate configuration.

## Boundaries

| Layer | Responsibility | Deliberate boundary |
| --- | --- | --- |
| Core | Immutable plate snapshot, parser, unit conversion, validation, contracts, analytics and CSV | No UI, database or network dependencies |
| Desktop | Bindable state, navigation, commands, timer, native rendering | Communicates with the API over HTTP; does not open SQLite |
| API | HTTP contract, validation, lifecycle rules, persistence and migrations | Revalidates incoming sets, regardless of the client |
| SQLite | Durable sessions and sets, uniqueness, foreign keys, revision concurrency | Totals are derived from saved bar and stack snapshots |

## Exact weights and immutable snapshots

`PlateLoad` copies its input collections into read-only arrays. Editing an array used to construct a load cannot silently change that snapshot. Weights use `decimal`, including fractional plates and unit conversion. Formatting and rounding happen at display boundaries.

The left and right stack arrays are stored as JSON columns on the set. They are small ordered values with a maximum of 16 entries per side, rather than separately managed plate inventory. The database is queried by session, date and exercise; the current application does not need SQL aggregation over individual plate positions. A normalized plate table would be appropriate for equipment inventory or cross-session plate queries.

The API never accepts a client-supplied total as authoritative: it creates a validated `PlateLoad` from the supplied bar and stacks. Each set retains its original unit. Analytics convert at calculation time; they do not rewrite historical records after a unit-selector change.

## Save and completion lifecycle

```mermaid
sequenceDiagram
    participant UI as WPF command
    participant API as Workout API
    participant Rules as Shared validation
    participant DB as SQLite
    UI->>Rules: Parse stack expression and preview
    UI->>API: POST workout (on first set)
    API->>DB: Create open session
    UI->>API: POST set with client GUID
    API->>Rules: Validate plates, reps, RPE and kind
    API->>DB: Insert set and increment session revision
    DB-->>API: Commit one SaveChanges transaction
    API-->>UI: Session with saved set snapshots
    UI->>UI: Refresh and start rest timer
    UI->>API: Complete session
    API->>DB: Set CompletedAt and increment revision
    API-->>UI: Completed session; included in progress
```

A unique client GUID prevents the same set request from being inserted twice. An integer revision is an EF Core concurrency token on the session. Adding/removing sets and completing a session all increment it, so overlapping writes from stale requests result in `409 Conflict` instead of silently overwriting state. The set and revision update are in one `SaveChanges` transaction.

This protects duplicate requests with the same GUID. Two intentional log actions have different GUIDs and are distinct sets. The client does not promise automatic recovery from every ambiguous network timeout; refresh history before manually retrying an uncertain save.

Completed sessions are immutable in the current API. Open sessions permit set removal. The API rejects completing an empty session, and completing an already completed session is idempotent.

## Async UI and timer

View models expose observable collections, `INotifyPropertyChanged`, and commands. Async commands prevent repeated execution while running, and a shared busy state disables conflicting edits. HTTP errors are shown in the status area. Unfinished workouts are loaded from the API at startup.

The rest timer uses a deadline and recomputes the remaining seconds on dispatcher ticks, so delayed UI ticks do not accumulate timer drift. It is intentionally transient: closing the application stops the timer, while saved sets remain durable.

The native flat theme uses reusable XAML resources. Plate and volume controls draw with `DrawingContext` and respect DPI. Inputs, navigation and data tables use standard WPF controls with keyboard access and names for accessibility. Long views scroll at smaller window sizes.

## Persistence and operations

EF migrations run at API startup. The strength migration is additive to the original habits schema. The integration suite creates an original-version database, inserts a habit, starts the upgraded API, and verifies the habit and its statistics endpoint still work.

Demo seeding is explicit, and the source demo launcher selects `.local/demo.db`. It is idempotent for an existing populated workout journal. Personal data is never used as sample content in screenshots or the packaged demo. The packaged launcher selects separate files under LocalAppData.

The API is intended for loopback use by one person. Authentication, user partitioning, remote secret management and deployment are prerequisites for a hosted version, rather than implied capabilities of this local application.

## Verification

- Pure domain tests cover input rejection, plate copying, mixed units, analytics boundaries and CSV escaping.
- HTTP integration tests use temporary real SQLite databases and the actual migrations.
- A WPF test runs on an STA dispatcher, loads each view with the real theme, and drives view-model commands against the actual API. It verifies persisted sets, resume, completion and historical setup reuse.
- GitHub Actions runs Windows and Linux checks and builds a Windows demonstration artifact.

The test suites validate behavior; no performance or user adoption claims are inferred from their results.
