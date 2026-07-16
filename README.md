# The Antagonist

A custom AI storyteller for RimWorld 1.6 that **scales its threats to your party's actual power**, not just your wealth.

## Why

Vanilla's `StorytellerUtility.DefaultThreatPointsNow` sizes threats from colony **wealth + pawn count**. That badly under-reads an ISEKAI RPG Leveling party — a level-300 hero in plain gear reads as "cheap", so late-game raids become a formality. The Antagonist closes that gap.

## How it works

A Harmony **postfix** on `StorytellerUtility.DefaultThreatPointsNow` (the single function that sets the point budget for essentially every threat) multiplies the result by a factor derived from the party's Isekai levels.

- **Only** while The Antagonist is the active storyteller (`WR_TheAntagonist`).
- **Floored at 1.0×** — it only ever escalates, never makes the game easier.
- **Pawn count is already handled** by vanilla: it adds points *per colonist*, so 10 pawns already give ~10× the baseline of 1. Multiplying that count-aware baseline by a per-pawn power factor works out to roughly *sum of per-pawn power*.
- **Power-weighted average level** — the contraharmonic mean, `sum(level²) / sum(level)`. A flat average would let nine level-1 pawns hide one level-300 hero; weighting by level means the party's real muscle drives the threat.

Curve (avg level → multiplier), Isekai tiers ~50=A, 100=S, 200=SS, 400=SSS:

| avg level | 1 | 50 | 100 | 200 | 400 |
|---|---|---|---|---|---|
| multiplier | 1.0× | 1.7× | 2.5× | 4.0× | 4.5× |

Isekai is read via **reflection**, so it's an optional dependency — without it installed, The Antagonist loads fine and plays as a standard Cassandra-style storyteller.

## Event pacing

Currently a clone of Cassandra Classic's comps (pure-XML `StorytellerDef` inheriting `BaseStoryteller`). Ships with a placeholder portrait in `Textures/UI/HeroArt/Storytellers/`.

---

A RimWorld 1.6 mod by wishRobber.
