---
name: playwright-e2e
description: How to write and run the Playwright suite in e2e/, including page-object conventions, shared-server state hazards, and why a green run is not evidence the UI is right. Use when adding or changing end-to-end tests or the wwwroot UI.
---

# End-to-end tests

```bash
cd e2e && npm install && npx playwright install chromium && npm test
npx playwright test tests/refusals.spec.ts          # one spec
npm run test:headed                                  # watch it happen
npm run report                                       # last HTML report
```

`playwright.config.ts` starts the API itself (`webServer`) with `reuseExistingServer: true`, and
runs with **one worker**. There is no need to start the app by hand.

## Layout

- `e2e/pages/` — page objects. `LoginPage`, `InvoicesPage`, `ApprovalsPage`, plus `demoUsers.ts`.
- `e2e/tests/` — specs, one per rule area.

Specs never touch locators directly; they go through a page object.

---

## The seven traps

### 1. CSS or XPath selectors

House rule, no exceptions: `getByRole`, `getByLabel`, `getByText`. `filter({ hasText })` is fine
for picking one row or list item out of many.

If an element is not reachable by role, **add the role to the markup** rather than reaching for a
CSS selector. `#approver-note` gained `role="status"` for exactly this reason; refusal messages
use `role="note"`; the error banner uses `role="alert"`.

### 2. Relying on seeded invoices

One in-memory server serves the whole run and **state persists between specs**. A spec that
approves `INV-1007` changes what the next spec sees.

Create your own data:

```ts
const customer = `Above Manager Limit ${Date.now()}`;
const id = await invoices.createAndSubmit(customer, 200000);
```

The timestamp suffix keeps retries and reruns from colliding.

### 3. Hardcoded demo passwords

Never. `pages/demoUsers.ts` reads them from `appsettings.json` at runtime, so there is one place
they live and the suite cannot drift from the app. The commit guard also blocks credential-shaped
literals.

### 4. Asserting status codes instead of visible text

The whole point of the refusal rules is that **a person is told why**. A spec that checks a 403
would pass against a UI that shows the user nothing.

```ts
await expect(approvals.refusalFor(customer)).toContainText('50,000.00');
await expect(approvals.itemFor(customer)).toContainText('1 of 2 - needs a Director');
```

Assert the text someone would read. Then confirm it is load-bearing by replacing the real message
with something vague and watching these specs fail.

### 5. Letting the approver under test also be the submitter

Nobody may approve what they submitted. If `demo.manager` creates the invoice *and* tries to
approve it, the attempt is refused — for the **wrong reason**, and the spec passes while proving
nothing about the rule you meant to test.

Raise invoices as `demo.admin`, who cannot approve anything anyway, then switch user.

### 6. Trusting a green run without looking at the page

This bit us for real. The suite was fully green while **"Sign out" was visible to signed-out
visitors** — a CSS `display:flex` on the session bar was overriding the `hidden` attribute. No
assertion covered it; it was only found by opening the page and looking.

So: after a UI change, **open the app and look at it**, or take a Playwright snapshot. Then turn
whatever you find into an assertion. The guard against that specific class of bug now lives in
`LoginPage.signInAs`, so every spec checks it.

Two related gotchas when looking manually:

- The browser **caches `styles.css`**. If a CSS fix seems not to work, confirm with
  `curl -s http://localhost:5000/styles.css | head` before concluding anything.
- `[hidden] { display: none !important; }` is in the stylesheet deliberately. Any `display` rule
  on an element the script hides would otherwise silently beat the attribute.

### 7. `import.meta.url`

Playwright transpiles specs to CommonJS, so `import.meta` is unavailable. Use `__dirname`:

```ts
const settingsPath = join(__dirname, '../../backend/src/InvoiceApp.Api/appsettings.json');
```

---

## Checklist for a new spec

- Does it create its own invoice, with a unique customer name?
- Is the approver a different person from the submitter?
- Does it assert on text a user would read, not a code or a test id?
- Does it go through a page object, with no raw locator in the spec?
- Have you broken the rule and watched *this* spec fail?
