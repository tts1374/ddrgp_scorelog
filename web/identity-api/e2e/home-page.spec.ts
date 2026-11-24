import { expect, test } from "@playwright/test";

for (const width of [1280, 390]) {
  test(`signed-out home at ${width}px introduces current usage and opens My Page`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    const errors: string[] = [];
    page.on("pageerror", error => errors.push(error.message));
    const context = await page.request.get("/api/v1/auth/web/context");
    expect(context.status()).toBe(200);
    expect((await page.context().cookies()).some(cookie => cookie.name.endsWith("-browser"))).toBe(true);
    const response = await page.goto("/");
    expect(response?.status()).toBe(200);
    expect(response?.headers()["cache-control"]).toBe("no-store");
    await expect(page).toHaveTitle("GP Score Log");
    await expect(page.getByRole("heading", { level: 1 })).toHaveText("DDR GRAND PRIXの自動記録");
    await expect(page.getByText("Windows 11・グランプリプレー・1280×720に対応")).toBeVisible();
    await expect(page.getByRole("link", { name: "アプリをダウンロード", exact: true }))
      .toHaveAttribute("href", "https://github.com/tts1374/ddrgp_scorelog/releases/latest");
    await expect(page.getByRole("navigation", { name: "利用を始める" }).getByRole("link", { name: "利用ガイド" }))
      .toHaveAttribute("href", "https://github.com/tts1374/ddrgp_scorelog/blob/main/docs/user-guide.md");
    const publicData = page.getByRole("region", { name: "Webへの公開について" });
    await expect(publicData.getByText(/公開URLを共有すると、ログインせずに/u)).toBeVisible();
    await expect(publicData.getByText(/Webには自動記録した自己ベストを送信します/u)).toBeVisible();
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
