# Epic Issue Template

An **epic** is the meta-issue that tracks a single larger goal delivered across
**more than one** issue and PR. Its job is to survive context compaction: when a
new session (or a post-compaction continuation) picks up the work, the epic is the
one place that still holds the whole picture — the goal, what is done, what is
left, and how the design has drifted from the original plan.

Open an epic **before** filing its sub-issues whenever a feature is too big to land
in one review-sized PR (≤ ~400 net LOC / ≤ 15 files). Apply the `epic` label. The
epic is a living document — edit its body as the target moves; do not treat the
first draft as fixed.

The rules for *when* to update an epic (before starting a sub-issue, after closing
one, re-evaluating trajectory) live in the skill body under "Epics". This file is
just the shape of the issue.

```markdown
## Goal
The single larger objective this epic delivers. One or two sentences a future
reader can absorb in isolation, with no other context loaded.

## Motivation
Why this work matters and what it unblocks. The problem being solved.

## Scope
What is in scope for this epic.

### Non-goals
What is deliberately out of scope, so the boundary is explicit and does not creep.

## Design / Approach (living spec)
The current plan of record. This section is expected to change — edit it in place
as decisions are made. This is the durable design memory; do not rely on chat
history or summaries to carry it.

## Sub-Issues
Right-sized child issues, each landing in its own review-sized PR. There must be
more than one. Check the box when the issue is closed and its PR is merged.

- [ ] #<id> — <short title> — PR #<pr> _(fill the PR in when opened/merged)_
- [ ] #<id> — <short title>
- [ ] #<id> — <short title>

## Trajectory Log
Append a brief entry each time a sub-issue closes: which issue/PR landed, and
**what changed from the original design** (if anything). This is the audit trail
of how the epic actually unfolded versus how it was planned.

- _<date>_ — closed #<id> via PR #<pr>. <what shipped; any deviation from the plan above>

## Status
Current state in one line: which sub-issue is in flight, what is blocked, what is
next. Updated on every epic touch.
```
