import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlayerApp } from "../../src/client/App";
import type { PublicBestItem, PublicPlayer } from "../../src/client/types";

function bestItem(title: string, overrides: Partial<PublicBestItem> = {}): PublicBestItem {
  return { chart_id: title, title, artist: "Artist", difficulty: "EXPERT",
    level: 17, version: "DDRMAX", is_removed: false, best: null, ...overrides };
}

function bestPage(items: PublicBestItem[], nextCursor: string | null = null) {
  return new Response(JSON.stringify({ style: "SP", mode: "title", summary: null,
    items, next_cursor: nextCursor }), { status: 200 });
}

function summary(bestCount: number) {
  return {
    published_best_count: bestCount,
    active_chart_count: 2,
    active_published_best_count: bestCount > 0 ? 1 : 0,
    levels: [{ level: 17, active_chart_count: 2, active_published_best_count: bestCount > 0 ? 1 : 0 }],
    flare_skill: {
      total: bestCount > 0 ? 977 : 0,
      rank: { main: bestCount > 0 ? "NONE" : "NONE", sub: bestCount > 0 ? "+" as const : null },
      categories: {
        CLASSIC: { total: 0, target_count: 0 },
        WHITE: { total: 0, target_count: 0 },
        GOLD: { total: bestCount > 0 ? 977 : 0, target_count: bestCount > 0 ? 1 : 0 },
      },
    },
  };
}

const player: PublicPlayer = {
  public_player_id: "p_test",
  display_name: "2TEN",
  public_bests_updated_at: "2026-09-21T01:02:03.000Z",
  default_style: "SP",
  styles: { SP: summary(1), DP: summary(1) },
};

afterEach(() => vi.unstubAllGlobals());

describe("PlayerApp", () => {
  it("renders Overview from bootstrap without an initial API request and switches global style", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    expect(screen.getByRole("heading", { name: "2TEN" })).toBeInTheDocument();
    expect(screen.getByText("SINGLE Flare Skill")).toBeInTheDocument();
    expect(screen.getByText("自己ベスト譜面数")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Level別の自己ベスト" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "自己ベストを見る" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Level別の自己ベスト集計について" })).toHaveAttribute("data-tip", expect.stringContaining("公開された自己ベストあり"));
    expect(fetchMock).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "DOUBLE" }));
    expect(screen.getByText("DOUBLE Flare Skill")).toBeInTheDocument();
    expect(window.location.search).toContain("style=DP");
  });

  it("restores a deep-linked Best state and shows a missing public Best", async () => {
    window.history.replaceState(null, "", "/player/p_test?style=DP&view=best&mode=title&q=max&sort=title_asc");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      style: "DP", mode: "title", summary: null, next_cursor: null,
      items: [{
        chart_id: "chart_1", title: "MAX 300", artist: "Ω", difficulty: "EXPERT",
        level: 15, version: "DDRMAX", is_removed: false, best: null,
      }],
    }), { status: 200, headers: { "Content-Type": "application/json" } })));
    render(<PlayerApp player={player} />);
    expect(screen.getByRole("button", { name: "DOUBLE" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("searchbox", { name: "曲名" })).toHaveValue("max");
    expect(screen.getByRole("combobox", { name: "並び順" })).toHaveValue("title_asc");
    expect(await screen.findByText("MAX 300")).toBeInTheDocument();
    const row = screen.getByText("MAX 300").closest("tr")!;
    for (const label of ["SCORE", "EX SCORE", "RANK", "CLEAR", "FLARE"]) {
      expect(row.querySelector(`[data-label="${label}"]`)).toHaveTextContent("—");
    }
    expect(row).not.toHaveTextContent("公開Bestなし");
    expect(row).not.toHaveTextContent("未プレー");
    expect(screen.getByRole("button", { name: "自己ベスト一覧について" })).toHaveAttribute(
      "data-tip", expect.stringContaining("「—」は公開された記録がないことを表し、未プレーを意味しません。"),
    );
  });

  it("shows loading, empty, and API error states", async () => {
    let resolveFetch: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn().mockImplementation(() => new Promise<Response>((resolve) => { resolveFetch = resolve; }));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    fireEvent.click(screen.getByRole("tab", { name: "自己ベスト" }));
    expect(screen.getByLabelText("自己ベストを読み込んでいます")).toBeInTheDocument();
    resolveFetch?.(new Response(JSON.stringify({ style: "SP", mode: "level", summary: { active_chart_count: 2, active_published_best_count: 0 }, items: [], next_cursor: null }), { status: 200 }));
    expect(await screen.findByText("条件に一致する自己ベストの対象譜面はありません")).toBeInTheDocument();

    fetchMock.mockRejectedValueOnce(new Error("network"));
    fireEvent.click(screen.getByRole("tab", { name: "レベルから" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("公開データを読み込めませんでした");
  });

  it("keeps loaded rows on additional-page failure and retries that cursor exactly once", async () => {
    window.history.replaceState(null, "", "/player/p_test?view=best");
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(bestPage([bestItem("First"), bestItem("Second")], "same-page-2"))
      .mockResolvedValueOnce(new Response("", { status: 503 }))
      .mockResolvedValueOnce(bestPage([bestItem("Third")]));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    expect(await screen.findByText("First")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "続きを見る" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("続きを読み込めませんでした");
    expect(document.querySelectorAll(".best-table tbody tr")).toHaveLength(2);
    expect(screen.getByText("2譜面")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "続きをもう一度読み込む" }));
    expect(await screen.findByText("Third")).toBeInTheDocument();
    expect(document.querySelectorAll(".best-table tbody tr")).toHaveLength(3);
    expect(screen.getByText("3譜面")).toBeInTheDocument();
    expect(screen.getAllByText("First")).toHaveLength(1);
    expect(fetchMock.mock.calls[1][0]).toBe(fetchMock.mock.calls[2][0]);
    expect(fetchMock.mock.calls[2][0]).toContain("cursor=same-page-2");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it.each(["UnknownVersion", "DDRMAX"])("restores the exact version option and API query for %s", async (version) => {
    window.history.replaceState(null, "", `/player/p_test?view=best&mode=version&version=${version}&sort=level_asc`);
    const fetchMock = vi.fn().mockResolvedValue(bestPage([bestItem("Version result")]));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    expect(await screen.findByText("Version result")).toBeInTheDocument();
    const select = screen.getByRole("combobox", { name: "バージョン" });
    expect(select).toHaveValue(version);
    expect(within(select).getByRole("option", { name: version })).toBeInTheDocument();
    expect(document.querySelector(".result-count")).toHaveTextContent(version);
    const query = new URL(fetchMock.mock.calls[0][0], window.location.origin).searchParams;
    expect(query.get("version")).toBe(version);
    expect(query.get("sort")).toBe("level_asc");
    fireEvent.change(select, { target: { value: "DDR X" } });
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock.mock.calls[1][0]).toContain("version=DDR+X");
  });

  it("initially browses all charts across levels and versions with the existing empty title query", async () => {
    window.history.replaceState(null, "", "/player/p_test?view=best");
    const fetchMock = vi.fn().mockResolvedValue(bestPage([
      bestItem("Lv15 DDRMAX", { level: 15 }),
      bestItem("Lv17 WORLD", { version: "DanceDanceRevolution WORLD" }),
    ], "more"));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    expect(await screen.findByText("Lv15 DDRMAX")).toBeInTheDocument();
    expect(screen.getByText("Lv17 WORLD")).toBeInTheDocument();
    expect(document.querySelector(".result-count")).toHaveTextContent("全譜面");
    expect(screen.getByRole("searchbox", { name: "曲名" })).toHaveValue("");
    const query = new URL(fetchMock.mock.calls[0][0], window.location.origin).searchParams;
    expect(query.get("mode")).toBe("title");
    expect(query.get("sort")).toBe("score_desc");
    for (const key of ["level", "version", "q"]) expect(query.has(key)).toBe(false);
    expect(screen.getByRole("button", { name: "続きを見る" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "レベルから" }));
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(fetchMock.mock.calls[1][0]).toContain("level=17");
  });

  it.each(["SP", "DP"] as const)("defaults level browsing to the highest recorded active level for %s", async (style) => {
    window.history.replaceState(null, "", `/player/p_test?style=${style}&view=best`);
    const levels = (maximum: number) => [
      { level: 19, active_chart_count: 2, active_published_best_count: 0 },
      { level: maximum, active_chart_count: 2, active_published_best_count: 1 },
      { level: 5, active_chart_count: 2, active_published_best_count: 1 },
    ];
    const customPlayer = { ...player, styles: {
      SP: { ...summary(2), levels: levels(16) }, DP: { ...summary(2), levels: levels(13) },
    } };
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(bestPage([bestItem("Recorded")])));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={customPlayer} />);
    expect(await screen.findByText("Recorded")).toBeInTheDocument();
    expect(document.querySelector(".result-count")).toHaveTextContent("全譜面");
    fireEvent.click(screen.getByRole("tab", { name: "レベルから" }));
    const initialLevel = style === "SP" ? 16 : 13;
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue(String(initialLevel));
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)?.[0]).toContain(`level=${initialLevel}`));
    fireEvent.change(screen.getByRole("combobox", { name: "レベル" }), { target: { value: "11" } });
    fireEvent.click(screen.getByRole("button", { name: style === "SP" ? "DOUBLE" : "SINGLE" }));
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue(style === "SP" ? "13" : "16");
    fireEvent.click(screen.getByRole("button", { name: style === "SP" ? "SINGLE" : "DOUBLE" }));
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue("11");
  });

  it.each(["", "&level=invalid"])("keeps the fallback for styles without active public records (%s)", async (suffix) => {
    window.history.replaceState(null, "", `/player/p_test?style=DP&view=best&mode=level${suffix}`);
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(bestPage([])));
    render(<PlayerApp player={{ ...player, styles: { ...player.styles, DP: summary(0) } }} />);
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue("17");
    expect(await screen.findByText("条件に一致する自己ベストの対象譜面はありません")).toBeInTheDocument();
  });

  it("prioritizes an explicit URL level and restores it without adding history", async () => {
    window.history.replaceState(null, "", "/player/p_test?style=DP&view=best&mode=level&level=12");
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(bestPage([])));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue("12");
    await waitFor(() => expect(fetchMock.mock.calls.at(-1)?.[0]).toContain("level=12"));
    fireEvent.change(screen.getByRole("combobox", { name: "レベル" }), { target: { value: "10" } });
    const historyLength = history.length;
    window.history.replaceState(null, "", "/player/p_test?style=DP&view=best&mode=level&level=12");
    act(() => window.dispatchEvent(new PopStateEvent("popstate")));
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue("12");
    expect(history.length).toBe(historyLength);
    fireEvent.click(screen.getByRole("button", { name: "SINGLE" }));
    fireEvent.click(screen.getByRole("button", { name: "DOUBLE" }));
    expect(screen.getByRole("combobox", { name: "レベル" })).toHaveValue("12");
  });

  it("expands each Flare category independently using its actual target count", async () => {
    const categories = (["CLASSIC", "WHITE", "GOLD"] as const).map((category, index) => ({
      category, total: 12345, target_count: [30, 17, 8][index],
      targets: Array.from({ length: [30, 17, 8][index] }, (_, row) => ({
        chart_id: `${category}-${row}`, title: `${category} song ${row + 1}`,
        difficulty: "EXPERT", level: 17, flare_rank: "EX", flare_skill: 1000 - row,
      })),
    }));
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      style: "SP", total: 37035, rank: { main: "WORLD", sub: null }, categories,
    }))));
    render(<PlayerApp player={player} />);
    fireEvent.click(screen.getByRole("tab", { name: "Flare Skill" }));
    await screen.findByText("CLASSIC song 1");
    const panels = Array.from(document.querySelectorAll(".flare-category-panel"));
    expect(panels.map((panel) => panel.querySelectorAll(".flare-target-row").length)).toEqual([10, 10, 8]);
    expect(within(panels[2] as HTMLElement).queryByRole("button")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "全30件を見る" }));
    expect(panels.map((panel) => panel.querySelectorAll(".flare-target-row").length)).toEqual([30, 10, 8]);
    fireEvent.click(screen.getByRole("button", { name: "全17件を見る" }));
    expect(panels.map((panel) => panel.querySelectorAll(".flare-target-row").length)).toEqual([30, 17, 8]);
    expect(Array.from(panels[0].querySelectorAll(".flare-target-rank")).map((row) => row.textContent))
      .toEqual(Array.from({ length: 30 }, (_, index) => String(index + 1)));
    expect(screen.getByText("37,035")).toBeInTheDocument();
    expect(panels.every((panel) => panel.querySelector(".flare-category-score")?.textContent === "12,345")).toBe(true);
  });

  it.each(["mode", "style", "sort"])("ignores an old load-more retry after %s changes", async (condition) => {
    let finishOldPage: ((response: Response) => void) | undefined;
    const response = (title: string, nextCursor: string | null) => new Response(JSON.stringify({
      style: "SP", mode: "title", summary: null, next_cursor: nextCursor,
      items: [{
        chart_id: title, title, artist: "Artist", difficulty: "EXPERT",
        level: 17, version: "DDRMAX", is_removed: false, best: null,
      }],
    }), { status: 200 });
    let additionalRequests = 0;
    vi.stubGlobal("fetch", vi.fn((url: string) => {
      if (url.includes("cursor=")) {
        if (++additionalRequests === 1) return Promise.resolve(new Response("", { status: 503 }));
        return new Promise<Response>((resolve) => { finishOldPage = resolve; });
      }
      const changed = url.includes("mode=version") || url.includes("style=DP") || url.includes("sort=title_asc");
      return Promise.resolve(response(changed ? "New result" : "Old result", changed ? null : "page-2"));
    }));
    window.history.replaceState(null, "", "/player/p_test?style=SP&view=best&mode=title");
    render(<PlayerApp player={player} />);
    expect(await screen.findByText("Old result")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "続きを見る" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("続きを読み込めませんでした");
    expect(screen.getByText("Old result")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "続きをもう一度読み込む" }));
    await waitFor(() => expect(finishOldPage).toBeDefined());
    if (condition === "mode") fireEvent.click(screen.getByRole("tab", { name: "バージョンから" }));
    if (condition === "style") fireEvent.click(screen.getByRole("button", { name: "DOUBLE" }));
    if (condition === "sort") fireEvent.change(screen.getByRole("combobox", { name: "並び順" }), { target: { value: "title_asc" } });
    expect(await screen.findByText("New result")).toBeInTheDocument();
    await act(async () => { finishOldPage!(response("Old next page", null)); });
    expect(screen.getByText("New result")).toBeInTheDocument();
    expect(screen.queryByText("Old next page")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("distinguishes an existing Player with zero public Best from 404", () => {
    render(<PlayerApp player={{ ...player, styles: { SP: summary(0), DP: summary(0) } }} />);
    expect(screen.getByText("自己ベストはまだありません")).toBeInTheDocument();
    expect(screen.queryByText("Playerが見つかりません")).not.toBeInTheDocument();
  });

  it("loads Flare Skill on demand and displays category targets", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      style: "SP", total: 977, rank: { main: "NONE", sub: "+" },
      categories: [
        { category: "CLASSIC", total: 0, target_count: 0, targets: [] },
        { category: "WHITE", total: 0, target_count: 0, targets: [] },
        { category: "GOLD", total: 977, target_count: 1, targets: [{ chart_id: "chart_1", title: "MAX 300", difficulty: "EXPERT", level: 17, flare_rank: "IX", flare_skill: 977 }] },
      ],
    }), { status: 200 })));
    render(<PlayerApp player={player} />);
    fireEvent.click(screen.getByRole("tab", { name: "Flare Skill" }));
    expect(await screen.findByText("MAX 300")).toBeInTheDocument();
    expect(screen.getByText("Top 1 / 1譜面")).toBeInTheDocument();
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
  });

  it("ignores an old Flare Skill response after switching style", async () => {
    let finishSingle: ((response: Response) => void) | undefined;
    const response = (style: "SP" | "DP", title: string) => new Response(JSON.stringify({
      style, total: 977, rank: { main: "NONE", sub: "+" },
      categories: [
        { category: "CLASSIC", total: 0, target_count: 0, targets: [] },
        { category: "WHITE", total: 0, target_count: 0, targets: [] },
        { category: "GOLD", total: 977, target_count: 1, targets: [{
          chart_id: title, title, difficulty: "EXPERT", level: 17,
          flare_rank: "IX", flare_skill: 977,
        }] },
      ],
    }), { status: 200 });
    vi.stubGlobal("fetch", vi.fn((url: string) => url.includes("style=SP")
      ? new Promise<Response>((resolve) => { finishSingle = resolve; })
      : Promise.resolve(response("DP", "Current DP"))));
    render(<PlayerApp player={player} />);
    fireEvent.click(screen.getByRole("tab", { name: "Flare Skill" }));
    await waitFor(() => expect(finishSingle).toBeDefined());
    fireEvent.click(screen.getByRole("button", { name: "DOUBLE" }));
    expect(await screen.findByText("Current DP")).toBeInTheDocument();
    await act(async () => { finishSingle!(response("SP", "Old SP")); });
    expect(screen.getByText("Current DP")).toBeInTheDocument();
    expect(screen.queryByText("Old SP")).not.toBeInTheDocument();
  });
});
