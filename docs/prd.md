# Product objective

Build a personal strength journal that records the real plate configuration behind each lift, preserves training history, and makes that history easy to review and demonstrate.

The signature acceptance case is a 45 lb bar with `45 + 25 + 10` on each side: the app previews, saves, reloads and exports a **205 lb** set without losing the original stacks.

## Implemented user flows

- Start a named workout from a curated routine and select a barbell exercise.
- Compose mirrored or independent stacks in pounds or kilograms, including fractional plates.
- Log working and warmup sets with reps, optional RPE and notes; remove mistakes while the workout is open.
- Rest between sets with pause/resume and time adjustment.
- Recover an unfinished session after closing the application.
- Complete the session, inspect history, reuse a saved setup, and export the journal.
- Review volume trends, muscle distribution, weekly consistency, best working loads and estimated 1RM.
- Continue using the original general habit tracker.
- Launch a separate populated demo and create a portable Windows review package.

## Engineering acceptance

The solution builds and runs on the documented Windows prerequisites. Core/API tests are cross-platform. SQLite migrations preserve the original habits. Server validation enforces workout rules independently of the UI. Runtime data is excluded from new Git commits, and the documented demo uses a separate database.

The [README](../README.md) describes the shipped behavior, [demo guide](demo.md) gives a reviewer walkthrough, and [architecture notes](architecture.md) explain tradeoffs and limitations.

## Current boundary

Single-user local operation, a Windows WPF client, and a curated barbell catalog. Hosted authentication, multi-user isolation, cloud sync, custom catalog editors, wearable integration and mobile apps are not shipped capabilities.
