# Project talking points and evidence

## Suggested resume entry

**HabitTracker — C# / .NET 10, WPF, ASP.NET Core, EF Core, SQLite, xUnit**

- Built a native strength-training journal with a shared C# domain model that preserves independent barbell plate stacks, fractional weights, units, repetitions and effort for every set.
- Implemented a REST API with SQLite persistence, additive EF Core migrations, duplicate-request protection and optimistic concurrency, plus an MVVM desktop client with progress charts, workout recovery and CSV export.
- Added 29 automated domain, HTTP/database and WPF integration test cases, cross-platform API CI, a separate seeded demo journal, and self-contained Windows packaging.

Use the wording only after you can explain and run these features. The implementation demonstrates engineering choices; it does not establish production traffic, performance gains or adoption metrics.

## Interview questions to prepare

| Question | Evidence to open |
| --- | --- |
| What real problem did you solve? | The 205 lb example and `PlateLoad` in `src/HabitTracker.Core/PlateLoad.cs` |
| Why decimal and immutable collections? | Exact fractional plates and a test that mutates the source array |
| Why keep plate arrays as JSON? | Ordered snapshots with no plate-inventory query requirement; see architecture notes |
| How do you avoid double saves? | A unique client-set GUID plus disabled async commands |
| How are simultaneous writes handled? | Session revision concurrency token and transactional SaveChanges |
| Can existing users upgrade? | The original-schema-to-current-schema SQLite integration test |
| Does the UI really work with the backend? | The STA WPF integration test uses the actual API and SQLite |
| How do you calculate progress? | Completed working sets, unit normalization, Monday-based weeks, limited Epley estimates |
| How does a reviewer run it? | One-command isolated demo or self-contained Windows package |
| What would hosted deployment require? | Authentication, per-user isolation, operational backups and deployment work; not claimed as implemented |

Start a demonstration with the plate model, then show persistence and tests. The point of the project is a specific user need carried through a complete implementation.
