# Context experiment — before

Does `AGENTS.md` actually change the answer? This is the **before** run: a clean checkout of
`main`, with no uncommitted or untracked files, asked a question whose answer is only partly in
the code.

## How it was run

| | |
| --- | --- |
| Worktree | `/tmp/agents-context-before` (`git worktree add /tmp/agents-context-before main`) |
| Commit | `66cb3f9005af1feafb7890f06bbc41fd652b432a` |
| Working tree | Clean — `git status --short` empty before and after |
| Instructions loaded | `AGENTS.md`, `CLAUDE.md` (confirmed with `copilot instruction list`) |
| Writes | Denied — `--excluded-tools "write,edit,create,apply_patch,bash,str_replace_editor"` |
| Command | `copilot -p "<prompt>" --allow-all-tools --excluded-tools "..." --silent` |

The worktree was still clean after the run, so the write denial held.

Note that `main` carries no `.github/skills` or `.github/hooks` — only `.github/workflows` — so no
skills, hooks or logging ran. The only context beyond the source itself was `AGENTS.md` and
`CLAUDE.md`.

## Prompt

> Can a Manager approve an invoice of 75,000? What happens to the invoice if they try, and which
> command runs the tests? Cite files.

## Answer

No. A Manager can't approve an invoice of 75,000, or of any other amount.

- **Who can approve:** `ApprovalsController` is marked `[RequireRole("Approver", "Admin")]` (`backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs:9`). The approve endpoint is `POST api/approvals/{invoiceId}/approve`. `AGENTS.md:17` states the same rule: approvals are Approver or Admin.
- **What happens to the invoice:** `RequireRoleAttribute` returns 403 Forbidden for any other role (`backend/src/InvoiceApp.Api/Auth/RequireRoleAttribute.cs:6,23`). The filter runs before the action, so `ApprovalService.Approve` is never called. The invoice keeps its current status, for example PendingApproval. If the Manager isn't signed in at all, `SessionAuthMiddleware.cs:29` returns 401 instead.
- **Amount rules:** I found no amount threshold or "Manager" role anywhere in the code. I didn't open `ApprovalService`, so I haven't checked it for amount limits.
- **Run the tests:** `dotnet test backend/InvoiceApp.sln` (`AGENTS.md`, Commands section). The Playwright end-to-end tests run with `cd e2e && npm install && npx playwright install chromium && npm test`.


## What this shows

1. **`AGENTS.md` is load-bearing, not decoration.** Two of the four claims cite it directly:
   the role rule (`AGENTS.md:17`) and the test command, which appears *only* in `AGENTS.md` and
   `README.md` — it is not inferable from the source tree.
2. **The role answer is correct for `main`.** There is no `Manager` role on this commit;
   `ApprovalsController` is `[RequireRole("Approver", "Admin")]`, so a Manager is refused by the
   filter before `ApprovalService` runs.
3. **It reported its own gap.** It flagged that it had not opened `ApprovalService` and so could not
   rule out amount limits — which happens to be correct: on `main` there are none.
4. **"Changes nothing" is already true here, for the wrong reason.** The invoice is untouched
   because the role filter rejects the request, not because the domain refuses it.

This is the baseline to compare against once the multi-level approval work lands, when the honest
answer becomes "yes, a Manager may start the chain but cannot approve 75,000 alone".
