"""Network-free evaluation of saved DDR WORLD jacket snapshots."""

from .evaluator import EvaluationError, evaluate_snapshot

__all__ = ["EvaluationError", "evaluate_snapshot"]
