import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlayerApp } from "../../src/client/App";
import type { PublicPlayer } from "../../src/client/types";

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
    expect(screen.getByText("公開Bestなし")).toBeInTheDocument();
  });

  it("shows loading, empty, and API error states", async () => {
    let resolveFetch: ((response: Response) => void) | undefined;
    const fetchMock = vi.fn().mockImplementation(() => new Promise<Response>((resolve) => { resolveFetch = resolve; }));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlayerApp player={player} />);
    fireEvent.click(screen.getByRole("tab", { name: "Best" }));
    expect(screen.getByLabelText("公開Bestを読み込んでいます")).toBeInTheDocument();
    resolveFetch?.(new Response(JSON.stringify({ style: "SP", mode: "level", summary: { active_chart_count: 2, active_published_best_count: 0 }, items: [], next_cursor: null }), { status: 200 }));
    expect(await screen.findByText("条件に一致する譜面はありません")).toBeInTheDocument();

    fetchMock.mockRejectedValueOnce(new Error("network"));
    fireEvent.change(screen.getByRole("combobox", { name: "レベル" }), { target: { value: "18" } });
    expect(await screen.findByRole("alert")).toHaveTextContent("公開データを読み込めませんでした");
  });

  it("distinguishes an existing Player with zero public Best from 404", () => {
    render(<PlayerApp player={{ ...player, styles: { SP: summary(0), DP: summary(0) } }} />);
    expect(screen.getByText("公開Bestはまだありません")).toBeInTheDocument();
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
});
