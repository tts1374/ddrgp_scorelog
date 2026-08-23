# ADR 0002: App-owned recognitionとformal evidenceによる正式保存境界

## Status

Accepted

## Context

初期PoCでは、manifest互換入力とconfirmed event境界を先に固定し、画像認識やmaster matchの候補を段階的に評価した。これらのcandidate、expected値、raw OCR、previewは開発中の観察には必要だが、正式個人スコアDBへ保存するplayの根拠としては不十分である。

Releaseアプリは、repository checkoutや開発者用toolがない利用環境で、capture、画像認識、identity解決、正式保存を一貫して実行する必要がある。同一RESULT画面の後続frameと再処理を同じeventとして扱いつつ、正式値が同じ別playを失わないevent identityも必要になる。

このADRは[ADR 0001](0001-foundational-poc-boundaries.md)のconfirmed event境界を維持し、その後段に置く正式保存根拠を固定する。

## Decision

Release runtimeのRESULT認識と正式保存入力の構築は、Windowsアプリ内のapp-owned runtimeが担当する。認識資材はapp packageまたは明示されたruntime data pathから解決し、通常のRelease runtimeはrepository内module、外部Python executable、Tesseractを呼び出さない。

confirmed eventは正式保存を検討できる時系列境界とする。正式playは、次の根拠をfield別に明示し、現在のdesign contractが要求するsource、confidence、完全性をformal evidence bridgeが再検査できた場合だけ構築する。

- `RESULT同定根拠`
- `RESULT数値認識根拠`
- `RESULT状態認識根拠`
- `capture event根拠`

candidate material、expected値、raw OCR、preview、診断用match、未採用の画像特徴量はformal値のfallbackにしない。必須値または根拠が不足するeventは`unresolved`としてplayを作らず、理由を診断境界へ渡す。

`play_id`、`played_at`、formal duplicate keyはconfirmed capture eventのIDとUTC時刻から構築する。同一RESULT画面の後続frame、再送、再処理では同じevent IDを使い、別confirmed eventではformal値が同じでも新しいIDを使う。RESULT fingerprintはevent groupingだけに使用し、formal duplicate keyには使用しない。

正式save workflowはeventごとに最大1回実行する。transactionが`saved`と非null `play_id`を返したplayだけをviewerがread-onlyで再読込する。duplicate、excluded、unresolved、invalid、artifact failure、DB拒否を保存成功へ丸めない。

## Consequences

- 開発用評価経路の候補値が正式playへ混入しない。
- Release packageは画像認識runtimeと必要資材を所有し、Python/Tesseractのlocal環境差から独立する。
- 認識できないplayは推測保存せず、診断可能な`unresolved`として残る。
- event identityとformal duplicateが分離され、同一画面の反復処理と、同値を持つ別playを区別できる。
- 新しい正式fieldや認識sourceを追加するときは、producer、formal evidence bridge、strict save input、transaction、viewer reload、関連testを同じ契約として更新する必要がある。
- app-owned runtimeとdeveloper評価経路の実装を別々に維持するcostを受け入れる。

## Alternatives Considered

### Release runtimeからPython PoCまたはTesseractを呼び出す

developer評価と同じ処理を再利用しやすい一方、repository配置、Python環境、外部engine、subprocess契約が利用者環境の正式保存へ持ち込まれる。Release runtimeの再現性とfailure boundaryを固定できないため採用しない。

### 最上位candidateやexpected値をformal値へ補完する

一部のplayを多く保存できるが、候補観測と正式根拠の区別が失われ、誤った個人履歴を永続化する可能性があるため採用しない。

### RESULT fingerprintをformal duplicate keyにする

同じ表示の反復抑制には利用できるが、別のconfirmed eventで正式値が同じplayまで同一扱いになるため採用しない。

## References

- [Pipeline全体](../design/01_pipeline_fsm.md)
- [イベントと保存境界](../design/03_event_and_save_boundary.md)
- [正式個人スコアDB schema](../design/10_personal_score_db_schema.md)
- [Windowsアプリ実行・保存契約](../../app/README.md)
