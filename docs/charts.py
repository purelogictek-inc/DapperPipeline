"""Generates the README's benchmark charts.

Numbers come straight from the BenchmarkDotNet runs recorded in BENCHMARKS.md — nothing here is
illustrative or rounded for effect. Re-run the benchmarks, update the tuples, re-run this.

White background on purpose: these render on GitHub (light AND dark) and on nuget.org, and a
transparent background turns the axis labels invisible in GitHub's dark theme.

    python -m venv .venv && .venv/bin/pip install matplotlib numpy
    .venv/bin/python docs/charts.py
"""
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

import os
OUT = os.path.dirname(os.path.abspath(__file__))

INK      = "#24292f"
MUTED    = "#57606a"
GRID     = "#d0d7de"
OURS     = "#1f6feb"   # DapperPipeline
RIVAL    = "#8250df"   # hand-optimized Dapper — the real competitor
NAIVE    = "#a0a8b0"   # what people actually write
FLOOR    = "#2da44e"   # the theoretical floor


def bar(fname, title, subtitle, rows, unit="µs", figsize=(9, 3.6), log=False):
    """rows: list of (label, value, colour)"""
    labels = [r[0] for r in rows]
    values = [r[1] for r in rows]
    colours = [r[2] for r in rows]

    fig, ax = plt.subplots(figsize=figsize, dpi=160)
    fig.patch.set_facecolor("white")
    ax.set_facecolor("white")

    y = range(len(rows))
    ax.barh(y, values, color=colours, height=0.62, zorder=3)
    ax.set_yticks(list(y))
    ax.set_yticklabels(labels, fontsize=10, color=INK)
    ax.invert_yaxis()

    ax.set_xlabel(f"{unit} — lower is better", fontsize=9, color=MUTED)
    ax.tick_params(axis="x", labelsize=9, colors=MUTED)
    ax.tick_params(axis="y", length=0)
    ax.xaxis.grid(True, color=GRID, linewidth=0.8, zorder=0)
    ax.set_axisbelow(True)
    for s in ("top", "right", "left"):
        ax.spines[s].set_visible(False)
    ax.spines["bottom"].set_color(GRID)

    if log:
        # A 55x spread flattens the comparison that matters (us vs the real rival) into two
        # indistinguishable slivers next to the naive bar. Log scale shows both honestly.
        ax.set_xscale("log")
        ax.set_xlim(min(values) * 0.55, max(values) * 3.2)
        for i, v in enumerate(values):
            ax.text(v * 1.10, i, f"{v:,.1f} {unit}", va="center", fontsize=9.5,
                    color=INK, fontweight="bold", zorder=4)
        ax.set_xlabel(f"{unit}, log scale — lower is better", fontsize=9, color=MUTED)
    else:
        pad = max(values) * 0.012
        for i, v in enumerate(values):
            ax.text(v + pad, i, f"{v:,.1f} {unit}", va="center", fontsize=9.5,
                    color=INK, fontweight="bold", zorder=4)
        ax.set_xlim(0, max(values) * 1.20)

    # Title and subtitle drawn by hand: set_title + a second text line collide once the axes are
    # short, because pad is in points and the axes height is not.
    ax.text(0, 1.20, title, transform=ax.transAxes, fontsize=13,
            color=INK, fontweight="bold", va="bottom")
    ax.text(0, 1.06, subtitle, transform=ax.transAxes, fontsize=9.5, color=MUTED, va="bottom")

    fig.tight_layout()
    fig.savefig(f"{OUT}/{fname}", facecolor="white", bbox_inches="tight")
    plt.close(fig)
    print("wrote", fname)


# 1. Single SELECT — PostgreSQL
bar(
    "bench-read.png",
    "A single SELECT (PostgreSQL)",
    "The abstraction costs ~4 µs. The transaction costs 154 µs — so we stopped opening one.",
    [
        ("Dapper (direct)",            177.8, FLOOR),
        ("DapperPipeline",             188.7, OURS),
        ("Dapper (in a transaction)",  331.6, NAIVE),
    ],
)

# 2. Three writes in one transaction — PostgreSQL
bar(
    "bench-batch.png",
    "Three writes, one transaction (PostgreSQL)",
    "We match hand-tuned Dapper, and beat the version everyone actually writes.",
    [
        ("Dapper (hand-batched, no tx)\nthe floor",  294.5, FLOOR),
        ("DapperPipeline",                           300.1, OURS),
        ("Dapper (hand-batched)",                    430.1, RIVAL),
        ("Dapper (3 round-trips)\nwhat people write", 705.3, NAIVE),
    ],
    figsize=(9, 4.2),
)

# 3. Bulk insert, 1000 rows — PostgreSQL
bar(
    "bench-bulk.png",
    "Inserting 1,000 rows (PostgreSQL)",
    "RowSet binds one parameter per COLUMN, not per row. Note the axis is milliseconds.",
    [
        ("DapperPipeline (RowSet)",       1.622, OURS),
        ("Dapper (multi-row VALUES)",     5.221, RIVAL),
        ("Dapper (row per round-trip)", 155.829, NAIVE),
    ],
    unit="ms",
    log=True,
)

# 4. What a transaction costs, per engine
bar(
    "bench-transaction-cost.png",
    "What BEGIN/COMMIT actually costs (SELECT 1)",
    "Cheap on the server. Not cheap on the wire — they are two extra round-trips.",
    [
        ("PostgreSQL — no transaction",   164.4, FLOOR),
        ("PostgreSQL — in a transaction", 307.9, RIVAL),
        ("SQL Server — no transaction",   331.8, FLOOR),
        ("SQL Server — in a transaction", 841.8, NAIVE),
    ],
    figsize=(9, 4.2),
)


# 5. Why RowSet scales: one parameter per COLUMN, not per row
import numpy as np

fig, ax = plt.subplots(figsize=(9, 4.2), dpi=160)
fig.patch.set_facecolor("white")
ax.set_facecolor("white")

groups = ["100 rows", "1,000 rows"]
rowset = [1.304, 1.622]      # DapperPipeline
values = [2.097, 5.221]      # Dapper, multi-row VALUES
x = np.arange(len(groups))
w = 0.34

ax.bar(x - w/2, rowset, w, label="DapperPipeline (RowSet)", color=OURS, zorder=3)
ax.bar(x + w/2, values, w, label="Dapper (multi-row VALUES)", color=RIVAL, zorder=3)

for i, v in enumerate(rowset):
    ax.text(i - w/2, v + 0.12, f"{v:.2f} ms", ha="center", fontsize=9.5, color=INK, fontweight="bold")
for i, v in enumerate(values):
    ax.text(i + w/2, v + 0.12, f"{v:.2f} ms", ha="center", fontsize=9.5, color=INK, fontweight="bold")

ax.set_xticks(x)
ax.set_xticklabels(groups, fontsize=10.5, color=INK)
ax.set_ylabel("ms — lower is better", fontsize=9, color=MUTED)
ax.set_ylim(0, 6.2)
ax.tick_params(axis="y", labelsize=9, colors=MUTED)
ax.tick_params(axis="x", length=0)
ax.yaxis.grid(True, color=GRID, linewidth=0.8, zorder=0)
ax.set_axisbelow(True)
for sp in ("top", "right", "left"):
    ax.spines[sp].set_visible(False)
ax.spines["bottom"].set_color(GRID)
ax.legend(frameon=False, fontsize=9.5, loc="upper left", labelcolor=INK)

ax.text(0, 1.20, "Why RowSet scales", transform=ax.transAxes, fontsize=13,
        color=INK, fontweight="bold", va="bottom")
ax.text(0, 1.06,
        "10x the rows costs RowSet 1.24x. It binds one parameter per COLUMN, not per row.",
        transform=ax.transAxes, fontsize=9.5, color=MUTED, va="bottom")

fig.tight_layout()
fig.savefig(f"{OUT}/bench-bulk-scaling.png", facecolor="white", bbox_inches="tight")
plt.close(fig)
print("wrote bench-bulk-scaling.png")
