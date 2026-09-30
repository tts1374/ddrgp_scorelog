import type { BestSort, BrowseMode, PageState, PublicStyle, ViewName } from "./types";

const styles = new Set<PublicStyle>(["SP", "DP"]);
const views = new Set<ViewName>(["overview", "best", "flare"]);
const modes = new Set<BrowseMode>(["level", "version", "title"]);
const sorts = new Set<BestSort>(["score_desc", "score_asc", "ex_score_desc", "title_asc", "level_asc"]);

export const defaultVersion = "DanceDanceRevolution WORLD";

export function readPageState(search: string, defaultStyle: PublicStyle): PageState {
  const query = new URLSearchParams(search);
  const style = query.get("style") as PublicStyle | null;
  const view = query.get("view") as ViewName | null;
  const mode = query.get("mode") as BrowseMode | null;
  const sort = query.get("sort") as BestSort | null;
  const parsedLevel = Number(query.get("level"));
  return {
    style: style !== null && styles.has(style) ? style : defaultStyle,
    view: view !== null && views.has(view) ? view : "overview",
    mode: mode !== null && modes.has(mode) ? mode : "level",
    level: Number.isInteger(parsedLevel) && parsedLevel >= 1 && parsedLevel <= 19 ? parsedLevel : 17,
    version: query.get("version")?.slice(0, 100) || defaultVersion,
    q: query.get("q")?.slice(0, 100) ?? "",
    sort: sort !== null && sorts.has(sort) ? sort : "score_desc",
  };
}

export function pageStateSearch(state: PageState): string {
  const query = new URLSearchParams();
  query.set("style", state.style);
  if (state.view !== "overview") query.set("view", state.view);
  if (state.view === "best") {
    query.set("mode", state.mode);
    if (state.mode === "level") query.set("level", String(state.level));
    if (state.mode === "version") query.set("version", state.version);
    if (state.mode === "title" && state.q.trim().length > 0) query.set("q", state.q.trim());
    if (state.sort !== "score_desc") query.set("sort", state.sort);
  }
  return `?${query.toString()}`;
}
