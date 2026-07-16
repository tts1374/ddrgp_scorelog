# 現在PR完了記録

developer-only jacket collectorの収集画面を、通常の作業順序が分かるcollection-first UIへ再構成した。detector、capture lifecycle、artifact/checkpoint publish、catalog ingest、DB schemaは変更していない。

## 今回の完了範囲

- 初期表示を `ジャケット収集` とし、preview、window選択、収集開始、曲移動、明示保存を1画面へ集約した。
- 主操作を `このジャケットを保存` に固定し、再開・catalog retryは `詳細・復旧操作`、master/catalog操作、migration、title/artist評価は `管理・設定` へ分離した。
- `DuplicatePreview` などのdetector内部状態を通常画面へ露出せず、未保存stable候補は `新しいジャケットを検出`、保存後は `このジャケットは保存済み` と次操作を表示するpresentation stateを追加した。
- detectorが同じpreviewをduplicateとして扱っても既存のadoptable candidateを維持し、A→B→Aの再訪時もcheckpointの保存済み観測集合から `保存済み` と表示する境界を回帰testで固定した。
- 別jacketへの切替中は直前のstable候補を保存ボタンへ流用せず、表示中jacketがstableまたはduplicateとして確定した場合だけ明示保存を許可する。
- 明示開始前に候補のvisible/minimized状態を確認できる表示を維持した。
- 収集タブが先頭であること、主操作と開始・終了操作が存在すること、内部状態をprimary layoutへbindingしないことをXAML回帰testで固定した。
- READMEへ初回DB選択から収集終了までの基本操作を同期した。
- collector全test、build、実際のWPF起動とcollection-first layoutを確認した。
- ローカルmaster/catalog、source/crop/checkpoint、実capture画像、評価dataset/report、その他生成物は変更していない。

## 維持した境界

- window候補の列挙、明示選択・開始確認、暗黙再選択禁止、capture resource lifecycleを変更していない。
- stable/duplicate判定、明示採用、artifact atomic publish、checkpoint/resume/retry、catalog v1/v2 ingestを変更していない。
- catalog schema、manual review revision/history、runtime current reference、正式個人スコアDBを変更していない。
- ゲーム操作、focus操作、grid自動巡回、公開 `DDRGpScoreViewer`、cleanup/retentionへ進んでいない。

## 未決事項

- 実captureのexpected付き評価母数を収集し、M5c-4の固定gateを満たすtitle/artist取得方式があるか確認する作業は未実施。
- animation、言語、解像度差を含む追加方式/局所前処理が必要かは実測後に判断する。
- gateを満たす方式が得られた場合のcatalog auto-confirm mutation契約、observation receipt version、manual review競合時の接続方法は未固定。
- source locator/retention、reject/cleanupの操作契約は引き続き別責務。

次PRの仕様は、実capture評価結果とM5c全体の残作業優先順位から一意に決まらないため、この記録では固定しない。次PRには着手していない。
