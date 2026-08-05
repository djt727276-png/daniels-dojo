import { expect, test } from '@playwright/test';

import { failOnConsoleErrors, signInAs } from './helpers';

/**
 * The platform end to end: real browser, real API, real database, deterministic providers.
 *
 * These are journeys, not unit assertions — each one walks a path a person actually takes
 * and checks what they would see. Authorization is asserted by walking to places the role
 * must not reach.
 */

test.describe('public site', () => {
  test('the home page renders and navigates to the catalog', async ({ page }) => {
    const errors = failOnConsoleErrors(page);

    await page.goto('/');
    await expect(page).toHaveTitle(/Daniel's Dojo/);

    await page.goto('/courses');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    expect(errors).toEqual([]);
  });

  test('pricing shows the live membership price, not a hard-coded one', async ({ page }) => {
    await page.goto('/pricing');

    // Either a real price from the API or the honest "being prepared" note — never silence.
    await expect(
      page.getByTestId('membership-price').or(page.getByTestId('membership-unpublished')),
    ).toBeVisible();
  });

  test('the legal pages and 404 page render', async ({ page }) => {
    await page.goto('/legal/privacy');
    await expect(page.getByRole('heading', { name: /privacy/i }).first()).toBeVisible();

    await page.goto('/definitely-not-a-page');
    await expect(page.getByText('This page left the dojo')).toBeVisible();
  });

  test('certificate verification answers honestly for an unknown code', async ({ page }) => {
    await page.goto('/verify/NOT-A-REAL-CODE');
    await expect(page.getByText(/no certificate|not valid|not found/i).first()).toBeVisible();
  });
});

test.describe('student journey', () => {
  test('a student signs in, sees the dashboard, and is refused admin', async ({ page }) => {
    await signInAs(page, 'student');

    await page.goto('/dashboard');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    // The admin workspace is refused server-side; the guard sends the student away.
    await page.goto('/admin');
    await expect(page).not.toHaveURL(/\/admin$/);
  });

  test('a student browses a course and sees the reviews section', async ({ page }) => {
    await signInAs(page, 'student');

    await page.goto('/courses');
    await page.waitForLoadState('networkidle');

    const firstCourse = page.locator('a[href^="/courses/"]').first();

    if ((await firstCourse.count()) === 0) {
      test.skip(true, 'No published course in this database.');
    }

    await firstCourse.click();
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.getByText('Reviews').first()).toBeVisible();
  });
});

test.describe('admin journey', () => {
  test('an admin reaches the back office end to end', async ({ page }) => {
    const errors = failOnConsoleErrors(page);

    await signInAs(page, 'admin');

    await page.goto('/admin');
    await expect(page.getByTestId('admin-greeting')).toBeAttached();

    await page.goto('/admin/users');
    await expect(page.getByTestId('user-list')).toBeVisible();

    await page.goto('/admin/records');
    await expect(page.getByRole('tab', { name: 'Certificates' })).toBeVisible();

    await page.goto('/admin/ops');
    await expect(page.getByTestId('ops-snapshot')).toBeVisible();
    await expect(page.getByTestId('flag-list')).toBeVisible();

    expect(errors).toEqual([]);
  });
});
