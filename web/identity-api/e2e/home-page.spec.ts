import { expect, test } from "@playwright/test";

for (const width of [1280, 390]) {
  test(`signed-out home at ${width}px introduces current usage and opens My Page`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    const response = await page.goto("/");
    expect(response?.status()).toBe(200);
    expect(response?.headers()["cache-control"]).toBe("no-store");
    await expect(page).toHaveTitle("GP Score Log");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("GP Score Logについて");
    await expect(page.getByRole("link", { name: "アプリをダウンロード", exact: true }))
      .toHaveAttribute("href", "https://github.com/tts1374/ddrgp_scorelog/releases/latest");
    await page.getByRole("link", { name: "使い方を見る", exact: true }).click();
    await expect(page).toHaveURL(/#getting-started$/);
    await expect(page.getByRole("heading", { name: "使い方", exact: true })).toBeInViewport();
    await expect(page.getByText(/Webとアプリのコードを確認/u)).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    await expect(page.locator("vite-error-overlay")).toHaveCount(0);
    await page.getByRole("link", { name: "マイページ", exact: true }).click();
    await expect(page).toHaveURL(/\/my\/profile$/);
    await expect(page.getByRole("link", { name: "Googleでログイン", exact: true })).toBeVisible();
    expect(errors).toEqual([]);
  });
}
