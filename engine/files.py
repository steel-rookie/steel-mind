"""CSV·JSONL 읽고 쓰기."""
from __future__ import annotations

import csv
import json
from pathlib import Path

import numpy as np

RUN_COLUMNS = ("t", "setpoint", "torque", "current", "vib", "temp")


def read_run(path) -> dict:
    with open(path, newline="", encoding="utf-8") as fh:
        rows = list(csv.DictReader(fh))
    if not rows:
        raise ValueError(f"빈 파일입니다: {path}")
    return {k: np.array([float(r[k]) for r in rows]) for k in rows[0].keys()}


def write_run(path, data: dict, columns=None) -> None:
    columns = list(columns or data.keys())
    n = len(data[columns[0]])
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(columns)
        for i in range(n):
            w.writerow([f"{float(data[c][i]):.4f}" for c in columns])


def read_jsonl(path) -> list:
    with open(path, encoding="utf-8") as fh:
        return [json.loads(line) for line in fh if line.strip()]


def write_jsonl(path, rows) -> None:
    with open(path, "w", encoding="utf-8") as fh:
        for r in rows:
            fh.write(json.dumps(r, ensure_ascii=False) + "\n")


def run_files(directory) -> list:
    return sorted(Path(directory).glob("*.csv"))
