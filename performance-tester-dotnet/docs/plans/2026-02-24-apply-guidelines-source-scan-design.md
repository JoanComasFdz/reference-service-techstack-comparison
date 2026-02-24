# Apply-Guidelines Source Scan Enhancement — Design

**Date:** 2026-02-24

**Goal:** Improve the `apply-guidelines` skill so it not only fixes code blocks within the plan but also scans the actual source files the plan references, catching guideline violations the plan author missed and appending fix tasks automatically.

**Problem:** Currently `apply-guidelines` only reviews the plan's proposed code. If the plan doesn't mention a file or a violation, `apply-guidelines` can't catch it. This was demonstrated when the DockerMonitoring guideline-fixes plan missed 9 violations that `audit-guidelines` later found in the actual source code — including 4 medium-severity `Api.cs` formatting issues and a `WarmupAsync` Match violation.

**Approach:** Two-phase pipeline — add a parallel read-only source scan before the existing sequential plan review, then append gap tasks for uncovered violations.

---

## Architecture: 4-Step Pipeline

```
Step 1: Discovery (inline, no subagent)
   ├── Parse args, backup plan
   ├── Discover guideline files (unchanged)
   └── NEW: Extract referenced source file paths from plan

Step 2: Source Scan (NEW — parallel subagents, read-only)
   ├── One subagent per guideline file, all in parallel
   ├── Each scans ONLY the plan-referenced source files
   └── Returns structured violations

Step 3: Plan Review (EXISTING — sequential subagents, mutating)
   ├── One subagent per guideline file, sequential
   └── Each fixes code blocks in the plan (unchanged)

Step 4: Gap Append + Summary (REPLACES existing summary step)
   ├── Receives source scan violations from Step 2
   ├── Reads the plan (post-Step 3 fixes)
   ├── Identifies violations NOT already covered by existing tasks
   ├── Appends new fix tasks to the plan
   └── Produces final summary
```

Invocation is unchanged: `/apply-guidelines <plan-path> <guidelines-path> [focus-hint]`

---

## Step 1: Discovery (Modified)

Existing behavior is unchanged. The following is added after guideline file discovery:

### File Path Extraction

The orchestrator reads the plan file (just this once, for path extraction only) and collects source file paths from `Files:` sections.

**Patterns to match:**
```
- Modify: `path/to/file.cs`
- Modify: `path/to/file.cs:123-145`
- Modify: `$WT/path/to/file.cs`
```

**Processing:**
1. Strip line-number suffixes (`:123-145`)
2. Strip worktree variable prefixes (`$WT/` or similar) — replace with the plan's base directory if inferrable, or warn and skip
3. Resolve to absolute paths
4. Exclude `Create:` entries (files that don't exist yet)
5. Deduplicate
6. Verify each path exists on disk — skip missing files with a warning

**Output:** `SOURCE_FILES` — a deduplicated list of absolute paths to existing source files.

**Edge case:** If zero source files are found (all `Create:`, or paths can't be resolved), skip Step 2 entirely and proceed to Step 3. Display: "No existing source files referenced in plan. Skipping source scan."

---

## Step 2: Source Scan (New)

Dispatch all subagents in a single message (parallel). One per guideline file.

### Source Scan Subagent Prompt

```
You are auditing source code against a single coding guideline document. You are READ-ONLY — do NOT modify any files.

## Inputs

- Files to audit (read each one):
{FILE_LIST}
- Guideline document: {GUIDELINE_FILE_PATH}
- Guideline ID: {GUIDELINE_ID}
- Guideline name: {GUIDELINE_NAME}

## Instructions

1. Use the Read tool to read the coding guideline from: {GUIDELINE_FILE_PATH}
2. Understand what rules this guideline defines. Note each rule's ID and what it requires.
3. Use the Read tool to read each source file listed above.
4. For each file, check whether any rules from THIS guideline are violated.
5. For each violation found, record:
   - The rule ID (e.g., 01-03)
   - The file path (absolute)
   - The approximate line number or range
   - A short description of the issue
   - A short suggestion for how to fix it
   - A severity: "high" (clearly wrong), "medium" (should fix), or "low" (style preference)

{FOCUS_SECTION}

## Rules
- Only report violations from THIS guideline document — not guidelines you know from elsewhere.
- Be precise about file paths and line numbers.
- Be concise in issue descriptions and suggestions.
- Do NOT output any code blocks from the source files. Only describe the violations.
- Do NOT modify any files. This is a read-only scan.
- Your response must contain ONLY the structured report below. Nothing else.

## Output Format

If no violations found:

GUIDELINE: {GUIDELINE_ID} - {GUIDELINE_NAME}
VIOLATIONS: 0

If violations found:

GUIDELINE: {GUIDELINE_ID} - {GUIDELINE_NAME}
VIOLATIONS: <count>

RULE: <rule-id>
FILE: <absolute-file-path>
LINE: <line-number-or-range>
SEVERITY: <high|medium|low>
ISSUE: <short description>
SUGGESTION: <short description>
```

**`{FILE_LIST}` format:** One absolute path per line, bulleted.

**`{FOCUS_SECTION}` substitution:** Same rule as existing apply-guidelines (include focus hint if provided, empty string otherwise).

**After all subagents return:**
- Collect all violation reports
- Parse into structured data: list of `{guideline_id, rule_id, file, line, severity, issue, suggestion}`
- Store as `SOURCE_VIOLATIONS` (in orchestrator memory, not written to disk)
- Display: "Source scan complete. Found N violations across M guidelines."

---

## Step 3: Plan Review (Unchanged)

Identical to the current apply-guidelines Step 2. Sequential subagents, one per guideline, each reads and potentially rewrites the plan file.

No modifications needed.

---

## Step 4: Gap Append + Summary (Replaces Current Step 3)

This step replaces the existing summary-only step. It now has two responsibilities: appending gap tasks and producing the summary.

### Gap Append Subagent Prompt

Dispatch a single subagent:

```
You are adding missing guideline-fix tasks to an implementation plan.

## Inputs

- Plan file: {PLAN_FILE_PATH}
- Source scan violations (not yet addressed in the plan):
{FORMATTED_VIOLATIONS}

## Instructions

1. Read the implementation plan from: {PLAN_FILE_PATH}
2. For each violation listed above, determine whether the plan ALREADY has a task that addresses it.
   A violation is "already addressed" if:
   - A task modifies the same file AND
   - The task description or code changes fix the same rule violation (same rule ID, same issue)
3. For violations NOT already addressed, generate new tasks and append them to the plan.
4. If ALL violations are already addressed, do NOT write the file. Respond: "No gap tasks needed. All source violations already covered."
5. If gap tasks are needed, write the updated plan to: {PLAN_FILE_PATH}

## Format for Appended Tasks

Add a new section at the end of the plan (before any "Summary of Changes" section if one exists):

---

## Additional Guideline Fixes (Auto-Detected)

> These tasks were added by apply-guidelines after scanning source files
> referenced in the plan. They address violations not covered by the
> original plan tasks.

### Task N+1: Fix [filename] — [Rule Description] ([Guideline ID])

**Files:**

- Modify: `[absolute-path-to-file]`

**Step 1: [Description of the fix]**

[Specific instruction with code showing what to change. Include the current code and what it should become.]

**Step 2: Build the project**

Run: [appropriate build command based on project type]
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

---

Number tasks sequentially after the last existing task number.
Group multiple violations in the same file into a single task with multiple steps.

## Rules
- Preserve ALL existing plan content. Only append.
- Follow the plan's existing code style and task format as closely as possible.
- Include complete code in fixes (not "change X to Y" — show the actual code).
- Your response must be ONLY a short summary. Do NOT output plan content in your response.
```

**`{FORMATTED_VIOLATIONS}` format:** The structured violations from Step 2, formatted as a readable list:

```
1. [SEVERITY] Rule 06-04 in Api.cs (line 126-132): Expression body => on Starting factory method has expression on new line. Fix: Place => new( on same line or convert to block body.
2. [SEVERITY] Rule 03-03 in DockerMonitorBackgroundService.cs (line 52-55): WarmupAsync uses IsSuccess instead of Match. Fix: Replace with idResult.Match(...).
...
```

### Summary Display

After the gap-append subagent returns, the orchestrator:

1. Displays the subagent's status (e.g., "Added 3 gap tasks" or "No gap tasks needed")
2. Dispatches the existing summary subagent (reads plan, extracts `<!-- applied guideline -->` comments, groups by guideline)
3. Appends to the summary:
   - Count of auto-detected gap tasks added
   - List of guidelines with no violations (from Step 2 and Step 3 tracking)

### Cleanup

Same as current: delete `.bak` file, report plan path.

---

## Error Handling

All existing error handling remains. Additional cases:

| Scenario | Recovery |
|----------|----------|
| Source file doesn't exist on disk | Skip it with warning, continue scanning other files |
| Source scan subagent fails | Log failure, continue with other guidelines, note in summary |
| Gap-append subagent fails | Restore backup, report failure (plan reverts to post-Step-3 state, not original) |
| Zero source files extracted from plan | Skip Step 2 entirely, proceed normally |
| Source scan finds zero violations | Skip gap-append portion of Step 4, proceed to summary |

---

## Performance Characteristics

| Phase | Concurrency | Expected Duration |
|-------|-------------|-------------------|
| Step 1: Discovery | Inline | <5 seconds |
| Step 2: Source Scan | Parallel (N subagents) | ~30-60 seconds (bounded by slowest guideline) |
| Step 3: Plan Review | Sequential (N subagents) | ~2-4 minutes (N * ~20-30s per guideline) |
| Step 4: Gap Append | Single subagent | ~30-60 seconds |

Total added time from the enhancement: ~1-2 minutes (Steps 2 + gap portion of Step 4).

---

## What This Catches That Current apply-guidelines Doesn't

Using the DockerMonitoring case as a concrete example:

| Violation | Current apply-guidelines | Enhanced apply-guidelines |
|-----------|------------------------|--------------------------|
| 03-03 Match in MonitoringModule.cs | Fixes (in plan code) | Fixes (in plan code) |
| 06-04 switch arms in MonitoringModule.cs | Fixes (in plan code) | Fixes (in plan code) |
| 05-06 visibility in StatsModule.cs | Fixes (in plan code) | Fixes (in plan code) |
| **03-03 Match in DockerMonitorBackgroundService.cs** | Misses | **Catches via source scan** |
| **06-04 in Api.cs (4 factory methods)** | Misses | **Catches via source scan** |
| **05-04 context record in BackgroundService** | Misses | **Catches via source scan** |
| **01-11 guard clause in StatsModule.cs** | Misses | **Catches via source scan** |

The enhancement would have caught all violations that the post-execution audit found, eliminating the need for a second plan-execute cycle.
