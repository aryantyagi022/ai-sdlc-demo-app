# Context experiment — after

Same prompt, same read-only flags as `before.md`, run on the commit that carries the extended
`AGENTS.md`.

## How it was run

| | |
| --- | --- |
| Worktree | `/tmp/agents-context-after` (`git worktree add /tmp/agents-context-after HEAD`) |
| Commit | `ca90faa15509bb4bfac808127b3b61b984e1cf3c` (`feature/approval-limits-by-role`) |
| Working tree | Clean before the run. After it, only `ai-logs/ai-interactions.jsonl` changed (+5 lines) |
| Instructions loaded | `AGENTS.md`, `CLAUDE.md` (confirmed with `copilot instruction list`) |
| Writes | Denied — `--excluded-tools "write,edit,create,apply_patch,bash,str_replace_editor"` |
| Command | `copilot -p "<prompt>" --allow-all-tools --excluded-tools "..." --silent` |

Differences from the before run: this commit also carries `.github/skills`, `.github/agents` and
`.github/hooks`, and the answer cites a skill. The hooks wrote five lines to
`ai-logs/ai-interactions.jsonl` inside the worktree, so "clean after" does not hold for the log file;
no source file changed.

## Prompt

> Can a Manager approve an invoice of 75,000? What happens to the invoice if they try, and which
> command runs the tests? Cite files.

## Answer

**No.** A Manager's limit is 50,000, so 75,000 falls in the 50,000.01–500,000 band, which only a Director can approve (`AGENTS.md`, "Approval rules" table; `.github/skills/approval-rules/SKILL.md:22`).

**What happens to the invoice:** nothing. The refusal is thrown as `ApprovalRefusedException` with reason `AboveApprovalLimit` (`backend/src/InvoiceApp.Domain/ApprovalService.cs:133`, `ApprovalRefusal.cs:13`). The API returns 403 with `{ error, reason }`, and the message names the limit. `Status`, `Approvals`, `DecidedBy`, `DecidedAt`, `DecisionNote` and `Version` stay unchanged, because the checks run before any mutation (`AGENTS.md`, "Conventions" and "How a refusal surfaces").

Tests for this case:
- `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs:130,152,359`
- `backend/tests/InvoiceApp.Tests/ApprovalsControllerTests.cs:100`

I took the behaviour from the docs and the reason-code lines in the code and tests. I didn't run the tests or read the full `ApprovalService` logic.

**Run the tests:** `dotnet test backend/InvoiceApp.sln` (`AGENTS.md`, "Commands"; `README.md:31`). To run only the approval tests, add `--filter FullyQualifiedName~ApprovalServiceTests`.
