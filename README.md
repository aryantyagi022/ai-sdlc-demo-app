# Invoice Approval Demo App

A small invoice approval app used for live AI-in-SDLC demos. It is deliberately simple: a .NET 8 API, a plain web UI, and an in-memory store, so it runs with one command and no database.

## Run it

```
dotnet run --project backend/src/InvoiceApp.Api
```

Open http://localhost:5000 and sign in:

| User | Password | Role |
| --- | --- | --- |
| `demo.manager` | `ChangeMe123!` | Manager (approves up to 50,000) |
| `demo.director` | `ChangeMe123!` | Director (approves up to 500,000) |
| `demo.director2` | `ChangeMe123!` | Director (a second Director, for the two-approver rule) |
| `demo.admin` | `ChangeMe123!` | Admin (administrative only; cannot approve or reject) |

These are demo accounts for a local, in-memory app. They are not real credentials.

## What it does

1. Create an invoice (it starts as a draft).
2. Submit it for approval.
3. A Manager or Director approves it, or rejects it with a reason.

Invoices move through Draft, Pending Approval, then Approved or Rejected.

### Approval limits

Limits come from `Approval:RoleLimits` in `backend/src/InvoiceApp.Api/appsettings.json`.

| Amount | Who can approve | Signatures |
| --- | --- | --- |
| up to 50,000 | Manager or Director | 1 |
| 50,000.01 to 500,000 | Director only | 1 |
| above 500,000 | first: Manager or Director; second: a different Director | 2 |

The submitter cannot approve their own invoice, and the same user cannot sign one invoice twice. A refused attempt returns a reason and changes nothing.

## Tests

```
dotnet test backend/InvoiceApp.sln
```

## Project layout

```
backend/
  src/InvoiceApp.Domain   business rules
  src/InvoiceApp.Api      controllers, sign-in, web UI (wwwroot)
  tests/InvoiceApp.Tests  xUnit tests
docs/use-cases            short feature briefs
AGENTS.md                 conventions for AI coding agents
```

## Use cases

- [UC-1 Bulk invoice approval](docs/use-cases/UC-1-bulk-approval.md)
- [UC-2 Partial approval](docs/use-cases/UC-2-partial-approval.md)
