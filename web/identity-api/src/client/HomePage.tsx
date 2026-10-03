const repositoryUrl = "https://github.com/tts1374/ddrgp_scorelog";
const guideUrl = `${repositoryUrl}/blob/main/docs/user-guide.md`;

export function HomePage() {
  return <>
    <header className="site-header"><div className="site-header-inner">
      <a className="site-brand" href="/">GP Score Log</a>
      <a className="text-button site-nav-link" href="/my/profile">マイページ</a>
    </div></header>
    <main className="home-shell">
      <section className="home-about" aria-labelledby="home-heading">
        <h1 id="home-heading">GP Score Logについて</h1>
        <p>DDR GRAND PRIXのリザルト画面を読み取り、プレー結果をPCに記録するWindows用アプリです。自己ベストやプレー履歴、FLARE SKILLを確認でき、自己ベストをWebに公開できます。</p>
        <p className="home-note">Windows 11・グランプリプレー・1280×720に対応</p>
        <nav className="home-links" aria-label="利用を始める">
          <a href={`${repositoryUrl}/releases/latest`}>アプリをダウンロード</a>
          <a href="#getting-started">使い方を見る</a>
          <a href={guideUrl}>利用ガイド</a>
        </nav>
      </section>
      <section className="home-section" aria-labelledby="features-heading">
        <h2 id="features-heading">できること</h2>
        <dl className="home-features">
          <div><dt>Windowsアプリ</dt><dd>自己ベスト、プレー履歴、スコアの推移、FLARE SKILLを確認できます。</dd></div>
          <div><dt>公開Player Data</dt><dd>SINGLE / DOUBLE別のBestとFLARE SKILLを確認できます。Bestはレベル・バージョン・曲名から探せます。</dd></div>
          <div><dt>マイページ</dt><dd>公開名の変更や、Google連携・公開記録の管理ができます。</dd></div>
        </dl>
      </section>
      <section className="home-section" id="getting-started" aria-labelledby="getting-started-heading">
        <h2 id="getting-started-heading">使い方</h2>
        <ol className="home-steps">
          <li><strong>アプリを準備する</strong>
            <p>セットアップファイルをインストールし、GP Score Logを起動します。DDR GRAND PRIXは1280×720でプレーします。</p>
          </li>
          <li><strong>プレーを記録する</strong>
            <p>ゲーム画面を自動で見つけて監視します。リザルトを読み取ると、自己ベストやプレー履歴を確認できます。自動で始まらない場合は「監視開始」を押します。</p>
          </li>
          <li><strong>Webに公開する</strong>
            <p>設定の「アカウントを作成・引き継ぐ」からGoogleで連携。Webとアプリのコードを確認し、連携を終えると、このPCの自己ベストの送信が始まります。</p>
          </li>
        </ol>
      </section>
      <section className="home-section" aria-labelledby="public-data-heading">
        <h2 id="public-data-heading">Webへの公開について</h2>
        <p>公開URLを共有すると、ログインせずに記録を見てもらえます。公開する名前はマイページで変更できます。</p>
        <p className="home-note">Webには自動記録した自己ベストを送信します。スクリーンショットから追加した記録と、復元した記録はPC内で確認できます。</p>
      </section>
    </main>
    <footer className="home-footer"><div className="site-header-inner">
      <span>GP Score Log</span>
      <nav aria-label="関連リンク"><a href={guideUrl}>利用ガイド</a><a href={repositoryUrl}>GitHub</a></nav>
    </div></footer>
  </>;
}
