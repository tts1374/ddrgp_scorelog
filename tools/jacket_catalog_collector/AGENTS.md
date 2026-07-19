# Jacket Catalog Collector Agent Rules

このファイルは `tools/jacket_catalog_collector/` 配下の実装・検証に追加適用する。コマンド、UI操作、機能説明は `README.md` を事実源とし、ここでは複数PRで維持する実装境界を定める。既存設計・READMEから固定済みの規則だけを記載し、新しい状態遷移や製品仕様を決めない。

## Scope And Data Safety

- developer-only collectorを公開 `DDRGpScoreViewer`、installer、Release、GitHub Actions artifact、正式保存workflow、正式個人スコアDBへ接続しない。
- capture、crop、feature、checkpoint、catalog/master DBはlocal dataとし、Git、通常stdout、公開logへ混入させない。物理削除、retention cleanup、既存DB修復は明示scopeなしに行わない。
- Python subprocessのstdoutは契約データ、stderrはdiagnosticとして分離し、C#側で非0終了、empty/truncated/unknown/invalid JSONを部分成功へ丸めない。

## Master And Catalog Publication

- 既存非空masterはnetwork/build前にinspectし、0 byte placeholderだけを明示的に許容する。
