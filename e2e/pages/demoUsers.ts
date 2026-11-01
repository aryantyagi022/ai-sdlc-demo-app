import { readFileSync } from 'node:fs';
import { join } from 'node:path';

type DemoUser = { username: string; role: string; signInKey: string };

const settingsPath = join(__dirname, '../../backend/src/InvoiceApp.Api/appsettings.json');

/**
 * Demo sign-in details are read from the application's own configuration rather than copied
 * into the tests, so there is exactly one place where they live and the suite cannot drift
 * from the running app.
 */
const users: DemoUser[] = JSON.parse(readFileSync(settingsPath, 'utf8')).Users.map(
  (user: Record<string, string>) => ({
    username: user.Username,
    role: user.Role,
    signInKey: user.Password
  })
);

export function demoUser(username: string): DemoUser {
  const user = users.find((candidate) => candidate.username === username);
  if (!user) {
    throw new Error(
      `No demo user named ${username}. Known users: ${users.map((u) => u.username).join(', ')}.`
    );
  }
  return user;
}

export const MANAGER = 'demo.manager';
export const DIRECTOR = 'demo.director';
export const SECOND_DIRECTOR = 'demo.director2';
export const ADMIN = 'demo.admin';
