import { expect, test, type Page } from "@playwright/test";
import { join } from "node:path";

const player = "/player/p_e2eeeeeeeeeeeeeeeeeeee";
const routes = ["/", player, `${player}?view=flare`, "/my/profile", "/player/unknown-theme-player"];
const background = { light: "rgb(245, 247, 250)", dark: "rgb(11, 15, 23)" };
const panel = { light: "rgb(255, 255, 255)", dark: "rgb(20, 28, 42)" };
const accent = { light: "rgb(37, 99, 235)", dark: "rgb(96, 165, 250)" };
const categoryColors = {
  light: ["rgb(201, 124, 78)", "rgb(150, 179, 196)", "rgb(217, 163, 30)"],
  dark: ["rgb(210, 144, 103)", "rgb(185, 201, 209)", "rgb(236, 191, 47)"],
};
const categories = ["CLASSIC", "WHITE", "GOLD"];
const categoryRanges = ["1st〜X3 VS 2ndMIX", "2013〜A", "A20〜WORLD"];
const stages = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];
const screenshotDirectory = process.env.REVIEW_SCREENSHOT_DIR;

async function stubProfile(page: Page) {
  await page.route("**/api/v1/account/session", route => route.fulfill({ json: {
    email: "theme@example.com", google_linked: true, csrf_token: "test-csrf",
  } }));
  await page.route("**/api/v1/account/profile", route => route.fulfill({ json: {
    display_name: "N".repeat(64), public_player_id: "p_fixture", public_url: "http://127.0.0.1:5173/player/p_fixture",
  } }));
}

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
  await stubProfile(page);
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
        .toHaveCSS("background-color", panel[scheme]);
      if (route.includes("view=flare")) {
        for (const [index, category] of categories.entries()) {
          const card = page.locator(`.flare-category-panel[data-flare-category="${category.toLowerCase()}"]`);
          await expect(card).toHaveCSS("background-color", panel[scheme]);
          await expect(card).toHaveCSS("border-top-color", categoryColors[scheme][index]);
        }
      }
      if (route.includes("unknown-theme-player")) await expect(page.locator("main")).toHaveCSS("background-color", panel[scheme]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
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
    await expect(row.locator(".score-number")).toHaveCSS("color", accent[scheme]);
    await expect(row).toHaveCSS("background-color", panel[scheme]);
    await expect(page.getByRole("button", { name: "SINGLE", exact: true })).toHaveCSS("min-height", "44px");
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    await page.screenshot({ path: `../../logs/theme-best-${scheme}-390.png`, fullPage: true });
  }
});

function contrast(foreground: string, backgroundColor: string) {
  const luminance = (rgb: string) => rgb.match(/\d+/g)!.slice(0, 3).map((value) => Number(value) / 255)
    .map((value) => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4)
    .reduce((sum, value, index) => sum + value * [0.2126, 0.7152, 0.0722][index], 0);
  const levels = [luminance(foreground), luminance(backgroundColor)];
  return (Math.max(...levels) + 0.05) / (Math.min(...levels) + 0.05);
}

function specimenMarkup() {
  const badge = (kind: string, value: string, label = value) => {
    const lamp = kind === "clear" ? `<span class="clear-lamp" data-value="${value}" aria-hidden="true"></span>` : "";
    return `<span class="result-badge" data-kind="${kind}" data-value="${value}">${lamp}${label}</span>`;
  };
  const group = (label: string, items: string[]) =>
    `<div class="color-legend-group"><dt>${label}</dt><dd class="color-legend-items">${items.join("")}</dd></div>`;
  return `<main class="page-shell"><section class="color-legend" aria-labelledby="guide-title">
    <h1 class="color-legend-title" id="guide-title">色の見方</h1>
    <dl>${group("難易度", ["BEGINNER", "BASIC", "DIFFICULT", "EXPERT", "CHALLENGE"].map((label, index) =>
      `<span class="difficulty-badge diff-${["bgn", "bas", "dif", "exp", "cha"][index]}">${label}</span>`))}
    ${group("判定", ["MARVELOUS", "PERFECT", "GREAT", "GOOD", "O.K.", "MISS"].map((label) =>
      `<span class="color-legend-item"><i class="judgement-swatch" data-value="${label}" aria-hidden="true"></i>${label}</span>`))}
    ${group("RANK", ["AAA–A-", "B+–B-", "C+–C-", "D+–D", "E"].map((label, index) =>
      badge("rank", ["AAA", "B+", "C+", "D+", "E"][index], label)))}
    ${group("CLEAR", ["MFC", "PFC", "GFC", "FC", "CLEAR", "FAILED"].map((label) => badge("clear", label)))}
    ${group("FLARE", stages.map((stage) => badge("flare", stage)))}
    ${group("フレアスキルカテゴリ", categories.map((category, index) =>
      `<span class="color-legend-item"><i class="flare-category-swatch" data-flare-category="${category.toLowerCase()}" aria-hidden="true"></i>${category} (${categoryRanges[index]})</span>`))}
    </dl></section></main>`;
}

for (const scheme of ["light", "dark"] as const) {
  test(`${scheme} shared color guide renders distinct FLARE stages and readable badges`, async ({ page }) => {
    await page.setViewportSize({ width: 1100, height: 560 });
    await page.emulateMedia({ colorScheme: scheme });
    await page.goto("/");
    const stylesheet = await page.locator('link[rel="stylesheet"][href^="/assets/"]').getAttribute("href");
    expect(stylesheet).toBeTruthy();
    await page.route("**/theme-specimen", route => route.fulfill({
      contentType: "text/html; charset=utf-8",
      headers: { "content-security-policy": "default-src 'none'; style-src 'self'" },
      body: `<!doctype html><html lang="ja"><head><meta name="viewport" content="width=device-width, initial-scale=1"><title>色の見方</title><link rel="stylesheet" href="${stylesheet}"></head><body>${specimenMarkup()}</body></html>`,
    }));
    await page.goto("/theme-specimen");
    await expect(page.getByRole("heading", { name: "色の見方" })).toBeVisible();
    await expect(page.locator(".color-legend")).toHaveCSS("background-color", panel[scheme]);
    const colors = await page.locator('.result-badge[data-kind="flare"]').evaluateAll(badges => badges.map(badge => {
      const style = getComputedStyle(badge);
      return { label: badge.textContent, fill: style.backgroundColor, gradient: style.backgroundImage,
        border: style.borderTopColor, text: style.color };
    }));
    expect(colors.map(color => color.label)).toEqual(stages);
    expect(new Set(colors.map(color => color.fill)).size).toBe(10);
    expect(new Set(colors.map(color => color.border)).size).toBe(10);
    for (const color of colors.slice(0, -1)) expect(contrast(color.text, color.fill), color.label ?? "").toBeGreaterThanOrEqual(4.5);
    const rainbow = colors.at(-1)!;
    expect(rainbow.gradient).toContain("linear-gradient");
    for (const stop of rainbow.gradient.match(/rgb\([^)]+\)/g)!) expect(contrast(rainbow.text, stop)).toBeGreaterThanOrEqual(4.5);
    const solidBadges = await page.locator('.result-badge:is([data-kind="rank"], [data-kind="clear"]:not([data-value="MFC"]))')
      .evaluateAll(badges => badges.map(badge => {
        const style = getComputedStyle(badge);
        return { label: badge.textContent, fill: style.backgroundColor, text: style.color };
      }));
    expect(solidBadges).toHaveLength(10);
    for (const color of solidBadges) expect(contrast(color.text, color.fill), color.label ?? "").toBeGreaterThanOrEqual(4.5);
    const clearPills = page.locator('.result-badge[data-kind="clear"] > .clear-lamp');
    await expect(clearPills).toHaveCount(6);
    await expect(clearPills.and(page.locator('[data-value="MFC"]'))).toHaveCSS("background-image", /conic-gradient/);
    await expect(page.locator(".difficulty-badge")).toHaveCount(5);
    await expect(page.locator(".judgement-swatch")).toHaveCount(6);
    for (const [index, category] of categories.entries()) {
      await expect(page.locator(`.flare-category-swatch[data-flare-category="${category.toLowerCase()}"]`))
        .toHaveCSS("background-color", categoryColors[scheme][index]);
      await expect(page.getByText(`${category} (${categoryRanges[index]})`, { exact: true })).toBeVisible();
    }
    if (screenshotDirectory) await page.screenshot({ path: join(screenshotDirectory, `web-legend-specimen-${scheme}.png`) });
    await page.setViewportSize({ width: 390, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
  });

  test(`${scheme} desktop public and management routes use the shared theme without overflow`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 844 });
    await page.emulateMedia({ colorScheme: scheme });
    await stubProfile(page);
    for (const [name, route, ready] of [
      ["home", "/", ".home-about"],
      ["player-bests", `${player}?view=best`, ".score-number"],
      ["player-flare", `${player}?view=flare`, ".flare-category-panel"],
      ["profile", "/my/profile", ".management-card"],
    ]) {
      const response = await page.goto(route);
      expect(response?.status()).toBe(200);
      await expect(page.locator(ready).first()).toBeVisible();
      await expect(page.locator("html")).toHaveCSS("background-color", background[scheme]);
      expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
      if (screenshotDirectory) await page.screenshot({ path: join(screenshotDirectory, `web-${name}-${scheme}.png`), fullPage: true });
    }
  });
}
