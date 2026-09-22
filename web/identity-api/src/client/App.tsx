import {
  useDeferredValue,
  useEffect,
  useMemo,
  useState,
} from "react";
import { fetchBests, fetchFlareSkill } from "./api";
import type {
  BrowseMode,
  Difficulty,
  PageState,
  PublicBestItem,
  PublicBestsResponse,
  PublicFlareResponse,
  PublicPlayer,
  ViewName,
} from "./types";
import { pageStateSearch, readPageState } from "./url-state";

const numberFormat = new Intl.NumberFormat("ja-JP");
const dateFormat = new Intl.DateTimeFormat("ja-JP", {
  year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit",
});
const versions = [
  "DDR GRAND PRIX", "DanceDanceRevolution WORLD", "DanceDanceRevolution A3",
  "DanceDanceRevolution A20 PLUS", "DanceDanceRevolution A20", "DanceDanceRevolution A",
  "DanceDanceRevolution (2014)", "DanceDanceRevolution (2013)", "DDR X3 VS 2ndMIX",
  "DDR X2", "DDR X", "DDR SuperNOVA 2", "DDR SuperNOVA", "DDR EXTREME", "DDRMAX2",
  "DDRMAX", "DDR 5thMIX", "DDR 4thMIX", "DDR 3rdMIX", "DDR 2ndMIX", "DDR 1st",
];
const difficultyClasses: Record<Difficulty, string> = {
  BEGINNER: "diff-bgn", BASIC: "diff-bas", DIFFICULT: "diff-dif",
  EXPERT: "diff-exp", CHALLENGE: "diff-cha",
};
const rankJapanese: Record<string, string> = {
  NONE: "なし", MERCURY: "水星", VENUS: "金星", EARTH: "地球", MARS: "火星",
  JUPITER: "木星", SATURN: "土星", URANUS: "天王星", NEPTUNE: "海王星",
  SUN: "太陽", WORLD: "世界",
};

function LoadingState({ label = "Player Dataを読み込んでいます" }: { label?: string }) {
  return <div className="state-screen" aria-live="polite"><div className="skeleton-stack" aria-label={label}>
    <div className="skeleton-line" /><div className="skeleton-line short" />
    <div className="skeleton-block" /><div className="skeleton-block" />
  </div></div>;
}

function ErrorState({ onRetry }: { onRetry: () => void }) {
  return <div className="state-screen" role="alert"><div className="state-content">
    <h2>公開データを読み込めませんでした</h2>
    <p>通信状態を確認して、もう一度お試しください。</p>
    <button className="text-button" type="button" onClick={onRetry}>もう一度読み込む</button>
  </div></div>;
}

function InfoTip({ label, children }: { label: string; children: string }) {
  return <button className="info-tip" type="button" aria-label={label} data-tip={children}>i</button>;
}

function FlareSummaryView({ summary }: { summary: PublicPlayer["styles"]["SP"]["flare_skill"] }) {
  return <>
    <div className="flare-summary">
      <div className="flare-summary-block">
        <span className="flare-summary-label">TOTAL FLARE SKILL RANK</span>
        <strong className="flare-rank-name">
          <span className="flare-rank-main">{summary.rank.main}</span>
          {summary.rank.sub !== null ? <span className="flare-rank-sub">{summary.rank.sub}</span> : null}
        </strong>
        <span className="flare-rank-ja">{rankJapanese[summary.rank.main] ?? summary.rank.main}</span>
      </div>
      <div className="flare-summary-block">
        <span className="flare-summary-label">TOTAL FLARE SKILL</span>
        <strong className="flare-total-value">{numberFormat.format(summary.total)}</strong>
        <span className="flare-metric-note">3カテゴリのTARGET合計</span>
      </div>
    </div>
    <div className="flare-category-summary">
      {(["CLASSIC", "WHITE", "GOLD"] as const).map((category) => <div className="flare-category-stat" key={category}>
        <span className="flare-category-label">{category}</span>
        <strong className="flare-category-value">{numberFormat.format(summary.categories[category].total)}</strong>
      </div>)}
    </div>
  </>;
}

function Overview({ player, state, openView }: {
  player: PublicPlayer;
  state: PageState;
  openView: (view: ViewName) => void;
}) {
  const summary = player.styles[state.style];
  return <section className="view-panel" aria-labelledby="overview-heading">
    <div className="section-heading"><div className="heading-with-info">
      <h2 id="overview-heading">{state.style === "SP" ? "SINGLE" : "DOUBLE"} Flare Skill</h2>
      <InfoTip label="公開フレアスキルについて">公開BestのFLARE実績から算出します。ローカルアプリや公式の値とは異なる場合があります。</InfoTip>
      <button className="text-button" type="button" onClick={() => openView("flare")}>対象楽曲を見る</button>
    </div></div>
    <FlareSummaryView summary={summary.flare_skill} />
    <div className="overview-best-row">
      <div className="overview-best-count"><span className="overview-best-label">公開Best譜面数</span>
        <strong className="overview-best-value">{numberFormat.format(summary.published_best_count)}</strong></div>
      <button className="text-button" type="button" onClick={() => openView("best")}>公開Bestを見る</button>
    </div>
    <div className="overview-grid"><section className="plain-panel">
      <div className="plain-panel-header"><div className="heading-with-info"><h3>Level別の公開Best</h3>
        <InfoTip label="Level別集計について">数値は公開Bestあり / 現行譜面です。収録終了譜面は母数に含みません。</InfoTip>
      </div></div>
      <div className="level-overview">
        {summary.levels.map((level) => <div className="level-overview-row" key={level.level}>
          <span className="level-label">Lv.{level.level}</span>
          <progress className="progress-track" aria-label={`Lv.${level.level} ${level.active_published_best_count} / ${level.active_chart_count}`} max={level.active_chart_count || 1} value={level.active_published_best_count} />
          <span className="level-count">{level.active_published_best_count} / {level.active_chart_count}</span>
        </div>)}
      </div>
    </section></div>
  </section>;
}

function ResultBadge({ value, kind }: { value: string | null; kind: "rank" | "clear" | "flare" }) {
  if (value === null) return <span className="no-best">—</span>;
  const className = kind === "rank" ? "rank-badge" :
    kind === "clear" ? (value === "FAILED" ? "failed-badge" : "clear-badge") :
      `flare-badge ${value === "EX" ? "flare-badge-ex" : ""}`;
  return <span className={`result-badge ${className}`}>{value}</span>;
}

function BestRow({ item }: { item: PublicBestItem }) {
  return <tr>
    <td className="music-cell" data-label="Music"><div className="music-stack">
      <span className="music-title">{item.title}</span><span className="music-artist">{item.artist}</span>
    </div></td>
    <td className="chart-cell" data-label="Chart"><div className="chart-stack">
      <span className={`difficulty-badge ${difficultyClasses[item.difficulty]}`}>{item.difficulty}</span>
      <span className="chart-meta">Lv.{item.level}</span>
      {item.is_removed ? <span className="availability-badge">収録終了</span> : null}
    </div></td>
    <td className="score-cell" data-label="SCORE"><span className={item.best === null ? "no-best" : "score-number"}>
      {item.best === null ? "公開Bestなし" : numberFormat.format(item.best.score)}
    </span></td>
    <td className="ex-cell" data-label="EX SCORE"><span className={item.best === null ? "no-best" : "ex-number"}>
      {item.best === null ? "—" : numberFormat.format(item.best.ex_score)}
    </span></td>
    <td data-label="RANK"><ResultBadge kind="rank" value={item.best?.rank ?? null} /></td>
    <td data-label="CLEAR"><ResultBadge kind="clear" value={item.best?.clear_type ?? null} /></td>
    <td data-label="FLARE"><ResultBadge kind="flare" value={item.best?.flare_rank ?? null} /></td>
  </tr>;
}

function BestView({ state, setState, response, loading, error, retry, loadMore, loadingMore }: {
  state: PageState;
  setState: (patch: Partial<PageState>) => void;
  response: PublicBestsResponse | null;
  loading: boolean;
  error: boolean;
  retry: () => void;
  loadMore: () => void;
  loadingMore: boolean;
}) {
  const context = state.mode === "level" ? `・Lv.${state.level}` : state.mode === "version" ? `・${state.version}` : state.q.trim() ? `・「${state.q.trim()}」` : "・曲名検索";
  return <section className="view-panel" aria-label="公開Best">
    <div className="browse-card"><div className="browse-top"><h2>公開Bestを探す</h2>
      <div className="browse-tabs" role="tablist" aria-label="探索方法">
        {(["level", "version", "title"] as BrowseMode[]).map((mode) => <button
          className={`browse-tab ${state.mode === mode ? "active" : ""}`} type="button" role="tab"
          aria-selected={state.mode === mode} key={mode} onClick={() => setState({ mode })}
        >{{ level: "レベルから", version: "バージョンから", title: "曲名から" }[mode]}</button>)}
      </div></div>
      <div className="browse-controls">
        {state.mode === "level" ? <label className="control-field">レベル<select className="control-select" value={state.level} onChange={(event) => setState({ level: Number(event.target.value) })}>
          {Array.from({ length: 19 }, (_, index) => index + 1).map((level) => <option value={level} key={level}>Lv.{level}</option>)}
        </select></label> : null}
        {state.mode === "version" ? <label className="control-field">バージョン<select className="control-select" value={state.version} onChange={(event) => setState({ version: event.target.value })}>
          {versions.map((version) => <option key={version}>{version}</option>)}
        </select></label> : null}
        {state.mode === "title" ? <label className="control-field">曲名<input className="search-input" type="search" maxLength={100} placeholder="例：MAX" value={state.q} onChange={(event) => setState({ q: event.target.value })} /></label> : null}
      </div>
    </div>
    {response?.summary !== null && response?.summary !== undefined ? <div className="progress-summary">
      <div className="progress-summary-item"><span className="progress-summary-label">対象譜面（現行）</span><strong className="progress-summary-value">{response.summary.active_chart_count}</strong></div>
      <div className="progress-summary-item"><span className="progress-summary-label">公開Bestあり</span><strong className="progress-summary-value">{response.summary.active_published_best_count}</strong></div>
      <div className="progress-summary-item"><span className="progress-summary-label">公開Bestなし</span><strong className="progress-summary-value">{response.summary.active_chart_count - response.summary.active_published_best_count}</strong></div>
    </div> : null}
    <div className="results-bar"><div className="inline-with-info"><p className="result-count"><strong>{response?.items.length ?? 0}譜面</strong> {context}</p>
      <InfoTip label="Best一覧について">SCORE / EX SCORE / CLEAR / FLAREは、それぞれの最高記録です。異なるプレーの記録が表示される場合があります。「公開Bestなし」は未プレーを意味しません。</InfoTip>
    </div><label className="sort-control">並び順<select className="control-select" value={state.sort} onChange={(event) => setState({ sort: event.target.value as PageState["sort"] })}>
      <option value="score_desc">SCORE 高い順</option><option value="score_asc">SCORE 低い順</option>
      <option value="ex_score_desc">EX SCORE 高い順</option><option value="title_asc">曲名 昇順</option><option value="level_asc">レベル 昇順</option>
    </select></label></div>
    {loading ? <LoadingState label="公開Bestを読み込んでいます" /> : error ? <ErrorState onRetry={retry} /> : <>
      <div className="best-table-wrap"><table className="best-table"><thead><tr>
        <th>Music</th><th>Chart</th><th>SCORE</th><th>EX SCORE</th><th>RANK</th><th>CLEAR</th><th>FLARE</th>
      </tr></thead><tbody>
        {response !== null && response.items.length > 0 ? response.items.map((item) => <BestRow item={item} key={item.chart_id} />) : <tr className="empty-row"><td colSpan={7}>条件に一致する譜面はありません</td></tr>}
      </tbody></table></div>
      {response?.next_cursor !== null && response?.next_cursor !== undefined ? <div className="load-more"><button className="text-button" type="button" disabled={loadingMore} onClick={loadMore}>{loadingMore ? "読み込み中…" : "続きを見る"}</button></div> : null}
    </>}
  </section>;
}

function FlareView({ state, response, loading, error, retry }: {
  state: PageState;
  response: PublicFlareResponse | null;
  loading: boolean;
  error: boolean;
  retry: () => void;
}) {
  if (loading) return <LoadingState label="Flare Skillを読み込んでいます" />;
  if (error || response === null) return <ErrorState onRetry={retry} />;
  const summary = {
    total: response.total, rank: response.rank,
    categories: Object.fromEntries(response.categories.map((category) => [category.category, { total: category.total, target_count: category.target_count }])) as PublicPlayer["styles"]["SP"]["flare_skill"]["categories"],
  };
  const ranges = { CLASSIC: "1st〜X3 VS 2ndMIX", WHITE: "2013〜A", GOLD: "A20〜WORLD" };
  return <section className="view-panel" aria-label="Flare Skill">
    <div className="flare-page-header"><div className="heading-with-info"><h2>{state.style === "SP" ? "SINGLE" : "DOUBLE"} Flare Skill対象楽曲</h2>
      <InfoTip label="フレアスキル対象楽曲について">公開BestのFLARE実績から算出します。ローカルアプリや公式の値とは異なる場合があります。</InfoTip>
    </div></div>
    <FlareSummaryView summary={summary} />
    <div className="flare-category-grid">{response.categories.map((category) => <article className="flare-category-panel" data-flare-category={category.category.toLowerCase()} key={category.category}>
      <header className="flare-category-header"><div><h3 className="flare-category-title">{category.category}</h3><span className="flare-category-range">{ranges[category.category]}</span></div>
        <div><div className="flare-category-score">{numberFormat.format(category.total)}</div><span className="flare-category-count">{category.target_count} / 30 TARGET</span></div>
      </header>
      <div className="flare-target-list">{category.targets.slice(0, 10).map((target, index) => <div className="flare-target-row" key={target.chart_id}>
        <span className="flare-target-rank">{index + 1}</span><div className="flare-target-song"><span className="flare-target-title" title={target.title}>{target.title}</span>
          <div className="flare-target-meta"><span className={`difficulty-badge ${difficultyClasses[target.difficulty]}`}>{target.difficulty}</span><span className="chart-meta">Lv.{target.level}</span></div>
        </div><div className="flare-target-result"><ResultBadge kind="flare" value={target.flare_rank} /><strong className="flare-target-skill">{numberFormat.format(target.flare_skill)}</strong></div>
      </div>)}</div><div className="flare-category-footer">Top {Math.min(10, category.target_count)} / {category.target_count}譜面</div>
    </article>)}</div>
  </section>;
}

export function PlayerApp({ player }: { player: PublicPlayer }) {
  const [state, setPageState] = useState(() => readPageState(window.location.search, player.default_style));
  const [bestResponse, setBestResponse] = useState<PublicBestsResponse | null>(null);
  const [bestStatus, setBestStatus] = useState<"idle" | "loading" | "error">("idle");
  const [bestRetry, setBestRetry] = useState(0);
  const [loadingMore, setLoadingMore] = useState(false);
  const [flareResponse, setFlareResponse] = useState<PublicFlareResponse | null>(null);
  const [flareStatus, setFlareStatus] = useState<"idle" | "loading" | "error">("idle");
  const [flareRetry, setFlareRetry] = useState(0);
  const deferredQuery = useDeferredValue(state.q);

  const setState = (patch: Partial<PageState>) => setPageState((current) => ({ ...current, ...patch }));
  useEffect(() => {
    history.replaceState(null, "", `${window.location.pathname}${pageStateSearch(state)}`);
  }, [state]);
  useEffect(() => {
    const restore = () => setPageState(readPageState(window.location.search, player.default_style));
    window.addEventListener("popstate", restore);
    return () => window.removeEventListener("popstate", restore);
  }, [player.default_style]);

  const bestRequestState = useMemo(() => ({ ...state, q: deferredQuery }), [state, deferredQuery]);
  useEffect(() => {
    if (state.view !== "best") return;
    const controller = new AbortController();
    setBestStatus("loading");
    setBestResponse(null);
    void fetchBests(player.public_player_id, bestRequestState, null, controller.signal)
      .then((response) => { setBestResponse(response); setBestStatus("idle"); })
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === "AbortError")) setBestStatus("error");
      });
    return () => controller.abort();
  }, [player.public_player_id, bestRequestState, bestRetry, state.view]);

  useEffect(() => {
    if (state.view !== "flare") return;
    const controller = new AbortController();
    setFlareStatus("loading");
    setFlareResponse(null);
    void fetchFlareSkill(player.public_player_id, state.style, controller.signal)
      .then((response) => { setFlareResponse(response); setFlareStatus("idle"); })
      .catch((error: unknown) => {
        if (!(error instanceof DOMException && error.name === "AbortError")) setFlareStatus("error");
      });
    return () => controller.abort();
  }, [player.public_player_id, state.style, state.view, flareRetry]);

  const loadMore = async () => {
    if (bestResponse?.next_cursor === null || bestResponse === null || loadingMore) return;
    setLoadingMore(true);
    try {
      const next = await fetchBests(player.public_player_id, bestRequestState, bestResponse.next_cursor);
      setBestResponse({ ...next, items: [...bestResponse.items, ...next.items] });
    } catch {
      setBestStatus("error");
    } finally {
      setLoadingMore(false);
    }
  };

  const hasAnyBest = player.styles.SP.published_best_count + player.styles.DP.published_best_count > 0;
  const updatedAt = player.public_bests_updated_at === null ? "まだありません" : dateFormat.format(new Date(player.public_bests_updated_at));
  return <>
    <header className="site-header"><div className="site-header-inner"><a className="site-brand" href={`/player/${player.public_player_id}`}>GP Score Log</a><span className="site-context">公開 Player Data</span></div></header>
    <main className="page-shell"><header className="player-header"><div className="player-title"><h1>{player.display_name}</h1><p>公開データ更新：{updatedAt}</p></div>
      <div className="style-switch" role="group" aria-label="プレイスタイル">
        {(["SP", "DP"] as const).map((style) => <button className={state.style === style ? "active" : ""} type="button" aria-pressed={state.style === style} key={style} onClick={() => setState({ style })}>{style === "SP" ? "SINGLE" : "DOUBLE"}</button>)}
      </div></header>
      <nav className="primary-tabs" role="tablist" aria-label="Player Data">{(["overview", "best", "flare"] as ViewName[]).map((view) => <button className={`primary-tab ${state.view === view ? "active" : ""}`} type="button" role="tab" aria-selected={state.view === view} key={view} onClick={() => setState({ view })}>{{ overview: "Overview", best: "Best", flare: "Flare Skill" }[view]}</button>)}</nav>
      {!hasAnyBest ? <section className="state-screen"><div className="state-content"><h2>公開Bestはまだありません</h2><p>このPlayerの公開Bestが同期されると、Player Dataに表示されます。</p></div></section> : state.view === "overview" ? <Overview player={player} state={state} openView={(view) => setState({ view })} /> : state.view === "best" ? <BestView
        state={state} setState={setState} response={bestResponse} loading={bestStatus === "loading"} error={bestStatus === "error"}
        retry={() => setBestRetry((value) => value + 1)} loadMore={() => void loadMore()} loadingMore={loadingMore}
      /> : <FlareView state={state} response={flareResponse} loading={flareStatus === "loading"} error={flareStatus === "error"} retry={() => setFlareRetry((value) => value + 1)} />}
    </main>
  </>;
}
