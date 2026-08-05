import { test } from '@playwright/test';

import { REVIEW_WIDTHS, signInAs } from './helpers';

/**
 * The visual QA pass: full-page screenshots of every key route at the four review widths.
 *
 * These are not pixel-comparison tests — they produce the artifacts a human (or a
 * reviewing agent) inspects for layout defects: overflow, collisions, unreadable text.
 * Output lands in e2e/screenshots/{width}/{route}.png.
 */

const PUBLIC_ROUTES: readonly (readonly [name: string, path: string])[] = [
  ['home', '/'],
  ['catalog', '/courses'],
  ['pricing', '/pricing'],
  ['faq', '/faq'],
  ['about', '/about'],
  ['privacy', '/legal/privacy'],
  ['not-found', '/definitely-not-a-page'],
];

const STUDENT_ROUTES: readonly (readonly [name: string, path: string])[] = [
  ['dashboard', '/dashboard'],
  ['my-learning', '/my-learning'],
  ['account', '/account'],
  ['community', '/community'],
  ['community-search', '/community/search'],
  ['messages', '/messages'],
  ['notifications', '/notifications'],
  ['certificates', '/certificates'],
];

const ADMIN_ROUTES: readonly (readonly [name: string, path: string])[] = [
  ['admin', '/admin'],
  ['admin-users', '/admin/users'],
  ['admin-records', '/admin/records'],
  ['admin-ops', '/admin/ops'],
  ['admin-catalog', '/admin/catalog'],
];

for (const width of REVIEW_WIDTHS) {
  test.describe(`at ${width}px`, () => {
    test.use({ viewport: { width, height: 900 } });

    test(`public routes`, async ({ page }) => {
      for (const [name, path] of PUBLIC_ROUTES) {
        await page.goto(path);
        await page.waitForTimeout(1200); // settle: SignalR keeps the network from ever idling
        await page.screenshot({
          path: `e2e/screenshots/${width}/${name}.png`,
          fullPage: true,
        });
      }
    });

    test(`student routes`, async ({ page }) => {
      await signInAs(page, 'student');

      for (const [name, path] of STUDENT_ROUTES) {
        await page.goto(path);
        await page.waitForTimeout(1200); // settle: SignalR keeps the network from ever idling
        await page.screenshot({
          path: `e2e/screenshots/${width}/${name}.png`,
          fullPage: true,
        });
      }
    });

    test(`admin routes`, async ({ page }) => {
      await signInAs(page, 'admin');

      for (const [name, path] of ADMIN_ROUTES) {
        await page.goto(path);
        await page.waitForTimeout(1200); // settle: SignalR keeps the network from ever idling
        await page.screenshot({
          path: `e2e/screenshots/${width}/${name}.png`,
          fullPage: true,
        });
      }
    });
  });
}
