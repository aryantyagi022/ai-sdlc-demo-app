import { Locator, Page, expect } from '@playwright/test';

/**
 * The "Approvals" tab. Everything here is asserted through text a person would actually read,
 * because the point of the refusal rules is that a human is told why, not that a status code
 * came back.
 */
export class ApprovalsPage {
  constructor(private readonly page: Page) {}

  async open() {
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
    await expect(this.page.getByRole('heading', { name: 'Pending approvals' })).toBeVisible();
  }

  itemFor(customerName: string): Locator {
    return this.page.getByRole('listitem').filter({ hasText: customerName });
  }

  approveButtonFor(invoiceId: string): Locator {
    return this.page.getByRole('button', { name: `Approve invoice ${invoiceId}` });
  }

  rejectButtonFor(invoiceId: string): Locator {
    return this.page.getByRole('button', { name: `Reject invoice ${invoiceId}` });
  }

  async approve(invoiceId: string) {
    await this.approveButtonFor(invoiceId).click();
  }

  async reject(invoiceId: string, reason: string) {
    await this.page.getByLabel(`Rejection reason for ${invoiceId}`).fill(reason);
    await this.rejectButtonFor(invoiceId).click();
  }

  /** The explanation shown next to an Approve button this user is not allowed to press. */
  refusalFor(customerName: string): Locator {
    return this.itemFor(customerName).getByRole('note');
  }

  /** Who raised the invoice, as a reviewer reads it on the pending card. */
  submittedByFor(customerName: string): Locator {
    return this.itemFor(customerName).getByText(/^Submitted by /);
  }

  /** Who has already signed the invoice, in the order they signed. */
  approvedByFor(customerName: string): Locator {
    return this.itemFor(customerName).getByText(/^Approved by /);
  }

  /** The error banner shown after an attempt that the server turned down. */
  get decisionError(): Locator {
    return this.page.getByRole('alert');
  }

  /** The message telling a role it can never approve anything. */
  get roleNote(): Locator {
    return this.page.getByRole('status');
  }

  get emptyMessage(): Locator {
    return this.page.getByText('Nothing is waiting for approval.');
  }
}
