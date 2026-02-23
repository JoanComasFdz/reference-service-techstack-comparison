# Design: `apply-guidelines` v2 — File-Mediated Architecture

> **Date:** 2026-02-23
> **Status:** Design approved, ready for implementation planning

## Problem

The current `apply-guidelines` skill runs out of context before finishing. The orchestrator accumulates ~260KB of plan text (6 full copies, one per guideline pass) because each subagent receives the plan in its prompt and returns the entire corrected plan as output. By the 4th-5th guideline, the orchestrator hits context limits and triggers compaction.

**Root cause:** Plan content flows through the orchestrator's context. Each of the 6 sequential passes adds ~86KB (plan sent + plan received) to the orchestrator.

## Solution

Restructure so **the orchestrator never holds plan content**. All plan data flows through the filesystem. Each reviewer subagent reads the plan file directly, writes corrections back to the file, and returns only a short status string.

**Workflow position (unchanged):** `brainstorming` → `writing-plans` → **`apply-guidelines`** → `executing-plans`

## Architecture

```
Orchestrator (thin — no plan content in context)
  │
  ├─ 1. Discovery (inline)
  │     Read plan path + guidelines path from arguments
  │     Back up plan file to <plan-path>.bak
  │     Discover guideline files (glob or index-link extraction)
  │     Store ordered list of file paths only
  │
  ├─ 2. For each guideline (sequential):
  │     └─ Reviewer Subagent (general-purpose)
  │           Reads: plan file + guideline file (from disk, not prompt)
  │           Writes: corrected plan back to same file
  │           Returns: short status string (~100 bytes)
  │
  └─ 3. Summary Subagent (general-purpose)
        Reads: final plan file
        Returns: grouped change summary
```

**Context budget per pass:** ~500 bytes (status string) vs ~86KB (full plan round-trip in v1).

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Plan data transport | File I/O (not prompt/response) | Eliminates context bloat entirely |
| Sequential ordering | Preserved | Each reviewer reads the file the previous one wrote |
| Backup strategy | `.bak` file copy before first pass | Simple, restorable on failure |
| Discovery phase | Inline in orchestrator | Too small to justify a subagent |
| Summary phase | Dedicated subagent | Keeps final plan content out of orchestrator |
| Subagent type | `general-purpose` | Needs file read/write + reasoning |

## Reviewer Subagent Prompt

```
You are reviewing an implementation plan against a single coding guideline document.

## Instructions
1. Read the implementation plan from: {PLAN_FILE_PATH}
2. Read the coding guideline from: {GUIDELINE_FILE_PATH}
3. Review every task in the plan against THIS guideline document only.
4. Fix any code examples, descriptions, or architectural decisions that violate guidelines in this document.
5. Before each changed code block, add: <!-- applied guideline #N: brief description -->
6. If no violations found, report "No violations" and do NOT write the file.
7. If violations found, write the corrected plan to: {PLAN_FILE_PATH}

## Rules
- Only fix violations from THIS guideline document — not guidelines you know from elsewhere.
- Preserve structure, task numbering, and intent. Change only what's needed.
- Check BOTH file/directory organization AND internal code structure.
- Do NOT output the plan content in your response.
- Report ONLY a short summary: how many violations found, brief description of each fix.
```

### Key Differences from v1

| Aspect | v1 | v2 |
|--------|----|----|
| Plan delivery to subagent | Embedded in prompt (~43KB) | Subagent reads from file path |
| Plan return from subagent | Full plan as output (~43KB) | Written to file; status string returned |
| Orchestrator context per pass | ~86KB | ~500 bytes |
| Total orchestrator context (6 passes) | ~516KB | ~3KB |
| File writes | Once at end | After each guideline pass |
| Backup | Not needed (in-memory only) | `.bak` file before first pass |

## Summary Subagent Prompt

```
Read the implementation plan at: {PLAN_FILE_PATH}
Extract all <!-- applied guideline ... --> comments.
Group them by guideline number.
Return a concise summary: which guidelines were applied (with brief descriptions), which had no violations.
Do NOT output the plan content.
```

## Error Handling

| Scenario | Recovery |
|----------|----------|
| Subagent fails to write file | Orchestrator detects unchanged mtime, re-dispatches once |
| Subagent returns plan content instead of status | Ignored (file already written) |
| Mid-loop failure | Restore from `.bak` file |
| All passes complete, no changes | Delete `.bak`, report "no violations found" |

## File Safety

- Original plan backed up to `<plan-path>.bak` before any subagent writes
- Each subagent writes directly to the plan file (in-place update)
- On success: `.bak` deleted
- On failure: `.bak` restored to original path
