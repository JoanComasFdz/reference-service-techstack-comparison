# Design: Per-Rule Coding Guideline Files

**Date:** 2026-02-26
**Status:** Approved

## Problem

The coding guidelines are organized as 6 markdown files, each containing multiple rules (e.g., `01-core-architecture.md` holds rules 01-01 through 01-11). This makes individual rules hard to link to precisely, hard to navigate directly, and bulky to read in isolation.

## Goal

Refactor the structure so each guideline rule lives in its own file. Cross-references between rules become direct, precise hyperlinks.

## Approach: Direct split, delete originals (Approach A)

Split each `NN-name.md` file into a `NN-name/` folder containing one file per rule. Delete the original files. Update all cross-references to link directly to the target rule file.

## New Structure

```
coding-guidelines/
├── 01-core-architecture/
│   ├── 01-01-static-classes.md
│   ├── 01-02-explicit-parameters.md
│   ├── 01-03-inline-single-use-code.md
│   ├── 01-04-descriptive-names.md
│   ├── 01-05-toolbox-pattern.md
│   ├── 01-06-vertical-slice.md
│   ├── 01-07-reasons-for-change.md
│   ├── 01-08-explicit-over-implicit.md
│   ├── 01-09-no-wrapper-functions.md
│   ├── 01-10-composition-over-interfaces.md
│   └── 01-11-return-early.md
├── 02-delegates-and-dependency-wiring/
│   ├── 02-01-named-delegates.md
│   ├── 02-02-named-over-action-func.md
│   ├── 02-03-interfaces-vs-delegates.md
│   ├── 02-04-dependency-composition.md
│   ├── 02-05-static-class-as-module.md
│   └── 02-06-delegate-suffix.md
├── 03-error-handling/
│   ├── 03-01-result-over-exceptions.md
│   ├── 03-02-using-static-for-results.md
│   ├── 03-03-dunet-match.md
│   ├── 03-04-option-for-absence.md
│   └── 03-05-no-null-checks-in-constructors.md
├── 04-value-objects/
│   ├── 04-01-use-value-objects.md
│   ├── 04-02-value-object-structure.md
│   ├── 04-03-unwrap-at-boundaries.md
│   ├── 04-04-no-unit-tests.md
│   └── 04-05-value-object-families.md
├── 05-state-and-composition-patterns/
│   ├── 05-01-higher-order-helpers.md
│   ├── 05-02-consumer-owns-defaults.md
│   ├── 05-03-three-bucket-rule.md
│   ├── 05-04-context-record-pattern.md
│   ├── 05-05-immutable-state-threading.md
│   └── 05-06-thin-shell-pattern.md
└── 06-formatting/
    ├── 06-01-always-use-braces.md
    ├── 06-02-blank-line-after-brace.md
    ├── 06-03-all-or-nothing-params.md
    └── 06-04-expression-body-same-line.md
```

37 rule files total. No folder index files.

## Rule File Format

Each file contains:
1. **H1 heading** — the rule number and name, promoted from H3 (e.g., `# 01-01. Static Classes for Pure Logic`)
2. **Rule content** — unchanged from the current file
3. No back-reference header, no folder link

Example:
```markdown
# 01-01. Static Classes for Pure Logic

If a class has no instance state, make it `static`...
```

## CODING_GUIDELINES.md Changes

- **Routing table links** — retargeted to the first rule file in each folder
- **Document Overview links** — same retargeting
- **Summary Checklist** — no changes (plain text, no links today, none added)

Example routing table entry after change:
```markdown
| Writing a new class or function | [Core Architecture](coding-guidelines/01-core-architecture/01-01-static-classes.md) (Guidelines 01-01 to 01-11) |
```

## Cross-Reference Rules

Every "Guideline XX-YY" mention in any rule file becomes an explicit markdown link to the specific rule file. Link text uses the surrounding prose (e.g., `[Guideline 03-01](../03-error-handling/03-01-result-over-exceptions.md)`), not the filename slug.

Old document-level links (e.g., `[Core Architecture](01-core-architecture.md)`) are replaced by the specific rule link.

### Complete cross-reference map

**01-core-architecture rules → outbound:**
- 01-09: "Guideline 02-05" → `../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md`

**02-delegates-and-dependency-wiring rules → outbound:**
- 02-02: "Guideline 01-04" → `../01-core-architecture/01-04-descriptive-names.md`
- 02-05: "Guideline 01-09" → `../01-core-architecture/01-09-no-wrapper-functions.md`
- 02-05: "Guideline 05-06" → `../05-state-and-composition-patterns/05-06-thin-shell-pattern.md`
- 02-06: "Guideline 02-01" → `02-01-named-delegates.md`
- 02-06: "Guideline 02-02" → `02-02-named-over-action-func.md`
- 02-06: "Guideline 02-05" → `02-05-static-class-as-module.md`

**03-error-handling rules → outbound:**
- 03-01 note: "Guideline 04-01" → `../04-value-objects/04-01-use-value-objects.md`
- 03-01 note: "Guideline 03-05" → `03-05-no-null-checks-in-constructors.md`
- 03-04: "Guideline 01-11" → `../01-core-architecture/01-11-return-early.md`
- 03-04: "Guideline 03-03" → `03-03-dunet-match.md`

**04-value-objects rules → outbound:**
- 04-01: "Guideline 03-01" → `../03-error-handling/03-01-result-over-exceptions.md`

**05-state-and-composition-patterns rules → outbound:**
- 05-03: "Guideline 02-04" → `../02-delegates-and-dependency-wiring/02-04-dependency-composition.md`
- 05-04: "Guideline 01-02" → `../01-core-architecture/01-02-explicit-parameters.md`
- 05-04: "Guideline 05-05" → `05-05-immutable-state-threading.md`
- 05-04: "Guideline 05-06" → `05-06-thin-shell-pattern.md`
- 05-05: "Guideline 05-04" → `05-04-context-record-pattern.md`
- 05-05: "Guideline 01-01" → `../01-core-architecture/01-01-static-classes.md`
- 05-05: "Guideline 01-02" → `../01-core-architecture/01-02-explicit-parameters.md`
- 05-06: "Guideline 01-01" → `../01-core-architecture/01-01-static-classes.md`
- 05-06: "Guideline 01-02" → `../01-core-architecture/01-02-explicit-parameters.md`
- 05-06: "Guideline 02-01" → `../02-delegates-and-dependency-wiring/02-01-named-delegates.md`
- 05-06: "Guideline 02-03" → `../02-delegates-and-dependency-wiring/02-03-interfaces-vs-delegates.md`
- 05-06: "Guideline 02-05" → `../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md`
- 05-06: "Guideline 03-01" → `../03-error-handling/03-01-result-over-exceptions.md`
- 05-06: "Guideline 05-04" → `05-04-context-record-pattern.md`
- 05-06: "Guideline 05-05" → `05-05-immutable-state-threading.md`

**06-formatting rules → outbound:** none

## What Does NOT Change

- Rule content (wording, code samples, notes) — verbatim
- The H3 section group headers within a file (e.g., "## Functional Architecture Principles") — dropped since each file is now a single rule
- The intro paragraphs at the top of each current document — dropped (superseded by individual file headings)
