# PromptCrew desktop improvement acceptance

Baseline: `26b628a979d5a260a861e588bbe2769f8514d233`. Work on a `codex/` branch and push verified batches. Preserve the Chinese application, English README, existing SQLite records, employee snapshots, append-only output history, project permissions, and draft recovery.

- Add a separate `+` beside each employee, labelled “为该员工记录提示词”. Open a lightweight composer bound to that project and employee. Reuse the current task, select an existing task or create one inline. Show the actual parent node. Continuing a selected historical node must use that node as parent. Employee chronology does not imply task ancestry.
- Keep input prominent and advanced fields collapsed. Save input alone as PendingOutput. Update history and show confirmation. Preserve drafts, payloads and original request IDs across failures, cancellation and restart. Double clicks and retries must not create duplicate rounds or tasks.
- Polish Chinese flat UI, focus/hover/disabled/loading/empty/error states, long text, scrolling and narrow windows. Provide 120–220 ms lightweight transitions and a persisted reduced motion preference respecting OS settings.
- Configure, validate and save API URL, key-file path and timeout. Provide connection testing, cancellation, differentiated error messages and reconnect recovery. Cancel superseded project/employee reads and discard stale responses. Require HTTPS outside loopback; credentials stay outside logs and source control.
- Keep OpenAPI, executable Agent tools, function definitions, Skill and examples aligned. Verify actual HTTP success, timeout, cancellation, offline/restart, lost reply, replay, changed-payload conflict, invalid key and forbidden project.
- Review MVVM boundaries, async/UI responsiveness, concurrency, ownership, drafts, resources and growing histories. Fix material issues with targeted regressions. Report exact locations, effects, fixes and residual limits.
- Deliver real UI screenshots, a recorded quick-capture walkthrough, test evidence, English README/instructions, and a directly runnable self-contained Windows package.
