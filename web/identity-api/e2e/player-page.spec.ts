import { expect, test } from "@playwright/test";

const publicPlayerId = "p_e2eeeeeeeeeeeeeeeeeeee";

test("valid Player renders Overview and restores a Best deep link", async ({ page }) => {
  await page.goto(`/player/${publicPlayerId}`);
  await expect(page).toHaveTitle("2TEN - GP Score Log");
  await expect(page.getByRole("heading", { name: "2TEN" })).toBeVisible();
  await expect(page.getByText("SINGLE Flare Skill")).toBeVisible();

  await page.goto(`/player/${publicPlayerId}?style=SP&view=best&mode=title&q=MAX&sort=score_desc`);
  await expect(page.getByRole("searchbox", { name: "曲名" })).toHaveValue("MAX");
  await expect(page.getByText("MAX 300")).toBeVisible();
  await expect(page.getByText("990,000")).toBeVisible();
});

test("unknown Player is a real 404", async ({ page }) => {
  const response = await page.goto("/player/p_zzzzzzzzzzzzzzzzzzzzzz");
  expect(response?.status()).toBe(404);
  await expect(page.getByRole("heading", { name: "Playerが見つかりません" })).toBeVisible();
});

test("390px Best view uses cards without horizontal page scrolling", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`/player/${publicPlayerId}?style=SP&view=best&mode=title`);
  await expect(page.getByText("MAX 300")).toBeVisible();
  const hasHorizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(hasHorizontalOverflow).toBe(false);
  await expect(page.locator(".best-table tbody tr").first()).toHaveCSS("display", "grid");
  await page.screenshot({ path: "../../logs/player-data-mobile.png", fullPage: true });
});
