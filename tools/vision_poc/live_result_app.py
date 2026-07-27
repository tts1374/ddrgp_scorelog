from __future__ import annotations

import base64
import binascii
import io
import json
import sys
from collections.abc import Iterable

from PIL import Image

from .live_result import analyze_live_result


def run_lines(lines: Iterable[str]) -> Iterable[str]:
    for line in lines:
        if not line.strip():
            continue
        try:
            payload = json.loads(line)
            image_bytes = base64.b64decode(payload["png_base64"], validate=True)
            with Image.open(io.BytesIO(image_bytes)) as source:
                observation = analyze_live_result(source.convert("RGB"))
        except (binascii.Error, KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
            observation = {
                "result_screen": False,
                "score": "",
                "title_signature": "",
                "reason": f"live_input_invalid:{exc}",
                "score_status": "input_invalid",
            }
        except Exception as exc:  # pragma: no cover - process boundary safeguard
            observation = {
                "result_screen": False,
                "score": "",
                "title_signature": "",
                "reason": f"live_analysis_failed:{exc}",
                "score_status": "analysis_failed",
            }
        yield json.dumps(observation, ensure_ascii=False, sort_keys=True)


def main() -> int:
    for output in run_lines(sys.stdin):
        print(output, flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
