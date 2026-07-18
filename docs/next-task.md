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

516件評価ではOCR低confidence/emptyが主因で、master未一致やalias不足よりtitle/artist認識側の診断が先行課題である。artistはok 93件に対して非ok 423件で、titleの非ok 358件より悪い。titleを正しく読めてもartist失敗またはconfidence 0.90未満で候補化されない例が複数ある。

日本語文字はOCR rawへほぼ出ていない。current M5c評価はTesseractの`lang`を明示せず、`M3_TEXT_TESSERACT_CONFIGS`もtitle/artistとも`lang=None`相当で実質default languageへ依存している。artist ROIはtitleより狭く、既存設計でも`auxiliary_clipped_reference`として扱われるため、language不足、ROI切れ、文字サイズ、前処理のいずれが支配的かはまだ分離できていない。

canonical exact unique 39件とambiguous 4件は人手監査済みtruthではない。expectedまたは監査済みtruth setがないためcandidate precisionは主張できず、confidence閾値やauto-confirm条件を変更する根拠にもできない。

## 優先順位

1. `tesseract --list-langs`で実環境のinstalled languageと`jpn` traineddataの有無を確認し、実行時`-l jpn+eng`、language不足時の明示failure reasonを調べる。同じ実captureで`eng`と`jpn+eng`を比較する。
2. 代表例のsource、artist ROI、enlarged、binary、OCR rawを並べ、ROI切れ、文字サイズ、装飾混入、二値化による線欠損を目視診断する。この段階ではROI変更を確定しない。
3. normalized titleがmaster canonical titleと完全一致し一意な場合、artistが利用不能でも`title_exact_unique_artist_unavailable`等の理由付きmanual review candidateを提示できるか検討する。auto-confirmやpersisted state変更には使わず、titleが複数候補のときだけartistをtie-breakerとして扱う。診断後または独立PRで判断する。
4. artistだけ現行scaleと2～3倍相当の追加拡大、sharpen有無、`psm=7`、`eng` / `jpn+eng`を比較し、empty率、confidence分布、OCR raw、候補結果を測る。titleは`psm=6` / `psm=7`も比較対象とする。
5. confidence gateは最後に検討する。0.90を単純に下げず、normalized title完全一致、master上で一意、manual candidate限定などの複合条件と組み合わせ、auto-confirm条件へ昇格しない。

## 次PR候補

次PRは「日本語OCR環境確認とtitle/artist失敗原因の診断・比較評価」に限定する。OCR改善方式そのものはまだ確定しない。

含める候補:

- Tesseract installed languageとlanguage不足failure reasonの検査
- 同一実captureでの`eng` / `jpn+eng`比較
- titleの`psm=6` / `psm=7`比較
- title/artistのstatus組合せ、confidence分布、OCR raw、候補結果の集計
- source、ROI、enlarged、binaryのrepresentative local reportまたはcontact sheet
- exact unique 39件とambiguous 4件を人手監査可能にする補助
- local report、README、roadmap、design、`docs/next-task.md`の同期

含めない:

- auto-confirm、bulk review、confidence閾値の単純緩和
- artist ROI変更の確定実装、OCR engine全面置換
- catalog schema、manual review transaction、capture lifecycleの変更
- 正式個人スコアDB接続、grid巡回、ゲーム操作

## 未決事項

- 実環境で`jpn+eng`を利用可能か、language不足をどのversion付きfailure reasonへ固定するか。
- artist失敗の主因がlanguage、ROI切れ、scale、装飾、二値化のどれか。
- title exact unique manual candidate化を診断PRに含めるか、診断後の独立PRにするか。
- exact unique 39件とambiguous 4件をどこまで人手監査し、比較評価のtruth setへ使えるか。
- 診断結果を受けてartist ROI、前処理、confidence gateのどれを後続PRへ選ぶか。

実測結果なしにauto-confirm、bulk review、OCR全面刷新へ進まない。次PRではlanguage設定とartist ROI/前処理の診断・比較評価を先に行い、title exact unique manual candidate化は診断後または独立PR、confidence gate変更は最後に扱う。
