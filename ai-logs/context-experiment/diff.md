# Context experiment — before vs after

Prompt (identical): *Can a Manager approve an invoice of 75,000? What happens to the invoice if they
try, and which command runs the tests? Cite files.*

| | Before (`66cb3f9`, `main`) | After (`ca90faa`, feature branch) |
| --- | --- | --- |
| **AGENTS.md** | 38 lines | 158 lines |
| **Can a Manager approve 75,000?** | "No — a Manager can't approve any amount." | "No — 75,000 is in the 50,000.01–500,000 band, Director only." |
| **Why** | `[RequireRole("Approver", "Admin")]` on the controller; no `Manager` role exists on `main` | Manager limit is 50,000; amount band needs a Director (`AGENTS.md` table, `approval-rules` skill) |
| **Is the amount considered?** | No. States it found no amount threshold, and says it did not open `ApprovalService` | Yes. Names the limit and the band |
| **Refusal mechanism** | Route filter returns 403 before the service runs; 401 if not signed in | `ApprovalRefusedException` / `AboveApprovalLimit` from the domain, mapped to 403 `{ error, reason }` |
| **What happens to the invoice** | Untouched, because the filter never reaches the service | Untouched; lists `Status`, `Approvals`, `DecidedBy`, `DecidedAt`, `DecisionNote`, `Version` and says checks run before any mutation |
| **Code citations** | `ApprovalsController.cs:9`, `RequireRoleAttribute.cs:6,23`, `SessionAuthMiddleware.cs:29` | `ApprovalService.cs:133`, `ApprovalRefusal.cs:13`, plus test files and line numbers |
| **Doc citations** | `AGENTS.md:17`, `AGENTS.md` Commands | `AGENTS.md` (3 sections), `.github/skills/approval-rules/SKILL.md:22`, `README.md:31` |
| **Test command** | `dotnet test backend/InvoiceApp.sln` (+ e2e command) | `dotnet test backend/InvoiceApp.sln` (+ single-class `--filter` form); no e2e command |
| **Stated limits** | Did not open `ApprovalService` | Did not run tests or read the full `ApprovalService` logic |

## Caveats on the comparison

- The two commits differ in more than `AGENTS.md`: the after commit also has the approval code,
  `.github/skills`, agents and hooks. The after answer cites a skill, so the improvement is not
  attributable to `AGENTS.md` alone.
- The before answer was correct for its commit; the roles and limits did not exist yet. The
  comparison shows how much of the answer comes from context, not that the before answer was wrong.
- Single run each, no repeats, so wording and citation choices may vary between runs.
- The after run's hooks appended five lines to `ai-logs/ai-interactions.jsonl` in its worktree.

## Score (human)

| Run | Score |
| --- | --- |
| Before | 3 / 5 |
| After | 4.3 / 5 |

Reason: "It does rejection and the reasoning is more elaborate and concrete."
