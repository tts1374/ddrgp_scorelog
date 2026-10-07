import type { BestSort, BrowseMode, PageState, PublicPlayer, PublicStyle, ViewName } from "./types";

const styles = new Set<PublicStyle>(["SP", "DP"]);
const views = new Set<ViewName>(["overview", "best", "flare"]);
const modes = new Set<BrowseMode>(["level", "version", "title"]);
const sorts = new Set<BestSort>(["score_desc", "score_asc", "ex_score_desc", "title_asc", "level_asc"]);

export const defaultVersion = "DanceDanceRevolution WORLD";

export function defaultLevel(player: PublicPlayer, style: PublicStyle): number {
  return player.styles[style].levels.reduce((maximum, row) =>
    row.active_published_best_count > 0 ? Math.max(maximum, row.level) : maximum, 0) || 1;
}

export function readPageState(search: string, player: PublicPlayer): PageState {
  const query = new URLSearchParams(search);
  const style = query.get("style") as PublicStyle | null;
  const view = query.get("view") as ViewName | null;
  const mode = query.get("mode") as BrowseMode | null;
  const sort = query.get("sort") as BestSort | null;
  const parsedLevel = Number(query.get("level"));
  const selectedStyle = style !== null && styles.has(style) ? style : player.default_style;
  return {
    style: selectedStyle,
    view: view !== null && views.has(view) ? view : "overview",
    mode: mode !== null && modes.has(mode) ? mode : "title",
    level: Number.isInteger(parsedLevel) && parsedLevel >= 1 && parsedLevel <= 19 ? parsedLevel : defaultLevel(player, selectedStyle),
    version: query.get("version")?.slice(0, 100) || defaultVersion,
    q: query.get("q")?.slice(0, 100) ?? "",
    sort: sort !== null && sorts.has(sort) ? sort : "score_desc",
  };
}

export function pageStateSearch(state: PageState): string {
  const query = new URLSearchParams();
  query.set("style", state.style);
  if (state.view !== "overview") query.set("view", state.view);
  // Keep the selected conditions when visiting another top-level tab as well.
  query.set("mode", state.mode);
  query.set("level", String(state.level));
  query.set("version", state.version);
  if (state.q.length > 0) query.set("q", state.q);
  if (state.sort !== "score_desc") query.set("sort", state.sort);
  return `?${query.toString()}`;
}
