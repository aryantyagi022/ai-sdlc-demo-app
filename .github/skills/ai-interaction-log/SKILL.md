---
name: ai-interaction-log
description: Writes one ai-logs/ai-interactions.jsonl entry summarising a completed task. Use at the end of any task that changed files, ran a tool chain, or produced a decision, and when backfilling work done before the logging hooks were loaded.
---

# AI interaction log

The `postToolUse` hooks already log every individual tool call. This skill adds the
**task-level** entry: one record describing the task as a whole, so the log can be read
without replaying hundreds of tool calls.

## When to use

Write exactly **one** entry per task, after the work is finished and verified:

- a feature, fix, or refactor is complete
- an investigation or review has produced a conclusion
- a setup or configuration task has finished
- you are backfilling work done before the hooks were loaded

Do **not** write an entry per tool call or per file.

## How to write an entry

Run the helper. It reuses the hook library, so the schema, redaction, and file locking
match the automatic records exactly.

```bash
.github/skills/ai-interaction-log/scripts/append-entry.sh \
  --stage implementation \
  --request "Add per-role approval limits." \
  --suggestion "Added ApprovalLimit to ApprovalService and rejected over-limit approvals." \
  --verification "dotnet test backend/InvoiceApp.sln - 11 passed, 0 failed." \
  --tools "view,edit,bash" \
  --artifacts "backend/src/InvoiceApp.Domain/ApprovalService.cs" \
  --status applied
```

Run `append-entry.sh --help` for the full option list.

## Rules

1. **`human_decision` is verbatim or absent.** Pass `--human-decision` only with the
   user's own words, copied exactly. If the user did not decide anything, omit the flag
   and the field stays `null`. Never summarise, paraphrase, or invent a decision.
2. **`--verification` must describe a real check** that was actually run: a test command
   and its result, a build, or sample output. If nothing was verified, say so plainly.
3. **Record limitations honestly.** Use `--limitation` for anything that did not work,
   was skipped, or was out of scope. A clean log is not the goal; an accurate one is.
4. **Keep `--request` close to what the user asked for**, not a reinterpretation.
5. **Never pass real credentials.** Redaction runs on write, but do not rely on it.

## Fields

| Field | Source |
| --- | --- |
| `stage` | `--stage`, else the tracked `Stage:` value |
| `request` | What the user asked for |
| `suggestion` | What the AI did or proposed |
| `human_decision` | The user's exact words, or `null` |
| `action_verification` | How the result was checked |
| `limitation` | What did not work, or `null` |
| `status` | `applied`, `rejected`, `failed`, or `partial` |
| `artifacts` | Files created or changed |

## Checking your entry

```bash
tail -1 ai-logs/ai-interactions.jsonl | jq .
```
