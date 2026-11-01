# Reviewer evaluation: 12 runs of `pr-reviewer`

Evidence for the claim that the `pr-reviewer` agent catches planted defects and leaves a harmless change alone. Raw material is preserved unedited in this folder; this page only summarises it.

| Path | Content |
|---|---|
| `diffs/diff-1..4.patch` | The four patches given to the reviewer |
| `runs/R01..R12.md` | The 12 reviewer outputs, copied verbatim |
| `answer-key.md` | The answer key, copied verbatim. That it was written before the runs is **not verifiable** from the files: they carry no timestamps. |

Audit note: the copies were compared byte for byte (`cmp`) against the original working files and are identical. This README was then checked line by line against all 12 runs, the four patches and the key. Items marked **[unverified]** are not supported by those files.

## Method

- Base commit: `e4f60c8` on `feature/approval-limits-by-role`.
- Four patches: one clean rename and three with one planted bug each. Each patch was applied only to a throwaway clone outside the repository, never to the feature branch.
- Three runs per patch, 12 runs in total. Each run file header names a distinct agent id and `pr-reviewer`; whether each agent was started fresh is **[unverified]**.
- Each reviewer received the diff text inline. The run files confirm this ("the diff you pasted") and the key states it. The exact prompt, "Review this PR against our checklist. Report findings by severity with file and line, suggest a fix for each, and say what you are unsure about.", is **[unverified]**: it comes from the session record, not from any file in this folder.
- `pr-reviewer` has no shell. No run executed a build or a test. The working tree it could read does **not** contain the change, so findings come from reading the diff and the current code.
- The run-to-diff mapping is in `answer-key.md`.

## Diffs and runs

| Diff | Change | Planted defect | Runs (repeat 1, 2, 3) |
|---|---|---|---|
| diff-1 | Rename local `refusal` to `approvalRefusal` in `ApprovalService.EligibilityFor` | None (clean) | R08, R11, R02 |
| diff-2 | `IsSameUser` uses `OrdinalIgnoreCase` | Breaks case-sensitive username rule (FR-012) | R03, R01, R07 |
| diff-3 | `AboveApprovalLimit` check uses `>=` instead of `>` | Breaks inclusive limits | R09, R06, R05 |
| diff-4 | `Reject` calls `Find` before `RefuseIfNotAuthorised` | Breaks authority-before-lookup | R04, R10, R12 |

## Scoring

The answer key proposed: bug diffs scored caught (exact defect and the rule it breaks named), partial (line flagged for the wrong reason) or missed; the clean diff scored clean or false positive.

The scoring was done by the repository owner, not by an automated check. Their words, given in chat (not recorded in any AI log): "My assessment is 2/2 for every run: all 9 planted bugs were detected, and all 3 clean-diff reviews correctly identified the harmless rename."

## Results

| Diff | Runs | Outcome (from the run files) |
|---|---|---|
| diff-1 (clean) | R08, R11, R02 | Each reported Info-level notes only and called the rename behaviour-neutral. No defect reported. |
| diff-2 (username case) | R03, R01, R07 | Each reported a High finding at `ApprovalPolicy.cs` around line 136-137 and tied it to the ordinal username rule. R01, R03 and R07 all noted that the doc comment now contradicts the code. R01 and R07 named existing tests (`ApprovalPolicyTests.cs:222-231`, `ApprovalServiceTests.cs:304-312`) that they predict would fail. R03 did not find the tests and inferred the failure only from `docs/testing/phase-b-break-rules.txt:22`. |
| diff-3 (`>=`) | R09, R06, R05 | R09, R06 and R05 each reported a Critical finding at about `ApprovalPolicy.cs:115`. R09 also reported that exactly 500,000.00 would then be approvable by no one, because the two-approver check still uses `>`. R05 and R06 reported the same inconsistency. |
| diff-4 (order) | R04, R10, R12 | Each reported a High or Critical finding at `ApprovalService.cs:66-68`: an unauthorised role such as Admin would get 404 for a missing invoice instead of 403, so it could probe which ids exist. R04, R10 and R12 all noted that `Approve` and `Reject` would then disagree. R04 and R10 noted the refusal log would miss the attempt (R10 rated this as inferred). R04, R10 and R12 predicted `ApprovalServiceTests.cs` tests would fail; none ran them. |

Audit check of the run files: in all 9 bug runs the reviewer named the changed line and the rule it breaks; in all 3 clean runs the only findings are Info and the verdict is that the change is sound. Severities are as the reviewers rated them.

Result as scored: 12 of 12 runs correct (9 of 9 planted bugs detected, 3 of 3 clean reviews). The score is the owner's chat statement **[unverified]** beyond the audit check above, which supports it.

## Limitations

- Small sample: 12 runs, four small single-hunk diffs. 12 of 12 is not a miss rate and does not predict performance on larger changes.
- The rules the diffs break are written down in `AGENTS.md` and the spec, which the reviewer can read. This tests rule-following, not discovery of unwritten rules.
- Scoring was by one person, and the 2/2 figure exists only in chat. Whether they had the key in hand is **[unverified]**.
- No run executed tests. Statements that existing tests "would fail" are inferred from reading, not observed.
- R02 and R11 (both clean-diff runs) said they did not open `AGENTS.md` or the spec. R02, R08, R09, R10, R11 and R12 said the `pr-self-review` skill was unavailable to them, so they used the `AGENTS.md` checklist or their prompt instead. Reviewers saw only the single hunk, not a full PR.
- The patches are synthetic: per the key, each was applied to a `/tmp` clone only, so none is a real regression. Who authored them is **[unverified]**.
- Only the one-line summaries above are interpretations; read `runs/` for the full text.
