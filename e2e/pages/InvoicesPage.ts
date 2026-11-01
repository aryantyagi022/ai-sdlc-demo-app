import { Locator, Page, expect } from '@playwright/test';

/**
 * The "Invoices" tab. Tests create their own invoices here rather than relying on seeded data,
 * so one spec cannot leave the shared in-memory store in a state that breaks the next.
 */
export class InvoicesPage {
  constructor(private readonly page: Page) {}

  async open() {
    await this.page.getByRole('tab', { name: 'Invoices' }).click();
    await expect(this.page.getByRole('heading', { name: 'Invoices', level: 2 })).toBeVisible();
  }

  /** Creates a Draft invoice and submits it, returning the customer name that identifies its row. */
  async createAndSubmit(customerName: string, amount: number) {
    await this.open();
    await this.page.getByLabel('Customer name').fill(customerName);
    await this.page.getByLabel('Amount').fill(String(amount));
    await this.page.getByRole('button', { name: 'Create invoice' }).click();

    const row = this.rowFor(customerName);
    await expect(row).toBeVisible();

    const id = await this.idFor(customerName);
    await this.page
      .getByRole('button', { name: `Submit invoice ${id} for approval` })
      .click();
    await expect(row.getByText('Pending Approval')).toBeVisible();
    return id;
  }

  rowFor(customerName: string): Locator {
    return this.page.getByRole('row').filter({ hasText: customerName });
  }

  async idFor(customerName: string): Promise<string> {
    const cells = await this.rowFor(customerName).getByRole('cell').allInnerTexts();
    return cells[0].trim();
  }

  /** The cells of an invoice's row, keyed the way the table header names them. */
  async detailsFor(customerName: string) {
    const cells = await this.rowFor(customerName).getByRole('cell').allInnerTexts();
    const [id, customer, amount, submittedBy, status, approvals] = cells.map((cell) => cell.trim());
    return { id, customer, amount, submittedBy, status, approvals };
  }
}
