# ADR 0005: アプリ所有のユーザーテーマ設定とruntime token境界

## Status

Accepted

Date: 2026-08-25

Related issue: #184

## Context

M10のWPF appは、画面ごとに定義された色と、コードで生成するグラフや状態表示を持つ。#183で共通操作部品のfocus・pressed・disabled・selected状態を共通化したため、その状態色をライト・ダークの両テーマで同じ意味に保つ必要がある。

テーマ選択は既存の`user-settings.json`へ追加する正式DB外のユーザー設定であり、起動前の適用、保存成功後の即時反映、Windowsのアプリモード変更への追従を複数の画面・XAML resource dictionary・コード描画で共有する。既存ADRは正式個人スコアDB、reference data、application packageの責務を定めているが、app内のテーマ設定とsemantic tokenの境界は定めていない。

## Decision

1. テーマ設定の保存値は言語に依存しない`system`、`light`、`dark`の3値とし、既定値は`system`とする。欠落または未知の値は、他の有効な設定を保持したまま`system`へ正規化する。
2. appは保存されたテーマをメイン画面表示前に解決する。`system`はWindowsの既定アプリモードを参照し、値を取得できない場合はライトへフォールバックする。`light`と`dark`はOSの変更に追従しない。`system`選択中だけ、app lifetime中のWindowsアプリモード変更を再解決する。
3. appは`Theme.xaml`と`DarkTheme.xaml`のresource dictionaryを所有し、XAMLのユーザー向け色とコードで生成・描画するユーザー向け色をsemantic token経由で参照する。テーマ辞書の差し替えとコード描画の再生成で、保存成功後の再起動なしの反映を可能にする。
4. テーマ設定とtheme tokenは正式個人スコアDB、`plays`、保存境界、reference data set、OSのwindow chromeの責務を変更しない。

## Consequences

- ライト・ダーク間で、本文、surface、border、badge、graph、focus、pressed、disabled、selected、monitoring stateの意味を維持できる。
- テーマ値の正規化により、旧`user-settings.json`を破壊せずに新しい選択肢を追加できる。
- 新しいユーザー向け色は、XAMLまたはコードへ直接値を追加せず、両テーマのsemantic tokenを同期する必要がある。
- OSのテーマ変更を監視する責務がapp lifetimeに追加されるが、明示的なライト・ダーク選択では監視しない。

## Alternatives considered

- テーマ選択を追加せずライト固定にする案は、ユーザーが選択したテーマと既存の#183状態トークンを同じ画面で適用できないため採用しない。
- OSの設定だけを直接参照し、画面ごとに色を持つ案は、明示テーマ、保存後の即時反映、XAMLとコード描画の一貫性を保証できないため採用しない。
- テーマ変更のたびに再起動する案は、Issue #184の再起動なしの適用条件を満たさないため採用しない。

## References

- [Issue #184](https://github.com/tts1374/ddrgp_scorelog/issues/184)
- [Issue #183](https://github.com/tts1374/ddrgp_scorelog/issues/183)
- [`app/README.md`](../../app/README.md)
- [`docs/wireframe/screen-spec.md`](../wireframe/screen-spec.md)
- [`docs/wireframe/design-system.md`](../wireframe/design-system.md)
- [`docs/design/00_glossary.md`](../design/00_glossary.md)
