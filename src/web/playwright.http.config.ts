import { defineConfig, devices } from "@playwright/test";
import base from "./playwright.config";

// Run against a non-loopback HTTP origin with FJB_EXPECT_INSECURE_HTTP=true.
export default defineConfig({
  ...base,
  grep: /a grown-up assigns a recurring schedule/,
  projects: [
    { name: "chromium-http", use: { ...devices["Desktop Chrome"] } },
    { name: "webkit-http", use: { ...devices["iPhone 13"] } },
  ],
});
