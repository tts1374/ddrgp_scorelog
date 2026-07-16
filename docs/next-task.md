# 現在PR完了記録

developer-only jacket catalog collectorへ、current checkpointとcurrent catalogのcomposite identity集合を保存前に照合する、session単位・既定OFFの明示opt-in自動保存を追加した。既存local catalog、artifact、checkpoint、source/crop画像を移行、削除、上書き、repairせず、catalogに既存identityがある候補は新規artifact/checkpointを作らない。

## 今回の完了範囲

- current catalog schema version 1をstrict read-onlyで開き、catalog identity/schema/created-atを同じ接続で照合してから、`rejected`を含む全review状態のcomposite identity集合を返すversion付きJSON契約を追加した。
- 手動保存と自動保存のartifact publish前に、current checkpointとcurrent catalogのcomposite identity集合を照合する。
- checkpointにあるidentityは既存receipt/retry経路へ留め、catalogだけにあるidentityは保存済み表示にして新規artifact/checkpointを作らない。
- 自動保存はsession単位・既定OFF・非永続の明示opt-inとし、fresh session、resume、stopでOFFへ戻す。
- 自動保存は既存のartifact atomic publish、current catalog ingest、checkpoint receipt、明示catalog retryを再利用する。
- 保存前照合後の別process競合は、catalogのcomposite identity一意制約と冪等ingestで既存referenceへ収束させる。
- collector UIへ自動保存checkboxと、catalog/checkpoint保存済み候補の保存抑止表示を追加した。
- 公開`DDRGpScoreViewer`、正式個人スコアDB、ゲーム操作には接続していない。
- 関連design、roadmap、collector READMEを同じ契約へ同期した。

## 維持した境界

- current catalog schema version 1、artifact manifest/checkpoint v1/v2、observation ID、current ingest payload、manual review revision/historyを変更していない。
- title-line hashとcomposite identityをOCR文字列、master song ID、正式個人スコアDBの保存値へ昇格していない。
- capture開始、window選択、grid巡回、ゲーム操作を自動化していない。
- 既存local DB、artifact、checkpoint、source/crop画像、実入力JSON、生成物を変更・削除・Git管理していない。

# 次PR仕様

## 背景

実DDR GP選曲画面から516件のobservationを収集し、current catalogへ取り込み済みである。

current ingestは設計どおり、新規observationをsong未割当の`observation_unresolved`として保存する。そのため、516件すべてが`observation_unresolved`であること自体は異常ではない。

現在のcomposite identityとtitle-line hashは、観測の同一性と重複抑止に使う値であり、master song IDを直接決定するものではない。

一方、既存実装には次の足場がある。

- current catalogのstrict read-only projection
- manual review state、candidate、revision、append-only history
- local artifactからtitle/artistを評価するM5c evaluation path
- M4 masterのtitle-primary、artist tie-breaker候補照合
- candidateを正式確定へ暗黙昇格しないmanual review transaction

不足しているのは、current catalog内の`observation_unresolved`を既存title/artist evaluationへ通し、得られた候補をcollectorのmanual review画面へ表示する通常導線である。

## Goal

current catalogに保存済みの`observation_unresolved`を、既存のtitle/artist evaluationとM4 master照合へread-onlyで接続し、安全な候補song、候補理由、認識失敗理由をmanual review UIへ表示できるようにする。

候補表示だけではpersisted status、song、revision、historyを変更しない。人間が明示的にconfirm、reassign、reject、reopenした場合だけ、既存manual review transactionを通じてcatalogを更新する。

## やること

- current catalog schema version 1から、未解決observationを評価対象としてstrict read-onlyで取得する。
- observationとlocal artifact、checkpoint、source/crop、master、extractorのidentity/version整合を検査する。
- 既存のtitle/artist evaluation、normalization、confidence、M4 master候補照合を再利用する。
- evaluation結果をversion付きprojectionとしてPythonからC# collectorへ渡す。
- manual review画面へ次を表示する。
  - jacket preview
  - observation ID
  - persisted statusとrevision
  - OCR title/artist
  - title/artist confidence
  - candidate song ID、title、artist
  - candidate match reason
  - ambiguityまたはfailure reason
- 一意候補、複数候補、候補なし、低confidence、OCR失敗、評価不能を区別する。
- candidate表示、filter、sort、refreshではcatalog writerを呼ばない。
- 明示manual review操作だけが既存のexpected revision/status/song、action ID、append-only historyを使って更新する。
- stale revision、identity drift、異payload replayは副作用なしで拒否する。
- 516件を対象に、candidate分類と失敗理由をCSV、JSON、Markdownへ集計できるようにする。
- collector README、implementation roadmap、関連design、`docs/next-task.md`を同期する。

## 候補分類

既存語彙を優先しつつ、少なくとも次を画面とreportで区別できるようにする。

- canonical title + artistによる一意候補
- 一意alias候補
- 複数候補
- 候補なし
- title/artist低confidence
- OCRまたはevaluation失敗
- artifact、master、extractor、identity不整合による評価不能
- すでにreview済みまたはrejectedのため対象外

複数候補、confidence不足、候補なし、OCR失敗を、一意候補へ丸めない。

## 516件のローカル評価

実データを利用可能な場合は、Git管理外の`data/`配下へ評価reportを生成する。

最低限、次を集計する。

- total observations
- current unresolved observations
- eligible observations
- evaluated observations
- exact unique candidates
- alias unique candidates
- ambiguous candidates
- no candidates
- OCR/evaluation failures
- not evaluated
- 対象外・拒否理由別件数

expectedまたは人手確認結果がないobservationを正解扱いしない。candidate precisionやknown false candidateは、検証済みの範囲だけ報告する。

実データのpathが不明、GUI操作が必要、または安全にread-only利用できない場合はfixture検証を継続し、`ユーザー対応が必要`として操作、目的、未実施時の影響を明示する。

## 完了条件

- current catalog内の`observation_unresolved`を既存title/artist evaluationへread-only接続できる。
- 一意候補、複数候補、候補なし、低confidence、evaluation失敗、評価不能を区別できる。
- collectorのmanual review画面で候補song、OCR結果、confidence、候補理由、failure reasonを確認できる。
- candidate表示、refresh、report生成だけではcatalog DBが変化しない。
- 明示manual review操作だけが既存transactionを通じてpersisted stateを更新する。
- stale revision、identity drift、unsupported/corrupt/drift入力を副作用なしで拒否できる。
- 516件のcandidate分類と理由別内訳をlocal reportへ出力できる。
- 既存local capture、artifact、checkpoint、catalog/master DB、source/crop画像を変更・削除・Git混入しない。
- 関連README、roadmap、designが実装と一致する。
- 対象・影響範囲test、Ruff、compileall、collector test、`git diff --check`が成功する。
- GitHub Codex ReviewでP0/P1/P2の未対応指摘がない。

## 維持する境界

- current catalog schema version 1、M4 master schema、artifact manifest/checkpoint versionを変更しない。
- catalog migration、backfill、repair、既存rowの一括更新を行わない。
- candidate、expected song、OCR raw、title-line hash、composite identityを自動確定しない。
- 一意候補でもauto-confirmしない。
- title/artist OCR、ROI、preprocessing、normalizationの新方式へ進まない。
- source/crop欠損時に別画像やhashだけからsongを推測しない。
- capture lifecycle、window selection、自動保存契約、grid巡回、ゲーム操作を変更しない。
- 公開`DDRGpScoreViewer`、installer、Release、正式保存workflow、正式個人スコアDBへ接続しない。
- local capture、artifact、checkpoint、catalog/master DB、source/crop画像、evaluation reportをGit、CI artifact、Release、通常stdout、公開logへ含めない。

## 検証

変更箇所に直接関係する対象testと影響範囲testを実行する。

最低限:

```powershell
python -m ruff check tools\vision_poc pyproject.toml tests
python -m compileall -q master tools\vision_poc
dotnet test tools\jacket_catalog_collector\tests\JacketCatalogCollector.Tests\JacketCatalogCollector.Tests.csproj --no-restore
git status --short
git diff --check
```

共通Python loader、CLI、projection schema、fixture/helperへ影響する場合:

```powershell
python -m pytest -q tests
```

画像分類、result ROI、confirmed-events、M7a数字認識を変更しない場合は`python -m tools.vision_poc`本体を省略できる。省略理由と残るリスクを完了報告へ記載する。

## スコープ外

- auto-confirm
- 一括承認、guarded bulk review
- title/artist認識方式の改善
- jacket matcherや正式リザルト曲同定の変更
- catalog/master/artifact/checkpoint schema変更
- migration、repair、cleanup、retention
- capture方式やfalse-positive gateの変更
- grid自動巡回、ゲーム操作自動化
- 公開app、installer、auto-update、Release
- 正式個人スコアDB、M7/M8保存判定

## 完了後

実データ評価結果を確認し、次PRを次のどちらかへ絞る。

- candidate精度が十分でmanual review件数が主な課題の場合は、manual review効率化を検討する。
- OCR失敗、低confidence、master未一致が主な課題の場合は、原因別にtitle/artist認識またはmaster/alias改善を検討する。

評価結果なしにauto-confirm、bulk review、OCR改善へ進まない。

今回変更だけをcommitし、`codex/m5c-unresolved-candidate-projection`へ通常pushしてdraft PRを作成する。`main`への直接push、force-push、merge、release/tag、既存local dataのmigration・削除・repairは行わない。