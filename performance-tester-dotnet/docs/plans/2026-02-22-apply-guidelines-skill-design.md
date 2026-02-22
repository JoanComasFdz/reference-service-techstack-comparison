# Design: `apply-guidelines` Skill

> **Date:** 2026-02-22
> **Status:** Design approved, ready for implementation planning

## Problem

The `writing-plans` skill creates implementation plans but cannot systematically verify them against all 35 coding guidelines (~1500 lines). Guidelines are too large for the plan author's context window to consider thoroughly. This leads to plans that propose code violating project conventions, which is only caught during code review — too late in the workflow.

## Solution

A new Claude Code skill (`apply-guidelines`) that reviews an implementation plan against coding guidelines **after** the plan is written, **before** execution begins. It auto-fixes the plan to comply with all guidelines.

**Workflow position:** `brainstorming` -> `writing-plans` -> **`apply-guidelines`** -> `executing-plans`

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Trigger point | After `writing-plans`, before `executing-plans` | Clean separation of concerns |
| Output | Auto-fix the plan directly | Fastest feedback loop, no manual fix step |
| Scope | Generic with parameter | Works with any project's guideline files |
| Processing | Iterative one-by-one | Keeps context focused per guideline document |
| Execution | Sequential subagents | Each pass sees prior fixes; incremental improvement |

## Skill Identity

- **Name:** `apply-guidelines`
- **Frontmatter description:** `Use when an implementation plan has been written and needs to be verified against project coding guidelines before execution`
- **Invocation:** `/superpowers:apply-guidelines <plan-path> <guidelines-path>`
  - `<plan-path>`: Path to the implementation plan markdown file
  - `<guidelines-path>`: Path to guidelines — either a directory of `.md` files, or a single index file containing markdown links to guideline documents

## Guidelines Discovery

The `<guidelines-path>` argument supports two modes:

1. **Directory mode** — If the path is a directory, glob `*.md`, sort alphabetically, iterate over each file.
2. **Index file mode** — If the path is a single `.md` file, read it, extract all markdown links (e.g., `[Title](path/to/file.md)`), resolve paths relative to the index file's location, iterate over linked files in order of appearance.

This handles both:
- A flat `coding-guidelines/` directory with `01-*.md` through `06-*.md`
- An index like `CODING_GUIDELINES.md` that links to subdocuments

## Orchestration Flow

```
1. Parse arguments: plan_path, guidelines_path
2. Read plan file → store as current_plan (in memory)
3. Discover guideline files:
   - If directory: glob *.md, sort alphabetically
   - If file: extract markdown links, resolve relative paths
4. For each guideline file (sequentially):
   a. Read guideline content
   b. Dispatch subagent (Task tool, general-purpose) with:
      - Guideline document content
      - Current plan content
      - Review+fix instructions (see subagent prompt below)
   c. Subagent returns corrected plan
   d. Update current_plan with subagent's output
5. After all passes:
   a. Extract audit trail (HTML comments) from final plan
   b. Display grouped summary of changes
   c. Write final corrected plan to original file
```

**File safety:** The original plan file is only written once, at the end, after all reviews complete. If something fails mid-loop, the original is untouched.

## Subagent Prompt Template

Each subagent receives this prompt:

```
You are reviewing an implementation plan against coding guidelines.

## Your Task
Read the coding guideline document below. Then review every task in the
implementation plan. If any task proposes code (in code examples, step
descriptions, or architectural decisions) that would violate any guideline
in this document, fix the plan to comply.

## Rules
1. Only fix violations of guidelines in THIS document. Do not apply
   guidelines you know from elsewhere.
2. Preserve the plan's structure, task numbering, and intent. Only change
   what's necessary to comply.
3. If a task has no code examples (e.g., "create directory"), skip it.
4. When fixing, add a brief inline comment in the plan:
   `<!-- applied guideline #N: brief description -->`
5. Return the COMPLETE plan (not just the changed parts).

## Coding Guideline Document
<guideline>
{GUIDELINE_CONTENT}
</guideline>

## Implementation Plan
<plan>
{CURRENT_PLAN}
</plan>

Return the complete corrected plan.
```

**Why HTML comments for audit trail:** Invisible when rendered as markdown but provide traceability. The summary step extracts these to show the user what changed.

## Summary Output

After all passes, the skill displays:

```
## Guidelines Applied

### From 01-core-architecture.md
- Task 3: Applied guideline #2 — made parameters explicit
- Task 5: Applied guideline #11 — added guard clause

### From 06-formatting.md
- Task 3: Applied guideline #25 — added braces to if statement

### No violations found in:
- 03-error-handling.md
- 04-value-objects.md
```

## What the Skill Does NOT Do

- **Does not auto-commit.** The user reviews changes and commits when satisfied.
- **Does not modify guideline files.** Read-only access to guidelines.
- **Does not validate plan structure.** It assumes the plan follows `writing-plans` output format.
- **Does not apply guidelines from outside the specified path.** Only the provided guideline files are used.

## Prerequisites

- The guidelines must already be split into individual files (or an index linking to them)
- The plan must exist at the specified path
- The `writing-plans` skill should have been run first (the plan follows its format)

## Skill File Structure

```
apply-guidelines/
  SKILL.md    # Main skill document (~100-150 lines)
```

No supporting files needed — the subagent prompt is embedded in the skill document.
