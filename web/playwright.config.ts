import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e', timeout: 60000, expect: { timeout: 10000 }, fullyParallel: false, workers: 1,
  use: { channel: process.env.E2E_BROWSER_CHANNEL, baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  webServer: process.env.E2E_EXTERNAL ? undefined : { command: 'npm run dev', url: 'http://localhost:5173', reuseExistingServer: !process.env.CI },
});
