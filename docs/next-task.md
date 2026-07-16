# 現在PR完了記録

current catalog schema version 1の`observation_unresolved`を、既存M5c title/artist evaluationとM4 master照合へstrict read-onlyで接続し、collector manual review画面とlocal reportへ候補・理由・失敗分類を表示する通常導線を追加した。

## 今回の完了範囲

- projectionをversion 4へ更新し、各review rowへversion付き`candidate_evaluation`を追加した。
- current catalog rowとlocal manifest、source/crop、checkpoint、master、catalog、extractor、jacket/title-line/composite identity、hash、versionを照合する。
- artifact/checkpoint欠損、duplicate、corrupt、identity/version drift、評価中のfingerprint変化をrow単位の評価不能として扱い、別画像やhashだけからsongを推測しない。
- 既存`tesseract-autocontrast-v1`、normalization、confidence gate、M4 title-primary/artist tie-breakerを再利用した。
- `exact_unique`、`alias_unique`、`ambiguous`、`no_candidate`、`low_confidence`、`evaluation_failed`、`evaluation_unavailable`、`not_eligible`を区別する。
- collector manual review画面へjacket preview、observation ID、persisted status/revision、OCR title/artist/confidence、candidate song、candidate reason、failure reasonを表示した。
- candidate分類filter、安定sort、明示refreshを追加した。
- `data/jacket_catalog_collector/unresolved-candidate-evaluation/`へCSV、JSON、Markdown reportをatomic生成できるようにした。
- candidate表示、filter、sort、refresh、report生成はcatalog writerを呼ばず、明示manual review操作だけが既存transactionを使う境界を維持した。
- projection producer、C# strict loader、fixture、README、roadmap、storage/master-match designをversion 4契約へ同期した。

## 516件のローカル評価実績

current master/catalogとGit管理外artifact/checkpointをread-onlyで評価し、local reportを生成した。expectedまたは人手確認結果がないため、候補を正解扱いせずprecisionは報告していない。

- total observations: 516
- current unresolved observations: 516
- eligible observations: 516
- evaluated observations: 516
- canonical exact unique candidates: 39
- alias unique candidates: 0
- ambiguous candidates: 4
- no candidates: 0
- low confidence: 444
- evaluation failed: 29
- not evaluated: 0
- title status: ok 158 / low confidence 298 / empty 60
- artist status: ok 93 / low confidence 374 / empty 49
- candidate reasons: canonical exact 39 / title match + artist mismatch 4

低confidenceまたはemptyが473件で支配的であり、特にartistは423件が非okだった。一意候補39件と曖昧候補4件は未監査candidateであり、auto-confirmや正解率の根拠にはしない。

## 維持した境界

- current catalog schema version 1、M4 master schema、artifact manifest/checkpoint v1/v2、observation ID、manual review revision/historyを変更していない。
- candidate、OCR raw、title-line hash、composite identityをpersisted songや正式値へ自動昇格していない。
- 一意候補でもauto-confirmしていない。
- title/artist OCR、ROI、preprocessing、normalization方式を変更していない。
- catalog migration、backfill、repair、既存row一括更新、local DB/artifact/checkpoint/source/cropの削除・上書きを行っていない。
- capture lifecycle、window selection、自動保存、grid巡回、ゲーム操作、公開app、正式個人スコアDBへ接続していない。
- local report、DB、画像、artifact、checkpointをGit管理していない。

# 次PR

## 現在の判断材料

516件評価ではOCR低confidence/emptyが主因で、master未一致やalias不足よりtitle/artist認識側が先行課題である。artist非ok 423件、title非ok 358件だが、同一observation内の失敗組合せ、画像状態、文字種、confidence分布、代表失敗の目視確認はまだ固定していない。

## 未決事項

- artist単独改善、title/artist共通preprocessing、ROI、OCR engine/profileのどれを次の独立PRにするか。
- 低confidenceとemptyを原因別にどう分割し、どのfixture/実capture代表を受入条件にするか。
- canonical exact candidate 39件とambiguous 4件を人手監査し、recognition変更の回帰基準へ使えるか。

次PRの受入条件と変更境界は既存資料だけでは一意に決まらない。上記の原因分析と代表目視を行って仕様を固定するまで、OCR改善、bulk review、auto-confirmには着手しない。
