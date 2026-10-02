# Web自己歴代Bestの蓄積と同期設計

Issue [#210](https://github.com/tts1374/ddrgp_scorelog/issues/210)の追加決定を、後続[Issue #213](https://github.com/tts1374/ddrgp_scorelog/issues/213)へ固定する。未実装。architecture decisionは[ADR 0012](../adr/0012-purpose-bound-google-identity-confirmation.md)、認証は[14](14_google_player_recovery.md)、プロフィールは[15](15_web_my_profile.md)を正本とする。

## 責務と保証

- 正式個人スコアDBは、そのPCが保持する履歴・Local BestのSource of Truth。AppのBestはそのDBから算出する。正式バックアップを復元すれば復元履歴もLocal Bestへ含む。
- Webは同じPlayerについて、各時点・各PCから公開したcapture由来Bestを蓄積する。現在のPCにない過去のBestも保持し、現在のDBとの一致を同期条件にしない。履歴そのものは保存しない。
- 同期はLocal → Webだけ。Web Bestを正式個人スコアDBへ復元しない。公開Player引き継ぎ、履歴移行、同期ONは別操作。
- Web同期のeligible集合は既存の`source_kind = 'capture'`の正式保存playだけ。`manifest`復元をcaptureへ昇格しない。復元履歴があっても同期対象0件は正常。
- 通常同期は追加・改善だけ。空集合、欠落譜面、値の低下、Local削除から公開Bestを削除・低下させない。
- 意図的な訂正・削除は、既存の公開全削除または確認済みfull snapshot置換で行う。これらは本人が公開履歴をリセットする例外操作。

## fieldごとのmerge

`PlayerChartBestProjectionV1`の5 fieldと既存validation、stable `chart_id`は維持する。既存Web rowと送信rowの各fieldを独立に比較する。

| field | 更新規則 |
|---|---|
| `chart_id` | Webにない既知譜面なら有効rowを追加。unknown chartは既存どおりitem単位deferred |
| `best_score` | 大きい値を保持 |
| `best_ex_score` | 大きい値を保持。scoreとは別play/PC由来でよい |
| `best_clear_type` | `MFC > PFC > GFC > FC > CLEAR > FAILED`で良い方を保持 |
| `best_flare_rank` | `EX > IX > VIII > VII > VI > V > IV > III > II > I`で良い方を保持。nullより有効値を優先し、送信nullで既存値を消さない。FAILED由来flareの拒否等は既存validationを維持 |

同値・全field非改善・空batchは成功no-op。公開更新日時とrevisionは実際に公開集合が変わった時だけ更新する。maxのmergeは冪等で、順序・重複retryによらず同じ結果になる。Google link/login、Credential発行/activation、プロフィール編集ではBest日時を変えない。

## 新旧APIの境界

full snapshotのatomic replace-set契約は変更しない。改善だけの同期には別routeを使い、既存snapshotをmergeとして解釈しない。既存の通常`batch`による置換upsert/deleteは新契約で拒否する。

| Method / route | 契約 |
|---|---|
| `POST /api/v1/me/bests/merge` | active App Credential必須。V1 Projection最大50件。delete payload不可、空配列有効。serverでPlayerを解決しfieldごとにmerge。item単位結果は受領hashとchanged/no-op/既存errorを返す |
| 既存snapshot begin/items/commit/abort | staging、全件validation、revision、atomic replace、空snapshot有効性を維持。全Playerで明示置換許可が必要 |
| `POST /api/v1/me/bests/snapshots/{id}/replacement-review` | 当該Credential所有snapshotの全集合と全公開集合を比較。公開・Local同期対象件数、欠落/低下field・譜面、base revision、集合digestを返す |
| `POST /api/v1/me/bests/snapshots/{id}/replacement-authorize` | 本人の置換確認後、Credential/snapshot/digest/base revisionへ固定した10分TTLの一回限り許可を発行 |
| 既存`DELETE /api/v1/me/bests` | active App Credentialによる明示全削除。既存確認と成功後同期OFFを維持。Player/URL/Credential/Local履歴は保持 |

全Playerに追加・改善同期を適用する。未リリースなので旧版互換のPlayer別policyは追加しない。既存の開発データは保持し、名前/ID/所有権をmigrationで再作成しない。

既存batchの置換upsert/deleteは409 `HISTORICAL_BEST_REQUIRED`で拒否する。snapshot commitは置換許可なしなら409 `REPLACEMENT_CONFIRMATION_REQUIRED`。使用する全Worker originのwrite経路へ適用し、古いendpointから迂回できない。読み取りと明示公開全削除は維持する。

merge、snapshot commit、公開全削除は変更transaction内でCredentialの未失効を再検証する。旧PCのin-flight requestが認証middlewareを通過済みでも、権限移行後の書込みを許さない。並行mergeはfield比較と書込みを一つのtransactionで行い、更新を失わない。

## Windowsの同期と状態

- 対応版は通常同期をmergeへ統一する。同期OFF中は自動Web通信しない。初回ON、OFF→ON、bulk restore、repair、同期状態DB欠損では現在のcapture集合を50件ずつmergeで再送する。
- 再送途中の成功分は公開してよい。途中失敗・再起動はretryし、既存のより良いBestや未送信譜面は保持する。通常再送のためにsnapshotを作らない。
- desired/synced hashは正式DB外の既存同期SQLiteを使う。syncedは「このPCのProjectionをWebが受領済み」を表し、Web値との一致を意味しない。受領hashと一致した時だけackし、送信中にcaptureが追加された場合は最新desiredを残す。
- Localから消えたProjectionはpending deleteとして送らない。ローカル同期stateの追跡rowだけ除去できる。誤登録の訂正でも自動でWebを下げない。
- `Idle`はこのPCに未送信の変更がない状態。空eligible集合でも同期ON/Idleへ進める。Web 862件・このPC同期対象0件等の差を異常表示にしない。
- 新PCのCredential activation後は同期OFF。本人が「連携を再開する」を押せば空DB/manifest復元でもmergeできる。「連携を停止する」はこのPCの自動送信だけを止める。今すぐ同期はOFF中無効。完全なcapture集合再構築や既存Web値との比較を再開条件にしない。
- 説明文: 「Webにはこれまで公開した自己ベストが残ります。このPCからは新しい記録や改善した記録を追加します。スコア履歴の移行はバックアップから行ってください。」Local Best件数とcapture同期対象件数を混同しない。

## 明示置換と訂正

設定の別操作「このPCの記録で公開Bestを置き換える」を使用する。同期ON、引き継ぎ、復元、repair、retryでは自動で実行しない。

1. 現在のcapture集合Lを固定して既存snapshot stagingへuploadする。通常の公開集合Wは変更しない。
2. serverはremoved chartも含む全WとLを比較する。Public APIの絞込み・件数だけで判断しない。digestは`chart_id` ordinal昇順、V1固定field順canonical JSON arrayのUTF-8 SHA-256。
3. 公開名/URL、公開件数、同期対象件数、消える譜面と低下する項目、代表譜面を表示。「このPCにない公開Bestも削除されます」と未選択の確認checkbox・最終実行ボタンを置く。L空は「公開Bestをすべて削除します」。manifest履歴がLocalにあっても同期対象外であることを説明する。
4. 本人確認から許可を発行し、commit transactionでowner/active Credential/digest/revision/TTLを照合する。許可の転用、期限切れ、chunk変更、stale revisionはlive不変、再比較・再確認へ戻る。許可を自動更新しない。
5. commitで公開集合をatomicに置換し許可を消費する。空snapshotも有効。応答喪失retryは同じ成功を返す。同内容なら公開日時不変。Credential activation/他PC失効はこの操作に含めない。

固定snapshotで失った過去Web値はLocalへ復元しない。訂正後は追加・改善の通常同期へ戻る。送信中のcaptureは継続し、最新desiredを再送する。DB復元/差替えは置換commitと排他し、DB世代変更時に未commitの比較・許可を破棄する。純粋なmerge再送は新DBから再計算すればよい。

## Acceptance criteria / Required tests

| ID | 条件と検証 |
|---|---|
| H1 | 空DB・空merge・manifest復元・不完全なcapture集合・Local削除・各field低下から既存Web Bestを消さず下げない。Web/Local不一致を通常状態として同期ONにできる |
| H2 | score/EX/clear/flareを独立merge。null、同値、順序入替、重複retry、並行mergeで正しい最大値。非改善時はBest日時/revision不変 |
| H3 | 初回ON/再ON/復元/repair/state欠損はmerge再送だけ。部分成功・通信失敗・送信中capture・ack応答喪失でも再送可能。synced hashをWeb一致として扱わない |
| H4 | 全Playerの旧batch・無許可snapshotは409拒否。古いWorker origin、直接API、失効との並行writeも迂回不可。既存開発データのID/所有権はmigrationで保持 |
| H5 | 明示置換は全W比較・欠落/独立field低下/removed chart/空の確認を伴う。許可転用/TTL/chunk変更/revision競合/unknown chart/中断ではlive不変。空置換・同内容・commit retryのatomic/冪等性を維持 |
| H6 | 公開全削除は確認後同期OFF。Google解除やCredential activationでBest/URL/Localを変更しない。正式保存・backup・stable master ID・capture限定・一方向同期を維持 |

手動確認: 旧PCの公開Bestを残した新PCの空DB→Webログイン→activation→連携再開→新capture追加、manifest復元後再開、低いscoreと良いclearの組合せ、停止→再開、再起動、誤登録をLocal修正→明示置換、置換cancel/空全削除を開発環境で確認する。設計段階では実通信・runtime testを実施せず、後続実装でrepository既定CIとH1〜H6を実施する。
