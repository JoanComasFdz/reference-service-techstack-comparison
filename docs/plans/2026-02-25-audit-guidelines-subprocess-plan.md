# audit-guidelines Subprocess Redesign Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rewrite the `audit-guidelines` skill so the main loop is a content-free filesystem orchestrator, using CLI subprocesses (`claude --print`) for rule audits and delegated Task subagents for discovery and assembly — eliminating wall-of-text accumulation in main context.

**Architecture:** Four phases communicate through a shared tmp folder. Phase 1 (discovery) extracts rules to files via Task subagents. Phase 2 (audit) runs one `claude --print` CLI per rule, batched in 5s, writing structured results to the tmp folder. Phase 3 (assembly) consolidates result files via a single Task subagent. Main loop only ever holds file paths and shell exit codes.

**Tech stack:** Single markdown file edit — `/home/node/.claude/skills/audit-guidelines/SKILL.md`. No compilation. Verification by running the skill and observing output.

**Design doc:** `docs/plans/2026-02-25-audit-guidelines-subprocess-design.md`

---

## Task 1: Update Invocation and Process Overview

**Files:**
- Modify: `/home/node/.claude/skills/audit-guidelines/SKILL.md` (lines 1–25)

**Step 1: Open the file and confirm current Process Overview text**

Read the file and locate the `## Process Overview` section. It currently reads:

```
**Key principle:** Read-only. The skill NEVER modifies source files. It produces a markdown report and prints a summary.

1. **Discovery** (orchestrator does this directly — no subagent)
2. **Audit** — one subagent per rule, dispatched in **batches of 5**
3. **Report assembly** (orchestrator does this directly — no subagent)
```

**Step 2: Replace the Process Overview block**

Replace everything from `## Process Overview` through the line ending `3. **Report assembly**...` with:

```markdown
## Process Overview

**Key principle:** Read-only. The skill NEVER modifies source files. It produces a markdown report and prints a summary.

**Context principle:** The main loop never holds guideline content, code content, or audit results. All content lives in a tmp folder on disk. The main loop only holds file paths and exit codes.

1. **Setup** — create tmp folder structure (inline, no subagent)
2. **Discovery** — one Task subagent per guideline file extracts rules into individual files in `tmp/rules/`
3. **Audit** — one `claude --print` CLI subprocess per rule, batched in 5s; each writes structured results to `tmp/results/`
4. **Assembly** — one Task subagent reads all result files, builds and writes the final report
```

**Step 3: Verify**

```bash
grep -A 8 "## Process Overview" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: the new four-point numbered list with "Setup", "Discovery", "Audit", "Assembly".

**Step 4: Commit**

```bash
git add /home/node/.claude/skills/audit-guidelines/SKILL.md
git commit -m "feat(audit-guidelines): update Process Overview to four-phase architecture"
```

---

## Task 2: Rewrite Step 1 — Phase 0 (Setup) + Phase 1 (Delegated Discovery)

**Files:**
- Modify: `/home/node/.claude/skills/audit-guidelines/SKILL.md` (Step 1 section)

**Step 1: Locate and read the current Step 1 section**

The section starts at `## Step 1: Discovery (Inline)` and ends just before `## Step 2:`. It currently has the orchestrator reading all guideline files directly and extracting rules into an `ALL_RULES` list in memory.

**Step 2: Replace the entire Step 1 section**

Replace from `## Step 1: Discovery (Inline)` up to (not including) `## Step 2:` with:

````markdown
## Phase 0: Setup (Inline)

Do these steps directly (no subagent needed):

1. Parse arguments: `FOLDER_PATH`, `GUIDELINES_PATH`, optional `FOCUS_HINT`.
2. Validate: `FOLDER_PATH` exists and is a directory; `GUIDELINES_PATH` exists. Error immediately if not.
3. **Discover guideline files** from `GUIDELINES_PATH`:
   - **If directory:** Glob `*.md`, sort alphabetically → ordered list of absolute paths
   - **If index file:** Read it, extract markdown links `[...](path.md)`, resolve relative to index file's directory, deduplicate (preserving order) → ordered list of absolute paths
   - **If index file yields zero paths**, treat the index file itself as a single guideline.
   - Store as `GUIDELINE_FILES`. Count = `M`.
4. Derive `FOLDER_NAME` from `FOLDER_PATH` (basename).
5. Compute `REPORT_PATH`: sibling of `FOLDER_PATH`, named `{FOLDER_NAME}-audit-report.md`.
6. Generate `TIMESTAMP` (e.g. `date +%s` or current epoch seconds).
7. Create tmp directory: `TMP_DIR = /tmp/audit-{TIMESTAMP}/`. Create subdirs `rules/` and `results/` inside it.
8. Display: `"Found M guideline files. Extracting rules into {TMP_DIR}/rules/ ..."`

**CRITICAL:** Do NOT read code files or guideline files in this phase. Only discover their paths.

---

## Phase 1: Discovery (Parallel Task Subagents)

Dispatch one Task subagent per guideline file, all in a **single message** (parallel). Each subagent reads one guideline file and writes individual rule files to `TMP_DIR/rules/`.

```
Task tool call:
  subagent_type: general-purpose
  description: "Extract rules from guideline file {GUIDELINE_FILENAME}"
  prompt: <see Discovery Subagent Prompt below>
```

After all discovery subagents return:
- Use Glob to list `TMP_DIR/rules/*.md`. Count = `R`. **Do NOT read their contents.**
- Display: `"Extracted R rules across M guideline files. Launching audits in ceil(R/5) batches of 5..."`

### Discovery Subagent Prompt

```
You are extracting individual coding rules from a guideline file into separate files. READ-ONLY on the guideline file.

GUIDELINE FILE: {GUIDELINE_FILE_PATH}
OUTPUT FOLDER: {TMP_DIR}/rules/

## Instructions

1. Read the guideline file at GUIDELINE FILE.
2. Find all rule headings matching the pattern `### NN-NN` (two-digit number, hyphen, two-digit number), e.g. `### 01-03. Inline Single-Use Logic` or `### 02-04: Dependency Composition`.
3. For each rule heading found, extract:
   - RULE_ID: the NN-NN portion (e.g. "01-03")
   - RULE_TITLE: the text after the rule number and any separator (e.g. "Inline Single-Use Logic")
   - GUIDELINE_ID: the first two digits (e.g. "01")
   - GUIDELINE_NAME: derived from the guideline filename — strip the leading NN- prefix and .md extension, replace hyphens with spaces, title-case (e.g. "01-core-architecture.md" → "Core Architecture")
   - RULE_SECTION_TEXT: all markdown content from this `### NN-NN` heading up to (NOT including) the next `###`-level heading or end of file

4. For each rule, write a file to OUTPUT FOLDER named `rule-{RULE_ID}.md` using the Write tool, in this EXACT format:

```
RULE_ID: {RULE_ID}
RULE_TITLE: {RULE_TITLE}
GUIDELINE_ID: {GUIDELINE_ID}
GUIDELINE_NAME: {GUIDELINE_NAME}

--- RULE SECTION ---
{RULE_SECTION_TEXT}
```

5. Return ONLY: the JSON list of rule IDs you wrote, e.g. `["01-01","01-02","01-03"]`

## Constraints
- Do NOT skip any rule heading. Check the entire file.
- RULE_SECTION_TEXT must stop before the next `###` heading — do not bleed into the next rule.
- The file MUST be named exactly `rule-{RULE_ID}.md` (lowercase, e.g. `rule-01-03.md`).
- Do NOT modify the guideline file.
```
````

**Step 3: Verify**

```bash
grep -c "Phase" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: at least 4 matches (Phase 0, Phase 1, Phase 2, Phase 3 or 4).

```bash
grep "Discovery Subagent Prompt" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: one match.

**Step 4: Commit**

```bash
git add /home/node/.claude/skills/audit-guidelines/SKILL.md
git commit -m "feat(audit-guidelines): replace inline discovery with Phase 0 setup + Phase 1 delegated subagents"
```

---

## Task 3: Rewrite Step 2 — Phase 2 (CLI Subprocess Batches)

**Files:**
- Modify: `/home/node/.claude/skills/audit-guidelines/SKILL.md` (Step 2 section + Per-Rule Auditor Subagent Prompt)

**Step 1: Locate the current Step 2 section**

Find `## Step 2: Audit (Batched Parallel Subagents)` through the end of `### Per-Rule Auditor Subagent Prompt` (including the `{FOCUS_SECTION}` substitution rule). This is the entire audit section.

**Step 2: Replace the entire Step 2 section**

Replace from `## Step 2: Audit` through the `{FOCUS_SECTION}` substitution block (ending just before `## Step 3:`) with:

````markdown
## Phase 2: Audit (CLI Subprocesses, Batched)

Process rule files in batches of 5. For each batch:

1. Take up to 5 rule file paths from the Glob'd list in `TMP_DIR/rules/`.
2. For each rule file in the batch, construct a prompt and write it to `TMP_DIR/prompts/rule-{RULE_ID}-prompt.txt` using the Write tool. See **CLI Auditor Prompt** below for the template. The main loop substitutes only paths — it does NOT read the rule file contents.
3. Issue a **single Bash call** that launches all 5 as background `claude --print` processes and waits:

```bash
cat {TMP_DIR}/prompts/rule-{RULE_ID_1}-prompt.txt | claude --print \
  --allowedTools "Read Write Glob" \
  --no-session-persistence \
  --add-dir {FOLDER_PATH} \
  --add-dir {TMP_DIR} \
  > /dev/null 2>&1 &

cat {TMP_DIR}/prompts/rule-{RULE_ID_2}-prompt.txt | claude --print \
  --allowedTools "Read Write Glob" \
  --no-session-persistence \
  --add-dir {FOLDER_PATH} \
  --add-dir {TMP_DIR} \
  > /dev/null 2>&1 &

# repeat for remaining rules in batch (up to 5 total)

wait
echo "batch complete"
```

4. The Bash call returns only `"batch complete"` — no audit content enters the main loop.
5. After the Bash call returns, verify that each expected result file exists in `TMP_DIR/results/`. For any missing file, log: `"⚠️ Rule {RULE_ID}: result file not written — marked as audit failed."`
6. Display: `"Batch N/ceil(R/5) complete (X/R rules done)."`
7. Proceed to the next batch.

### CLI Auditor Prompt Template

For each rule in the batch, write this prompt to `TMP_DIR/prompts/rule-{RULE_ID}-prompt.txt`. Substitute only the values shown — do NOT read the rule file to build this prompt.

| Placeholder | Value (main loop substitutes from paths only) |
|---|---|
| `{RULE_FILE_PATH}` | `{TMP_DIR}/rules/rule-{RULE_ID}.md` |
| `{FOLDER_PATH}` | The code folder being audited |
| `{RESULT_FILE_PATH}` | `{TMP_DIR}/results/rule-{RULE_ID}.txt` |
| `{FOCUS_SECTION}` | See substitution rule below |

```
You are auditing source code against a single coding rule. READ-ONLY on source files.

YOUR RULE FILE: {RULE_FILE_PATH}
CODE FOLDER: {FOLDER_PATH}
RESULT FILE: {RESULT_FILE_PATH}

{FOCUS_SECTION}

## Instructions

1. Read YOUR RULE FILE. Extract: RULE_ID, RULE_TITLE, GUIDELINE_ID, GUIDELINE_NAME, and the rule definition from the "--- RULE SECTION ---" block.
2. Use Glob to list all non-build source files in CODE FOLDER (exclude bin/, obj/, node_modules/, .git/, dist/, target/). Call this count K.
3. For EACH file found:
   a. Read the file.
   b. Check whether this specific rule is violated anywhere in it.
   c. Record your conclusion.
4. SELF-VERIFY: re-read the rule definition. Did you check every file? Did you interpret the rule correctly? If you find a missed case, re-check it now.
5. Use the Write tool to write your structured results to RESULT FILE in the exact format below.

## Output Format

Write the following to RESULT FILE (do NOT print to stdout):

RULE: <RULE_ID from your rule file> - <RULE_TITLE from your rule file>
GUIDELINE: <GUIDELINE_ID> - <GUIDELINE_NAME>

FILES_CHECKED:
- <relative-path>: clear
- <relative-path>: VIOLATION (line <N>)
[one entry per file — MUST have exactly K entries, one per file found]

VIOLATIONS: <count>

[Include one block per violation when VIOLATIONS > 0:]
FILE: <relative-path>
LINE: <line-number-or-range>
SEVERITY: <high|medium|low>
ISSUE: <short description of the violation>
SUGGESTION: <short description of how to fix it>

SELF_VERIFICATION: <one sentence confirming you re-read the rule, checked all K files, and identified no missed cases — or describing what you caught and re-checked>

## Constraints
- Apply ONLY the rule from YOUR RULE FILE. Do not apply rules from memory or other guidelines.
- FILES_CHECKED MUST have exactly one entry per file you found with Glob.
- Do NOT copy source code into your output. Describe violations only.
- Do NOT modify any source files or the rule file.
- WRITE output using the Write tool to RESULT FILE. Do not print it to stdout.
```

**`{FOCUS_SECTION}` substitution rule:**
- If `FOCUS_HINT` was provided: substitute with:
  ```
  FOCUS AREA: The user requests extra attention on: "{FOCUS_HINT}"
  If this relates to your rule: report borderline cases you would otherwise skip, and be more detailed in ISSUE and SUGGESTION.
  If this does not relate to your rule: audit normally.
  ```
- If no `FOCUS_HINT`: substitute with an empty string (remove the placeholder entirely).
````

**Step 3: Verify**

```bash
grep "claude --print" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: at least one match showing the subprocess invocation.

```bash
grep "RESULT FILE" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: matches in the CLI Auditor Prompt section.

**Step 4: Commit**

```bash
git add /home/node/.claude/skills/audit-guidelines/SKILL.md
git commit -m "feat(audit-guidelines): replace Task subagent audit with claude --print CLI subprocess batches"
```

---

## Task 4: Rewrite Step 3 — Phase 3 (Assembly Subagent) + Phase 4 (Cleanup)

**Files:**
- Modify: `/home/node/.claude/skills/audit-guidelines/SKILL.md` (Step 3 section)

**Step 1: Locate the current Step 3 section**

Find `## Step 3: Report Assembly (Inline)` through the end of section `### 3c. Write and display`. This is the inline assembly section.

**Step 2: Replace the entire Step 3 section**

Replace from `## Step 3: Report Assembly (Inline)` through `### 3c. Write and display` (ending just before `## Error Handling`) with:

````markdown
## Phase 3: Assembly (One Task Subagent)

After all audit batches complete, launch a single Task subagent to consolidate result files into the final report.

```
Task tool call:
  subagent_type: general-purpose
  description: "Assemble audit report from result files"
  prompt: <see Assembly Subagent Prompt below>
```

The subagent returns only: `"Report written to {REPORT_PATH}. Found {total} violations ({high} high, {medium} medium, {low} low)."` (or an equivalent short summary).

After the subagent returns, display its summary to the user.

### Assembly Subagent Prompt

```
You are assembling a code audit report from individual rule result files. READ-ONLY on source files.

RESULTS FOLDER: {TMP_DIR}/results/
REPORT PATH: {REPORT_PATH}
FOLDER NAME: {FOLDER_NAME}
FOLDER PATH: {FOLDER_PATH}
GUIDELINES PATH: {GUIDELINES_PATH}
DATE: {YYYY-MM-DD}
EXPECTED RULES: {R}
EXPECTED FILES PER RULE: {K}

## Instructions

1. Use Glob to find all rule-*.txt files in RESULTS FOLDER.
2. Read each file and parse it:
   - Extract RULE, GUIDELINE header lines → rule ID, title, guideline ID, guideline name
   - Parse FILES_CHECKED entries — count them. If count < K, record this rule as incomplete coverage.
   - Extract VIOLATIONS count.
   - Parse each violation block (FILE, LINE, SEVERITY, ISSUE, SUGGESTION).
3. Group all violations by GUIDELINE_ID (sort by GUIDELINE_ID ascending, then RULE_ID ascending within each guideline).
4. Build the report using the template below.
5. Write the report to REPORT PATH using the Write tool.
6. Return: "Report written to {REPORT_PATH}. Found {total} violations ({high} high, {medium} medium, {low} low) across {R} rules, all {K} files checked." (adjust coverage message if any rules had incomplete coverage).

## Report Template

```markdown
# Audit Report: {FOLDER_NAME}

**Date:** {YYYY-MM-DD}
**Folder:** `{FOLDER_PATH}`
**Guidelines:** `{GUIDELINES_PATH}`
**Files scanned:** {K}
**Rules audited:** {R}
**Total violations:** {TOTAL}

## Summary

| Guideline | Violations | High | Medium | Low |
|-----------|-----------|------|--------|-----|
| {ID} - {Name} | {count} | {high} | {medium} | {low} |
| **Total** | **{total}** | **{high}** | **{medium}** | **{low}** |

## Coverage

**Rules audited:** {R}/{R}
**Files checked per rule:** {K} files (all files)

{COVERAGE_NOTES}

## Violations by Guideline

### {GUIDELINE_ID}: {GUIDELINE_NAME}

#### {RULE_ID}: {RULE_TITLE}

- **File:** `{relative-path}` (line {line})
  - **Severity:** {severity}
  - **Issue:** {description}
  - **Suggestion:** {suggestion}

### {GUIDELINE_ID}: {GUIDELINE_NAME}

_No violations found._
```

Where `{COVERAGE_NOTES}` is:
- If all rules reported exactly K files: `_All rules checked all files._`
- If any rule had fewer: list each rule with actual count and missing filenames, e.g.: `⚠️ Rule 05-03: only 4/{K} files checked (missing: Internal/ProcessMonitorModule.cs)`
- If any rule was flagged as "audit failed" (result file missing or unparseable): `⚠️ Rule {RULE_ID}: audit failed — result file missing or unreadable.`

Guidelines with zero violations get the `_No violations found._` message.

## Constraints
- Do NOT modify any source files.
- Do NOT include SELF_VERIFICATION lines in the final report.
- Sort guideline sections by GUIDELINE_ID. Sort rule subsections by RULE_ID within each guideline.
```

---

## Phase 4: Cleanup (Inline)

After the assembly subagent returns and the summary has been displayed to the user:

```bash
rm -rf {TMP_DIR}
```

Issue this as a Bash call. Display: `"Temporary files cleaned up."`
````

**Step 3: Verify**

```bash
grep "Assembly Subagent Prompt" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: one match.

```bash
grep "Phase 4" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: one match for the cleanup section.

**Step 4: Commit**

```bash
git add /home/node/.claude/skills/audit-guidelines/SKILL.md
git commit -m "feat(audit-guidelines): replace inline assembly with Phase 3 Task subagent + Phase 4 cleanup"
```

---

## Task 5: Update Error Handling and Red Flags

**Files:**
- Modify: `/home/node/.claude/skills/audit-guidelines/SKILL.md` (Error Handling + Red Flags sections)

**Step 1: Locate current Error Handling table**

Find `## Error Handling` and read the existing table rows. The table currently covers subagent failures, unparseable output, folder/guideline path errors, and coverage gaps.

**Step 2: Replace the Error Handling table**

Replace the full `## Error Handling` section (table + any following text) with:

```markdown
## Error Handling

| Scenario | Recovery |
|----------|----------|
| Discovery subagent fails or returns no rule IDs | Log the guideline file as failed; skip its rules; note "discovery failed for {filename}" in the Coverage section |
| Discovery subagent writes fewer rule files than expected | Log missing rule IDs; skip their audits; flag as "discovery incomplete" in Coverage |
| `claude --print` process exits non-zero for a rule | Check if result file was written anyway; if yes, continue; if no, mark rule as "audit failed" |
| Result file missing after batch completes | Mark rule as "audit failed" in Coverage section; do not re-run automatically |
| Result file present but unparseable | Include raw content in report under that rule; mark as "format error" |
| `FILES_CHECKED` count < K in a result file | Flag that rule as "incomplete coverage" in the Coverage section |
| `FILES_CHECKED` section absent from result file | Treat as 0 files checked; mark as "audit format error" |
| Assembly subagent fails | Error to user; print path to raw result files for manual inspection; do not delete TMP_DIR |
| Tmp folder creation fails | Error immediately |
| Folder path not found | Error immediately |
| Folder path is a file, not directory | Error immediately |
| Guidelines path not found | Error immediately |
| No guideline files discovered | Warn and exit |
| No code files found in folder | Warn and exit |
```

**Step 3: Replace the Red Flags table**

Find `## Red Flags` and replace the full section with:

```markdown
## Red Flags

Re-dispatch at most once per red flag. If the second attempt still fails, mark as failed and continue.

| Problem | Fix |
|---------|-----|
| Discovery subagent writes 0 rule files | Re-dispatch with emphasis: "You MUST write one rule-{RULE_ID}.md file per ### NN-NN heading found. Use the Write tool." |
| CLI result file is empty or contains only whitespace | Re-run that specific rule's `claude --print` call once more |
| Assembly subagent includes SELF_VERIFICATION lines in the report | Re-dispatch with emphasis: "Do NOT include SELF_VERIFICATION lines in the final report." |
| Assembly subagent outputs source code in the report | Re-dispatch with emphasis: "Do NOT copy source code. Describe violations only." |
```

**Step 4: Verify**

```bash
grep "claude --print.*exits non-zero" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: one match in the Error Handling table.

```bash
grep "Red Flags" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: one match.

**Step 5: Commit**

```bash
git add /home/node/.claude/skills/audit-guidelines/SKILL.md
git commit -m "feat(audit-guidelines): update Error Handling and Red Flags for subprocess architecture"
```

---

## Task 6: Update Key Differences Table

**Files:**
- Modify: `/home/node/.claude/skills/audit-guidelines/SKILL.md` (Key Differences section at the end)

**Step 1: Find the Key Differences table**

Locate `## Key Differences from apply-guidelines`. It currently compares the two skills on Input, Mutation, Source scanning, Concurrency, Output, Backup, and When to use.

**Step 2: Update the Concurrency row**

Find the row:
```
| Concurrency | Source scan: parallel; Plan review: sequential | Batched parallel (5 per batch) |
```

Replace it with:
```
| Concurrency | Source scan: parallel; Plan review: sequential | Batched parallel (5 CLI subprocesses per batch); discovery parallel (1 subagent per guideline file) |
```

**Step 3: Add a new "Main loop context" row**

After the Concurrency row, add:
```
| Main loop context | Accumulates plan + scan results | Near-zero: only holds file paths and exit codes; all content lives in tmp folder |
```

**Step 4: Verify**

```bash
grep "Near-zero" /home/node/.claude/skills/audit-guidelines/SKILL.md
```

Expected: one match in the Key Differences table.

**Step 5: Commit**

```bash
git add /home/node/.claude/skills/audit-guidelines/SKILL.md
git commit -m "feat(audit-guidelines): update Key Differences table for subprocess architecture"
```

---

## Task 7: Smoke Test — Run on PerformanceTester.ProcessMonitoring

**Purpose:** Verify the redesigned skill produces an equivalent report to the previous run while consuming substantially less main loop context.

**Step 1: Note current context usage before running**

Observe the context bar at the bottom of the UI. Note the percentage.

**Step 2: Run the skill**

```
/audit-guidelines performance-tester-dotnet/src/PerformanceTester.ProcessMonitoring performance-tester-dotnet/CODING_GUIDELINES.md
```

**Step 3: Verify Phase 0 and Phase 1 output**

Expected console output during Phase 0/1:
```
Found M guideline files. Extracting rules into /tmp/audit-.../rules/ ...
Extracted R rules across M guideline files. Launching audits in ceil(R/5) batches of 5...
```

If it still says the old "Discovered R rules..." message from Step 1 discovery, Phase 0/1 were not updated correctly.

**Step 4: Verify tmp folder was created**

```bash
ls /tmp/ | grep audit
```

Expected: a folder named `audit-{timestamp}` (it will be deleted after completion, so run this mid-execution or check after a batch log line appears).

**Step 5: Verify batch log lines appear**

Expected during Phase 2:
```
Batch 1/8 complete (5/37 rules done).
Batch 2/8 complete (10/37 rules done).
...
```

**Step 6: Verify context usage after completion**

Check the context bar. Expected: substantially below 95% (target: under 30% for a 5-file folder).

**Step 7: Verify report matches known violations**

Open the generated `performance-tester-dotnet/src/PerformanceTester.ProcessMonitoring-audit-report.md`. Confirm it contains the same 14 violations found by the previous run:
- 04-01: high severity — `SamplingInterval` value object missing
- 06-01: three braceless if/continue violations in `ProcessMonitorModule.cs`
- 05-06: `StartMonitoringAsync` business logic in thin shell
- (and the remaining 10 medium/low violations)

**Step 8: Verify tmp folder was cleaned up**

```bash
ls /tmp/ | grep audit
```

Expected: no matching folder (cleanup ran in Phase 4).

**Step 9: If a known violation is missing**

Check the relevant rule's result file (it exists in `TMP_DIR/results/` only during execution — if missing post-run, the cleanup already removed it). If a violation was missed, note which rule and file and add it to the next iteration's test cases. Do not block completion for a single missed violation — the smoke test is to verify the architecture works end-to-end.
