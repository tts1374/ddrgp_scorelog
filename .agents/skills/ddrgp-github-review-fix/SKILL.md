---
name: ddrgp-github-review-fix
description: DDRGP scorelogの既存GitHub PRで、repository ownerまたはwrite権限ユーザーから最新reviewの全指摘修正を明示依頼されたときに、安全なcheckout確認、Issue契約に基づくreview thread分類、修正、検証、通常push、冪等な報告を行う。通常のPR実装、一般review、権限不明・外部contributorからの依頼には使わない。
---

# DDRGP GitHub Review Fix

このSkillは、権限確認済みの明示review-fix起動にだけ使う。ルート`AGENTS.md`のProject Rules、Task Scope、GitHub Workflowを維持し、このSkillをreview-fix固有手