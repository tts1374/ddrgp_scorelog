import { expect, test } from "@playwright/test";

const player = "/player/p_e2eeeeeeeeeeeeeeeeeeee";
const routes = ["/", player, "/my/profile", "/player/unknown-theme-player"];
const background = { light: "rgb(244, 247, 249)", dark: "rgb(17, 27, 37)" };

for (const scheme of ["light", "dark", "no-preference"] as const) {
  test(`initial ${scheme} colors work before application JavaScript, including the real 404`, async ({ browser }) => {
    const context = await browser.newContext({ javaScriptEnabled: false, colorScheme: scheme });
    const page = await context.newPage();
    try {
      for (const route of routes) {
        const response = await page.goto(route);
        expect(response?.status()).toBe(route.includes("unknown-theme-player") ? 404 : 200);
        await expect(page.locator("html")).toHaveCSS("background-color", background[scheme === "dark" ? "dark" : "light"]);
        await expect(page.locator("html")).toHaveCSS("color-scheme", scheme === "dark" ? "dark" : "light");
        expect(response?.headers()["content-security-policy"]).not.toContain("unsafe-inline");
      }
    } finally { await context.close(); }
  });
}

test("open public, management and 404 pages follow live changes without theme state", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.route("**/api/v1/account/session", route => route.fulfill({ json: {
    email: "theme@example.com", google_linked: true, csrf_token: "test-csrf",
  } }));
  await page.route("**/api/v1/account/profile", route => route.fulfill({ json: {
    display_name: "N".repeat(64), public_player_id: "p_fixture", public_url: "http://127.0.0.1:5173/player/p_fixture",
  } }));
  const errors: string[] = [];
  page.on("pageerror", error => errors.push(error.message));
  for (const route of routes) {
    await page.emulateMedia({ colorScheme: "light" });
    await page.goto(route);
    await expect(page.locator("h1")).toBeVisible();
    if (route === player) await expect(page).toHaveURL(/\?style=SP&/);
    const url = page.url();
    const historyLength = await page.evaluate(() => history.length);
    for (const scheme of ["dark", "light"] as const) {
      await page.emulateMedia({ colorScheme: scheme });
      await expect(page.locator("html")).toHaveCSS("background-color", background[scheme]);
      await expect(page.locator("html")).toHaveCSS("color-scheme", scheme);
      if (route === "/my/profile") await expect(page.locator(".management-card").first())
        .toHaveCSS("background-color", scheme === "dark" ? "rgb(24, 38, 49)" : "rgb(255, 255, 255)");
    }
    expect(page.url()).toBe(url);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    expect(await page.evaluate(() => ({ history: history.length, local: localStorage.length, session: sessionStorage.length })))
      .toEqual({ history: historyLength, local: 0, session: 0 });
    await expect(page.locator("vite-error-overlay")).toHaveCount(0);
  }
  expect((await page.context().cookies()).some(cookie => /theme|appearance/i.test(cookie.name))).toBe(false);
  expect(errors).toEqual([]);
});

test("mobile best cards keep scores in two columns and results in three in both schemes", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  for (const scheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme: scheme });
    await page.goto(`${player}?view=best`);
    await expect(page.locator(".score-number").first()).toBeVisible();
    const row = page.locator(".best-table tbody tr").first();
    const cells = await row.locator("td").evaluateAll(elements => elements.map(element => {
      const { x, y, width } = element.getBoundingClientRect();
      return { label: element.getAttribute("data-label"), x, y, width };
    }));
    expect(cells.map(cell => cell.label)).toEqual(["Music", "Chart", "SCORE", "EX SCORE", "RANK", "CLEAR", "FLARE"]);
    expect(cells[2].y).toBe(cells[3].y);
    expect(cells[2].x).toBeLessThan(cells[3].x);
    expect(cells[4].y).toBe(cells[5].y);
    expect(cells[5].y).toBe(cells[6].y);
    expect(cells[4].x).toBeLessThan(cells[5].x);
    expect(cells[5].x).toBeLessThan(cells[6].x);
    expect(cells[2].width / cells[4].width).toBeCloseTo(1.5);
    await expect(row.locator(".score-number")).toHaveCSS("font-size", "20px");
    await expect(row.locator(".score-number")).toHaveCSS("color", scheme === "dark" ? "rgb(97, 203, 211)" : "rgb(7, 110, 125)");
    await expect(page.getByRole("button", { name: "SINGLE", exact: true })).toHaveCSS("min-height", "44px");
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    await page.screenshot({ path: `../../logs/theme-best-${scheme}-390.png`, fullPage: true });
  }
});
