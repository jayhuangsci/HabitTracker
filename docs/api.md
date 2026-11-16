# API contract

The local development base URL is `http://localhost:5253`. Interactive documentation is served at `/swagger` in Development. `/health` checks database connectivity and reports whether demo mode is enabled.

JSON uses camelCase, string enum values, ISO `YYYY-MM-DD` workout dates, and UTC timestamps. Weights are JSON numbers backed by C# `decimal`. The API does not require authentication and is intended to be bound to loopback for one user.

## Training catalog

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/training/exercises` | Twelve curated exercises: ID, name, primary muscle, equipment |
| GET | `/api/training/routines` | Upper body, lower body and full body starter exercise lists |

Catalogs are defined in `TrainingCatalog`. They are not user-editable resources in this version.

## Workouts

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/workouts?from=2026-11-01&to=2026-11-30` | Sessions and their sets, newest date/ID first; date filters optional and inclusive |
| GET | `/api/workouts/{id}` | One session with complete plate snapshots |
| POST | `/api/workouts` | Create an open session; `201` and Location |
| POST | `/api/workouts/{id}/sets` | Add a validated set; `200` and updated session |
| DELETE | `/api/workouts/{id}/sets/{setId}` | Remove a set from an open session; `200` and updated session |
| POST | `/api/workouts/{id}/complete` | Complete a nonempty session; `200` and updated session |
| GET | `/api/workouts/export.csv?from=2026-11-01` | Download CSV, with optional inclusive date filters |

### Create a session

```json
{
  "name": "Upper body",
  "date": "2026-11-15",
  "notes": "Optional session note"
}
```

Name is required, trimmed, and limited to 100 characters. Notes are limited to 1,000. The API accepts a non-default calendar date, including backdated records. The desktop fixes session metadata after the first save.

### Log a plate-based set

```json
{
  "clientId": "413ee0b8-9cc1-49af-8cf0-07b6e57bf660",
  "exerciseId": "bench-press",
  "unit": "Pounds",
  "barWeight": 45,
  "leftPlates": [45, 25, 10],
  "rightPlates": [45, 25, 10],
  "reps": 5,
  "rpe": 8,
  "kind": "Working",
  "notes": "Controlled eccentric"
}
```

The response includes `totalWeight: 205`, the original arrays and unit, the exercise name and muscle, and the complete session. The client sends both stacks even when its mirror control is enabled. The server derives the total independently; it does not trust an extra client-supplied total field.

Rules:

- `unit`: `Pounds` or `Kilograms`; `kind`: `Working` or `Warmup`.
- Exercise ID must exist in the catalog. Reps: 1–100. RPE: optional, 1–10. Set notes: at most 500 characters.
- Bar: 0–100 in the selected unit. Each side: at most 16 positive plate values, each at most 250. Total load: at most 2,000.
- Use `[]` for an empty sleeve. Different left/right totals are permitted and preserved.
- Client ID is a nonempty GUID unique across saved sets. Reusing it returns `409`; issue a new GUID for a genuinely new set.
- Completed sessions reject set additions/removals. Completing an empty session returns `409`; completing a completed session returns its current state.
- Overlapping stale session writes return `409` through the revision concurrency token.

The workout list currently returns the matching journal without pagination. For a hosted or large-data version, add pagination and user partitioning before exposing this route publicly.

## Errors

Training endpoints return `400` for invalid data, `404` for a missing session/set, and `409` for state or uniqueness conflicts. Domain errors use Problem Details:

```json
{
  "title": "The request could not be completed",
  "status": 409,
  "detail": "This workout is completed. Start a new session to log more sets."
}
```

Automatic request-model validation uses ASP.NET Core's validation Problem Details with an `errors` object. The original habit controllers retain their `{ "message": "..." }` business error bodies.

## CSV

The export includes date, session, status, exercise, kind, bar, ordered left/right plate lists, original unit, total, reps, RPE, and set notes. It includes open as well as completed sessions, labeled in the status column. Values are quoted, quotes/newlines escaped, and spreadsheet-formula prefixes in free text neutralized. Mixed-unit totals are not relabeled: each row retains the original unit.

## Original habit endpoints

| Method | Route | Behavior |
| --- | --- | --- |
| GET / POST | `/api/habits` | List/create habits |
| PUT / DELETE | `/api/habits/{id}` | Edit/delete habit; deletion cascades to its logs |
| GET / POST | `/api/habit-logs` | List/create logs; GET optionally filters by `habitId` |
| PUT / DELETE | `/api/habit-logs/{id}` | Edit/delete a log |
| GET | `/api/stats/habits/{habitId}?range=week` | Daily quantities, filling missing dates with zero; `week`, `month`, `year` accepted |

Habit create/update fields are `name`, `unit`, `description`. Log create/update fields are `habitId`, `date`, `quantity`, `comment`. Quantity must be nonnegative. Only one log per habit/date is allowed. The statistics ranges are rolling 7/30/365-day windows through the server's local today.

For a runnable mixed-unit demonstration, see `scripts/Invoke-ApiDemo.ps1` and [the demo guide](demo.md).
