import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { accountRequest, ManagementError, type Session, type Profile, type Confirmation, type Deletion } from "./management-api";
import "./management.css";

const appPath = (id: string, action: string) => `auth/app-authorizations/${encodeURIComponent(id)}/${action}`;
const deleteNotice = "公開した自己ベスト、Google連携、すべてのPCとの連携が削除され、公開URLは利用できなくなります。PC内のスコア履歴は残ります。";
const terminal = new Set(["CANCELLED", "INVALIDATED", "EXPIRED"]);
function Card({ title, children }: { title?: string; children: ReactNode }) {
  return <section className="management-card">{title ? <h2>{title}</h2> : null}{children}</section>;
}
function Target({ player }: { player: Pick<Profile, "display_name" | "public_player_id"> | null }) {
  return player ? <><strong className="management-value">{player.display_name}</strong><p className="management-value">{location.origin}/player/{player.public_player_id}</p></>
    : <><strong>Player（作成後に変更できます）</strong><p>公開URLは作成完了後に発行されます。</p></>;
}
function errorText(error: unknown): string {
  if (error instanceof ManagementError) {
    if (error.status === 401) return "ログインの有効期限が切れました。もう一度ログインしてください。";
    if (/EXPIRED|FINISHED|INVALID/u.test(error.code) && error.code !== "INVALID_DISPLAY_NAME") return "操作の期限が切れたか、無効になりました。操作を始めた画面からやり直してください。";
    if (error.code === "INVALID_DISPLAY_NAME") return "公開名は空白を除いて1〜64文字で入力してください。";
    if (error.code === "PLAYER_NOT_LINKED") return "このGoogleアカウントは、アプリのアカウントとつながっていません。";
    if (error.code === "GOOGLE_ALREADY_REGISTERED") return "既に登録済みのアカウントです。ログインを選び直してください。";
    if (/CONFLICT|IDENTITY/u.test(error.code)) return "選択したGoogleアカウントと対象が一致しません。操作を始めた画面からやり直してください。";
  }
  return "通信結果を確認できませんでした。情報を更新してから、もう一度お試しください。";
}

export function ManagementApp() {
  const route = location.pathname;
  const params = new URLSearchParams(location.search);
  const requestId = params.get("request") ?? "";
  const isApp = route === "/my/app-connect";
  const isDelete = route === "/my/account-delete";
  const isPublicDelete = route === "/my/public-data-delete";
  const authError = params.get("auth_error");
  const [session, setSession] = useState<Session | null>(null);
  const [profile, setProfile] = useState<Profile | null>(null);
  const [csrf, setCsrf] = useState("");
  const [name, setName] = useState("");
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null);
  const [appBound, setAppBound] = useState(false);
  const [deletion, setDeletion] = useState<Deletion | null>(null);
  const [publicDeletion, setPublicDeletion] = useState<{ status: "UNCONFIRMED" | "DELETED" } | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [compared, setCompared] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [syncStopped, setSyncStopped] = useState(false);
  const [unlinkHelp, setUnlinkHelp] = useState(false);
  const [cancelled, setCancelled] = useState(false);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    document.title = `${isApp ? "アカウントを作成・引き継ぐ" : isDelete ? "アカウントの削除" : isPublicDelete ? "公開記録の削除" : "マイプロフィール"} - GP Score Log`;
  }, [isApp, isDelete, isPublicDelete]);

  useEffect(() => {
    let active = true;
    async function load() {
      setLoading(true);
      try {
        const current = await accountRequest<Session>("account/session").catch(e => {
          if (e instanceof ManagementError && e.status === 401) return null;
          throw e;
        });
        if (!active) return;
        const context = current ?? await accountRequest<{ csrf_token: string }>("auth/web/context");
        const data = current?.google_linked ? await accountRequest<Profile>("account/profile") : null;
        const app = isApp && requestId && current
          ? await accountRequest<Confirmation>(appPath(requestId, "confirmation")).catch(e => {
            if (e instanceof ManagementError && e.code === "AUTHORIZATION_INVALID") return null;
            throw e;
          }) : null;
        const pendingDelete = isDelete && !authError
          ? await accountRequest<Deletion>("account/deletion-confirmations/current").catch(e => {
            if (e instanceof ManagementError && e.code === "DELETION_CONFIRMATION_EXPIRED") return null;
            throw e;
          }) : null;
        const publicDelete = isPublicDelete && data && !authError
          ? await accountRequest<{ status: "UNCONFIRMED" | "DELETED" }>("account/bests/deletion-confirmation").catch(e => {
            if (e instanceof ManagementError && e.code === "OPERATION_PROOF_REQUIRED") return null;
            throw e;
          }) : null;
        if (!active) return;
        setSession(current); setCsrf(context.csrf_token); setProfile(data); setName(data?.display_name ?? "");
        setAppBound(app !== null); setConfirmation(authError ? null : app); setDeletion(pendingDelete); setCompared(false); setConfirmed(false);
        setPublicDeletion(publicDelete);
        setSyncStopped(false);
      } catch (e) { if (active) {
        setError(errorText(e));
        if (e instanceof ManagementError && e.status === 401) {
          setSession(null); setProfile(null); setName(""); setConfirmation(null); setConfirmed(false); setCompared(false);
        }
      } }
      finally { if (active) setLoading(false); }
    }
    void load();
    return () => { active = false; };
  }, [isApp, isDelete, isPublicDelete, requestId, authError, reload]);

  useEffect(() => {
    if (!isApp || confirmation?.status !== "APPROVED") return;
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const result = await accountRequest<{ status: string }>(appPath(requestId, "status"));
        if (!active) return;
        setConfirmation(previous => previous ? { ...previous, status: result.status } : previous);
        if (result.status === "APPROVED") timer = setTimeout(() => void poll(), 5000);
      } catch (e) { if (active) setError(errorText(e)); }
    }
    timer = setTimeout(() => void poll(), 5000);
    return () => { active = false; clearTimeout(timer); };
  }, [isApp, requestId, confirmation?.status, reload]);

  useEffect(() => {
    if (!confirmation || !["ACTIVATED", "LINKED"].includes(confirmation.status)) return;
    let active = true;
    void accountRequest<Profile>("account/profile").then(value => { if (active) setProfile(value); })
      .catch(e => { if (active) setError(errorText(e)); });
    return () => { active = false; };
  }, [confirmation?.status]);

  async function action(work: () => Promise<void>) {
    setBusy(true); setError(""); setNotice("");
    try { await work(); } catch (e) {
      setError(errorText(e));
      setConfirmed(false); setCompared(false);
      setSyncStopped(false);
      if (isDelete && e instanceof ManagementError && /EXPIRED|OPERATION_PROOF/u.test(e.code)) setDeletion(null);
      if (isPublicDelete && e instanceof ManagementError && /EXPIRED|OPERATION_PROOF/u.test(e.code)) setPublicDeletion(null);
      if (e instanceof ManagementError && e.status === 401) {
        setSession(null); setProfile(null); setName(""); setConfirmation(null); setConfirmed(false); setCompared(false);
      }
    } finally { setBusy(false); }
  }
  function navigate(url: string) { location.assign(url); }
  async function switchAccount(login: boolean) {
    setName(""); setCompared(false); setConfirmed(false);
    await accountRequest("auth/web/logout", "POST", csrf, isApp && appBound ? { app_authorization_id: requestId } : {});
    if (isApp) navigate(`/my/app-connect?request=${encodeURIComponent(requestId)}`);
    else navigate(login ? "/api/v1/auth/web/google/start" : "/my/profile");
  }
  async function start(intent: "register" | "login") {
    const result = await accountRequest<{ url: string }>(appPath(requestId, "web-start"), "POST", csrf, { intent });
    navigate(result.url);
  }
  async function save(event: FormEvent) {
    event.preventDefault();
    const trimmed = name.trim();
    if (Array.from(trimmed).length < 1 || Array.from(trimmed).length > 64) { setError(errorText(new ManagementError("INVALID_DISPLAY_NAME", 400))); return; }
    await action(async () => {
      try {
        await accountRequest("account/profile", "PATCH", csrf, { display_name: trimmed });
      } catch (e) {
        if (e instanceof ManagementError && e.status < 500) throw e;
        const recovered = await accountRequest<Profile>("account/profile");
        setProfile(recovered);
        if (recovered.display_name !== trimmed) throw e;
      }
      setProfile(previous => previous ? { ...previous, display_name: trimmed } : previous);
      setName(trimmed); setNotice("公開プレーヤー名を保存しました。");
    });
  }
  async function approve() {
    await action(async () => {
      let result: { status: string };
      try { result = await accountRequest(appPath(requestId, "approve"), "POST", csrf, { confirmed, app_compared: compared }); }
      catch (e) {
        if (e instanceof ManagementError && e.status < 500) throw e;
        result = await accountRequest(appPath(requestId, "status"));
        if (result.status === "PENDING") throw e;
      }
      setConfirmation(previous => previous ? { ...previous, status: result.status } : previous);
      setConfirmed(false); setCompared(false);
    });
  }
  async function deleteAccount() {
    if (!deletion) return;
    await action(async () => {
      const resultPath = `account/deletion-confirmations/${encodeURIComponent(deletion.id)}/result`;
      let result: Deletion;
      try { result = await accountRequest("account/deletion-confirmations", "POST", csrf, { confirmed: true }); }
      catch (e) {
        if (e instanceof ManagementError && e.status < 500) throw e;
        result = await accountRequest(resultPath, "POST");
        if (result.status === "UNCONFIRMED") throw e;
      }
      if (result.status === "CONFIRMED") {
        try { result = await accountRequest("account", "DELETE", csrf); }
        catch (e) {
          result = await accountRequest(resultPath, "POST");
          if (result.status !== "DELETED") throw e;
        }
      }
      setDeletion(result); setConfirmed(false);
      if (result.status === "DELETED") { setSession(null); setProfile(null); setName(""); }
    });
  }
  async function deletePublicRecords() {
    await action(async () => {
      let result: { status: "UNCONFIRMED" | "DELETED" };
      try { result = await accountRequest("account/bests", "DELETE", csrf, { confirmed, sync_stopped: syncStopped }); }
      catch (e) {
        if (e instanceof ManagementError && e.status < 500) throw e;
        result = await accountRequest("account/bests/deletion-confirmation");
        if (result.status !== "DELETED") throw e;
      }
      setPublicDeletion(result); setConfirmed(false); setSyncStopped(false);
    });
  }

  function publicDeleteView() {
    if (!session) return <><h1>公開記録を削除する</h1><Card><p>Googleでログインして、削除する記録を確認してください。</p><a className="management-button" href="/api/v1/auth/web/google/start">Googleでログイン</a></Card></>;
    if (!profile) return <><h1>公開記録を削除する</h1><Card><p>このGoogleアカウントには、削除できる公開記録がありません。</p><a href="/my/profile">マイプロフィールへ</a></Card></>;
    if (publicDeletion?.status === "DELETED") return <><h1>公開記録を削除しました</h1><Card><Target player={profile} /><p>アカウント、Googleとの連携、公開ページのURL、PC内のスコア履歴は残っています。</p><p>このPCの記録で作り直すには、アプリで「Webへの送信を再開する」を押してください。このPCの自己ベストをすべてWebに送ります。</p><a href="/my/profile">マイプロフィールへ</a></Card></>;
    return <><h1>公開記録を削除する</h1><Card><p className="management-value">{session.email}</p><Target player={profile} /><div className="management-notice error">Webに公開した自己ベストをすべて削除します。アカウント、Googleとの連携、公開ページのURL、PC内のスコア履歴は残ります。</div>{publicDeletion ? <><p>このアカウントの公開記録を削除してよいか、もう一度確認してください。</p><p>先にアプリで「Webへの送信を止める」を押してください。送信を止めないと、削除した記録が再びWebに送られます。</p><label className="management-check"><input type="checkbox" checked={syncStopped} disabled={busy} onChange={e => setSyncStopped(e.target.checked)} />アプリでWebへの送信を止めました。</label><label className="management-check"><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />このアカウントの公開記録をすべて削除することを確認しました。</label><button className="danger" disabled={busy || !confirmed || !syncStopped} onClick={() => void deletePublicRecords()}>Webの公開記録を削除</button></> : <><p>Googleで同じアカウントを確認してから、削除する記録をもう一度確認します。</p><button disabled={busy} onClick={() => void action(async () => navigate((await accountRequest<{ url: string }>("account/bests/deletion-start", "POST", csrf, {})).url))}>Googleでアカウントを確認</button></>}<div className="management-actions"><button disabled={busy} onClick={() => void action(async () => { await accountRequest("account/bests/deletion-cancel", "POST", csrf, {}); navigate("/my/profile"); })}>キャンセル</button></div></Card></>;
  }

  function googleCard() {
    return <Card title="Googleアカウント"><div className="management-line"><span className="management-value">{session?.email}</span><span className="management-badge">{session?.google_linked ? "連携済み" : "未連携"}</span></div><p className="management-muted">このメールアドレスは、あなたの管理画面にだけ表示されます。</p><div className="management-actions"><button disabled={busy} onClick={() => void action(() => switchAccount(false))}>ログアウト</button><button disabled={busy} onClick={() => void action(() => switchAccount(true))}>別のアカウントでログイン</button></div></Card>;
  }
  function entry() {
    return <><h1>アカウントを作成・引き継ぐ</h1><p>Googleアカウントでログインして、このPCとWebをつなぎます。</p>{session ? googleCard() : null}<Card title="初めて利用する方"><p>Googleでログインした後に内容を確認して、アカウントを作成します。</p><button className="primary" disabled={busy || !requestId || !csrf} onClick={() => void action(() => start("register"))}>新規登録</button></Card><Card title="既にアカウントをお持ちの方"><p>同じアカウントをこのPCで使います。公開ページのURLは変わりません。</p><button disabled={busy || !requestId || !csrf} onClick={() => void action(() => start("login"))}>ログイン</button></Card><p className="management-muted">前のPCのスコア履歴を使う場合は、バックアップから読み込んでください。</p>{!requestId ? <p role="alert">アプリの「アカウントを作成・引き継ぐ」から開始してください。</p> : null}</>;
  }
  function appView() {
    if (cancelled || (confirmation && terminal.has(confirmation.status))) return <><h1>操作を終了しました</h1><p>アプリに戻り、状態を確認してから操作をやり直してください。</p></>;
    if (!confirmation) return entry();
    if (confirmation.status === "APPROVED") return <><h1>アプリで完了を確認してください</h1><Card><p>このPCでアカウントを使えるようにしています。</p><p>引き継ぎの場合、途中で失敗しても、前のPCは引き続き使えます。作成済みのアカウントも残ります。</p><div className="management-notice">PC内のスコア履歴は、バックアップから読み込めます。Webに公開した自己ベストは残ります。</div>{error ? <button onClick={() => setReload(value => value + 1)}>完了結果を確認</button> : null}</Card></>;
    if (["ACTIVATED", "LINKED", "UNLINKED"].includes(confirmation.status)) return <><h1>{confirmation.status === "UNLINKED" ? "Google連携を解除しました" : confirmation.status === "LINKED" ? "Googleアカウントを連携しました" : confirmation.intent === "register" ? "アカウントの作成とこのPCへの連携が完了しました" : "このPCへの引き継ぎが完了しました"}</h1><Card>{profile || confirmation.player ? <Target player={profile ?? confirmation.player} /> : null}<p>{confirmation.status === "UNLINKED" ? "公開ページのURL、公開した自己ベスト、PC内の記録は残ります。現在のアプリから再連携できます。" : confirmation.status === "LINKED" ? "公開ページのURL、公開した自己ベスト、PC内の記録、同期の設定は変わりません。" : "アプリで連携を完了すると、同期がONになります。このPCの自己ベストをWebに送ります。"}</p>{confirmation.status === "ACTIVATED" ? <p>Webにはこれまで公開した自己ベストが残ります。このPCで自己ベストを更新すると、Webにも送ります。前のPCのスコア履歴を使う場合は、バックアップから読み込んでください。</p> : null}<a href="/my/profile">マイプロフィールへ</a></Card></>;
    const registered = confirmation.intent === "register" && confirmation.registered_google;
    const unregistered = confirmation.intent === "login" && !confirmation.registered_google;
    if (registered || unregistered) return <><h1>{registered ? "既に登録済みのアカウントです" : "アカウントが見つかりません"}</h1><Card><p className="management-value">{session?.email}</p><p>{registered ? "既存のアカウントをこのPCへ引き継げます。" : "このGoogleアカウントは未登録です。新規登録する場合は、内容を確認してから作成してください。"}</p>{registered ? <Target player={confirmation.player} /> : null}<button className="primary" disabled={busy} onClick={() => void action(() => start(registered ? "login" : "register"))}>{registered ? "ログインして引き継ぎへ" : "新規登録へ"}</button></Card></>;
    const unlink = confirmation.purpose === "unlink";
    const title = unlink ? "Google連携を解除" : confirmation.intent === "register" ? "アカウントの作成を確認" : confirmation.intent === "link" ? "Googleアカウントを連携" : "このPCへ引き継ぐ";
    return <><h1>{title}</h1><Card title="対象のアカウント"><p className="management-value">{session?.email}</p><Target player={confirmation.player} /><button disabled={busy} onClick={() => void action(() => switchAccount(true))}>別のアカウントでログイン</button></Card><Card title="アプリのコードを確認"><p>操作を始めたアプリにも、同じコードが表示されていることを確認してください。</p><div className="management-code">{confirmation.comparison_code}</div><div className="management-notice">{unlink ? "このGoogleアカウントでのログイン・別のPCへの引き継ぎができなくなります。公開ページのURL、公開した自己ベスト、PC内の記録は残ります。" : confirmation.intent === "register" ? "新しいアカウントを作り、このPCで使います。アプリで連携を完了すると、同期がONになり、このPCの自己ベストをWebに送ります。" : confirmation.intent === "link" ? "現在のアプリと同じアカウントに連携します。公開した自己ベスト、PC内のスコア履歴、同期の設定は変わりません。" : "連携が完了すると、前のPCからの同期は止まり、このPCの同期がONになります。このPCの自己ベストをWebに送ります。公開した自己ベストとPC内のスコア履歴は残ります。"}</div><label className="management-check"><input type="checkbox" checked={compared} disabled={busy} onChange={e => setCompared(e.target.checked)} />手元のアプリとコードが一致しています。</label><label className="management-check"><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />{unlink ? "Googleでの引き継ぎができなくなることを確認しました。" : "アカウントと、この後に行うことを確認しました。"}</label><div className="management-actions"><button className={unlink ? "danger" : "primary"} disabled={busy || !confirmed || !compared} onClick={() => void approve()}>{unlink ? "Google連携を解除する" : confirmation.intent === "register" ? "このアカウントを作成する" : confirmation.intent === "link" ? "このアカウントに連携する" : "このPCへ引き継ぐ"}</button><button disabled={busy} onClick={() => void action(async () => { await accountRequest(appPath(requestId, "cancel"), "POST", csrf, {}); setCancelled(true); })}>キャンセル</button></div></Card></>;
  }
  function profileView() {
    if (!session) return <><h1>マイプロフィール</h1><Card><p>Googleアカウントでログインしてください。</p><a className="management-button primary" href="/api/v1/auth/web/google/start">Googleでログイン</a></Card><p className="management-muted">公開ページはログインせずに閲覧できます。</p></>;
    return <><h1>マイプロフィール</h1><p className="management-muted">公開ページに出す名前や、Googleとの連携を変更できます。</p>{googleCard()}{profile ? <><Card title="公開プロフィール"><form onSubmit={save}><label htmlFor="public-name">公開プレーヤー名</label><input id="public-name" type="text" value={name} disabled={busy} onChange={e => setName(e.target.value)} /><p className="management-muted">1〜64文字。公開ページに表示されます。</p><p>現在の公開名: <strong>{profile.display_name}</strong></p><label htmlFor="public-url">公開URL</label><input id="public-url" type="text" readOnly value={profile.public_url} /><div className="management-actions"><button className="primary" disabled={busy}>名前を保存</button><a className="management-button" href={profile.public_url}>公開ページを開く</a></div></form></Card><Card title="Google連携"><p>解除すると、このGoogleアカウントでのログイン・別のPCへの引き継ぎができなくなります。</p><button className="danger" onClick={() => setUnlinkHelp(value => !value)}>Google連携を解除</button>{unlinkHelp ? <div className="management-notice">現在利用できるアプリで「Web連携」から「Google連携を解除」を選び、この画面で確認してください。解除後もアプリを利用できることを確認してから、解除します。</div> : null}</Card><Card title="公開記録の削除"><p>Webに公開した自己ベストを削除できます。</p><a className="management-button danger" href="/my/public-data-delete">公開記録を削除する</a></Card><Card title="アカウントの削除"><p>アカウントとすべてのPCとの連携を削除します。</p><a className="management-button danger" href="/my/account-delete">アカウントを削除する</a></Card></> : <Card title="アカウントが登録・連携されていません"><p>アプリで「アカウントを作成・引き継ぐ」から開始し、Webで新規登録してください。Google連携を解除した方は、現在のアプリの「Web連携」から同じアカウントへ再連携できます。</p><p className="management-muted">ログインだけでは、新しいアカウントは作られません。</p></Card>}</>;
  }
  function deleteView() {
    if (deletion?.status === "DELETED") return <><h1>アカウントを削除しました</h1><Card><p>公開URLは利用できなくなりました。PC内のスコア履歴は残ります。</p><a href="/my/profile">マイプロフィールへ</a></Card></>;
    if (!session || !profile) return profileView();
    return <><h1>アカウントを削除する</h1><Card><p className="management-value">{session.email}</p><Target player={profile} /><div className="management-notice error">{deleteNotice}</div>{deletion ? <><p>Googleアカウントを確認できました。このアカウントを削除してよいか確認してください。</p><label className="management-check"><input type="checkbox" checked={confirmed} disabled={busy} onChange={e => setConfirmed(e.target.checked)} />アカウントと公開した自己ベストを削除することを確認しました。</label><button className="danger" disabled={busy || !confirmed} onClick={() => void deleteAccount()}>アカウントを削除する</button></> : <><p>Googleで同じアカウントを確認してください。その後、削除する対象をもう一度確認します。</p><button disabled={busy} onClick={() => void action(async () => navigate((await accountRequest<{ url: string }>("account/deletion-start", "POST", csrf, {})).url))}>Googleでアカウントを確認</button></>}<div className="management-actions"><a href="/my/profile">キャンセル</a></div></Card></>;
  }
  return <><header className="site-header"><div className="site-header-inner"><a href="/my/profile" className="site-brand">GP Score Log</a>{route === "/my/profile" && profile ? <a className="text-button site-nav-link" href={profile.public_url}>公開ページ</a> : <span className="site-context">アカウントの管理</span>}</div></header><main className="management-shell">{error ? <div className="management-notice error" role="alert">{error}<div><button disabled={busy} onClick={() => { setError(""); setReload(value => value + 1); }}>情報を更新</button></div></div> : null}{authError ? <div className="management-notice error" role="alert">{authError === "cancelled" ? "Googleログインを中止しました。" : "Googleログインに失敗しました。"} アカウントとアプリの記録は変更されていません。操作を始めた画面からやり直せます。</div> : null}{notice ? <div className="management-notice success" role="status">{notice}</div> : null}{route === "/my/auth-error" ? <><h1>ログインを確認できませんでした</h1><p>操作を始めた画面からやり直してください。</p><a href="/my/profile">マイプロフィールへ</a></> : loading ? <p role="status">情報を確認しています…</p> : isApp ? appView() : isDelete ? deleteView() : isPublicDelete ? publicDeleteView() : profileView()}</main></>;
}
