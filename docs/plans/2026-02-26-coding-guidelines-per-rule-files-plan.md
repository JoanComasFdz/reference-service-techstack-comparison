# Coding Guidelines Per-Rule Files Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Split each of the 6 coding guideline section files into individual rule files (one file per guideline), update all cross-references to link directly to specific rule files, and delete the original section files.

**Architecture:** Each `coding-guidelines/NN-name.md` becomes a folder `coding-guidelines/NN-name/` containing one `NN-NN-slug.md` file per rule. Rule content is extracted verbatim, headings promoted from H3 to H1, and all "Guideline XX-YY" mentions made into explicit links. See the approved design doc at `docs/plans/2026-02-26-coding-guidelines-per-rule-files-design.md`.

**Tech Stack:** Markdown files only. No code, no tests, no build system involved.

---

## Content Extraction Algorithm

> Read this once — it applies to every task below.

For each rule inside a section file:
1. **Content boundaries** — each rule starts at `### NN-NN.` and ends just before the next `### NN-NN.` (or EOF)
2. **Drop** any `---` separators that fall between rules
3. **Drop** any `## Section Group Headers` (e.g. `## Functional Architecture Principles`, `## Function Composition`) — these are document-organization headers, not rule content
4. **Promote heading** — change `### NN-NN. Rule Title` to `# NN-NN. Rule Title`
5. **Apply cross-reference updates** listed in the task — replace old document-level links and bare "Guideline XX-YY" mentions with direct rule file links

Rule files contain **nothing else** — no document header, no blockquote intro, no back-reference. Just the promoted rule heading and its content.

---

## Task 1: Create `01-core-architecture/` with 11 rule files

**Source file:** `performance-tester-dotnet/coding-guidelines/01-core-architecture.md`

**Files to create:**
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-01-static-classes.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-02-explicit-parameters.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-03-inline-single-use-code.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-04-descriptive-names.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-05-toolbox-pattern.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-06-vertical-slice.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-07-reasons-for-change.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-08-explicit-over-implicit.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-09-no-wrapper-functions.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-10-composition-over-interfaces.md`
- `performance-tester-dotnet/coding-guidelines/01-core-architecture/01-11-return-early.md`

**Step 1: Apply the extraction algorithm to all 11 rules**

For each rule, extract content per the algorithm above. The only cross-reference change in this section is in `01-09`:

In `01-09-no-wrapper-functions.md`, find:
```
**Exception — `BuildDependencies` in FP Module classes (Guideline 02-05):** A module's `BuildDependencies` factory (see [Delegates and Dependency Wiring, Guideline 02-05](02-delegates-and-dependency-wiring.md)) must exist even when it only forwards
```
Replace with:
```
**Exception — `BuildDependencies` in FP Module classes ([Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md)):** A module's `BuildDependencies` factory (see [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md)) must exist even when it only forwards
```

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/01-core-architecture/
```
Expected: 11 files (`01-01-static-classes.md` through `01-11-return-early.md`)

```bash
grep -r "01-core-architecture.md" performance-tester-dotnet/coding-guidelines/01-core-architecture/
```
Expected: no output (no old document-level links)

**Step 3: Commit**

```bash
git add performance-tester-dotnet/coding-guidelines/01-core-architecture/
git commit -m "docs(guidelines): split 01-core-architecture into per-rule files"
```

---

## Task 2: Create `02-delegates-and-dependency-wiring/` with 6 rule files

**Source file:** `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring.md`

**Files to create:**
- `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/02-01-named-delegates.md`
- `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/02-02-named-over-action-func.md`
- `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/02-03-interfaces-vs-delegates.md`
- `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/02-04-dependency-composition.md`
- `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/02-05-static-class-as-module.md`
- `performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/02-06-delegate-suffix.md`

**Step 1: Apply the extraction algorithm to all 6 rules**

Note: the section header `## Function-Typed Dependencies (Named Delegates)` above rule 02-01 is dropped. The `---` separators after rules 02-03 and 02-05 are dropped.

**Cross-reference changes:**

In `02-02-named-over-action-func.md`, find:
```
This extends **Guideline 01-04** (see [Core Architecture](01-core-architecture.md)) — Descriptive Names to function-typed parameters.
```
Replace with:
```
This extends **[Guideline 01-04](../01-core-architecture/01-04-descriptive-names.md)** — Descriptive Names to function-typed parameters.
```

In `02-05-static-class-as-module.md`, find:
```
This is explicitly exempt from Guideline 01-09 (No Wrapper Functions) (see [Core Architecture](01-core-architecture.md)).
```
Replace with:
```
This is explicitly exempt from [Guideline 01-09](../01-core-architecture/01-09-no-wrapper-functions.md) (No Wrapper Functions).
```

In `02-05-static-class-as-module.md`, find:
```
**File placement:** See Guideline 05-06 (see [State and Composition Patterns](05-state-and-composition-patterns.md)) for where Module files belong in the directory structure
```
Replace with:
```
**File placement:** See [Guideline 05-06](../05-state-and-composition-patterns/05-06-thin-shell-pattern.md) for where Module files belong in the directory structure
```

In `02-06-delegate-suffix.md`, find:
```
- Constrains **Guideline 02-01** (named delegates) — Guideline 02-01 says _when_ to use delegates; this says _how to name_ them
- Extends **Guideline 02-02** (named over Action/Func) — Guideline 02-02 says use a descriptive name; the `Delegate` suffix is part of that name
- Affects **Guideline 02-05** (static class as module) — Dependencies records benefit most from the suffix (type vs parameter disambiguation)
```
Replace with:
```
- Constrains **[Guideline 02-01](02-01-named-delegates.md)** (named delegates) — [Guideline 02-01](02-01-named-delegates.md) says _when_ to use delegates; this says _how to name_ them
- Extends **[Guideline 02-02](02-02-named-over-action-func.md)** (named over Action/Func) — [Guideline 02-02](02-02-named-over-action-func.md) says use a descriptive name; the `Delegate` suffix is part of that name
- Affects **[Guideline 02-05](02-05-static-class-as-module.md)** (static class as module) — Dependencies records benefit most from the suffix (type vs parameter disambiguation)
```

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/
```
Expected: 6 files

```bash
grep -r "02-delegates-and-dependency-wiring.md\|01-core-architecture.md\|05-state-and-composition-patterns.md" \
  performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/
```
Expected: no output

**Step 3: Commit**

```bash
git add performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring/
git commit -m "docs(guidelines): split 02-delegates-and-dependency-wiring into per-rule files"
```

---

## Task 3: Create `03-error-handling/` with 5 rule files

**Source file:** `performance-tester-dotnet/coding-guidelines/03-error-handling.md`

**Files to create:**
- `performance-tester-dotnet/coding-guidelines/03-error-handling/03-01-result-over-exceptions.md`
- `performance-tester-dotnet/coding-guidelines/03-error-handling/03-02-using-static-for-results.md`
- `performance-tester-dotnet/coding-guidelines/03-error-handling/03-03-dunet-match.md`
- `performance-tester-dotnet/coding-guidelines/03-error-handling/03-04-option-for-absence.md`
- `performance-tester-dotnet/coding-guidelines/03-error-handling/03-05-no-null-checks-in-constructors.md`

**Step 1: Apply the extraction algorithm to all 5 rules**

Note: the `---` separators after rules 03-03 and 03-04 are dropped.

**Cross-reference changes:**

In `03-01-result-over-exceptions.md`, find:
```
> **03-01 vs 04-01 — constructor guards for primitive parameters:**
```
Replace the entire note. Old text:
```
> **03-01 vs 04-01 — constructor guards for primitive parameters:** When a constructor validates a primitive parameter and throws (e.g., `if (samplingInterval <= TimeSpan.Zero) throw ...`), the preferred fix is **not** a Result-returning factory on the enclosing class. It is a **Value Object** for that parameter (Guideline 04-01). The Value Object's `Create()` returns `Result`; the constructor then receives an already-valid type and needs no guard. **Do NOT report such a throw guard as a 03-01 violation — it belongs exclusively to 04-01.**
>
> Apply a Result-returning factory on the enclosing class only when the class itself has construction failures that aren't reducible to a single constrained parameter (e.g., establishing a connection, parsing a composite configuration from multiple inputs).
>
> **03-01 vs 03-05 — null guards on DI-injected parameters:** A `?? throw new ArgumentNullException(...)` guard on a DI-injected constructor parameter is **not** a 03-01 violation — it belongs exclusively to Guideline 03-05. Do NOT report it here.
```
New text:
```
> **03-01 vs [Guideline 04-01](../04-value-objects/04-01-use-value-objects.md) — constructor guards for primitive parameters:** When a constructor validates a primitive parameter and throws (e.g., `if (samplingInterval <= TimeSpan.Zero) throw ...`), the preferred fix is **not** a Result-returning factory on the enclosing class. It is a **Value Object** for that parameter ([Guideline 04-01](../04-value-objects/04-01-use-value-objects.md)). The Value Object's `Create()` returns `Result`; the constructor then receives an already-valid type and needs no guard. **Do NOT report such a throw guard as a 03-01 violation — it belongs exclusively to [Guideline 04-01](../04-value-objects/04-01-use-value-objects.md).**
>
> Apply a Result-returning factory on the enclosing class only when the class itself has construction failures that aren't reducible to a single constrained parameter (e.g., establishing a connection, parsing a composite configuration from multiple inputs).
>
> **03-01 vs [Guideline 03-05](03-05-no-null-checks-in-constructors.md) — null guards on DI-injected parameters:** A `?? throw new ArgumentNullException(...)` guard on a DI-injected constructor parameter is **not** a 03-01 violation — it belongs exclusively to [Guideline 03-05](03-05-no-null-checks-in-constructors.md). Do NOT report it here.
```

In `03-04-option-for-absence.md`, find:
```
**Consuming `Option<T>`:** Use dunet `Match` at consumption points (branching on outcome, extracting values). Use `IsNone` / `IsSome` + early return (Guideline 01-11) in sequential pipelines, same split as `Result<T>` (Guideline 03-03).
```
Replace with:
```
**Consuming `Option<T>`:** Use dunet `Match` at consumption points (branching on outcome, extracting values). Use `IsNone` / `IsSome` + early return ([Guideline 01-11](../01-core-architecture/01-11-return-early.md)) in sequential pipelines, same split as `Result<T>` ([Guideline 03-03](03-03-dunet-match.md)).
```

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/03-error-handling/
```
Expected: 5 files

```bash
grep -r "03-error-handling.md\|01-core-architecture.md\|04-value-objects.md" \
  performance-tester-dotnet/coding-guidelines/03-error-handling/
```
Expected: no output

**Step 3: Commit**

```bash
git add performance-tester-dotnet/coding-guidelines/03-error-handling/
git commit -m "docs(guidelines): split 03-error-handling into per-rule files"
```

---

## Task 4: Create `04-value-objects/` with 5 rule files

**Source file:** `performance-tester-dotnet/coding-guidelines/04-value-objects.md`

**Files to create:**
- `performance-tester-dotnet/coding-guidelines/04-value-objects/04-01-use-value-objects.md`
- `performance-tester-dotnet/coding-guidelines/04-value-objects/04-02-value-object-structure.md`
- `performance-tester-dotnet/coding-guidelines/04-value-objects/04-03-unwrap-at-boundaries.md`
- `performance-tester-dotnet/coding-guidelines/04-value-objects/04-04-no-unit-tests.md`
- `performance-tester-dotnet/coding-guidelines/04-value-objects/04-05-value-object-families.md`

**Step 1: Apply the extraction algorithm to all 5 rules**

**Cross-reference changes:**

In `04-01-use-value-objects.md`, find:
```
the constructor receives an already-valid type and needs no guard at all (see also Guideline 03-01).
```
Replace with:
```
the constructor receives an already-valid type and needs no guard at all (see also [Guideline 03-01](../03-error-handling/03-01-result-over-exceptions.md)).
```

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/04-value-objects/
```
Expected: 5 files

```bash
grep -r "04-value-objects.md\|03-error-handling.md" \
  performance-tester-dotnet/coding-guidelines/04-value-objects/
```
Expected: no output

**Step 3: Commit**

```bash
git add performance-tester-dotnet/coding-guidelines/04-value-objects/
git commit -m "docs(guidelines): split 04-value-objects into per-rule files"
```

---

## Task 5: Create `05-state-and-composition-patterns/` with 6 rule files

**Source file:** `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns.md`

**Files to create:**
- `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/05-01-higher-order-helpers.md`
- `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/05-02-consumer-owns-defaults.md`
- `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/05-03-three-bucket-rule.md`
- `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/05-04-context-record-pattern.md`
- `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/05-05-immutable-state-threading.md`
- `performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/05-06-thin-shell-pattern.md`

**Step 1: Apply the extraction algorithm to all 6 rules**

Note: section group headers `## Function Composition` (above 05-01) and `## Lambda Binding Strategy` (above 05-03) are dropped. The `---` separator between 05-01/05-02 and 05-02/05-03 groups is dropped.

**Cross-reference changes:**

In `05-03-three-bucket-rule.md`, find:
```
- **Baking in a runtime value** forces you to delay delegate construction, breaking the clean Configure → Build → Run separation (Guideline 02-04) (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
```
Replace with:
```
- **Baking in a runtime value** forces you to delay delegate construction, breaking the clean Configure → Build → Run separation ([Guideline 02-04](../02-delegates-and-dependency-wiring/02-04-dependency-composition.md))
```

In `05-04-context-record-pattern.md`, find:
```
- Extends **Guideline 01-02** (explicit parameters) from single values to state bundles (see [Core Architecture](01-core-architecture.md))
- Used by **Guideline 05-06** (thin shell) as the state extraction technique
- For sequential code, prefer **Guideline 05-05** (immutable state threading)
```
Replace with:
```
- Extends **[Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md)** (explicit parameters) from single values to state bundles
- Used by **[Guideline 05-06](05-06-thin-shell-pattern.md)** (thin shell) as the state extraction technique
- For sequential code, prefer **[Guideline 05-05](05-05-immutable-state-threading.md)** (immutable state threading)
```

In `05-05-immutable-state-threading.md`, find:
```
- Companion to **Guideline 05-04** — same idea (explicit state), different concurrency model
- Extends **Guideline 01-01** (static classes) — static functions that transform state (see [Core Architecture](01-core-architecture.md))
- Extends **Guideline 01-02** (explicit parameters) — state is an input AND an output (see [Core Architecture](01-core-architecture.md))
```
Replace with:
```
- Companion to **[Guideline 05-04](05-04-context-record-pattern.md)** — same idea (explicit state), different concurrency model
- Extends **[Guideline 01-01](../01-core-architecture/01-01-static-classes.md)** (static classes) — static functions that transform state
- Extends **[Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md)** (explicit parameters) — state is an input AND an output
```

In `05-06-thin-shell-pattern.md`, apply these replacements in order:

**(a)** Find:
```
Guidelines 01-01 and 01-02 (see [Core Architecture](01-core-architecture.md)) say "make it static" and "pass all dependencies explicitly."
```
Replace with:
```
[Guideline 01-01](../01-core-architecture/01-01-static-classes.md) and [Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md) say "make it static" and "pass all dependencies explicitly."
```

**(b)** Find:
```
1. **Own the context** — a context record holding all mutable state (Guideline 05-04 or 05-05)
```
Replace with:
```
1. **Own the context** — a context record holding all mutable state ([Guideline 05-04](05-04-context-record-pattern.md) or [Guideline 05-05](05-05-immutable-state-threading.md))
```

**(c)** Find:
```
**Module naming** is owned by Guideline 02-05 (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md)) — it defines which classes qualify for the `Module` suffix.
```
Replace with:
```
**Module naming** is owned by [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md) — it defines which classes qualify for the `Module` suffix.
```

**(d)** Find:
```
**Combining with Guideline 02-05 (static class as module):** The module file lives in `Internal/` and follows the same co-location principle
```
Replace with:
```
**Combining with [Guideline 02-05](../02-delegates-and-dependency-wiring/02-05-static-class-as-module.md) (static class as module):** The module file lives in `Internal/` and follows the same co-location principle
```

**(e)** Find:
```
Types decorated with `[Union]` cannot be nested inside a module class — even an `internal` one. Dunet's source generator emits `public` extension methods (e.g., `Match`, `MatchAsync`) at namespace level that reference the union type in their signatures. Nesting the union inside an `internal` class causes **CS0051** (inconsistent accessibility). Place `[Union]` types at namespace level in the same module file, with a `<see cref="...Module"/>` doc comment linking them back. They are logically part of the module but structurally must remain top-level. See also Guideline 03-01 placement constraint.
```
Replace with:
```
Types decorated with `[Union]` cannot be nested inside a module class — even an `internal` one. Dunet's source generator emits `public` extension methods (e.g., `Match`, `MatchAsync`) at namespace level that reference the union type in their signatures. Nesting the union inside an `internal` class causes **CS0051** (inconsistent accessibility). Place `[Union]` types at namespace level in the same module file, with a `<see cref="...Module"/>` doc comment linking them back. They are logically part of the module but structurally must remain top-level. See also [Guideline 03-01](../03-error-handling/03-01-result-over-exceptions.md) placement constraint.
```

**(f)** Find:
```
- Applies **Guideline 05-04** (context record) or **Guideline 05-05** (immutable state) for the state extraction
- Extends **Guideline 01-01** (static classes) to cases where the class itself can't be static (see [Core Architecture](01-core-architecture.md))
- Applies **Guideline 01-02** (explicit parameters) — static functions take context + delegates, not fields (see [Core Architecture](01-core-architecture.md))
- Uses **Guideline 02-01** (named delegates) for the operations that the shell passes to static functions (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
- Follows **Guideline 02-03** (interfaces at DI boundaries, delegates internally) — the shell wires delegates to static functions (see [Delegates and Dependency Wiring](02-delegates-and-dependency-wiring.md))
```
Replace with:
```
- Applies **[Guideline 05-04](05-04-context-record-pattern.md)** (context record) or **[Guideline 05-05](05-05-immutable-state-threading.md)** (immutable state) for the state extraction
- Extends **[Guideline 01-01](../01-core-architecture/01-01-static-classes.md)** (static classes) to cases where the class itself can't be static
- Applies **[Guideline 01-02](../01-core-architecture/01-02-explicit-parameters.md)** (explicit parameters) — static functions take context + delegates, not fields
- Uses **[Guideline 02-01](../02-delegates-and-dependency-wiring/02-01-named-delegates.md)** (named delegates) for the operations that the shell passes to static functions
- Follows **[Guideline 02-03](../02-delegates-and-dependency-wiring/02-03-interfaces-vs-delegates.md)** (interfaces at DI boundaries, delegates internally) — the shell wires delegates to static functions
```

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/
```
Expected: 6 files

```bash
grep -r "05-state-and-composition-patterns.md\|02-delegates-and-dependency-wiring.md\|01-core-architecture.md\|03-error-handling.md" \
  performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/
```
Expected: no output

**Step 3: Commit**

```bash
git add performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns/
git commit -m "docs(guidelines): split 05-state-and-composition-patterns into per-rule files"
```

---

## Task 6: Create `06-formatting/` with 4 rule files

**Source file:** `performance-tester-dotnet/coding-guidelines/06-formatting.md`

**Files to create:**
- `performance-tester-dotnet/coding-guidelines/06-formatting/06-01-always-use-braces.md`
- `performance-tester-dotnet/coding-guidelines/06-formatting/06-02-blank-line-after-brace.md`
- `performance-tester-dotnet/coding-guidelines/06-formatting/06-03-all-or-nothing-params.md`
- `performance-tester-dotnet/coding-guidelines/06-formatting/06-04-expression-body-same-line.md`

**Step 1: Apply the extraction algorithm to all 4 rules**

No cross-reference changes — this section has no outbound guideline references.

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/06-formatting/
```
Expected: 4 files

**Step 3: Commit**

```bash
git add performance-tester-dotnet/coding-guidelines/06-formatting/
git commit -m "docs(guidelines): split 06-formatting into per-rule files"
```

---

## Task 7: Delete the 6 original section files

**Step 1: Delete**

```bash
rm performance-tester-dotnet/coding-guidelines/01-core-architecture.md
rm performance-tester-dotnet/coding-guidelines/02-delegates-and-dependency-wiring.md
rm performance-tester-dotnet/coding-guidelines/03-error-handling.md
rm performance-tester-dotnet/coding-guidelines/04-value-objects.md
rm performance-tester-dotnet/coding-guidelines/05-state-and-composition-patterns.md
rm performance-tester-dotnet/coding-guidelines/06-formatting.md
```

**Step 2: Verify**

```bash
ls performance-tester-dotnet/coding-guidelines/
```
Expected: 6 directories only, no `.md` files at this level

**Step 3: Commit**

```bash
git add -u performance-tester-dotnet/coding-guidelines/
git commit -m "docs(guidelines): delete original section files (replaced by per-rule folders)"
```

---

## Task 8: Update `CODING_GUIDELINES.md`

**File to modify:** `performance-tester-dotnet/CODING_GUIDELINES.md`

**Step 1: Update the routing table links**

Find and replace each of these 6 links in the routing table. Old → new:

```
[Core Architecture](coding-guidelines/01-core-architecture.md)
→
[Core Architecture](coding-guidelines/01-core-architecture/01-01-static-classes.md)
```

```
[Delegates and Dependency Wiring](coding-guidelines/02-delegates-and-dependency-wiring.md)
→
[Delegates and Dependency Wiring](coding-guidelines/02-delegates-and-dependency-wiring/02-01-named-delegates.md)
```

```
[Error Handling and Absence](coding-guidelines/03-error-handling.md)
→
[Error Handling and Absence](coding-guidelines/03-error-handling/03-01-result-over-exceptions.md)
```

```
[Value Objects](coding-guidelines/04-value-objects.md)
→
[Value Objects](coding-guidelines/04-value-objects/04-01-use-value-objects.md)
```

```
[State and Composition Patterns](coding-guidelines/05-state-and-composition-patterns.md)
→
[State and Composition Patterns](coding-guidelines/05-state-and-composition-patterns/05-01-higher-order-helpers.md)
```

```
[Formatting Rules](coding-guidelines/06-formatting.md)
→
[Formatting Rules](coding-guidelines/06-formatting/06-01-always-use-braces.md)
```

**Step 2: Update the Document Overview section links**

The Document Overview section has the same 6 links in a different format (`### [01 - Core Architecture](coding-guidelines/01-core-architecture.md)`). Apply the same 6 replacements as above.

**Step 3: Verify**

```bash
grep "coding-guidelines/0" performance-tester-dotnet/CODING_GUIDELINES.md
```
Expected: all matches show paths with a subfolder (e.g., `coding-guidelines/01-core-architecture/01-01-...`), none pointing at a bare `.md` file in `coding-guidelines/`.

**Step 4: Commit**

```bash
git add performance-tester-dotnet/CODING_GUIDELINES.md
git commit -m "docs(guidelines): update CODING_GUIDELINES.md links to point to per-rule files"
```

---

## Task 9: Final verification

**Step 1: Check for any remaining stale links**

```bash
grep -r "\](0[1-6]-[a-z].*\.md)" performance-tester-dotnet/coding-guidelines/
```
Expected: no output (all old `](...section-file.md)` patterns gone from rule files)

```bash
grep -r "coding-guidelines/0[1-6]-[a-z][^/]" performance-tester-dotnet/CODING_GUIDELINES.md
```
Expected: no output (no bare section-file links in main index)

**Step 2: Spot-check a cross-reference link**

```bash
grep "02-05-static-class-as-module" performance-tester-dotnet/coding-guidelines/01-core-architecture/01-09-no-wrapper-functions.md
```
Expected: a line containing the link

**Step 3: Verify total file count**

```bash
find performance-tester-dotnet/coding-guidelines -name "*.md" | wc -l
```
Expected: `37`
