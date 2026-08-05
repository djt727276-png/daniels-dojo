import { Page, expect } from '@playwright/test';

/** The widths the visual pass reviews, per the v1 checklist. */
export const REVIEW_WIDTHS = [375, 768, 1024, 1440] as const;

/**
 * Signs in through the Development harness screen — the same path a person uses. The
 * harness exists only in local Development; these tests cannot run against a deployed
 * environment, by design.
 */
export async function signInAs(page: Page, profile: 'admin' | 'student'): Promise<void> {
  await page.goto('/development-login');
  await page.getByTestId(`continue-as-${profile}`).click();

  // The session is loaded when the account page can render identity from the API.
  await page.goto('/account');
  await expect(page.getByTestId('account-name')).toBeVisible();
}

/** Fails the test on any browser console error, which is how template bugs surface. */
export function failOnConsoleErrors(page: Page, ignore: RegExp[] = []): string[] {
  const errors: string[] = [];

  page.on('console', (message) => {
    if (message.type() === 'error' && !ignore.some((pattern) => pattern.test(message.text()))) {
      errors.push(message.text());
    }
  });

  return errors;
}
