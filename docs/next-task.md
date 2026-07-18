# 現在PR完了記録

current unresolved observation 516件を対象に、Tesseract installed language、title `psm=6/7`、artist scale/sharpen、`eng` / `jpn+eng`をstrict read-onlyで比較するM5c OCR診断導線を追加した。OCR改善方式、ROI、confidence gate、candidate昇格条件は変更していない。

## 今回の完了範囲

- `tools.vision_poc.title_artist_ocr_diagnostics` の1コマンド入口を追加した。
- current catalog/masterとmanifest/source/crop/checkpointを既存projectionでstrict照合し、persisted `unresolved` かつ検証済みsourceだけを診断対象にした。
- Tesseract executable、`--list-langs`、各available language構成のTSV contractを事前検査し、要求language不足をversion付き`m5c-title-artist-ocr-diagnostics-report-v1`の`ocr_unavailable` / `tesseract_language_unavailable_v1:<lang>`として固定した。別languageへfallbackせず、TSV config欠損やinvalid outputは全件処理前にfail-fastする。
- titleは4倍sharpenの`psm=6/7`、artistは`psm=7`の現行5倍・追加2倍相当10倍・追加3倍相当15倍とsharpen有無を、`eng` / `jpn+eng`で比較するprofile matrixを固定した。
- profile/configuration別にraw、normalized、confidence、status、failure reason、field status組合せ、M4 candidate status/reason/song IDsを集計する。
- confidenceのcount/minimum/p25/median/p75/maximumをprofile別に出す。
- `data/jacket_catalog_collector/title-artist-ocr-diagnostics/`へ`ocr_diagnostics.csv/json/md`と`representative_contact_sheet.png`をatomic生成する。
- contact sheetは現行M5c baseline（title `psm=7`、artist 5倍sharpen）のtitle/artist statusとcandidate reasonの組合せから代表を安定選択し、source縮小、title ROI、artist ROIを並べる。
- `--representative-limit 0`はsource/ROI代表を1件も選ばず、空状態のcontact sheetだけを生成する。
- 診断前後でmaster/catalog hashとmanifest/source/crop/checkpoint fingerprintを再検査し、変化時はreportをpublishしない。
- 516枚を同時保持せず1枚ずつ読み込むstreaming処理にし、高倍率profile比較時のsource image memoryをboundedにした。
- README、roadmap、storage/master-match designを診断契約へ同期した。

## 516件のローカル比較実績

current master、catalog schema version 1の516 reference、Git管理外artifact/checkpointをread-onlyで評価した。全516件がeligibleで、DB/artifact/checkpoint fingerprint変化はなかった。reportとcontact sheetはGit管理外の`data/`だけへ生成した。

system Tesseract 5.5.0のinstalled languageは`eng` / `osd`だった。system installationを変更せず、Git管理外の`data/tessdata/`へ既存`eng`、公式`tesseract-ocr/tessdata_fast`の`jpn`、既存`configs/tsv`を隔離配置し、実行command内だけ`TESSDATA_PREFIX`を設定した。診断report上のavailable languageは`eng` / `jpn`、unavailable profileは0だった。

local tessdata SHA-256:

- `eng.traineddata`: `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`
- `jpn.traineddata`: `1F5DE9236D2E85F5FDF4B3C500F2D4926F8D9449F28F5394472D9E8D83B91B4D`
- `configs/tsv`: `59D079BB75D8B3D7C839A3564580CB559E362C93A9D70F234E421C0C3E767E04`

最初のcustom tessdata runでは`configs/tsv`欠損によりTesseractがplain textを返し、全profileが`output_invalid`になった。これを比較結果として採用せず、各language構成のTSV contractを全件処理前に実行probeするfail-fast gateを追加した。corrected runではengine/TSV failure 0、516件×全16 profileを評価し、約44分42秒だった。source image streaming後のPython working setは約65MBで、修正前の一括保持約2GBからboundedになった。

title `eng`比較:

- `psm=6`, scale 4, sharpen: empty 146 / low confidence 212 / ok 158、confidence median 0.62288634。
- `psm=7`, scale 4, sharpen: empty 60 / low confidence 298 / ok 158、confidence median 0.48773350625。
- `psm=6`は`psm=7`よりemptyを86件増やしたがlow confidenceを86件減らし、ok件数とbaselineのcandidate結果は同じだった。
- baseline artistとのcandidate結果はいずれもcanonical exact 39 / title match + artist mismatch 4 / extraction failed 473だった。

title `jpn+eng`比較:

- `psm=6`, scale 4, sharpen: empty 146 / low confidence 179 / ok 191、confidence median 0.901769913、日本語文字を含むraw 152件。
- `psm=7`, scale 4, sharpen: empty 61 / low confidence 265 / ok 190、confidence median 0.8553341875、日本語文字を含むraw 155件。
- baseline artistとのcandidate結果はいずれもcanonical exact 41 / title match + artist mismatch 3 / extraction failed 472だった。
- canonical exact件数はbaseline 39から41へ増えたが、同じcandidate集合への単純追加ではなく、baselineから8件増・6件減だった。

artist `eng`, `psm=7`比較:

| scale / sharpen | empty | low confidence | ok | confidence median | canonical exact | artist mismatch |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 5 / yes | 49 | 374 | 93 | 0.293126755 | 39 | 4 |
| 5 / no | 47 | 379 | 90 | 0.31201004 | 36 | 4 |
| 10 / yes | 46 | 389 | 81 | 0.36839947 | 32 | 1 |
| 10 / no | 47 | 390 | 79 | 0.38019272 | 32 | 1 |
| 15 / yes | 46 | 395 | 75 | 0.45808401 | 32 | 3 |
| 15 / no | 45 | 401 | 70 | 0.4712311933333333 | 29 | 3 |

追加拡大やsharpen除去はemptyを最大4件減らしたが、confidence 0.90以上のok件数とcanonical exact candidateを減らした。現行5倍sharpenが今回の`eng`比較ではok 93件、canonical exact 39件、artist mismatch 4件で最も多かった。median上昇だけではconfidence gate通過やcandidate改善を意味しない。

artist `jpn+eng`, `psm=7`比較:

| scale / sharpen | empty | low confidence | ok | confidence median | 日本語raw | canonical exact | artist mismatch |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5 / yes | 49 | 367 | 100 | 0.63674053 | 120 | 34 | 1 |
| 5 / no | 47 | 364 | 105 | 0.6866571 | 126 | 32 | 2 |
| 10 / yes | 46 | 362 | 108 | 0.7032935254166667 | 100 | 37 | 1 |
| 10 / no | 47 | 362 | 107 | 0.68055093875 | 101 | 39 | 1 |
| 15 / yes | 46 | 369 | 101 | 0.679593275 | 109 | 36 | 4 |
| 15 / no | 45 | 378 | 93 | 0.68548523 | 107 | 32 | 3 |

`jpn+eng`は全artist profileで同じ`eng`条件よりok件数を増やし、最大は10倍sharpenの108件だった。一方、canonical exact最大は10倍no-sharpenの39件で、baselineと同数でもcandidate集合は4件増・4件減だった。10倍sharpenはcanonical exact 37件で、baselineから4件増・6件減だった。ok、median、candidate件数のどれか1つだけでは方式を選べない。

expectedまたは監査済みtruth setがないため、candidateを正解扱いせずprecisionは報告していない。`jpn+eng`で増えたcandidateも、baselineから消えたcandidateも未監査であり、件数増加を精度改善や採用根拠にしていない。

## 維持した境界

- current catalog schema version 1、projection version 4、M4 master schema、artifact manifest/checkpoint v1/v2、observation ID、manual review revision/historyを変更していない。
- source/crop/manifest/checkpoint、master/catalog DB、persisted reference/candidate/song/statusを変更していない。
- OCR raw、diagnostic candidate、contact sheetをpersisted songや正式値へ昇格していない。
- title/artist ROI、normalization、confidence 0.90 gate、auto-confirm条件を変更していない。
- system Tesseractや永続環境変数を変更せず、local traineddataは`data/`配下だけに置いてGit管理していない。公式`jpn.traineddata`取得以外のnetwork操作、認証、費用発生操作を行っていない。
- catalog migration、backfill、repair、bulk review、source/crop削除を行っていない。
- capture lifecycle、grid巡回、ゲーム操作、公開app、正式個人スコアDBへ接続していない。
- local report、DB、画像、artifact、checkpointをGit管理していない。

# 次PR

## 完了状態

日本語OCR環境確認、local `jpn+eng`実測、title `psm=6/7`比較、artist scale/sharpen比較、status/confidence/raw/candidate集計、代表contact sheetまで完了した。`jpn+eng`は日本語rawとok件数を増やしたがcandidate集合を入れ替えた。truth setなしでは、language、title PSM、artist scale/sharpenの採用組合せを決められない。

## 未決事項

- representative contact sheetとrawを人手監査し、baseline 43 candidateと`jpn+eng`で増減したcandidateのtruth setをどの範囲で作るか。
- local `jpn` traineddataを後続のdeveloper運用でどう準備・version固定・検証するか。traineddata自体をrepository、Release、通常artifactへ含めるかは未決であり、今回PRでは含めない。
- title `psm=6`のempty増加とconfidence分布変化をどう評価し、現行`psm=7`から変更する根拠を何に固定するか。
- artist `jpn+eng` 10倍sharpenのok 108件と10倍no-sharpenのcanonical exact 39件のどちらを優先するか。candidate集合差をtruth監査せず決めない。
- artist ROI切れ、装飾、scale、sharpenの寄与を、現行ROIを変えずに追加診断できるか。
- 全16 profile約45分のdeveloper-only診断を毎回全件実行するか、監査済み固定subsetを別契約として作るか。
- title exact uniqueかつartist unavailable時のmanual candidate提示を独立PRとして仕様化するか。
- 診断結果を受けてlanguage設定、ROI、前処理、confidence gate、manual candidate条件のどれを後続PRへ選ぶか。

次PRの仕様は既存資料と今回実測だけから一意に決まらない。product判断またはcandidate truthの人手監査材料が得られるまで、auto-confirm、bulk review、ROI変更、confidence gate変更、OCR engine刷新へ進まない。
