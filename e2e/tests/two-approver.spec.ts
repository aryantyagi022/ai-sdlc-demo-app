import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/LoginPage';
import { InvoicesPage } from '../pages/InvoicesPage';
import { ApprovalsPage } from '../pages/ApprovalsPage';
import { ADMIN, DIRECTOR, MANAGER, SECOND_DIRECTOR } from '../pages/demoUsers';

/** Rule 3: above 500,000 two different approvers sign, and the second must be a Director. */
test.describe('two-approver rule', () => {
  const unique = (label: string) => `${label} ${Date.now()}`;

  test('a Manager starts the chain and a Director finishes it', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Two Signatures');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 900000);

    await login.switchTo(MANAGER);
    await approvals.open();
    await approvals.approve(id);

    // Half-signed: still pending, and the screen says what is missing.
    await expect(approvals.itemFor(customer)).toContainText('1 of 2 - needs a Director');
    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Pending Approval');

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(id);

    await invoices.open();
    const details = await invoices.detailsFor(customer);
    expect(details.status).toBe('Approved');
    expect(details.approvals).toBe('2 of 2');
  });

  test('two Directors can sign, so the first signature is not reserved to Managers', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Director Then Director');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 750000);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(id);
    await expect(approvals.itemFor(customer)).toContainText('1 of 2 - needs a Director');

    await login.switchTo(SECOND_DIRECTOR);
    await approvals.open();
    await approvals.approve(id);

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Approved');
  });

  test('a Manager cannot supply the second signature', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('Second Must Be Director');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 600001);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(id);

    // A different Manager is distinct from the first approver, but still the wrong role.
    await login.switchTo(MANAGER);
    await approvals.open();
    await expect(approvals.refusalFor(customer)).toContainText('Director');
    await expect(approvals.itemFor(customer).getByRole('button', { name: /^Approve/ })).toBeDisabled();

    await invoices.open();
    const details = await invoices.detailsFor(customer);
    expect(details.status).toBe('Pending Approval');
    expect(details.approvals).toBe('1 of 2 - needs a Director');
  });

  test('a large invoice cannot be approved by one Director alone', async ({ page }) => {
    const login = new LoginPage(page);
    const invoices = new InvoicesPage(page);
    const approvals = new ApprovalsPage(page);
    const customer = unique('One Director Is Not Enough');

    await login.signInAs(ADMIN);
    const id = await invoices.createAndSubmit(customer, 500000.01);

    await login.switchTo(DIRECTOR);
    await approvals.open();
    await approvals.approve(id);

    await invoices.open();
    expect((await invoices.detailsFor(customer)).status).toBe('Pending Approval');
  });
});
