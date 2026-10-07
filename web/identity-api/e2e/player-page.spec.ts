import { expect, test, type Page } from "@playwright/test";
import { readFileSync } from "node:fs";
import type { PublicBestItem, PublicFlareResponse } from "../src/client/types";

const publicPlayerId = "p_e2eeeeeeeeeeeeeeeeeeee";

const ranks = ["AAA", "AA+", "AA", "AA-", "A+", "A", "A-", "B+", "B", "B-", "C+", "C", "C-", "D+", "D", "E"];
const clears = ["MFC", "PFC", "GFC", "FC", "CLEAR", "FAILED"];
const flares = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "EX"];
const paletteItems: PublicBestItem[] = ranks.map((rank, index) => ({
  chart_id: `palette-${index}`, title: `MAX sample ${rank}`, artist: "Regression fixture",
  difficulty: "EXPERT", level: index % 2 ? 17 : 15,
  version: index % 2 ? "DanceDanceRevolution WORLD" : "DDRMAX", is_removed: false,
  best: { score: 1000000 - index * 10000, ex_score: 1500 - index * 10,
    rank, clear_type: clears[index % clears.length], flare_rank: flares[index % flares.length] },
}));
paletteItems.push({ ...paletteItems[0], chart_id: "missing", title: "Missing record", best: null });

const flareFixture: PublicFlareResponse = {
  style: "SP", total: 55000, rank: { main: "WORLD", sub: null },
  categories: (["CLASSIC", "WHITE", "GOLD"] as const).map((category, index) => ({
    category, total: [30000, 17000, 8000][index], target_count: [30, 17, 8][index],
    targets: Array.from({ length: [30, 17, 8][index] }, (_, row) => ({
      chart_id: `${category}-${row}`, title: `${category} target ${row + 1}`,
      difficulty: "EXPERT", level: 17, flare_rank: flares[row % flares.length], flare_skill: 1000 - row,
    })),
  })),
};

async function mockBrowse(page: Page) {
  await page.route("**/api/v1/public/players/*/bests?*", async (route) => {
    const query = new URL(route.request().url()).searchParams;
    const mode = query.get("mode");
    const items = paletteItems.filter((item) => mode === "level" ? item.level === Number(query.get("level"))
      : mode === "version" ? item.version === query.get("version")
      : item.title.toLowerCase().includes((query.get("q") ?? "").trim().toLowerCase()));
    await route.fulfill({ json: { style: query.get("style"), mode, summary: null, next_cursor: null,
      items: items.map((item) => ({ ...item, title: `${query.get("style")} ${item.title}` })) } });
  });
  await page.route("**/api/v1/public/players/*/flare-skill?*", (route) => route.fulfill({ json: flareFixture }));
}

let runtimeErrors: string[];
test.beforeEach(async ({ page }) => {
  runtimeErrors = [];
  page.on("pageerror", (error) => runtimeErrors.push(error.message));
  page.on("console", (message) => {
    // Only exclude the expected HTTP failures exercised by these flows.
    const expectedFailure = (test.info().title.includes("preserves rows after 503") && message.text().includes("503")) ||
      (test.info().title === "unknown Player is a real 404" && message.text().includes("404")) ||
      (page.url().includes("/my/profile") && message.location().url.includes("/api/v1/account/") && message.text().includes("401"));
    if (["error", "warning"].includes(message.type()) && !expectedFailure) runtimeErrors.push(message.text());
  });
});
test.afterEach(async ({ page }) => {
  await expect(page.locator("vite-error-overlay")).toHaveCount(0);
  expect(runtimeErrors).toEqual([]);
});

async function noOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
}

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

for (const width of [1280, 390]) {
  test(`${width}px selects the highest recorded level per style and preserves URL, manual choices and history`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route(`**/player/${publicPlayerId}*`, async (route) => {
      const response = await route.fetch();
      const html = (await response.text()).replace(/(<script id="player-bootstrap"[^>]*>)(.*?)(<\/script>)/s, (_, start, json, end) => {
        const bootstrap = JSON.parse(json);
        for (const [style, maximum] of [["SP", 15], ["DP", 17]] as const) {
          bootstrap.styles[style].levels = [
            { level: 19, active_chart_count: 1, active_published_best_count: 0 },
            { level: maximum, active_chart_count: 1, active_published_best_count: 1 },
            { level: 5, active_chart_count: 1, active_published_best_count: 1 },
          ];
        }
        return `${start}${JSON.stringify(bootstrap)}${end}`;
      });
      await route.fulfill({ response, body: html });
    });
    await mockBrowse(page);
    await page.goto(`/player/${publicPlayerId}?view=best`);
    await expect(page.locator(".result-count")).toContainText("全譜面");
    await page.getByRole("tab", { name: "レベルから" }).click();
    const level = page.getByRole("combobox", { name: "レベル", exact: true });
    await expect(level).toHaveValue("15");
    await expect(page.locator(".chart-meta")).toHaveText(Array(9).fill("Lv.15"));
    await level.selectOption("17");
    await page.getByRole("button", { name: "DOUBLE" }).click();
    await expect(level).toHaveValue("17");
    await expect(page.locator(".music-title").first()).toContainText("DP");
    await page.getByRole("button", { name: "SINGLE" }).click();
    await expect(level).toHaveValue("17");
    const length = await page.evaluate(() => history.length);
    await page.goBack();
    await expect(page.getByRole("button", { name: "DOUBLE" })).toHaveAttribute("aria-pressed", "true");
    await expect(level).toHaveValue("17");
    await page.goBack();
    await expect(page.getByRole("button", { name: "SINGLE" })).toHaveAttribute("aria-pressed", "true");
    await expect(level).toHaveValue("17");
    await page.goBack();
    await expect(level).toHaveValue("15");
    await expect(page.locator(".chart-meta")).toHaveText(Array(9).fill("Lv.15"));
    await page.goForward();
    await expect(level).toHaveValue("17");
    await expect(page.locator(".chart-meta")).toHaveText(Array(8).fill("Lv.17"));
    expect(await page.evaluate(() => history.length)).toBe(length);
    await page.goto(`/player/${publicPlayerId}?style=DP&view=best&mode=level&level=15`);
    await expect(level).toHaveValue("15");
    await expect(page.locator(".chart-meta")).toHaveText(Array(9).fill("Lv.15"));
    await noOverflow(page);
    await page.screenshot({ path: `../../logs/issue-224-level-default-${width}.png` });
  });

  test(`${width}px matches Desktop light badge colors and shares FLARE colors between views`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await mockBrowse(page);
    const theme = readFileSync("../../app/src/DDRGpScoreViewer/Resources/Theme.xaml", "utf8");
    const rgb = (hex: string) => `rgb(${[0, 2, 4].map((offset) => parseInt(hex.slice(offset, offset + 2), 16)).join(", ")})`;
    const brush = (key: string) => rgb(theme.match(new RegExp(`x:Key="${key}" Color="#([0-9A-F]{6})"`))![1]);
    const colors = async (kind: string, value: string, group: string, borderGroup = group) => {
      const badge = page.locator(`.best-table .result-badge[data-kind="${kind}"][data-value="${value}"]`).first();
      await expect(badge).toHaveText(value);
      await expect(badge).toHaveCSS("color", brush(`Badge${group}TextBrush`));
      await expect(badge).toHaveCSS("border-top-color", brush(`Badge${borderGroup}BorderBrush`));
      if (group !== "Mfc") await expect(badge).toHaveCSS("background-color", brush(`Badge${group}BackgroundBrush`));
      else {
        const stops = ["Danger", "Warning", "Success", "Cyan", "Purple"].map((name) =>
          rgb(theme.match(new RegExp(`x:Key="MfcGradient${name}Color">#([0-9A-F]{6})`))![1]));
        await expect(badge).toHaveCSS("background-image", `linear-gradient(90deg, ${stops.join(", ")})`);
      }
    };
    await page.goto(`/player/${publicPlayerId}?view=best`);
    await expect(page.locator(".best-table tbody tr")).toHaveCount(17);
    for (const rank of ranks) await colors("rank", rank,
      rank.startsWith("A") ? "Warning" : rank.startsWith("B") ? "Cyan" : rank.startsWith("C") ? "Pink" : rank.startsWith("D") ? "Danger" : "Neutral");
    for (const [index, group] of ["Mfc", "Warning", "Success", "Info", "Cyan", "Danger"].entries()) await colors("clear", clears[index], group);
    for (const [index, group] of ["Info", "Cyan", "Success", "Warning", "Danger", "Purple", "Slate", "Neutral", "Warning", "Mfc"].entries()) {
      await colors("flare", flares[index], group, index === 7 ? "Slate" : index === 8 ? "WarningStrong" : group);
    }
    const missing = page.locator(".best-table tbody tr").last();
    await expect(missing.locator(".no-best")).toHaveText(Array(5).fill("—"));
    await expect(missing.locator(".result-badge")).toHaveCount(0);
    const flareColors = await page.locator('.best-table .result-badge[data-kind="flare"]').evaluateAll((badges) =>
      Object.fromEntries(badges.map((badge) => {
        const style = getComputedStyle(badge);
        return [badge.getAttribute("data-value"), [style.backgroundColor, style.backgroundImage, style.borderTopColor, style.color]];
      })));
    await noOverflow(page);
    await page.screenshot({ path: `../../logs/issue-224-colors-${width}.png` });
    await page.getByRole("tab", { name: "Flare Skill", exact: true }).click();
    await expect(page.locator('[data-flare-category="classic"] .flare-target-row')).toHaveCount(10);
    const targetColors = await page.locator('.flare-target-list .result-badge').evaluateAll((badges) =>
      Object.fromEntries(badges.map((badge) => {
        const style = getComputedStyle(badge);
        return [badge.getAttribute("data-value"), [style.backgroundColor, style.backgroundImage, style.borderTopColor, style.color]];
      })));
    expect(targetColors).toEqual(flareColors);
  });

  test(`${width}px initially shows all levels and versions and can narrow the real API results`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.goto(`/player/${publicPlayerId}?view=best`);
    await expect(page.locator(".result-count")).toHaveText("2譜面 ・全譜面");
    await expect(page.locator(".music-title")).toHaveText(["MAX 300", "VOLAQUAS"]);
    await expect(page.getByRole("combobox", { name: "並び順" })).toHaveValue("score_desc");
    await page.getByRole("button", { name: "自己ベスト一覧について" }).focus();
    await expect(page.getByRole("button", { name: "自己ベスト一覧について" })).toHaveAttribute("data-tip", /未プレーを意味しません/);
    await expect(page.getByRole("button", { name: "自己ベスト一覧について" })).toHaveAttribute("data-tip", /それぞれの最高記録/);
    await page.getByRole("tab", { name: "レベルから" }).click();
    await expect(page.locator(".music-title")).toHaveText(["MAX 300"]);
    await expect(page.getByRole("combobox", { name: "レベル", exact: true })).toHaveValue("15");
    await page.getByRole("combobox", { name: "レベル", exact: true }).selectOption("17");
    await expect(page.locator(".music-title")).toHaveText(["VOLAQUAS"]);
    await page.getByRole("tab", { name: "バージョンから" }).click();
    await page.getByRole("combobox", { name: "バージョン", exact: true }).selectOption("DDRMAX");
    await expect(page.locator(".music-title")).toHaveText(["MAX 300"]);
    await page.getByRole("tab", { name: "曲名から" }).click();
    await page.getByRole("searchbox", { name: "曲名" }).fill("VOLA");
    await expect(page.locator(".music-title")).toHaveText(["VOLAQUAS"]);
    await noOverflow(page);
  });

  test(`${width}px preserves rows after 503 and retries the same additional page`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    let attempts = 0;
    const requests: string[] = [];
    await page.route("**/api/v1/public/players/*/bests?*", async (route) => {
      const url = route.request().url();
      requests.push(url);
      if (new URL(url).searchParams.has("cursor") && ++attempts === 1) {
        await route.fulfill({ status: 503, json: { error: { code: "UNAVAILABLE" } } });
      } else {
        await route.fulfill({ json: { style: "SP", mode: "title", summary: null,
          items: attempts ? paletteItems.slice(2, 3) : paletteItems.slice(0, 2), next_cursor: attempts ? null : "retry-page-2" } });
      }
    });
    await page.goto(`/player/${publicPlayerId}?view=best`);
    await expect(page.locator(".best-table tbody tr")).toHaveCount(2);
    await page.getByRole("button", { name: "続きを見る" }).click();
    await expect(page.getByRole("alert")).toContainText("続きを読み込めませんでした");
    await expect(page.locator(".best-table tbody tr")).toHaveCount(2);
    await expect(page.locator(".result-count")).toContainText("2譜面");
    await noOverflow(page);
    await page.screenshot({ path: `../../logs/issue-224-retry-${width}.png`, fullPage: true });
    await page.getByRole("button", { name: "続きをもう一度読み込む" }).click();
    await expect(page.locator(".best-table tbody tr")).toHaveCount(3);
    await expect(page.locator(".result-count")).toContainText("3譜面");
    await expect(page.locator(".music-title")).toHaveText(paletteItems.slice(0, 3).map((item) => item.title));
    expect(requests[1]).toBe(requests[2]);
    await expect(page.getByRole("alert")).toHaveCount(0);
    expect(new URL(page.url()).searchParams.has("cursor")).toBe(false);
  });

  test(`${width}px restores unknown and known version URLs consistently`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    const queriedVersions: string[] = [];
    page.on("request", (request) => {
      if (request.url().includes("/bests?")) queriedVersions.push(new URL(request.url()).searchParams.get("version") ?? "");
    });
    for (const version of ["UnknownVersion", "DDRMAX"]) {
      await page.goto(`/player/${publicPlayerId}?view=best&mode=version&version=${version}&sort=level_asc`);
      await expect(page.getByRole("combobox", { name: "バージョン", exact: true })).toHaveValue(version);
      await expect(page.locator(".result-count")).toContainText(version);
      if (version === "UnknownVersion") await expect(page.locator(".empty-row")).toContainText("自己ベストの対象譜面はありません");
      else await expect(page.locator(".music-title")).toHaveText(["MAX 300"]);
      expect(queriedVersions.at(-1)).toBe(version);
      await expect(page.getByRole("combobox", { name: "並び順" })).toHaveValue("level_asc");
      await noOverflow(page);
    }
    await page.getByRole("combobox", { name: "バージョン", exact: true }).selectOption("DanceDanceRevolution WORLD");
    await expect(page.locator(".music-title")).toHaveText(["VOLAQUAS"]);
  });

  test(`${width}px expands all Flare targets independently without changing totals or ranks`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await mockBrowse(page);
    await page.goto(`/player/${publicPlayerId}?view=flare`);
    const classic = page.locator('[data-flare-category="classic"]');
    const white = page.locator('[data-flare-category="white"]');
    const gold = page.locator('[data-flare-category="gold"]');
    await expect(classic.locator(".flare-target-row")).toHaveCount(10);
    await expect(white.locator(".flare-target-row")).toHaveCount(10);
    await expect(gold.locator(".flare-target-row")).toHaveCount(8);
    await expect(gold.getByRole("button")).toHaveCount(0);
    const totals = await page.locator(".flare-category-score, .flare-total-value").allTextContents();
    await classic.getByRole("button", { name: "全30件を見る" }).click();
    await expect(classic.locator(".flare-target-row")).toHaveCount(30);
    await expect(white.locator(".flare-target-row")).toHaveCount(10);
    await white.getByRole("button", { name: "全17件を見る" }).click();
    await expect(white.locator(".flare-target-row")).toHaveCount(17);
    await expect(classic.locator(".flare-target-rank")).toHaveText(Array.from({ length: 30 }, (_, index) => String(index + 1)));
    await expect(classic.locator(".flare-target-title")).toHaveText(flareFixture.categories[0].targets.map((target) => target.title));
    expect(await page.locator(".flare-category-score, .flare-total-value").allTextContents()).toEqual(totals);
    await noOverflow(page);
    await page.screenshot({ path: `../../logs/issue-224-flare-${width}.png`, fullPage: true });
  });

  test(`${width}px restores tabs, style, browse choices and sorting through browser history`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await mockBrowse(page);
    await page.goto(`/player/${publicPlayerId}`);
    await page.getByRole("tab", { name: "自己ベスト" }).click();
    await expect(page.locator(".result-count")).toContainText("全譜面");
    await page.getByRole("button", { name: "DOUBLE" }).click();
    await expect(page.locator(".music-title").first()).toContainText("DP");
    await page.getByRole("tab", { name: "レベルから" }).click();
    await expect(page.getByRole("combobox", { name: "レベル", exact: true })).toHaveValue("1");
    await page.getByRole("combobox", { name: "レベル", exact: true }).selectOption("15");
    await expect(page.locator(".chart-meta")).toHaveText(Array(9).fill("Lv.15"));
    await page.getByRole("tab", { name: "バージョンから" }).click();
    await page.getByRole("combobox", { name: "バージョン", exact: true }).selectOption("DDRMAX");
    await expect(page.locator(".music-title").first()).toHaveText("DP MAX sample AAA");
    await page.getByRole("combobox", { name: "並び順" }).selectOption("level_asc");
    await page.getByRole("tab", { name: "曲名から" }).click();
    const historyLength = await page.evaluate(() => history.length);
    for (const text of ["M", "MA", "MAX"]) await page.getByRole("searchbox", { name: "曲名" }).fill(text);
    await expect(page.locator(".music-title")).toHaveCount(16);
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
    await page.getByRole("tab", { name: "曲名から" }).click();
    await page.getByRole("combobox", { name: "並び順" }).selectOption("level_asc");
    expect(await page.evaluate(() => history.length)).toBe(historyLength);
    await page.getByRole("tab", { name: "Flare Skill", exact: true }).click();
    await expect(page.getByRole("heading", { name: "DOUBLE Flare Skill対象楽曲" })).toBeVisible();
    await page.getByRole("tab", { name: "Overview", exact: true }).click();
    await expect(page.getByRole("heading", { name: "DOUBLE Flare Skill", exact: true })).toBeVisible();
    const finalHistoryLength = await page.evaluate(() => history.length);
    await page.goBack();
    await expect(page.getByRole("heading", { name: "DOUBLE Flare Skill対象楽曲" })).toBeVisible();
    await page.goBack();
    await expect(page.getByRole("searchbox", { name: "曲名" })).toHaveValue("MAX");
    await expect(page.locator(".music-title")).toHaveCount(16);
    await page.goBack();
    await expect(page.getByRole("combobox", { name: "バージョン", exact: true })).toHaveValue("DDRMAX");
    await expect(page.getByRole("combobox", { name: "並び順" })).toHaveValue("level_asc");
    await page.goBack();
    await expect(page.getByRole("combobox", { name: "並び順" })).toHaveValue("score_desc");
    await page.goBack();
    await expect(page.getByRole("combobox", { name: "バージョン", exact: true })).toHaveValue("DanceDanceRevolution WORLD");
    await expect(page.locator(".music-title").first()).toHaveText("DP MAX sample AA+");
    await page.goBack();
    await expect(page.getByRole("combobox", { name: "レベル", exact: true })).toHaveValue("15");
    await expect(page.locator(".chart-meta")).toHaveText(Array(9).fill("Lv.15"));
    await page.goBack();
    await expect(page.getByRole("combobox", { name: "レベル", exact: true })).toHaveValue("1");
    await expect(page.locator(".chart-meta")).toHaveCount(0);
    await expect(page.locator(".empty-row")).toContainText("自己ベストの対象譜面はありません");
    await page.goBack();
    await expect(page.locator(".result-count")).toContainText("全譜面");
    await expect(page.locator(".music-title").first()).toContainText("DP");
    await page.goBack();
    await expect(page.getByRole("button", { name: "SINGLE" })).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator(".music-title").first()).toContainText("SP");
    await page.goBack();
    await expect(page.getByRole("heading", { name: "SINGLE Flare Skill", exact: true })).toBeVisible();
    const forwardStates = [
      { view: "自己ベスト", style: "SINGLE", mode: "title", count: 17, sort: "score_desc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "title", count: 17, sort: "score_desc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "level", level: "1", count: 0, sort: "score_desc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "level", level: "15", count: 9, sort: "score_desc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "version", version: "DanceDanceRevolution WORLD", count: 8, sort: "score_desc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "version", version: "DDRMAX", count: 9, sort: "score_desc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "version", version: "DDRMAX", count: 9, sort: "level_asc" },
      { view: "自己ベスト", style: "DOUBLE", mode: "title", q: "MAX", count: 16, sort: "level_asc" },
      { view: "Flare Skill", style: "DOUBLE" },
      { view: "Overview", style: "DOUBLE" },
    ];
    for (const expected of forwardStates) {
      await page.goForward();
      await expect(page.getByRole("tab", { name: expected.view, exact: true })).toHaveAttribute("aria-selected", "true");
      await expect(page.getByRole("button", { name: expected.style })).toHaveAttribute("aria-pressed", "true");
      if (expected.view === "自己ベスト") {
        await expect(page.locator(".music-title")).toHaveCount(expected.count!);
        if (expected.count! > 0) await expect(page.locator(".music-title").first()).toContainText(expected.style === "SINGLE" ? "SP" : "DP");
        else await expect(page.locator(".empty-row")).toContainText("自己ベストの対象譜面はありません");
        await expect(page.getByRole("combobox", { name: "並び順" })).toHaveValue(expected.sort!);
        if (expected.mode === "title") await expect(page.getByRole("searchbox", { name: "曲名" })).toHaveValue(expected.q ?? "");
        if (expected.mode === "level") await expect(page.getByRole("combobox", { name: "レベル", exact: true })).toHaveValue(expected.level!);
        if (expected.mode === "version") await expect(page.getByRole("combobox", { name: "バージョン", exact: true })).toHaveValue(expected.version!);
      }
      if (expected.view === "Flare Skill") await expect(page.getByRole("heading", { name: "DOUBLE Flare Skill対象楽曲" })).toBeVisible();
    }
    await expect(page.getByRole("heading", { name: "DOUBLE Flare Skill", exact: true })).toBeVisible();
    expect(await page.evaluate(() => history.length)).toBe(finalHistoryLength);
    await noOverflow(page);
    await page.getByRole("link", { name: "マイページ", exact: true }).click();
    await expect(page).toHaveURL(/\/my\/profile$/);
    await page.goBack();
    await page.getByRole("link", { name: "GP Score Log", exact: true }).click();
    await expect(page).toHaveURL("/");
    await expect(page.getByRole("heading", { name: "GP Score Logについて" })).toBeVisible();
  });
}
