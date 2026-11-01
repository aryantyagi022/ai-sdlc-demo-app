import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/LoginPage';
import { InvoicesPage } from '../pages/InvoicesPage';
import { ApprovalsPage } from '../pages/ApprovalsPage';
import { ADMIN, DIRECTOR, MANAGER } from '../pages/demoUsers';

/**
 * Rules 4 and 5: the same person may never supply both signatures on one invoice, and a refused
 * attempt explains itself and leaves the invoice alone. Every assertion here is on text a person
 * would read on screen.
 */
test.describe('refusals', () => {
  const unique = (label: string) => `${label} ${Date.now()}`;

  test('one Director cannot sign the same invoice twice, but may sign a different one', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const firstCustomer = unique('Signed Once Already');
    const secondCustomer = unique('A Different Large Invoice');

    await login.signInAs(ADMIN);
    const firstId = await invoices.createAndSubmit(firstCustomer, 800000);
    const secondId = await invoices.createAndSubmit(secondCustomer, 800000);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(firstId);

    // Rule 4: the second signature on *this* invoice is closed to them, and it says why.
    await expect(approvals.refusalFor(firstCustomer)).toContainText('already approved');
    await expect(
      approvals.itemFor(firstCustomer).getByRole('button', { name: /^Approve/ })
    ).toBeDisabled();

    // Rule 4 is per-invoice: the very same Director, in the very same session, may start
    // another large invoice. Approving one invoice places no restriction on any other.
    await approvals.approve(secondId);
    await expect(approvals.itemFor(secondCustomer)).toContainText('1 of 2 - needs a Director');

    await invoices.open();
    expect((await invoices.detailsFor(firstCustomer)).approvals).toBe('1 of 2 - needs a Director');
    expect((await invoices.detailsFor(secondCustomer)).approvals).toBe('1 of 2 - needs a Director');
  });

  test('nobody can approve an invoice they submitted themselves', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('My Own Submission');

    await login.signInAs(MANAGER);
    await invoices.createAndSubmit(customer, 1000);
    await approvals.open();

    await expect(approvals.refusalFor(customer)).toContainText('submitted');
    await expect(approvals.itemFor(customer).getByRole('button', { name: /^Approve/ })).toBeDisabled();

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Pending Approval');
  });

  test('a rejection without a reason is refused and the invoice is untouched', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Reject Without Saying Why');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 2500);

    await login.switchTo(MANAGER);
    await approvals.open();
    await approvals.reject(id, '   ');

    await expect(approvals.decisionError).toContainText('reason');

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Pending Approval');
  });

  test('a Manager may reject a large invoice alone, and the first approval is kept', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Rejected After One Signature');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 900000);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(id);

    // Rejecting needs no limit and no second signature, and it does not erase the history.
    await login.switchTo(MANAGER);
    await approvals.open();
    await approvals.reject(id, 'Duplicate of an earlier invoice.');

    await invoices.open();
    const details = await invoices.detailsFor(customer);
    expect(details.status).toBe('Rejected');
    expect(details.approvals).toBe('1 of 2');
  });

  test('an over-limit approval the page wrongly offered is refused by the server and the page shows why', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Offered In Error');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 200000);

    // Simulate a stale or tampered page: the invoice list claims this Manager may approve.
    // Authority is the server's, so the click must still be turned down.
    await page.route('**/api/invoices', async (route) => {
      if (route.request().method() !== 'GET') return route.continue();
      const response = await route.fetch();
      const list = await response.json();
      for (const invoice of list) {
        if (invoice.id === id) invoice.yourApproval = { canApprove: true, explanation: '' };
      }
      await route.fulfill({ response, json: list });
    });

    await login.switchTo(MANAGER);
    await approvals.open();
    await expect(approvals.approveButtonFor(id)).toBeEnabled();
    await approvals.approve(id);

    await expect(approvals.decisionError).toContainText('Manager may approve up to 50,000.00');
    await expect(approvals.decisionError).toContainText('200,000.00');

    await invoices.open();
    const details = await invoices.detailsFor(customer);
    expect(details.status).toBe('Pending Approval');
    expect(details.approvals).toBe('0 of 1');
  });

  test('a second window of the same Director is refused as a duplicate and the invoice keeps one signature', async ({ page, browser }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Signed In Another Window');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 800000);

    // Two windows for the same Director, both loaded while the invoice is still unsigned.
    await login.switchTo(DIRECTOR);
    await approvals.open();
    await expect(approvals.approveButtonFor(id)).toBeEnabled();

    const otherWindow = await browser.newPage();
    const otherLogin = new LoginPage(otherWindow);
    const otherApprovals = new ApprovalsPage(otherWindow);
    await otherLogin.signInAs(DIRECTOR);
    await otherApprovals.open();
    await expect(otherApprovals.approveButtonFor(id)).toBeEnabled();

    await approvals.approve(id);
    await expect(approvals.itemFor(customer)).toContainText('1 of 2 - needs a Director');

    // The other window still offers Approve; the server refuses and the window says why.
    await otherApprovals.approve(id);
    await expect(otherApprovals.decisionError).toContainText(`demo.director has already approved invoice ${id}`);

    await invoices.open();
    expect((await invoices.detailsFor(customer)).approvals).toBe('1 of 2 - needs a Director');
    await otherWindow.close();
  });

  test('an earlier error message is cleared once the next attempt succeeds', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Error Then Success');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 2500);

    await login.switchTo(MANAGER);
    await approvals.open();
    await approvals.reject(id, '   ');
    await expect(approvals.decisionError).toContainText('reason');

    await approvals.approve(id);
    await expect(approvals.decisionError).toBeHidden();
    await expect(approvals.emptyMessage).toBeHidden();
  });

  test('a reviewer sees who submitted an invoice and who has signed it so far', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Visible History');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 900000);

    await login.switchTo(MANAGER);
    await approvals.open();
    await expect(approvals.submittedByFor(customer)).toHaveText('Submitted by demo.admin');
    await expect(approvals.approvedByFor(customer)).toHaveCount(0);
    await approvals.approve(id);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await expect(approvals.submittedByFor(customer)).toHaveText('Submitted by demo.admin');
    await expect(approvals.approvedByFor(customer)).toHaveText('Approved by demo.manager (Manager)');
  });
});
