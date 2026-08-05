import { defineConfig } from '@playwright/test';

/**
 * End-to-end tests against the locally running platform: the Angular dev server proxying
 * to the real API, the real SQL Server container, and the deterministic providers.
 *
 * The system-installed Edge is used (`channel: 'msedge'`) so no browser download is ever
 * needed — CI and this machine both have it. Servers are expected to already be running;
 * `npm run e2e` documents the exact commands.
 */
export default defineConfig({
  testDir: './e2e',
  outputDir: './e2e/.artifacts',
  fullyParallel: false,
  workers: 1,
  retries: 1,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:4200',
    channel: 'msedge',
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
