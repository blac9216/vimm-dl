---
name: github-workflow
description: GitHub issue-driven workflow for tracking all work. Use when creating issues, branches, commits, or PRs.
argument-hint: issue
---

# GitHub Issue Workflow

This defines how Claude Code agents should track work using GitHub issues. All work follows an issue-first workflow.

## Issue-First Rule

Create a GitHub issue **immediately before writing any code**. No code changes without a corresponding issue. This applies to both bugs discovered during testing and enhancements/features requested by the user.

**Assignment.** As soon as you start work on an issue, assign it to yourself (or to the agent's unique identifier — e.g., the session/branch name). Always check the assignee before starting an new issue: if it's already assigned and the assignee is active, pick a different issue. This is how concurrent agents avoid duplicating work.

## Deferred Items Rule

When you notice a real bug, code smell, or improvement opportunity that you are not addressing in the current work, file a deferred issue immediately. **"Out of scope" is not a parking place — it is a signal to file.**

**Trigger phrases.** If you find yourself writing, saying, or thinking any of these while implementing, reviewing, or testing — stop and file before continuing:

- "out of scope" / "for this PR" / "in another PR"
- "pre-existing"
- "noted but not fixed" / "noted, non-blocking"
- "worked around" / "minor"
- "we can address later" / "won't fix here"

**Before filing, scan for duplicates.** A new issue for a problem that is already tracked dilutes the backlog and splits the conversation. Search open issues for the same keywords first:

```bash
gh issue list --state open --search "<keywords describing the problem>"
gh issue list --state open --label deferred --search "<keywords>"
```

If you find an existing issue, post a comment there with the new context (which PR or workflow surfaced it again, anything new about the symptom or repro) and link it from your in-progress work. Do not open a duplicate.

**Filing.** Use the Bug or Enhancement template per nature, apply the `deferred` label, and link the discovering PR or work in the Discovery / Motivation section. Then continue with the in-scope work.

**Before pushing a PR, run a self-scan.** Grep your own diff and notes for the trigger phrases above. Every match is either an inline fix or a deferred issue — nothing leaves the PR tagged "out of scope" without a record.

```bash
# Self-scan: look at your own diff and any notes for trigger phrases.
git diff main...HEAD | grep -iE 'out.of.scope|pre.existing|noted|worked.around|address.later|won.t fix'
```

## Bug Template

Use this template when something is broken or not working as expected:

```markdown
## Description
What is broken and how it manifests. Include exact error messages or log output.

## Discovery
How and when this was discovered. What operation or test triggered it.
Include the sequence of events that led to finding the problem.

## Root Cause (if known)
Technical explanation of why it happens. If unknown, state what has been investigated so far.

## Affected Files
| File | Relevance |
| ---- | --------- |
| `path/to/file` | Why this file is involved |

## Impact
What is affected (features, output, user experience, other components).

## Possible Fixes
- Option A: Description of approach and trade-offs
- Option B: Alternative approach if applicable
```

## Enhancement Template

Use this template for new features, improvements, or refactoring requested by the user:

```markdown
## Summary
What is being added or changed and why.

## Motivation
Why this enhancement is needed. What problem it solves or what capability it adds.
Include user request context if applicable.

## Current Behavior
How things work today (if applicable). What is missing or insufficient.

## Proposed Changes
- Change 1: Description and rationale
- Change 2: Description and rationale

## Affected Files
| File | Relevance |
| ---- | --------- |
| `path/to/file` | Why this file is involved |

## Acceptance Criteria
- [ ] Criterion 1
- [ ] Criterion 2

(Each acceptance criterion should map 1:1 to a Suggested Test Step in the PR
body — keep the wording aligned so reviewers can verify them directly.)

## Risks / Considerations
Anything that could go wrong, break existing behavior, or needs special attention.
```

## Chore Template

Use this template for dependency bumps, infrastructure changes, no-behavior-change refactors, formatting passes, and other maintenance work:

```markdown
## Summary
What is being changed and why. Keep this short — chores should be small.

## Type
- [ ] Dependency update
- [ ] Refactor (no behavior change)
- [ ] Build / CI / tooling
- [ ] Formatting / lint
- [ ] Documentation
- [ ] Other (specify)

## Affected Files
| File | Relevance |
| ---- | --------- |
| `path/to/file` | Why this file is involved |

## Verification
How to confirm the chore changed only what was intended (e.g., behavior tests
still pass, dependency lockfile diff matches expected upgrade, formatter output
is clean).
```

## Issue Labels

The **author of the issue** applies labels at creation. The author of the PR mirrors the same labels onto the PR at PR creation. Exactly one of `bug`/`enhancement`/`chore` should be applied to every issue. Severity is required on bugs, optional on enhancements/chores.

| Label | When to use |
| ----- | ----------- |
| `bug` | Something isn't working |
| `enhancement` | New feature or improvement |
| `chore` | Maintenance — deps, refactor, lint, build, infra |
| `help` | Requires human intervention — agent is blocked |
| `severity:critical` | Breaks runtime, blocks merge, or causes data loss |
| `severity:major` | Affects a core path but has a workaround |
| `severity:minor` | Cosmetic, edge case, or low-frequency |
| `regression` | A previously-fixed issue has returned, or a recent change broke something that worked |
| `deferred` | Real but intentionally out of scope for now |

The `help` label signals that the agent cannot resolve the issue autonomously. Always post a comment explaining what was tried and why it's blocked before adding this label.

## Progress Comments

As you work an issue, add structured comments so that a human or another agent can pick up where you left off:

```markdown
## Progress Update

**Status**: investigating | in-progress | testing | blocked

### What was done
- Step-by-step list of actions taken

### Findings
- What was learned from each step

### Current state
- Where things stand right now

### Next steps
- What remains to be done

### Blockers (if any)
- What is preventing progress and what help is needed
```

## Branches and Worktrees

Create a branch for each issue before starting work. Branch names follow the pattern `<issue-number>-<short-description>`.

To allow multiple agents to work on different issues concurrently, always create a **temporary git worktree** instead of switching branches in the main checkout:

```bash
git worktree add /tmp/issue-10 -b 10-short-description
cd /tmp/issue-10
```

All commits for that issue go in its worktree. Do not commit directly to `main`. Keep the worktree until the PR is merged — the review loop may need fixes applied in it. After the review agent merges the PR, clean up:

```bash
git worktree remove /tmp/issue-10
git branch -D 10-short-description
```

The local branch is removed with `-D` because a squash merge leaves it unmerged in local history.

**When to use worktrees vs. work in place:** worktrees are required when:
- Another agent or human is actively working in the main checkout, or
- Multiple issues are being worked on in parallel.

For solo serial work, committing directly on the issue branch in the main checkout is fine. The rest of the workflow (issue-first, `AI:` prefix, PR with full body, contextless review) still applies either way.

## Commits

All AI-authored commits use the `AI:` prefix:

```bash
git commit -m "AI: fix description of what was fixed"
```

Humans should not use the `AI:` prefix — it exists so commit history can be filtered by author type.

Two further commit conventions:
- **One logical change per commit.** Don't bundle unrelated fixes.
- **Squash on merge is the default.** Feature branches can carry multiple commits during review iterations; the final landed commit on `main` is one squashed commit per PR.

## Trivial Change Fast Path

For changes that are mechanically simple and behavior-free, skip the full review-subagent loop:

- Doc-only typo fixes (single word/punctuation in markdown)
- Comment-only changes that don't alter executable code
- Formatting-only changes from running an autoformatter
- Renaming a local variable for clarity, no signature change

Process for trivial changes:
1. Still open an issue (or reuse an existing one) so there's a record.
2. Single commit with `AI:` prefix.
3. Push directly to a small-scoped PR with `[trivial]` in the title.
4. The PR may be self-merged after CI passes — no review subagent required.

If you're unsure whether a change qualifies, treat it as non-trivial and use the full workflow.

## Pull Requests

Before opening a PR:
- All unit tests pass. (Unit tests mock external dependencies.)
- All integration tests pass. (Integration tests exercise real code against real dependencies — real network, filesystem, services. Discover the project's split from its testing skill, `CLAUDE.md`, or convention.)
- The project linter has been run and reported issues are fixed or explicitly justified. Examples by language:
  - PowerShell: `Invoke-ScriptAnalyzer -Path . -Recurse -Severity Warning`
  - JavaScript / TypeScript: `npm run lint` (or `npx eslint .`)
  - Python: `ruff check .` (or `pylint <pkg>`)
  - Go: `go vet ./... && golangci-lint run`

  Discover the actual command from the project's lint config files, `CLAUDE.md`, or `package.json` / `pyproject.toml` / etc. If the project has a linter installed but no canonical invocation is documented, that's worth surfacing to the human.

When everything passes, push the branch and create the pull request. The PR body must follow the template below — the **Suggested Test Steps**, **Risk**, and **Rollback** sections are required.

### PR Title Convention

Format: `<type>(<scope>): <description>`

- **type:** `fix`, `feat`, `refactor`, `test`, `docs`, `chore`, `perf`
- **scope:** module name (e.g. `parallelism`, `common`) or `all` for cross-cutting changes
- **description:** imperative mood, lowercase, no period
- **length:** ≤ 70 characters total
- example: `fix(parallelism): drain queued jobs on one-shot exit`

The commit prefix is still `AI:` — the type-scope convention applies to the PR title (and any generated changelog), not the commit body.

### PR Body Template

```markdown
Closes #<issue>

## Summary
What changed and why.

## Risk
What could regress, what areas are touched indirectly, blast radius.
Be honest — "no risk" is rarely true.

## Rollback
How to revert if this introduces a regression. Usually `git revert <sha>`,
but flag any state migrations, schema changes, or destructive operations that
complicate a clean revert.

## Suggested Test Steps
Concrete, change-specific steps a fresh reviewer can follow to validate this PR.
Each step must be reproducible and state its expected result. For enhancements,
align these 1:1 with the issue's Acceptance Criteria.

1. <step> — expected: <result>
2. <step> — expected: <result>
```

Create the PR with the body passed via `--body-file`. Use a temp path so the body file is not left in the repo:

```bash
BODY=$(mktemp)
cat > "$BODY" <<'PR_BODY'
Closes #10

## Summary
...
PR_BODY
git push -u origin 10-short-description
gh pr create \
  --title "fix(parallelism): drain queued jobs on one-shot exit" \
  --body-file "$BODY"
rm "$BODY"
```

After the PR is created, the **PR author** applies the same labels as the linked issue:

```bash
gh pr edit <N> --add-label bug,severity:major
```

### Draft PRs

If the work is incomplete or you want CI feedback without spawning a review subagent yet, open the PR as a draft:

```bash
gh pr create --draft --title "..." --body-file "$BODY"
```

When the PR is fully ready (tests pass, coverage holds, lint clean, body complete), mark it ready for review:

```bash
gh pr ready <N>
```

**Do not spawn the review subagent until the PR is out of Draft.** The review costs context and tokens; running it on incomplete work is wasted effort.

## PR Review Handoff

A PR is **never merged by the agent that wrote it**. After creating the PR (and marking it ready for review if it was a draft), hand it off for an independent review using the [github-pr-review](../github-pr-review/SKILL.md) skill.

### Review Loop

1. Parent creates the PR with Suggested Test Steps in the body.
2. Parent spawns a **subagent with no prior context** and instructs it to run the `github-pr-review` skill against the PR number. The review agent reviews the PR fresh, runs all tests, and either requests changes or merges.
3. If the review agent posts **`## PR Review — Changes Requested`**, the parent:
   - Fixes every finding in the issue's worktree.
   - Commits with the `AI:` prefix.
   - Re-runs unit + integration tests + lint.
   - Posts a **Fixes Applied** comment (template below).
4. Parent spawns a **new** contextless subagent for the next round. Every round is a fresh agent — it gets context only from the PR and its comments, never from the parent.
5. The cycle repeats until the review agent posts **`## PR Review — Approved`** and merges the PR.
6. After merge, the parent does final cleanup (worktree + local branch).

The review agent caps the loop at **3 cycles**. A "round" is counted by the number of `## PR Review — Changes Requested` (or `Decomposition Requested`) comments already on the PR — not by the number of pushes. If the PR is still not clean after 3 rounds, the review agent applies the `help` label and posts an escalation comment — the parent must then stop and surface the blocker to a human.

### Session Resume

If the parent session terminates mid-loop, the next session must re-derive state from the PR itself before responding:

- Count existing `## PR Review — Changes Requested` / `## PR Review — Decomposition Requested` comments → that's the round number completed.
- The latest `## Fixes Applied` comment shows what the previous parent did last.
- Pick up at the next step (either fixing newly-requested findings or spawning the next review).

Never spawn a review subagent until you have reconciled state from PR comments.

### Fixes Applied Template

Post this on the PR after addressing a round of review findings:

```markdown
## Fixes Applied — Round <N>

Responding to the round <N> review findings above.

### Changes Made
| Finding # | Resolution | Commit |
| --------- | ---------- | ------ |
| 1 | What was changed | `abc1234` |

### Verification
- Unit tests: pass — <counts>
- Integration tests: pass — <counts>
- Lint: clean
- Coverage: <N>%

Ready for re-review.
```

## Regressions

If a merged fix is found to have caused a regression:

- **Same area as the original fix:** reopen the original issue, add a `## Regression` comment describing what now fails, link the PR that introduced it, apply the `regression` label, and re-enter the workflow on the reopened issue.
- **Different area:** open a new issue with the bug template and the `regression` label. Reference the originating PR in the Discovery section.
