# Quickstart: Validating Approval Limits by Role

## Prerequisites

.NET 8 SDK. Demo accounts and password are in `backend/src/InvoiceApp.Api/appsettings.json`.

## Automated

```bash
dotnet test backend/InvoiceApp.sln
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~ApprovalPolicyTests
```

Expected: all green. Each refusal test shows `UpdateCount == 0` and an unchanged `Version`.

Mutation check (must not fall below `docs/mutation/before.txt`):

```bash
cd backend/src/InvoiceApp.Domain && dotnet stryker
```

## Manual, against the running app

```bash
dotnet run --project backend/src/InvoiceApp.Api   # http://localhost:5000
```

| # | As | Action | Expected |
|---|---|---|---|
| 1 | demo.manager | approve INV-1006 (200,000) | 403 `AboveApprovalLimit`, message names 50,000.00 |
| 2 | demo.director | approve INV-1006 | 200, Approved |
| 3 | demo.manager | approve INV-1008 (600,000) | 200, still PendingApproval, one signature |
| 4 | demo.manager | approve INV-1008 again | 403 `DuplicateApprover` |
| 5 | demo.director | approve INV-1008 | 200, Approved (second signature, Director) |
| 6 | demo.admin | approve or reject anything, even `INV-0000`, with no body | 403 `RoleCannotApprove` |
| 7 | demo.manager | approve INV-1009 (second signature needed, first by demo.manager) | 403 `DuplicateApprover` |
| 8 | demo.director | reject INV-1004 with a blank reason | 400 |
| 9 | demo.director | reject INV-1003 (already Approved) with a blank reason | 409 |

After any 403: re-read the invoice and confirm `Version` and all decision fields are unchanged.
