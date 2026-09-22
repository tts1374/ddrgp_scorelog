import { Hono } from "hono";
import type { Context } from "hono";
import type { AppEnvironment } from "./index";
import {
  calculateFlareSkill,
  difficultyOrder,
  scoreRank,
  type Difficulty,
  type FlareRank,
  type PublicStyle,
} from "./public-domain";

interface PlayerRow {
  id: string;
  public_player_id: string;
  display_name: string;
  public_bests_updated_at: string | null;
}

interface StyleAggregateRow {
  play_style: "SINGLE" | "DOUBLE";
  active_chart_count: number;
  active_published_best_count: number;
}

interface PublishedCountRow {
  play_style: "SINGLE" | "DOUBLE";
  published_best_count: number;
}

interface LevelAggregateRow {
  play_style: "SINGLE" | "DOUBLE";
  level: number;
  active_chart_count: number;
  active_published_best_count: number;
}

interface FlareRow {
  play_style: "SINGLE" | "DOUBLE";
  chart_id: string;
  title: string;
  difficulty: Difficulty;
  level: number;
  version: string;
  best_flare_rank: FlareRank;
}

export interface PublicPlayerV1 {
  public_player_id: string;
  display_name: string;
  public_bests_updated_at: string | null;
  default_style: PublicStyle;
  styles: Record<PublicStyle, PublicPlayerStyleSummaryV1>;
}

interface PublicPlayerStyleSummaryV1 {
  published_best_count: number;
  active_chart_count: number;
  active_published_best_count: number;
  levels: Array<{
    level: number;
    active_chart_count: number;
    active_published_best_count: number;
  }>;
  flare_skill: ReturnType<typeof publicFlareSummary>;
}

interface BestRow {
  chart_id: string;
  title: string;
  artist: string;
  difficulty: Difficulty;
  level: number;
  version: string;
  is_removed: number;
  best_score: number | null;
  best_ex_score: number | null;
  best_clear_type: "MFC" | "PFC" | "GFC" | "FC" | "CLEAR" | "FAILED" | null;
  best_flare_rank: FlareRank | null;
}

export interface PublicChartBestItemV1 {
  chart_id: string;
  title: string;
  artist: string;
  difficulty: Difficulty;
  level: number;
  version: string;
  is_removed: boolean;
  best: null | {
    score: number;
    ex_score: number;
    rank: ReturnType<typeof scoreRank>;
    clear_type: NonNullable<BestRow["best_clear_type"]>;
    flare_rank: FlareRank | null;
  };
}

type BrowseMode = "level" | "version" | "title";
type BestSort = "score_desc" | "score_asc" | "ex_score_desc" | "title_asc" | "level_asc";

interface BestQuery {
  style: PublicStyle;
  mode: BrowseMode;
  level: number | null;
  version: string | null;
  q: string;
  sort: BestSort;
  limit: number;
  cursor: string | null;
}

interface CursorPayload {
  v: 1;
  scope: string;
  sort: BestSort;
  chart_id: string;
  title: string;
  difficulty: Difficulty;
  level: number;
  score: number | null;
  ex_score: number | null;
}

const publicPlayerIdPattern = /^p_[A-Za-z0-9_-]{20,64}$/u;
const styles = ["SP", "DP"] as const;
const difficulties = new Set(Object.keys(difficultyOrder));
const modes = new Set<BrowseMode>(["level", "version", "title"]);
const sorts = new Set<BestSort>([
  "score_desc", "score_asc", "ex_score_desc", "title_asc", "level_asc",
]);

function errorResponse(
  c: Context<AppEnvironment>,
  status: 400 | 404 | 500,
  code: string,
  message: string,
): Response {
  return c.json({ error: { code, message } }, status);
}

function toStyle(playStyle: "SINGLE" | "DOUBLE"): PublicStyle {
  return playStyle === "SINGLE" ? "SP" : "DP";
}

function toPlayStyle(style: PublicStyle): "SINGLE" | "DOUBLE" {
  return style === "SP" ? "SINGLE" : "DOUBLE";
}

async function findPlayer(db: D1Database, publicPlayerId: string): Promise<PlayerRow | null> {
  if (!publicPlayerIdPattern.test(publicPlayerId)) return null;
  return db.prepare(
    `SELECT id, public_player_id, display_name, public_bests_updated_at
     FROM players
     WHERE public_player_id = ?1`,
  ).bind(publicPlayerId).first<PlayerRow>();
}

function publicFlareSummary(result: ReturnType<typeof calculateFlareSkill>) {
  return {
    total: result.total,
    rank: result.rank,
    categories: Object.fromEntries(result.categories.map((category) => [
      category.category,
      { total: category.total, target_count: category.target_count },
    ])) as Record<"CLASSIC" | "WHITE" | "GOLD", { total: number; target_count: number }>,
  };
}

export async function loadPublicPlayer(
  db: D1Database,
  publicPlayerId: string,
): Promise<PublicPlayerV1 | null> {
  const player = await findPlayer(db, publicPlayerId);
  if (player === null) return null;

  const [styleResult, publishedResult, levelResult, flareResult] = await Promise.all([
    db.prepare(
      `SELECT c.play_style,
              COUNT(*) AS active_chart_count,
              SUM(CASE WHEN b.chart_id IS NOT NULL THEN 1 ELSE 0 END) AS active_published_best_count
       FROM charts c
       LEFT JOIN player_chart_bests b
         ON b.chart_id = c.chart_id AND b.player_id = ?1
       WHERE c.is_removed = 0
       GROUP BY c.play_style`,
    ).bind(player.id).all<StyleAggregateRow>(),
    db.prepare(
      `SELECT c.play_style, COUNT(*) AS published_best_count
       FROM player_chart_bests b
       JOIN charts c ON c.chart_id = b.chart_id
       WHERE b.player_id = ?1
       GROUP BY c.play_style`,
    ).bind(player.id).all<PublishedCountRow>(),
    db.prepare(
      `SELECT c.play_style,
              c.level,
              COUNT(*) AS active_chart_count,
              SUM(CASE WHEN b.chart_id IS NOT NULL THEN 1 ELSE 0 END) AS active_published_best_count
       FROM charts c
       LEFT JOIN player_chart_bests b
         ON b.chart_id = c.chart_id AND b.player_id = ?1
       WHERE c.is_removed = 0
       GROUP BY c.play_style, c.level
       ORDER BY c.play_style, c.level`,
    ).bind(player.id).all<LevelAggregateRow>(),
    db.prepare(
      `SELECT c.play_style, c.chart_id, s.title, c.difficulty, c.level, s.version,
              b.best_flare_rank
       FROM player_chart_bests b
       JOIN charts c ON c.chart_id = b.chart_id
       JOIN songs s ON s.song_id = c.song_id
       WHERE b.player_id = ?1
         AND b.best_flare_rank IS NOT NULL
         AND c.is_removed = 0`,
    ).bind(player.id).all<FlareRow>(),
  ]);

  const summaries = Object.fromEntries(styles.map((style) => {
    const playStyle = toPlayStyle(style);
    const aggregate = styleResult.results.find((row) => row.play_style === playStyle);
    const published = publishedResult.results.find((row) => row.play_style === playStyle);
    const flare = calculateFlareSkill(flareResult.results
      .filter((row) => row.play_style === playStyle)
      .map((row) => ({
        chart_id: row.chart_id,
        title: row.title,
        difficulty: row.difficulty,
        level: row.level,
        version: row.version,
        flare_rank: row.best_flare_rank,
      })));
    return [style, {
      published_best_count: Number(published?.published_best_count ?? 0),
      active_chart_count: Number(aggregate?.active_chart_count ?? 0),
      active_published_best_count: Number(aggregate?.active_published_best_count ?? 0),
      levels: levelResult.results
        .filter((row) => row.play_style === playStyle)
        .map((row) => ({
          level: Number(row.level),
          active_chart_count: Number(row.active_chart_count),
          active_published_best_count: Number(row.active_published_best_count),
        })),
      flare_skill: publicFlareSummary(flare),
    } satisfies PublicPlayerStyleSummaryV1];
  })) as Record<PublicStyle, PublicPlayerStyleSummaryV1>;

  return {
    public_player_id: player.public_player_id,
    display_name: player.display_name,
    public_bests_updated_at: player.public_bests_updated_at,
    default_style: summaries.SP.published_best_count > 0 || summaries.DP.published_best_count === 0
      ? "SP"
      : "DP",
    styles: summaries,
  };
}

function parsePositiveInteger(value: string | undefined): number | null {
  if (value === undefined || !/^\d+$/u.test(value)) return null;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : null;
}

function parseBestQuery(url: URL): BestQuery | null {
  const style = url.searchParams.get("style");
  const mode = url.searchParams.get("mode");
  const sort = url.searchParams.get("sort") ?? "score_desc";
  const limitValue = url.searchParams.get("limit");
  if (!styles.includes(style as PublicStyle) || !modes.has(mode as BrowseMode) || !sorts.has(sort as BestSort)) {
    return null;
  }
  const limit = limitValue === null ? 50 : parsePositiveInteger(limitValue);
  if (limit === null || limit < 1 || limit > 100) return null;

  const levelValue = url.searchParams.get("level") ?? undefined;
  const level = levelValue === undefined ? null : parsePositiveInteger(levelValue);
  const version = url.searchParams.get("version");
  const q = (url.searchParams.get("q") ?? "").trim();
  if (
    (mode === "level" && (level === null || level < 1 || level > 19)) ||
    (mode !== "level" && levelValue !== undefined) ||
    (mode === "version" && (version === null || version.length < 1 || version.length > 100)) ||
    (mode !== "version" && version !== null) ||
    (mode !== "title" && url.searchParams.has("q")) ||
    q.length > 100
  ) return null;

  const cursor = url.searchParams.get("cursor");
  if (cursor !== null && cursor.length > 2048) return null;
  return {
    style: style as PublicStyle,
    mode: mode as BrowseMode,
    level,
    version,
    q,
    sort: sort as BestSort,
    limit,
    cursor,
  };
}

function toItem(row: BestRow): PublicChartBestItemV1 {
  const best = row.best_score === null ? null : {
    score: Number(row.best_score),
    ex_score: Number(row.best_ex_score),
    rank: scoreRank(Number(row.best_score)),
    clear_type: row.best_clear_type!,
    flare_rank: row.best_flare_rank,
  };
  return {
    chart_id: row.chart_id,
    title: row.title,
    artist: row.artist,
    difficulty: row.difficulty,
    level: Number(row.level),
    version: row.version,
    is_removed: Boolean(row.is_removed),
    best,
  };
}

function compareText(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function compareItems(left: PublicChartBestItemV1, right: PublicChartBestItemV1, sort: BestSort): number {
  const leftMissing = left.best === null ? 1 : 0;
  const rightMissing = right.best === null ? 1 : 0;
  if ((sort === "score_desc" || sort === "score_asc" || sort === "ex_score_desc") && leftMissing !== rightMissing) {
    return leftMissing - rightMissing;
  }
  if (left.best !== null && right.best !== null) {
    if (sort === "score_desc" && left.best.score !== right.best.score) return right.best.score - left.best.score;
    if (sort === "score_asc" && left.best.score !== right.best.score) return left.best.score - right.best.score;
    if (sort === "ex_score_desc" && left.best.ex_score !== right.best.ex_score) return right.best.ex_score - left.best.ex_score;
  }
  if (sort === "level_asc" && left.level !== right.level) return left.level - right.level;
  return compareText(left.title, right.title) ||
    difficultyOrder[left.difficulty] - difficultyOrder[right.difficulty] ||
    compareText(left.chart_id, right.chart_id);
}

function queryScope(query: BestQuery): string {
  return JSON.stringify([query.style, query.mode, query.level, query.version, query.q]);
}

function cursorItem(payload: CursorPayload): PublicChartBestItemV1 {
  return {
    chart_id: payload.chart_id,
    title: payload.title,
    artist: "",
    difficulty: payload.difficulty,
    level: payload.level,
    version: "",
    is_removed: false,
    best: payload.score === null ? null : {
      score: payload.score,
      ex_score: payload.ex_score!,
      rank: scoreRank(payload.score),
      clear_type: "CLEAR",
      flare_rank: null,
    },
  };
}

function encodeCursor(item: PublicChartBestItemV1, query: BestQuery): string {
  const payload: CursorPayload = {
    v: 1,
    scope: queryScope(query),
    sort: query.sort,
    chart_id: item.chart_id,
    title: item.title,
    difficulty: item.difficulty,
    level: item.level,
    score: item.best?.score ?? null,
    ex_score: item.best?.ex_score ?? null,
  };
  const bytes = new TextEncoder().encode(JSON.stringify(payload));
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

export function decodeCursor(value: string, query: BestQuery): CursorPayload | null {
  try {
    const padded = value.replaceAll("-", "+").replaceAll("_", "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
    const binary = atob(padded);
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    const payload = JSON.parse(new TextDecoder().decode(bytes)) as Partial<CursorPayload>;
    if (
      payload.v !== 1 || payload.scope !== queryScope(query) || payload.sort !== query.sort ||
      typeof payload.chart_id !== "string" || payload.chart_id.length < 1 || payload.chart_id.length > 128 ||
      typeof payload.title !== "string" || payload.title.length > 500 ||
      typeof payload.difficulty !== "string" || !difficulties.has(payload.difficulty) ||
      !Number.isInteger(payload.level) || payload.level! < 1 || payload.level! > 19 ||
      !(payload.score === null || (Number.isInteger(payload.score) && payload.score! >= 0 && payload.score! <= 1_000_000)) ||
      !(payload.ex_score === null || (Number.isSafeInteger(payload.ex_score) && payload.ex_score! >= 0)) ||
      (payload.score === null) !== (payload.ex_score === null)
    ) return null;
    return payload as CursorPayload;
  } catch {
    return null;
  }
}

async function loadBestRows(db: D1Database, playerId: string, query: BestQuery): Promise<BestRow[]> {
  let filter = "";
  const bindings: unknown[] = [playerId, toPlayStyle(query.style)];
  if (query.mode === "level") {
    filter = " AND c.level = ?3";
    bindings.push(query.level);
  } else if (query.mode === "version") {
    filter = " AND s.version = ?3";
    bindings.push(query.version);
  }
  const result = await db.prepare(
    `SELECT c.chart_id, s.title, s.artist, c.difficulty, c.level, s.version, c.is_removed,
            b.best_score, b.best_ex_score, b.best_clear_type, b.best_flare_rank
     FROM charts c
     JOIN songs s ON s.song_id = c.song_id
     LEFT JOIN player_chart_bests b
       ON b.chart_id = c.chart_id AND b.player_id = ?1
     WHERE c.play_style = ?2
       AND (c.is_removed = 0 OR b.chart_id IS NOT NULL)${filter}`,
  ).bind(...bindings).all<BestRow>();
  const normalizedQuery = query.q.toLocaleLowerCase("ja-JP");
  return query.mode === "title" && normalizedQuery.length > 0
    ? result.results.filter((row) => row.title.toLocaleLowerCase("ja-JP").includes(normalizedQuery))
    : result.results;
}

async function loadSummary(db: D1Database, playerId: string, query: BestQuery) {
  if (query.mode === "title") return null;
  const field = query.mode === "level" ? "c.level" : "s.version";
  const value = query.mode === "level" ? query.level : query.version;
  const row = await db.prepare(
    `SELECT COUNT(*) AS active_chart_count,
            SUM(CASE WHEN b.chart_id IS NOT NULL THEN 1 ELSE 0 END) AS active_published_best_count
     FROM charts c
     JOIN songs s ON s.song_id = c.song_id
     LEFT JOIN player_chart_bests b
       ON b.chart_id = c.chart_id AND b.player_id = ?1
     WHERE c.play_style = ?2 AND c.is_removed = 0 AND ${field} = ?3`,
  ).bind(playerId, toPlayStyle(query.style), value).first<{
    active_chart_count: number;
    active_published_best_count: number;
  }>();
  return {
    active_chart_count: Number(row?.active_chart_count ?? 0),
    active_published_best_count: Number(row?.active_published_best_count ?? 0),
  };
}

export function registerPublicPlayerRoutes(app: Hono<AppEnvironment>): void {
  app.get("/api/v1/public/players/:publicPlayerId", async (c) => {
    const player = await loadPublicPlayer(c.env.DB, c.req.param("publicPlayerId"));
    return player === null
      ? errorResponse(c, 404, "PLAYER_NOT_FOUND", "The player was not found.")
      : c.json(player);
  });

  app.get("/api/v1/public/players/:publicPlayerId/bests", async (c) => {
    const query = parseBestQuery(new URL(c.req.url));
    if (query === null) return errorResponse(c, 400, "INVALID_QUERY", "The query is invalid.");
    const player = await findPlayer(c.env.DB, c.req.param("publicPlayerId"));
    if (player === null) return errorResponse(c, 404, "PLAYER_NOT_FOUND", "The player was not found.");
    const cursor = query.cursor === null ? null : decodeCursor(query.cursor, query);
    if (query.cursor !== null && cursor === null) {
      return errorResponse(c, 400, "INVALID_CURSOR", "The cursor is invalid.");
    }
    const [rows, summary] = await Promise.all([
      loadBestRows(c.env.DB, player.id, query),
      loadSummary(c.env.DB, player.id, query),
    ]);
    const ordered = rows.map(toItem).sort((left, right) => compareItems(left, right, query.sort));
    const start = cursor === null
      ? 0
      : ordered.findIndex((item) => compareItems(item, cursorItem(cursor), query.sort) > 0);
    const remaining = start < 0 ? [] : ordered.slice(start);
    const items = remaining.slice(0, query.limit);
    const hasMore = remaining.length > query.limit;
    return c.json({
      style: query.style,
      mode: query.mode,
      summary,
      items,
      next_cursor: hasMore ? encodeCursor(items.at(-1)!, query) : null,
    });
  });

  app.get("/api/v1/public/players/:publicPlayerId/flare-skill", async (c) => {
    const style = new URL(c.req.url).searchParams.get("style");
    if (!styles.includes(style as PublicStyle)) {
      return errorResponse(c, 400, "INVALID_QUERY", "The query is invalid.");
    }
    const player = await findPlayer(c.env.DB, c.req.param("publicPlayerId"));
    if (player === null) return errorResponse(c, 404, "PLAYER_NOT_FOUND", "The player was not found.");
    const result = await c.env.DB.prepare(
      `SELECT c.chart_id, s.title, c.difficulty, c.level, s.version, b.best_flare_rank
       FROM player_chart_bests b
       JOIN charts c ON c.chart_id = b.chart_id
       JOIN songs s ON s.song_id = c.song_id
       WHERE b.player_id = ?1
         AND c.play_style = ?2
         AND c.is_removed = 0
         AND b.best_flare_rank IS NOT NULL`,
    ).bind(player.id, toPlayStyle(style as PublicStyle)).all<FlareRow>();
    const flare = calculateFlareSkill(result.results.map((row) => ({
      chart_id: row.chart_id,
      title: row.title,
      difficulty: row.difficulty,
      level: Number(row.level),
      version: row.version,
      flare_rank: row.best_flare_rank,
    })));
    return c.json({ style, ...flare });
  });
}
