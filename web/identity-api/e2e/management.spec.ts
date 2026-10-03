import { expect, test, type Page } from "@playwright/test";

const profile = { display_name: "DDR GP Player", public_player_id: "p_owner", public_url: "http://127.0.0.1:5173/player/p_owner" };
async function mockManagement(page: Page, options: { app?: boolean; delete?: boolean; unlinked?: boolean } = {}) {
  let name = profile.display_name;
  let appStatus = "PENDING";
  await page.route("**/api/v1/account/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/session")) return route.fulfill({ json: { email: "owner@example.com", google_linked: !options.unlinked, csrf_token: "csrf" } });
    if (path.endsWith("/profile")) {
      if (route.request().method() === "PATCH") name = route.request().postDataJSON().display_name;
      return route.fulfill({ json: { ...profile, display_name: name } });
    }
    if (path.endsWith("/current")) return route.fulfill({ json: { id: "dc_test", status: "UNCONFIRMED" } });
    if (path.endsWith("/deletion-confirmations")) return route.fulfill({ json: { id: "dc_test", status: "CONFIRMED" } });
    await route.fulfill({ status: 400, json: { error: { code: "UNEXPECTED_TEST_REQUEST" } } });
  });
  await page.route("**/api/v1/account", route => route.fulfill({ json: { id: "dc_test", status: "DELETED" } }));
  if (options.app) await page.route("**/api/v1/auth/app-authorizations/**", async route => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/confirmation")) return route.fulfill({ json: { status: appStatus, intent: "login", purpose: "connect", player: profile, registered_google: true, comparison_code: "482719AB" } });
    if (path.endsWith("/approve")) { appStatus = "APPROVED"; return route.fulfill({ json: { status: appStatus } }); }
    if (path.endsWith("/status")) { appStatus = "ACTIVATED"; return route.fulfill({ json: { status: appStatus } }); }
    return route.fulfill({ status: 400, json: { error: { code: "UNEXPECTED_TEST_REQUEST" } } });
  });
}

for (const width of [1280, 390]) {
  for (const registered of [false, true]) {
    test(`unified App entry at ${width}px confirms ${registered ? "transfer" : "creation"} after Google`, async ({ page }) => {
      const errors: string[] = [];
      page.on("pageerror", error => errors.push(error.message));
      page.on("console", message => {
        if (message.type() === "error" && !(message.location().url.endsWith("/api/v1/account/session") && message.text().includes("401"))) errors.push(message.text());
      });
      await page.setViewportSize({ width, height: 900 });
      let authenticated = false;
      let approvals = 0;
      await page.route("**/api/v1/account/**", route => {
        const path = new URL(route.request().url()).pathname;
        if (path.endsWith("/session")) return authenticated
          ? route.fulfill({ json: { email: "owner@example.com", google_linked: registered, csrf_token: "csrf" } })
          : route.fulfill({ status: 401, json: { error: { code: "WEB_SESSION_REQUIRED" } } });
        if (path.endsWith("/profile")) return route.fulfill({ json: profile });
        return route.fulfill({ status: 400 });
      });
      await page.route("**/api/v1/auth/web/context", route => route.fulfill({ json: { csrf_token: "browser-csrf" } }));
      await page.route("**/api/v1/auth/app-authorizations/**", route => {
        const path = new URL(route.request().url()).pathname;
        if (path.endsWith("/web-start")) {
          expect(route.request().postDataJSON()).toEqual({});
          expect(route.request().headers()["x-csrf-token"]).toBe("browser-csrf");
          authenticated = true;
          return route.fulfill({ json: { url: "/my/app-connect?request=unified-test" } });
        }
        if (path.endsWith("/confirmation")) return route.fulfill({ json: { status: "PENDING", intent: registered ? "login" : "register", purpose: "connect", player: registered ? profile : null, registered_google: registered, comparison_code: "482719AB" } });
        if (path.endsWith("/approve")) approvals++;
        return route.fulfill({ status: 400 });
      });
      await page.goto("/my/app-connect?request=unified-test");
      await expect(page.getByRole("button", { name: "Googleで続ける", exact: true })).toBeEnabled();
      await expect(page.getByRole("button", { name: /^(新規登録|ログイン)$/ })).toHaveCount(0);
      await page.screenshot({ path: `../../logs/unified-entry-${width}.png`, fullPage: true });
      await page.getByRole("button", { name: "Googleで続ける", exact: true }).click();
      await expect(page.getByRole("heading", { name: registered ? "このPCへ引き継ぐ" : "アカウントの作成を確認", exact: true })).toBeVisible();
      const approve = page.getByRole("button", { name: registered ? "このPCへ引き継ぐ" : "このアカウントを作成する", exact: true });
      await expect(approve).toBeDisabled();
      await page.getByRole("checkbox", { name: "手元のアプリとコードが一致しています。" }).check();
      await expect(approve).toBeDisabled();
      await page.getByRole("checkbox", { name: "アカウントと、この後に行うことを確認しました。" }).check();
      await expect(approve).toBeEnabled();
      expect(approvals).toBe(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
      expect(errors).toEqual([]);
    });
  }
  test(`management profile at ${width}px saves the public name and exposes only Web management actions`, async ({ page }) => {
    const errors: string[] = [];
    page.on("pageerror", e => errors.push(e.message));
    page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
    await page.setViewportSize({ width, height: 900 });
    await mockManagement(page);
    const response = await page.goto("/my/profile");
    expect(response?.headers()["cache-control"]).toContain("no-store");
    await expect(page).toHaveTitle("マイプロフィール - GP Score Log");
    await expect(page.getByRole("heading", { name: "マイプロフィール" })).toBeVisible();
    await page.getByRole("textbox", { name: "公開プレーヤー名" }).fill("  2TEN  ");
    await page.getByRole("button", { name: "名前を保存" }).click();
    await expect(page.getByRole("status")).toHaveText("公開プレーヤー名を保存しました。");
    await expect(page.getByRole("textbox", { name: "公開プレーヤー名" })).toHaveValue("2TEN");
    await expect(page.getByRole("button", { name: "Google連携を解除", exact: true })).toHaveCount(0);
    await expect(page.getByRole("link", { name: "公開記録を削除する", exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    expect(errors).toEqual([]);
    await page.screenshot({ path: `../../logs/management-profile-${width}.png`, fullPage: true });
  });
}
test("App comparison starts unchecked, waits for activation, and completes with sync enabled", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockManagement(page, { app: true });
  await page.goto("/my/app-connect?request=app-test");
  const approve = page.getByRole("button", { name: "このPCへ引き継ぐ", exact: true });
  await expect(approve).toBeDisabled();
  await page.getByRole("checkbox", { name: "手元のアプリとコードが一致しています。" }).check();
  await expect(approve).toBeDisabled();
  await page.getByRole("checkbox", { name: "アカウントと、この後に行うことを確認しました。" }).check();
  await page.screenshot({ path: "../../logs/management-app-confirmation-390.png", fullPage: true });
  await approve.click();
  await expect(page.getByRole("heading", { name: "アプリで完了を確認してください" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "このPCへの引き継ぎが完了しました" })).toBeVisible({ timeout: 15000 });
  await expect(page.getByText("同期がONになります。", { exact: false })).toBeVisible();
});
test("Web account deletion without App repeats target and requires explicit consent", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockManagement(page, { delete: true });
  await page.goto("/my/account-delete");
  const remove = page.getByRole("button", { name: "アカウントを削除する", exact: true });
  await expect(remove).toBeDisabled();
  await expect(page.getByText(profile.display_name, { exact: true })).toBeVisible();
  await expect(page.getByText("PC内のスコア履歴は残ります。", { exact: false })).toBeVisible();
  await page.screenshot({ path: "../../logs/management-delete-390.png", fullPage: true });
  await page.getByRole("checkbox").check();
  await remove.click();
  await expect(page.getByRole("heading", { name: "アカウントを削除しました" })).toBeVisible();
});

test("public-record deletion at mobile width confirms stopped sending and guides manual recreation", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await mockManagement(page);
  let deleted = false;
  const requests: unknown[] = [];
  await page.route("**/api/v1/account/bests/deletion-confirmation", route => route.fulfill({ json: { status: "UNCONFIRMED" } }));
  await page.route("**/api/v1/account/bests", route => {
    requests.push(route.request().postDataJSON()); deleted = true;
    return route.fulfill({ json: { status: "DELETED" } });
  });
  await page.goto("/my/profile");
  await page.getByRole("link", { name: "公開記録を削除する", exact: true }).click();
  await expect(page).toHaveTitle("公開記録の削除 - GP Score Log");
  await expect(page.getByText(profile.display_name, { exact: true })).toBeVisible();
  const remove = page.getByRole("button", { name: "Webの公開記録を削除", exact: true });
  await expect(remove).toBeDisabled();
  await page.getByRole("checkbox", { name: "このアカウントの公開記録をすべて削除することを確認しました。" }).check();
  await expect(remove).toBeDisabled();
  await page.getByRole("checkbox", { name: "アプリでWebへの送信を止めました。" }).check();
  await expect(remove).toBeEnabled();
  expect(deleted).toBe(false);
  expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
  await page.screenshot({ path: "../../logs/management-public-delete-390.png", fullPage: true });
  await remove.click();
  await expect(page.getByRole("heading", { name: "公開記録を削除しました" })).toBeVisible();
  await expect(page.getByText("Webへの送信を再開する", { exact: false })).toBeVisible();
  expect(requests).toEqual([{ confirmed: true, sync_stopped: true }]);
});

test("cancelling public-record deletion clears its consent and returns to the profile without deleting", async ({ page }) => {
  await mockManagement(page);
  let cancelled = false;
  let deletes = 0;
  await page.route("**/api/v1/account/bests/deletion-confirmation", route => cancelled
    ? route.fulfill({ status: 403, json: { error: { code: "OPERATION_PROOF_REQUIRED" } } })
    : route.fulfill({ json: { status: "UNCONFIRMED" } }));
  await page.route("**/api/v1/account/bests/deletion-cancel", route => {
    expect(route.request().postDataJSON()).toEqual({});
    expect(route.request().headers()["x-csrf-token"]).toBe("csrf");
    cancelled = true;
    return route.fulfill({ status: 204 });
  });
  await page.route("**/api/v1/account/bests", route => { deletes++; return route.fulfill({ json: { status: "DELETED" } }); });
  await page.goto("/my/public-data-delete");
  await page.getByRole("checkbox").first().check();
  await page.getByRole("button", { name: "キャンセル", exact: true }).click();
  await expect(page.getByRole("heading", { name: "マイプロフィール", exact: true })).toBeVisible();
  await page.getByRole("link", { name: "公開記録を削除する", exact: true }).click();
  await expect(page.getByRole("button", { name: "Googleでアカウントを確認", exact: true })).toBeVisible();
  await expect(page.getByRole("checkbox")).toHaveCount(0);
  expect(cancelled).toBe(true);
  expect(deletes).toBe(0);
});
test("unlinked and cancelled Google states cannot silently approve or create", async ({ page }) => {
  await mockManagement(page, { unlinked: true, app: true });
  await page.goto("/my/profile");
  await expect(page.getByText("アカウントが登録・連携されていません", { exact: true })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "公開プレーヤー名" })).toHaveCount(0);
  await page.goto("/my/app-connect?request=app-test&auth_error=cancelled");
  await expect(page.getByRole("button", { name: "Googleで続ける", exact: true })).toBeVisible();
  await expect(page.getByRole("checkbox")).toHaveCount(0);
  await expect(page.getByRole("alert")).toContainText("Googleログインを中止しました。");
});

test("Google choice logs out the bound session while preserving the App transaction and clearing consent", async ({ page }) => {
  await mockManagement(page, { app: true });
  const logouts: unknown[] = [];
  let signedOut = false;
  await page.route("**/api/v1/auth/web/logout", async route => {
    logouts.push(route.request().postDataJSON()); signedOut = true;
    return route.fulfill({ status: 204 });
  });
  await page.route("**/api/v1/account/session", route => signedOut
    ? route.fulfill({ status: 401, json: { error: { code: "WEB_SESSION_REQUIRED" } } })
    : route.fulfill({ json: { email: "owner@example.com", google_linked: true, csrf_token: "csrf" } }));
  await page.route("**/api/v1/auth/web/context", route => route.fulfill({ json: { csrf_token: "browser-csrf" } }));
  await page.goto("/my/app-connect?request=app-test");
  await page.getByRole("checkbox", { name: "手元のアプリとコードが一致しています。" }).check();
  await page.getByRole("button", { name: "別のアカウントでログイン" }).click();
  await expect(page.getByRole("button", { name: "Googleで続ける", exact: true })).toBeVisible();
  expect(logouts).toEqual([{ app_authorization_id: "app-test" }]);
  await expect(page).toHaveURL(new URL("/my/app-connect?request=app-test", test.info().project.use.baseURL).href);
  await expect(page.getByRole("checkbox")).toHaveCount(0);
});

test("choosing Google before binding uses ordinary logout and retains the App entry", async ({ page }) => {
  await mockManagement(page, { app: true });
  const logouts: unknown[] = [];
  let signedOut = false;
  await page.route("**/api/v1/auth/app-authorizations/**/confirmation", route => route.fulfill({ status: 403, json: { error: { code: "AUTHORIZATION_INVALID" } } }));
  await page.route("**/api/v1/auth/web/logout", async route => { logouts.push(route.request().postDataJSON()); signedOut = true; return route.fulfill({ status: 204 }); });
  await page.route("**/api/v1/account/session", route => signedOut
    ? route.fulfill({ status: 401, json: { error: { code: "WEB_SESSION_REQUIRED" } } })
    : route.fulfill({ json: { email: "owner@example.com", google_linked: true, csrf_token: "csrf" } }));
  await page.route("**/api/v1/auth/web/context", route => route.fulfill({ json: { csrf_token: "browser-csrf" } }));
  await page.goto("/my/app-connect?request=app-test");
  await page.getByRole("button", { name: "別のアカウントでログイン" }).click();
  await expect(page.getByRole("button", { name: "Googleで続ける", exact: true })).toBeVisible();
  expect(logouts).toEqual([{}]);
  await expect(page.getByText("owner@example.com", { exact: true })).toHaveCount(0);
});
