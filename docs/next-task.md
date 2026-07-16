# 現在PR完了記録

M5c-4 merge後に判明したdeveloper-only collectorの起動不能と、実DDR GP windowを候補として認識できない問題を修正した。read-only view-model propertyを表示する`Run.Text` bindingを明示的なone-way bindingへ統一し、実行ファイル名`ddr-konaste`を限定的な候補根拠として追加した。

## 今回の完了範囲

- catalog identity/schema/capabilityとobservation session/catalog receiptの`Run.Text` bindingに`Mode=OneWay`を指定した。
- `Run.Text`へ追加されるbindingが明示的にone-wayであることを確認する回帰testを追加した。
- 実環境の`ddr-konaste` / `DanceDanceRevolution`を候補として認識し、類似名やtitleだけの誤検出を避ける回帰testを追加した。
- DDR GPが管理者権限で動作する環境ではcollectorも同等権限で起動する必要があり、権限不足時にprocess start identityを省略して候補化しない境界をREADMEへ明記した。
- 保護された`ddr-konaste`へ応答しない`PrintWindow` previewを呼ばず、候補列挙を完了させる境界と回帰testを追加した。明示開始後のWindows Graphics Captureは変更していない。
- collector全test、build、実際のウィンドウ起動と応答を確認した。
- catalog schema、収集・評価処理、保存境界、ローカルDBや画像・生成物は変更していない。

## 前提となるM5c-4完了範囲

M5c-4として、developer-only collectorが明示採用したimmutable artifactからtitle/artist取得方式を比較する評価経路を追加した。

- Git管理外のlocal datasetを読むstrict loaderを追加した。artifact root外path、未知/欠損field、重複manifest/observation ID、欠損・改変画像、source dimensions不一致、master/catalog/extractor driftをreport生成前に拒否する。
- `m5c-song-select-title-artist-roi-v1`に対して、追加Python packageを増やさないlocal Tesseractの`autocontrast` / `white-threshold`方式をversion付きで比較する。
- raw/normalized title/artist、confidence、field status/failure、既存M4/M5bのtitle-primary・artist tie-breaker候補、expected coverageをCSV/JSON/Markdownへbyte-stableに生成する。
- expected title/artistが両方ある行を`evaluated`、片方だけを`partially_evaluated`、両方ない行を`no_expected_values`とし、accuracy/adoption gateには`evaluated`だけを使う。
- 採用gateを、repository fixture gate、実capture evaluated 30件以上、pair exact 95%以上、field confidence 0.90以上、auto-confirm候補precision 100%、既知誤自動確定0件に固定した。
- collectorからlocal datasetを明示選択して評価CLIを実行し、方式別の採用状態と失敗理由を表示できる入口を追加した。
- 同一入力再評価、別observation IDの同一画像、title一致/artist不一致、低confidence/部分失敗、old/corrupt/root外入力、master/catalog byte不変をfixtureで固定した。
- README、roadmap、M4/M5b/M5c designを同期した。

## 維持した境界

- 採用済みcollector実capture datasetがローカルに存在しないため、どの方式も`adopted`として扱わず、auto-confirm writerへ接続していない。
- catalog schema、v1/v2 ingest、manual review revision/history、runtime current reference、checkpointを変更していない。
- 条件未達、複数候補、不一致、空、低confidence、engine failureは既存の空title/artist・`unresolved` / manual review経路へ残る。
- ゲーム操作、focus操作、grid自動巡回、公開`DDRGpScoreViewer`、正式保存workflow、正式個人スコアDB、cleanup/retentionへ進んでいない。
- source/crop/checkpoint、実capture画像、dataset、評価report、ローカルDBはGit管理していない。

## 未決事項

- 実captureのexpected付き評価母数を収集し、固定gateを満たす方式があるか確認する作業は未実施。
- animation、言語、解像度差を含む追加方式/局所前処理が必要かは実測後に判断する。
- gateを満たす方式が得られた場合のcatalog auto-confirm mutation契約、observation receipt version、manual review競合時の接続方法は未固定。
- source locator/retention、reject/cleanupの操作契約は引き続き別責務。

次PRの仕様は、実capture評価結果とM5c全体の残作業優先順位から一意に決まらないため、この記録では固定しない。次PRには着手していない。
