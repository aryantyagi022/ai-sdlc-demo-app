import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/LoginPage';
import { InvoicesPage } from '../pages/InvoicesPage';
import { ApprovalsPage } from '../pages/ApprovalsPage';
import { ADMIN, DIRECTOR, MANAGER } from '../pages/demoUsers';

/** Rules 1 and 2: a Manager may approve up to 50,000, a Director up to 500,000. */
test.describe('single-approver limits', () => {
  // Invoices are raised by the Admin so that the approver under test is never the submitter,
  // which would refuse the attempt for the wrong reason.
  const unique = (label: string) => `${label} ${Date.now()}`;

  test('a Manager approves an invoice inside their limit', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Within Manager Limit');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 49999.99);

    await login.switchTo(MANAGER);
    await approvals.open();
    await approvals.approve(id);

    await invoices.open();
    const details = await invoices.detailsFor(customer);
    expect(details.status).toBe('Approved');
    expect(details.approvals).toBe('1 of 1');
  });

  test('the Manager limit is inclusive at exactly 50,000', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Exactly At Manager Limit');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 50000);

    await login.switchTo(MANAGER);
    await approvals.open();
    await approvals.approve(id);

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Approved');
  });

  test('a Manager is told the limit when the invoice is above it', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Above Manager Limit');

    await login.signInAs(ADMIN);
    await invoices.createAndSubmit(customer, 200000);

    await login.switchTo(MANAGER);
    await approvals.open();

    // The reason is on screen before anything is clicked, and the button cannot be pressed.
    await expect(approvals.refusalFor(customer)).toContainText('50,000.00');
    await expect(approvals.itemFor(customer).getByRole('button', { name: /^Approve/ })).toBeDisabled();

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Pending Approval');
  });

  test('a Director approves the same invoice a Manager could not', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Director Territory');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 200000);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(id);

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Approved');
  });

  test('an Admin is told their role cannot approve and is given no controls', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Admin Sees No Buttons');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 1000);
    await approvals.open();

    await expect(approvals.roleNote).toContainText('cannot approve or reject');
    await expect(approvals.approveButtonFor(id)).toHaveCount(0);
    await expect(approvals.rejectButtonFor(id)).toHaveCount(0);
  });
});
