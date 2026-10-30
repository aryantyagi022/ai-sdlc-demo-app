import { Page, expect } from '@playwright/test';
import { demoUser } from './demoUsers';

export class LoginPage {
  constructor(private readonly page: Page) {}

  async open() {
    await this.page.goto('/');
  }

  /** Signs in from a clean session, so each test starts without inherited sessionStorage. */
  async signInAs(username: string) {
    await this.open();
    // A signed-out visitor must not be offered session controls. This caught a real defect:
    // a CSS display rule on the session bar was overriding the hidden attribute.
    await expect(this.page.getByRole('button', { name: 'Sign out' })).toBeHidden();
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Password').fill(demoUser(username).signInKey);
    await this.page.getByRole('button', { name: 'Sign in' }).click();
    await expect(this.page.getByRole('button', { name: 'Sign out' })).toBeVisible();
  }

  async signOut() {
    await this.page.getByRole('button', { name: 'Sign out' }).click();
    await expect(this.page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expect(this.page.getByRole('button', { name: 'Sign out' })).toBeHidden();
  }

  /** Swaps the signed-in user within one browser context, as a handover would in real life. */
  async switchTo(username: string) {
    await this.signOut();
    await this.signInAs(username);
  }

  signedInAs(username: string, role: string) {
    return expect(this.page.getByText(`${username} (${role})`)).toBeVisible();
  }
}
