# Apply-Guidelines v2 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rewrite the `apply-guidelines` skill to use file-mediated subagents, eliminating context window bloat.

**Architecture:** The orchestrator never holds plan content. Each reviewer subagent reads/writes the plan file directly and returns only a short status string. Sequential ordering preserved via filesystem.

**Tech Stack:** Claude Code skills (SKILL.md), Task tool subagents (general-purpose)

---

### Task 1: Rewrite SKILL.md with file-mediated architecture

**Files:**
- Modify: `/home/node/.claude/skills/apply-guidelines/SKILL.md`

**Step 1: Replace the entire SKILL.md with the v2 content**

Write the following content to `/home/node/.claude/skills/apply-guidelines/SKILL.md`:

```markdown
---
name: apply-guidelines
description: Use when an implementation plan has been written and needs to be verified against project coding guidelines before execution - catches guideline violations at plan time rather than code review time
---

# Apply Guidelines to Implementation Plans

## Invocation

` ` `
/apply-guidelines <plan-path> <guidelines-path>
` ` `

- `<plan-path>`: Absolute path to the implementation plan markdown file
- `<guidelines-path>`: Directory of `.md` files OR single index `.md` with links to guideline documents

## Process Overview

**Key principle:** The orchestrator NEVER reads or holds plan content. All plan data flows through the filesystem. This prevents context window bloat.

1. **Discovery** (orchestrator does this directly — no subagent)
2. **Review loop** — one subagent per guideline file, sequentially
3. **Summary** — one subagent reads final plan and reports changes

## Step 1: Discovery (Inline)

Do these steps directly (no subagent needed):

1. Parse arguments to get `PLAN_PATH` and `GUIDELINES_PATH`
2. Verify `PLAN_PATH` exists (error if not)
3. **Back up the plan file:** Copy `PLAN_PATH` to `PLAN_PATH.bak` using Bash: `cp "PLAN_PATH" "PLAN_PATH.bak"`
4. **Discover guideline files** from `GUIDELINES_PATH`:
   - **If directory:** Glob `*.md`, sort alphabetically → ordered list of absolute paths
   - **If index file:** Read it, extract markdown links `[...](path.md)`, resolve relative to the index file's directory → ordered list of absolute paths
5. Display: "Found N guideline files. Starting sequential review..."
6. Store the list of guideline file paths (just paths, NOT content)

**CRITICAL:** Do NOT read the plan file content. You only need the path.

## Step 2: Review Loop (One Subagent Per Guideline)

For each guideline file path in order, dispatch a subagent:

```
Task tool call:
  subagent_type: general-purpose
  description: "Review plan against guideline NN"
  prompt: <see Reviewer Subagent Prompt below>
```

After each subagent returns:
- Display its status message (e.g., "Guideline 02: Applied 3 fixes")
- Proceed to next guideline

**Do NOT read the plan file between passes.** The next subagent will read the file the previous one wrote.

### Reviewer Subagent Prompt

```
You are reviewing an implementation plan against a single coding guideline document.

## Instructions

1. Use the Read tool to read the implementation plan from: {PLAN_FILE_PATH}
2. Use the Read tool to read the coding guideline from: {GUIDELINE_FILE_PATH}
3. Review every task in the plan against THIS guideline document only.
4. Fix any code examples, descriptions, or architectural decisions that violate guidelines in this document.
5. Before each changed code block, add an HTML comment: <!-- applied guideline #N: brief description -->
6. If NO violations found:
   - Do NOT write the file
   - Respond with exactly: "No violations found."
7. If violations ARE found:
   - Use the Write tool to write the corrected plan to: {PLAN_FILE_PATH}
   - Respond with a short summary: "Applied N fixes: [brief list of what changed]"

## Rules
- Only fix violations from THIS guideline document — not guidelines you know from elsewhere.
- Preserve ALL structure, task numbering, and intent. Change only what's needed.
- Skip tasks with no code examples (e.g., "create directory").
- When a guideline shows good/bad code examples, check BOTH levels:
  (a) file/directory organization (which files exist, where they live), AND
  (b) internal code structure (class nesting, type placement, member organization within files).
- Your response text must be ONLY the short summary. Do NOT output any plan content in your response.
```

**Substitute** `{PLAN_FILE_PATH}` and `{GUIDELINE_FILE_PATH}` with actual absolute paths before dispatching.

## Step 3: Summary (Subagent)

After all guideline passes complete, dispatch one final subagent:

```
Task tool call:
  subagent_type: general-purpose
  description: "Summarize guideline changes"
  prompt: <see Summary Subagent Prompt below>
```

### Summary Subagent Prompt

```
Read the implementation plan at: {PLAN_FILE_PATH}

Extract all <!-- applied guideline ... --> HTML comments from the plan.
Group them by guideline number.

Return a concise summary in this format:

## Guidelines Applied
- **Guideline #NN: [name]** — N fixes: [brief descriptions]
- **Guideline #NN: [name]** — N fixes: [brief descriptions]

## No Violations Found
- Guideline #NN: [name]
- Guideline #NN: [name]

Do NOT output any plan content. Only the summary above.
```

Display the summary to the user.

## Step 4: Cleanup

1. Delete the backup file: `rm "PLAN_PATH.bak"` using Bash
2. Report: "Plan updated at PLAN_PATH. Not auto-committed."

## Error Handling

| Scenario | Recovery |
|----------|----------|
| Subagent fails or errors | Restore backup: `cp "PLAN_PATH.bak" "PLAN_PATH"`, report failure |
| Subagent returns plan content in response | Ignore it — the file write already happened |
| Plan file not found | Error immediately, before any backup |
| Guidelines path not found | Error immediately |
| No guideline files discovered | Warn and exit (no review needed) |

## Red Flags

| Problem | Fix |
|---------|-----|
| Subagent outputs plan content in response text | Re-dispatch with emphasis: "Your response must be ONLY the short summary" |
| Subagent applies guidelines from memory | Re-dispatch with emphasis: "Only fix violations from THIS document" |
| Plan structure changes (task numbers, sections removed) | Re-dispatch with emphasis: "Preserve ALL structure and task numbering" |

## File Safety

- Plan file is backed up ONCE before the first reviewer subagent runs
- Each reviewer writes directly to the plan file (sequential, no conflicts)
- On success: backup deleted
- On failure: backup restored
- The orchestrator NEVER reads or holds plan content — only file paths
```

**Step 2: Verify the skill file was written correctly**

Run: Read the file back and confirm it starts with `---` frontmatter and contains all 4 steps.

**Step 3: Commit**

```bash
cd /home/node/.claude/skills/apply-guidelines
git add SKILL.md
git commit -m "refactor(apply-guidelines): v2 file-mediated architecture

Rewrites the skill to route plan content through file I/O instead of
prompt/response, reducing orchestrator context from ~516KB to ~3KB
across 6 guideline passes."
```

---

### Task 2: Manual Integration Test

**Step 1: Run the skill against the existing plan and guidelines**

Invoke: `/apply-guidelines /workspace/performance-tester-dotnet/docs/plans/2026-02-23-docker-monitoring-module-refactoring.md /workspace/performance-tester-dotnet/CODING_GUIDELINES.md`

**Step 2: Verify these success criteria:**

- [ ] Orchestrator does NOT trigger "compacting" message
- [ ] Each guideline pass returns a short status string (not the full plan)
- [ ] The plan file is updated with `<!-- applied guideline -->` comments where appropriate
- [ ] A summary is displayed at the end grouping changes by guideline
- [ ] The `.bak` file is cleaned up

**Step 3: If the skill fails, fix issues and re-run**

Common issues to watch for:
- Subagent not using Read/Write tools (may need model hint: `model: sonnet`)
- Subagent returning plan content in response despite instructions
- File path issues (relative vs absolute)
