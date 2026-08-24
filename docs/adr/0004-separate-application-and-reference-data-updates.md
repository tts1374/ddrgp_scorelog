# ADR 0004: Application packageとreference data setの更新分離

## Status

Accepted

## Context

GP Score LogのReleaseは、Windowsアプリ本体・app-owned runtimeと、M4 master DB・M5b jacket reference catalogを配布する。application binaryとreference dataは変更頻度、検証方法、配置先、rollback単位が異なる。

application updateが永続data rootを書き換えると、更新失敗が正式個人スコアDBやsettingsへ波及する。master DBとcatalog DBを個別に更新すると、異なるcontent versionの組合せをruntimeが参照する可能性がある。既存appをofflineまたは更新失敗時にも利用できる境界が必要である。

## Decision

application package updateとreference data set updateを別の責務として実装する。同じGitHub Releaseを配布元にできるが、取得、検証、適用、rollbackの状態機械を統合しない。

application packageはVeloPackのstable Windows feedを使用し、app binaryとapp-owned runtimeを更新する。VeloPackのinstall rootと`%LOCALAPPDATA%\DDRGpScoreViewer\`の永続data rootを分離し、正式個人スコアDB、settings、reference DB、Release logをapplication packageの書換対象にしない。適用前は既存の完全終了経路でcapture、解析・保存workflow、runtime、open handleを停止する。

reference data setは次の3 assetを1つの整合単位として扱う。

- `reference-set.json`
- `ddrgp-master.sqlite`
- `jacket-catalog.sqlite`

manifest、checksum、schema、master/catalogの対応versionを検証した新しいsetだけをstagingへ取得する。master DBとcatalog DBはdirectory単位で切り替え、個別更新しない。同一versionはno-op、downgradeと不整合を拒否する。切替または再検証に失敗した場合は直前の検証済みsetへ戻し、正式個人スコアDBとsettingsを変更しない。

起動時に両更新がある場合はreference data set処理を完了してからapplication packageのdownload・適用へ進み、install rootと永続data rootの操作を並行させない。確認、download、適用に失敗した場合は、検証済みの現行appまたはreference data setを維持する。

## Consequences

- app binaryの更新失敗が利用者の正式playやsettingsを直接変更しない。
- master DBとcatalog DBを同じversion pairとして切り替えられる。
- reference dataの更新判定と適用をapplication binaryの置換から分離して扱える。
- offline、checksum不一致、asset欠落、downgradeでは検証済みの現行環境を維持できる。
- 1つのReleaseにapplication feedとreference asset setを揃え、2系統の検証・状態表示・失敗処理を維持する必要がある。
- reference data setのmanifest生成、checksum、cross-DB compatibility、直前1世代のrollback storageが追加で必要になる。

## Alternatives Considered

### Reference DBをapplication package更新だけで配布する

配布経路を1つにできる一方、reference dataの変更ごとにbinary package更新が必要になり、install rootと永続reference dataのlifecycleが結合するため採用しない。

### Master DBとcatalog DBを個別に更新する

変更されたfileだけを取得できる一方、途中失敗やversion差により互換性のない組合せを公開する可能性があるため採用しない。

### Application側で独自updaterを実装する

applicationとreference dataを1つの状態機械にまとめられる一方、package展開、channel、rollback、終了連携を独自に所有することになる。application binaryはVeloPackへ委譲し、reference data固有の検証だけをapp側に保持する。

## References

- [ストレージI/O仕様](../design/05_storage_io_spec.md)
- [Windowsアプリpackage・更新契約](../../app/README.md)
- [実装ロードマップ](../implementation-roadmap.md)
