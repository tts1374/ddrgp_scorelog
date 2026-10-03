import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ManagementApp } from "../../src/client/ManagementApp";

const session = { email: "owner@example.com", google_linked: true, csrf_token: "csrf" };
const profile = { display_name: "Player", public_player_id: "p_owner", public_url: "https://example.com/player/p_owner" };
const confirm = { status: "PENDING", intent: "register", purpose: "connect", player: null, registered_google: false, comparison_code: "482719AB" };
function mockApi(extra: (path: string, options: RequestInit) => unknown = () => undefined) {
  const fetcher = vi.fn(async (url: string, options: RequestInit = {}) => {
    const path = url.replace("/api/v1/", "");
    let body = extra(path, options);
    if (body === undefined) {
      if (path === "account/session") body = session;
      else if (path === "account/profile") body = profile;
      else if (path === "auth/web/context") body = { csrf_token: "browser-csrf" };
      else if (path.endsWith("/confirmation")) body = confirm;
      else throw new Error(`Unexpected request ${path}`);
    }
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200 });
  });
  vi.stubGlobal("fetch", fetcher);
  return fetcher;
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });
describe("Web management", () => {
  it("an unresolved unified request shows the entry without premature consent or approval", async () => {
    window.history.replaceState(null, "", "/my/app-connect?request=app-request");
    const fetcher = mockApi(path => path.endsWith("/confirmation") ? { ...confirm, intent: null } : undefined);
    render(<ManagementApp />);
    expect(await screen.findByRole("button", { name: "Googleで続ける" })).toBeEnabled();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button", { name: "新規登録" })).toBeNull();
    expect(screen.queryByRole("button", { name: "ログイン" })).toBeNull();
    expect(fetcher.mock.calls.some(([path]) => path.endsWith("/approve"))).toBe(false);
  });
  it("public-record deletion requires stopped-sync consent, preserves the account and recovers a lost response", async () => {
    window.history.replaceState(null, "", "/my/public-data-delete");
    let deleted = false;
    const fetcher = mockApi((path, options) => {
      if (path === "account/bests/deletion-confirmation") return { status: deleted ? "DELETED" : "UNCONFIRMED" };
      if (path === "account/bests" && options.method === "DELETE") { deleted = true; throw new TypeError("Network failure"); }
    });
    render(<ManagementApp />);
    const button = await screen.findByRole("button", { name: "Webの公開記録を削除" });
    expect(screen.getByText(profile.display_name)).toBeVisible();
    expect(screen.getByText(/アカウント、Googleとの連携、公開ページのURL、PC内のスコア履歴は残ります/u)).toBeVisible();
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes).toHaveLength(2);
    expect(boxes[0]).not.toBeChecked(); expect(boxes[1]).not.toBeChecked();
    expect(button).toBeDisabled();
    fireEvent.click(boxes[1]); expect(button).toBeDisabled();
    fireEvent.click(boxes[0]); expect(button).toBeEnabled();
    expect(screen.getByRole("button", { name: "キャンセル" })).toBeEnabled();
    fireEvent.click(button);
    expect(await screen.findByRole("heading", { name: "公開記録を削除しました" })).toBeVisible();
    expect(screen.getByText(/Webへの送信を再開する/u)).toBeVisible();
    const [, options] = fetcher.mock.calls.find(([path]) => path === "/api/v1/account/bests")!;
    expect(options?.body).toBe(JSON.stringify({ confirmed: true, sync_stopped: true }));
    expect(options?.headers).toHaveProperty("X-CSRF-Token", "csrf");
    expect(fetcher.mock.calls.some(([path]) => path === "/api/v1/account")).toBe(false);
  });
  it("public-record deletion without its purpose proof starts Google verification without a delete button", async () => {
    window.history.replaceState(null, "", "/my/public-data-delete");
    const fetcher = mockApi(path => path === "account/bests/deletion-confirmation"
      ? new Response(JSON.stringify({ error: { code: "OPERATION_PROOF_REQUIRED" } }), { status: 403 }) : undefined);
    render(<ManagementApp />);
    expect(await screen.findByRole("button", { name: "Googleでアカウントを確認" })).toBeEnabled();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(screen.queryByRole("button", { name: "Webの公開記録を削除" })).toBeNull();
    expect(fetcher.mock.calls.some(([, options]) => options?.method === "DELETE")).toBe(false);
  });
  it("requires Google login and renders no editing controls for an expired session", async () => {
    window.history.replaceState(null, "", "/my/profile");
    mockApi(path => path === "account/session" ? new Response(JSON.stringify({ error: { code: "WEB_SESSION_REQUIRED" } }), { status: 401 }) : undefined);
    render(<ManagementApp />);
    expect(await screen.findByRole("link", { name: "Googleでログイン" })).toHaveAttribute("href", "/api/v1/auth/web/google/start");
    expect(screen.queryByRole("textbox", { name: "公開プレーヤー名" })).toBeNull();
  });
  it("unlinked Google can log out but never sees a creation or editing form", async () => {
    window.history.replaceState(null, "", "/my/profile");
    const fetcher = mockApi(path => path === "account/session" ? { ...session, google_linked: false } : undefined);
    render(<ManagementApp />);
    expect(await screen.findByText("アカウントが登録・連携されていません")).toBeVisible();
    expect(screen.getByRole("button", { name: "ログアウト" })).toBeEnabled();
    expect(fetcher.mock.calls.some(([path]) => path.includes("account/profile"))).toBe(false);
    expect(screen.queryByRole("button", { name: "新規登録" })).toBeNull();
  });
  it("validates trimmed Unicode name boundaries, sends only name+CSRF, and recovers a lost save response", async () => {
    window.history.replaceState(null, "", "/my/profile");
    let saved = false;
    const fetcher = mockApi((path, options) => {
      if (path === "account/profile" && options.method === "PATCH") { saved = true; throw new TypeError("Network failure"); }
      if (path === "account/profile" && saved) return { ...profile, display_name: "新しい名前" };
    });
    render(<ManagementApp />);
    const input = await screen.findByRole("textbox", { name: "公開プレーヤー名" });
    fireEvent.change(input, { target: { value: "   " } });
    fireEvent.click(screen.getByRole("button", { name: "名前を保存" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("1〜64文字");
    fireEvent.change(input, { target: { value: "名".repeat(65) } });
    fireEvent.click(screen.getByRole("button", { name: "名前を保存" }));
    expect(fetcher.mock.calls.some(([, options]) => options?.method === "PATCH")).toBe(false);
    fireEvent.change(input, { target: { value: " 新しい名前 " } });
    fireEvent.click(screen.getByRole("button", { name: "名前を保存" }));
    expect(await screen.findByText("公開プレーヤー名を保存しました。")).toBeVisible();
    const [, options] = fetcher.mock.calls.find(([, options]) => options?.method === "PATCH")!;
    expect(options?.body).toBe(JSON.stringify({ display_name: "新しい名前" }));
    expect(options?.headers).toHaveProperty("X-CSRF-Token", "csrf");
    expect(input).toHaveValue("新しい名前");
  });
  it("clears editing on session expiry and safely displays HTML-like names", async () => {
    window.history.replaceState(null, "", "/my/profile");
    mockApi((path, options) => path === "account/profile" ? options.method === "PATCH"
      ? new Response(JSON.stringify({ error: { code: "WEB_SESSION_REQUIRED" } }), { status: 401 })
      : { ...profile, display_name: "<img src=x onerror=alert(1)>" } : undefined);
    render(<ManagementApp />);
    const input = await screen.findByRole("textbox", { name: "公開プレーヤー名" });
    expect(document.querySelector("img")).toBeNull();
    fireEvent.change(input, { target: { value: "変更" } });
    fireEvent.click(screen.getByRole("button", { name: "名前を保存" }));
    expect(await screen.findByRole("link", { name: "Googleでログイン" })).toBeVisible();
    expect(screen.queryByRole("textbox", { name: "公開プレーヤー名" })).toBeNull();
  });
  it("requires two unchecked confirmations and keeps APPROVED separate from activation", async () => {
    window.history.replaceState(null, "", "/my/app-connect?request=app-request");
    const fetcher = mockApi(path => path.endsWith("/approve") ? { status: "APPROVED" } : undefined);
    render(<ManagementApp />);
    const button = await screen.findByRole("button", { name: "このアカウントを作成する" });
    const boxes = screen.getAllByRole("checkbox");
    expect(boxes[0]).not.toBeChecked(); expect(boxes[1]).not.toBeChecked(); expect(button).toBeDisabled();
    fireEvent.click(boxes[0]); expect(button).toBeDisabled(); fireEvent.click(boxes[1]); expect(button).toBeEnabled();
    fireEvent.click(button);
    expect(await screen.findByRole("heading", { name: "アプリで完了を確認してください" })).toBeVisible();
    expect(screen.queryByText("アカウントの作成とこのPCへの連携が完了しました")).toBeNull();
    expect(fetcher.mock.calls.find(([path]) => path.endsWith("/approve"))?.[1]?.body).toBe(JSON.stringify({ confirmed: true, app_compared: true }));
    expect(fetcher.mock.calls.some(([path]) => path.endsWith("/result") || path.endsWith("/activate"))).toBe(false);
  });
  it.each([
    ["register", true, "既に登録済みのアカウントです", "ログインして引き継ぎへ"],
    ["login", false, "アカウントが見つかりません", "新規登録へ"],
  ])("%s intent requires explicit reselection for registered=%s", async (intent, registered, title, button) => {
    window.history.replaceState(null, "", "/my/app-connect?request=app-request");
    const fetcher = mockApi(path => path.endsWith("/confirmation") ? { ...confirm, intent, registered_google: registered } : undefined);
    render(<ManagementApp />);
    expect(await screen.findByRole("heading", { name: title as string })).toBeVisible();
    expect(screen.getByRole("button", { name: button as string })).toBeEnabled();
    expect(screen.queryByRole("checkbox")).toBeNull();
    expect(fetcher.mock.calls.some(([path]) => path.endsWith("/approve"))).toBe(false);
  });
  it("OAuth cancellation returns App start without restoring stale consent", async () => {
    window.history.replaceState(null, "", "/my/app-connect?request=app-request&auth_error=cancelled");
    const fetcher = mockApi(); render(<ManagementApp />);
    expect(await screen.findByRole("button", { name: "Googleで続ける" })).toBeEnabled();
    expect(screen.getByRole("alert")).toHaveTextContent("Googleログインを中止");
    expect(fetcher.mock.calls.some(([path]) => path.endsWith("/approve"))).toBe(false);
  });
  it("deletion re-displays target with unchecked consent and recovers both lost responses via result proof", async () => {
    window.history.replaceState(null, "", "/my/account-delete");
    let resultCount = 0;
    const fetcher = mockApi((path, options) => {
      if (path.endsWith("/current")) return { id: "dc_test", status: "UNCONFIRMED" };
      if (path === "account/deletion-confirmations" || (path === "account" && options.method === "DELETE")) throw new TypeError("Network failure");
      if (path.endsWith("/result")) return { id: "dc_test", status: ++resultCount === 1 ? "CONFIRMED" : "DELETED" };
    });
    render(<ManagementApp />);
    const checkbox = await screen.findByRole("checkbox");
    const button = screen.getByRole("button", { name: "アカウントを削除する" });
    expect(checkbox).not.toBeChecked(); expect(button).toBeDisabled();
    expect(screen.getByText(profile.display_name)).toBeVisible();
    fireEvent.click(checkbox); fireEvent.click(button);
    expect(await screen.findByRole("heading", { name: "アカウントを削除しました" })).toBeVisible();
    expect(resultCount).toBe(2);
    expect(fetcher.mock.calls.filter(([path]) => path.endsWith("/result")).every(([, options]) => options?.body === undefined)).toBe(true);
  });
  it("deletion start sends the empty JSON object required by the Worker", async () => {
    window.history.replaceState(null, "", "/my/account-delete");
    const fetcher = mockApi((path, options) => {
      if (path.endsWith("/current")) return new Response(JSON.stringify({ error: { code: "DELETION_CONFIRMATION_EXPIRED" } }), { status: 409 });
      if (path === "account/deletion-start") {
        expect(options.body).toBe("{}");
        expect(options.headers).toHaveProperty("Content-Type", "application/json");
        return new Response(JSON.stringify({ error: { code: "OPERATION_PROOF_REQUIRED" } }), { status: 403 });
      }
    });
    render(<ManagementApp />);
    fireEvent.click(await screen.findByRole("button", { name: "Googleでアカウントを確認" }));
    await waitFor(() => expect(fetcher.mock.calls.some(([path]) => path.endsWith("/deletion-start"))).toBe(true));
    const [, options] = fetcher.mock.calls.find(([path]) => path.endsWith("/deletion-start"))!;
    expect(options?.body).toBe("{}");
    expect(options?.headers).toHaveProperty("X-CSRF-Token", "csrf");
  });
  it("waits five seconds before initial and continued Web activation status polls", async () => {
    window.history.replaceState(null, "", "/my/app-connect?request=app-request");
    let polls = 0;
    const fetcher = mockApi(path => {
      if (path.endsWith("/approve")) return { status: "APPROVED" };
      if (path.endsWith("/status")) return { status: ++polls === 1 ? "APPROVED" : "ACTIVATED" };
    });
    render(<ManagementApp />);
    const button = await screen.findByRole("button", { name: "このアカウントを作成する" });
    for (const checkbox of screen.getAllByRole("checkbox")) fireEvent.click(checkbox);
    vi.useFakeTimers();
    await act(async () => { fireEvent.click(button); });
    await act(async () => { await vi.advanceTimersByTimeAsync(4999); });
    expect(polls).toBe(0);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(polls).toBe(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(4999); });
    expect(polls).toBe(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(polls).toBe(2);
    expect(fetcher.mock.calls.filter(([path]) => path.endsWith("/status"))).toHaveLength(2);
    expect(screen.getByRole("heading", { name: "アカウントの作成とこのPCへの連携が完了しました" })).toBeVisible();
  });
  it("an unconfirmed lost result never advances to DELETE", async () => {
    window.history.replaceState(null, "", "/my/account-delete");
    const fetcher = mockApi(path => {
      if (path.endsWith("/current") || path.endsWith("/result")) return { id: "dc_test", status: "UNCONFIRMED" };
      if (path === "account/deletion-confirmations") throw new TypeError("Network failure");
    });
    render(<ManagementApp />);
    fireEvent.click(await screen.findByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "アカウントを削除する" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("通信結果"));
    expect(fetcher.mock.calls.some(([, options]) => options?.method === "DELETE")).toBe(false);
  });
});
