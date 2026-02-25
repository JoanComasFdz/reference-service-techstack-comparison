# Audit-Guidelines: Subprocess + File-Rendezvous Redesign

**Date:** 2026-02-25
**Status:** Design approved, pending implementation plan

---

## Problem Statement

The current skill (per-rule Task subagents) was confirmed to consume ~95% of the main loop's context window when auditing a 5-file folder against 37 rules. The root cause is structural: every Task subagent returns its full structured report (FILES_CHECKED + violations) back into the main loop context. With 37 subagents, 37 walls of text accumulate in the main loop regardless of how focused each subagent is. This does not scale — a 20-file folder with 50 rules would reliably exhaust context before assembly.

---

## Design Goals

1. **Main loop uses near-zero context for content** — it never holds guideline text, code content, or audit results. It only holds file paths and exit codes.
2. **Each rule auditor has maximum context available** — a fresh subprocess with no session history, no CLAUDE.md overhead, no accumulated responses. Its full context window is available for reading code files.
3. **Scale independently** — adding more guideline files, more rules, or more code files does not increase main loop context cost.
4. **Batching preserved** — 5 auditors run in parallel per batch, same as current.

---

## Architecture Overview

The main loop is a **pure filesystem orchestrator**. All content lives in a temporary folder on disk. The main loop only ever holds file paths and shell exit codes.

```
Main loop  (content-free orchestrator)
│
├── Phase 0: Setup
│   └── Create /tmp/audit-{timestamp}/rules/ and /tmp/audit-{timestamp}/results/
│
├── Phase 1: Discovery  [parallel Task subagents, one per guideline file]
│   ├── Subagent A → reads guidelines/01-core.md → writes rules/rule-01-01.md … rule-01-07.md
│   ├── Subagent B → reads guidelines/02-delegates.md → writes rules/rule-02-01.md …
│   └── … (one subagent per guideline file, all launched in parallel)
│   Main loop: Globs rules/ folder to get complete rule list. Never reads guideline files itself.
│
├── Phase 2: Audit  [one claude -p CLI per rule, batched in 5s]
│   ├── Batch 1: 5 × claude -p → each writes results/rule-01-01.txt … (parallel via shell &)
│   ├── Batch 2: 5 × claude -p → each writes results/rule-01-06.txt …
│   └── … (ceil(R/5) batches)
│   Main loop: issues one Bash call per batch, waits for all 5 to finish, proceeds.
│
├── Phase 3: Assembly  [one Task subagent]
│   └── Reads all results/rule-*.txt → builds markdown report → writes REPORT_PATH
│   Main loop: receives only "report written to {path}".
│
└── Phase 4: Cleanup
    └── Remove /tmp/audit-{timestamp}/ entirely.
```

---

## Tmp Folder Structure

```
/tmp/audit-{timestamp}/
├── rules/
│   ├── rule-01-01.md       ← one file per rule, written by discovery subagents
│   ├── rule-01-02.md
│   └── … (R files total)
└── results/
    ├── rule-01-01.txt      ← one file per rule, written by CLI auditors
    ├── rule-01-02.txt
    └── … (R files total)
```

---

## Phase Details

### Phase 0: Setup (Inline)

1. Parse arguments: `FOLDER_PATH`, `GUIDELINES_PATH`, optional `FOCUS_HINT`.
2. Validate `FOLDER_PATH` is a directory and `GUIDELINES_PATH` exists.
3. Discover guideline files (same logic as current: directory glob or index file link extraction).
4. List code files in `FOLDER_PATH` (excluding `bin/`, `obj/`, `node_modules/`, `.git/`, `dist/`, `target/`). Store as relative paths. Count = `K`.
5. Create `TMP_DIR = /tmp/audit-{timestamp}/` with `rules/` and `results/` subdirs.
6. Derive `REPORT_PATH` (sibling of `FOLDER_PATH`, `{FOLDER_NAME}-audit-report.md`).
7. Display: `"Found M guideline files and K code files. Extracting rules..."`

**Context cost:** argument strings + file paths only. No content.

---

### Phase 1: Discovery (Parallel Task Subagents)

Dispatch one Task subagent per guideline file, all in a single message (parallel).

Each subagent receives:
- Path to its guideline file
- `TMP_DIR/rules/` path
- The `### NN-NN` rule heading pattern to scan for

Each subagent:
1. Reads the guideline file.
2. Identifies all rule headings (`### NN-NN`).
3. For each rule, writes `/tmp/audit-{timestamp}/rules/rule-{RULE_ID}.md` with this format:

```
RULE_ID: 01-03
RULE_TITLE: Inline Single-Use Logic
GUIDELINE_ID: 01
GUIDELINE_NAME: Core Architecture

--- RULE SECTION ---
{full markdown of the rule from its heading to the next ### heading}
```

4. Returns only: the list of rule IDs written (e.g. `["01-01","01-02","01-03"]`).

After all discovery subagents return:
- Main loop Globs `TMP_DIR/rules/*.md` to get the complete rule file list.
- Does **not** read rule file contents — only needs the paths.
- Computes `R` = count of rule files found.
- Display: `"Extracted R rules across M guideline files. Launching audits in ceil(R/5) batches of 5..."`

**Context cost:** subagent return values are small lists of rule IDs (trivial). Main loop never holds guideline content.

---

### Phase 2: Audit (CLI Subprocesses, Batched)

Process rule files in batches of 5. For each batch:

1. Take up to 5 rule file paths from the list.
2. Issue a **single Bash call** that launches all 5 as background `claude --print` processes:

```bash
claude --print --no-session-persistence --allowedTools "Read Write" \
  "$(cat /tmp/audit-.../rules/rule-01-01.md)" \
  > /tmp/audit-.../results/rule-01-01.txt 2>/dev/null &

claude --print --no-session-persistence --allowedTools "Read Write" \
  "$(cat /tmp/audit-.../rules/rule-01-02.md)" \
  > /tmp/audit-.../results/rule-01-02.txt 2>/dev/null &

# ... up to 5

wait
echo "batch done"
```

3. Wait for all processes in the batch (`wait`).
4. Verify each expected result file was created (if missing, log the rule as failed).
5. Display: `"Batch N/ceil(R/5) complete (X/R rules done)."`
6. Proceed to next batch.

**What the Bash call returns to the main loop:** only `"batch done"` + exit code. No audit content ever enters the main loop.

#### CLI Auditor Prompt

Each `claude --print` process receives a prompt constructed from its rule file:

```
You are auditing source code against a SINGLE coding rule. READ-ONLY — do NOT modify files.

## Rule

RULE_ID: {RULE_ID}
RULE_TITLE: {RULE_TITLE}
GUIDELINE: {GUIDELINE_ID} - {GUIDELINE_NAME}

{RULE_SECTION_TEXT}

## Code to Audit

Folder: {FOLDER_PATH}

Files ({K} total):
{FILE_LIST}

## Instructions

Phase 1 — For EACH file in the list:
1. Read the file.
2. Check whether this rule is violated.
3. Record your conclusion.

Check ALL {K} files. Do not skip any.

Phase 2 — Self-verification:
Re-read the rule. Ask: did I check every file? Did I interpret it correctly?
If you find a missed case, go back and re-check it.

## Output

Write your output to: {RESULT_FILE_PATH}

Use this exact format:
RULE: {RULE_ID} - {RULE_TITLE}
GUIDELINE: {GUIDELINE_ID} - {GUIDELINE_NAME}

FILES_CHECKED:
- <relative-path>: clear
- <relative-path>: VIOLATION (line <N>)
[exactly {K} entries]

VIOLATIONS: <count>

[one block per violation:]
FILE: <relative-path>
LINE: <line-number-or-range>
SEVERITY: <high|medium|low>
ISSUE: <description>
SUGGESTION: <how to fix>

SELF_VERIFICATION: <one sentence confirming all files checked>

Rules:
- Apply ONLY this rule. Not others from memory.
- FILES_CHECKED MUST have exactly {K} entries.
- Do NOT output source code. Describe violations only.
- Do NOT modify any files.
- Write output to the file path above using the Write tool. Do not print it to stdout.
```

**Context cost per CLI process:** rule text + K code files + its own output. Fresh context — no CLAUDE.md, no session history. Maximum context available for actual analysis.

**Note on FOCUS_HINT:** if provided, append a Focus Area section to the prompt (same as current skill).

---

### Phase 3: Assembly (One Task Subagent)

After all batches complete, launch a single Task subagent:

**Input it receives:**
- `TMP_DIR/results/` path
- Expected rule count `R`
- Expected file count `K`
- `REPORT_PATH`

**What it does:**
1. Globs `results/rule-*.txt` and reads all R result files.
2. Parses each file (same RULE/GUIDELINE/FILES_CHECKED/VIOLATIONS format).
3. Groups violations by guideline.
4. Builds the markdown report (same template as current skill: Summary table + Coverage block + Violations by Guideline).
5. Writes report to `REPORT_PATH`.
6. Returns: `"Report written to {REPORT_PATH}. Found {total} violations ({high} high, {medium} medium, {low} low)."`

**Context cost returned to main loop:** one short string. Assembly subagent context holds result files but never surfaces to main loop.

---

### Phase 4: Cleanup (Inline)

Remove `TMP_DIR` entirely. Display completion summary to user.

---

## Context Consumption Analysis

| Phase | What enters main loop context | Cost |
|-------|-------------------------------|------|
| Phase 0 | Argument strings, file paths | Trivial |
| Phase 1 | Discovery subagent return values (rule ID lists) | Trivial |
| Phase 2 | Bash call return values (`"batch done"`) per batch | Trivial |
| Phase 3 | One assembly subagent response (short summary string) | Trivial |
| Phase 4 | None | Zero |
| **Total** | **File paths + short strings** | **Near-zero** |

Compare to current design (37 subagents × structured reports → main loop):
- Current: ~18,000–37,000 tokens of audit content in main loop
- New: ~200 tokens total

---

## Key Design Decisions

### Why delegate discovery (Phase 1) instead of doing it inline?

The main loop reading all guideline files is the remaining content cost after the audit result problem is solved. For 6 guideline files of ~200 lines each, it is moderate (~12k tokens). For a project with 20 guideline files or very long guidelines, it becomes significant. Delegating discovery eliminates this cost entirely and makes the main loop scale independently of guideline volume.

The trade-off is one extra phase (discovery subagents). Since discovery subagents run in parallel (one per guideline file), the wall-clock cost is the time to read one guideline file, not all of them.

### Why `claude --print` CLI instead of Task subagents for auditing?

Task subagents return their response text to the calling context. Even if the response is only a file path, the subagent's reasoning and intermediate work still occupies tokens in the caller's accumulated messages. A `claude --print` subprocess is a completely separate process — its entire execution is isolated from the main loop's context. The main loop only sees the Bash tool result (a short string or empty).

Additionally, each CLI subprocess starts with a genuinely fresh context: no CLAUDE.md system prompt overhead, no accumulated session messages. This gives each rule auditor the maximum possible context window for reading code files.

### Why keep discovery as Task subagents (not CLI subprocesses)?

Discovery subagents return small structured outputs (lists of rule IDs — tens of tokens). The return cost is negligible, and Task subagents are simpler to orchestrate in parallel via a single message with multiple tool calls. The main loop uses Glob to confirm results rather than relying on return values, so even if a return value is verbose, it is ignored.

### Why assemble with a Task subagent (not inline)?

Inline assembly would bring the content of all 37 result files into the main loop's context, defeating Phase 2's isolation. A Task subagent reads the files internally and returns only a completion string. The main loop stays content-free.

### Why batches of 5 CLI processes?

Preserved from the current design. Launching all R processes simultaneously risks API rate limiting and unpredictable resource usage. Batching provides flow control. The batch size is a tunable constant.

### Tmp folder as shared state

All phases communicate exclusively through the filesystem. This makes each phase independently restartable (if Phase 2 partially fails, surviving result files are not lost). It also means the main loop never needs to buffer results in memory — it reads paths, not content.

---

## Error Handling

| Scenario | Recovery |
|----------|----------|
| Discovery subagent fails to write rule files | Log rule IDs as missing; skip their audits; note in report |
| `claude --print` process exits non-zero | Log rule as "audit failed"; check if result file was written anyway |
| Result file missing after batch completes | Mark rule as "audit failed" in Coverage section; do not re-run automatically |
| Result file present but unparseable | Include raw content in report under that rule; mark as "format error" |
| Assembly subagent fails | Error to user with path to raw result files for manual inspection |
| Tmp folder creation fails | Error immediately |

---

## What Does Not Change

- The output report format (Summary table + Coverage block + Violations by Guideline sections) is identical to the current skill.
- The per-rule audit logic (read files, check one rule, FILES_CHECKED + VIOLATIONS output format, self-verification) is identical.
- The `FOCUS_HINT` feature is preserved.
- The `apply-guidelines` skill is unaffected.
