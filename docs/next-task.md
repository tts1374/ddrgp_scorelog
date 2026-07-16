# 現在PR完了記録

developer-only jacket collectorのfresh observation保存経路へ、stable jacket featureと同じcapture frameのstable `INFORMATION`曲名行featureを組み合わせるversion付きcomposite identityを追加した。local observation manifest/checkpointだけをv2へ更新し、catalog schema/writer、自動保存、公開app、正式個人スコアDBは変更していない。

## 今回の完了範囲

- `m5c-jacket-title-composite-identity-v1`を追加し、composite identity version、jacket feature version/hash、title-line feature version/hashをこの順のUTF-8 NUL区切りbyte列としてSHA-256 lower hexを決定的に生成する。
- stable jacket候補とtitle-line detector結果のcapture sequence/timestampを照合し、同じframeかつ既知versionのstable title-lineだけを候補へ関連付ける。
- title-line未表示・未安定、unknown detector/ROI/feature version、非lower-hex、frame不一致ではcomposite identityを作らず、fresh sessionの明示採用を副作用なしで拒否する。
- fresh sessionを`m5c-observation-manifest-v2` / `m5c-observation-checkpoint-v2`へ更新し、jacket feature version/hash、title-line feature version/hash、composite identity version/hashをstrictな必須fieldとして保存する。
- unknown/missing/null/empty/非lower-hex、version不一致、canonical hash不一致、manifest/checkpoint identity drift、同一observation ID異payloadを拒否する。
- observation IDのlower SHA-256形式とcatalog ingest APIを維持しつつ、fresh sessionではcomposite identityを決定的ID入力として同一jacket・異title-lineを別observationにする。
- 既存v1 artifact/checkpointを変更・backfill・削除せず、legacy resume/retryはv1のまま、新規v2 resume/retryはv2のまま処理する。
- atomic artifact publish、checkpoint save、rollback、catalog receipt後のcheckpoint retry、resume時の全artifact照合を既存経路のまま維持した。
- title/artist評価consumerをv1/v2の明示分岐へ更新し、v2 composite identityをstrict検証する。
- synthetic frameで決定性、同一jacket・異title-line、同frame制約、保存内容、corrupt/drift拒否、v2 resumeを回帰testへ追加した。

## 維持した境界

- composite identityをOCR文字列、master song/chart ID、catalog field/index、catalog重複判定、保存可否の自動判定として扱っていない。
- catalog schema/migration/backfill、checkpointとcatalogを横断するduplicate、自動保存、ゲーム操作へ進んでいない。
- 既存v1 artifact/checkpointを暗黙migrationせず、source/crop画像、catalog/master DB、local DB、実capture素材、評価dataset/reportを作成・変更・移動・削除していない。
- 公開`DDRGpScoreViewer`、正式個人スコアDB、M7/M8保存判定、cleanup/retentionへ接続していない。

# 次PR仕様

次PRの実装仕様は未確定。今回PRでは次PR相当へ着手しない。

## 既存資料から確定している順序

次の候補は、catalogへcomposite identityを保持・検索できるschemaと、既存local `source.png`からの非破壊migration/backfillを追加する1件である。その後に、current checkpointとcatalogのidentity集合を保存前に照合する明示opt-in自動保存を別PRとして扱う。

## 未決事項

- catalogのfield/index、schema version、canonical uniquenessと既存jacket feature keyの責務分担。
- migration/backfillの入力artifact version、再開・失敗・競合時のtransaction/publish境界。
- title-lineを再計算できない、欠損、破損、旧version artifactの扱いとreport契約。
- catalog consumer、projection、manual review、ingest互換経路の変更範囲と受入条件。

これらを既存資料だけから一意に決められないため、次PR着手前に仕様と受入条件を固定する。
