export type PublicStyle = "SP" | "DP";
export type ViewName = "overview" | "best" | "flare";
export type BrowseMode = "level" | "version" | "title";
export type BestSort = "score_desc" | "score_asc" | "ex_score_desc" | "title_asc" | "level_asc";
export type Difficulty = "BEGINNER" | "BASIC" | "DIFFICULT" | "EXPERT" | "CHALLENGE";

export interface PublicPlayer {
  public_player_id: string;
  display_name: string;
  public_bests_updated_at: string | null;
  default_style: PublicStyle;
  styles: Record<PublicStyle, {
    published_best_count: number;
    active_chart_count: number;
    active_published_best_count: number;
    levels: Array<{
      level: number;
      active_chart_count: number;
      active_published_best_count: number;
    }>;
    flare_skill: FlareSummary;
  }>;
}

export interface FlareSummary {
  total: number;
  rank: { main: string; sub: "+" | "++" | "+++" | null };
  categories: Record<"CLASSIC" | "WHITE" | "GOLD", {
    total: number;
    target_count: number;
  }>;
}

export interface PublicBestItem {
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
    rank: string;
    clear_type: string;
    flare_rank: string | null;
  };
}

export interface PublicBestsResponse {
  style: PublicStyle;
  mode: BrowseMode;
  summary: null | {
    active_chart_count: number;
    active_published_best_count: number;
  };
  items: PublicBestItem[];
  next_cursor: string | null;
}

export interface PublicFlareResponse {
  style: PublicStyle;
  total: number;
  rank: { main: string; sub: "+" | "++" | "+++" | null };
  categories: Array<{
    category: "CLASSIC" | "WHITE" | "GOLD";
    total: number;
    target_count: number;
    targets: Array<{
      chart_id: string;
      title: string;
      difficulty: Difficulty;
      level: number;
      flare_rank: string;
      flare_skill: number;
    }>;
  }>;
}

export interface PageState {
  style: PublicStyle;
  view: ViewName;
  mode: BrowseMode;
  level: number;
  version: string;
  q: string;
  sort: BestSort;
}
