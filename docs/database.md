# Persistence

SQLite is accessed through EF Core 10. The desktop never opens the database directly.

| Runtime | Database location |
| --- | --- |
| Source personal launcher | `src/HabitTracker.Api/habittracker.db` |
| Source demo launcher | `.local/demo.db` |
| Published package personal | `%LOCALAPPDATA%\HabitTracker\training.db` |
| Published package demo | `%LOCALAPPDATA%\HabitTracker\demo.db` |
| Automated integration tests | Unique temporary database per test |

The connection can be overridden with `ConnectionStrings__DefaultConnection`. Relative data-source paths are resolved against the API content root. New parent directories are created at startup.

## Tables and constraints

- **Habits**: original name, unit, description and timestamp. Habit names have a unique index.
- **HabitLogs**: original habit/date/quantity/comment values. Foreign key cascades on habit deletion, habit/date is unique, date is indexed, and quantity has a nonnegative check constraint.
- **Workouts**: name, date, notes, start/completion timestamps and a concurrency revision. Date is indexed.
- **WorkoutSets**: session foreign key, unique client GUID, exercise ID, unit, bar weight, two ordered JSON plate arrays, reps, optional RPE, kind and notes. Session deletion cascades to sets at the schema level; there is no workout-delete endpoint in this release. Exercise ID is indexed, and reps have a database check constraint.

Bar/plate/repetition/RPE rules are validated by the API. SQLite does not enforce every EF `HasMaxLength` annotation; the workout service and request validators enforce input limits. The exercise catalog is C# data, not a separate database table.

Total load is derived from the bar and saved stacks. Working volume and estimated 1RM are computed by the shared Core library, rather than maintained as mutable aggregate columns.

## Migrations

The original `InitHabitTables` migration remains unchanged. `AddStrengthTraining` adds only the workout tables and indexes. The API applies pending migrations before serving requests.

```powershell
dotnet tool restore
dotnet ef migrations list --project src/HabitTracker.Api
dotnet ef migrations has-pending-model-changes --project src/HabitTracker.Api
```

After a model change, generate and review a migration instead of using `EnsureCreated`:

```powershell
dotnet ef migrations add DescriptiveChangeName --project src/HabitTracker.Api
```

The upgrade integration test applies the original migration to a fresh SQLite file, inserts an original habit, then starts the updated API and verifies that the habit and statistics remain readable.

## Demo lifecycle and data handling

`DemoMode=true` enables sample seeding only when an explicit non-default connection is configured. The source and package launchers always choose their separate demo paths. The seeder adds synthetic workouts when the workout table is empty and adds demo habits only when the habit table is empty. It does not reset an existing demo journal.

Database and journal files are ignored by Git. Earlier project commits contained database files; removing them from current tracking does not rewrite the earlier Git history. The included screenshots and package contain synthetic sample data rather than the local personal journal.

CSV is an export of workout records, not a full database backup: it does not include general habits or provide a restore operation. To back up all data, stop the local API and copy the database together with any remaining SQLite journal files, or use SQLite's online backup tooling while it is running.
