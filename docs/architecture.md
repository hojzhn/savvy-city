# Architecture

This document explains how the pawn generator works. For the YAML formats, see
[data-files.md](data-files.md). For the reasons behind choices that differ from
the spec, see [decisions.md](decisions.md).

## Technology

* **C# (.NET 8)** for all game logic. The generator lives in [Sim/](../Sim/), a
  plain class library with no Godot dependency, so it can be tested from the
  command line and run in batches for tuning.
* **Godot 4 (.NET)** for the user interface. [Game/](../Game/) references `Sim`
  and only builds screens.
* **YAML** data files in [Data/](../Data/), read with YamlDotNet. All rates,
  weights and rules live there; code changes are not needed to tune or add items.

## The pipeline

`PawnGenerator.Generate(seed)` runs three parts in order. Each part reads only
what the parts before it produced.

```
seed ─▶ 1. Starting point ─▶ 2. Life ─▶ 3. Adult traits ─▶ Pawn
         StartingPointGenerator  LifeSimulator  AdultProfiler
         family.yaml, config,    life.yaml,     adult.yaml
         locations, eras         eras base_rates
```

### 1. Starting point (spec steps 1–3)

[StartingPointGenerator.cs](../Sim/Generation/StartingPointGenerator.cs)

* **Seed draws.** Current city (fixed: the game's city), birth year, birth
  location (from the current city's origins table), sex, and temperament.
  Temperament traits are drawn together as correlated normals and stored as
  0–1 population percentiles.
* **Birth context.** Looked up from the birth location's period for the birth
  year: urbanicity, local politics, diversity, economic era, and norms.
* **Family and household.** Every item in `family.yaml`, in file order.
  Per-parent groups roll once for the mother and once for the father; the
  father may copy the mother's value (assortative pairing).

### 2. Life (spec sections 4–5)

[LifeSimulator.cs](../Sim/Life/LifeSimulator.cs)

The simulation runs from age 0 to the pawn's current age, one year at a time.
Each year it sets the clock facts (`age`, `cal_year`, `stage`) and the facts
for where the pawn lives that year (`place.*`), then runs every item in
`life.yaml` in file order.

* **Conditions** are state that persists: wealth, education, career, income,
  relationship status, children, and so on. They change by their own rule (a
  roll, a drift up or down, a derivation, a counter) or by the effects of
  events and forks.
* **Events** fire with `p = sigmoid(logit(base) + Σ weights + Σ mods)`
  (spec 4.6). Base rates can come from `eras.yaml` by calendar year.
* **Forks** choose one option with chance ∝ `weight × exp(utility / T)`.
* **Readings** (spec section 5): when an event fires, the pawn takes it in a
  way chosen by temperament, and the reading writes deltas to accumulator
  channels (spec 6.2).

Every fired event, resolved fork and logged condition change becomes a
**history entry** with its age, outcome, reading, changes, channel deltas, an
impact score, and the ids of the earlier entries that caused it. Relationship
tags on entries build the pawn's partner list.

### 3. Adult traits (spec section 6)

[AdultProfiler.cs](../Sim/Adult/AdultProfiler.cs)

The adult profile is computed once, after the life simulation. It first
prepares inputs: temperament as z-scores (`z.*`), stage-weighted channel sums
(`wchannel.*`), and a few history summaries (`hist.*`). Then it runs every item
in `adult.yaml` in file order. Item types include weighted scores, counts,
rolls, choices, derivations, lists (kinks, health conditions), goals, issue
positions, dealbreakers, and the inner life (core wound, belief, want, need,
secrets, regret).

**Calibration (spec 6.1).** Scores on the z-scale are normalized to mean 0 and
SD 1. The first time an adult profile is generated, the generator runs a
calibration batch (600 pawns by default), measures each z-scale score's raw
mean and SD in file order, and stores them. This happens once per load of the
data, in about a second.

## Facts

Every generated value is a **fact**: a key and either a categorical id or a
number, stored in the pawn's `Facts`. Rules in the data files read facts by key.

| Prefix | Part | Examples |
| ----- | ----- | ----- |
| `pawn.`, `year` | Starting point | `pawn.sex`, `pawn.threat_sensitivity`, `pawn.birth_location` |
| `context.` | Starting point | `context.norms_sex` (birth context) |
| `mother.`, `father.`, `parents.same.` | Starting point | `mother.education` |
| `household.`, `surroundings.`, `climate.` | Starting point | `household.wealth` |
| `age`, `cal_year`, `stage`, `place.` | Life | where and when the pawn is, during the simulation and at the end |
| `state.` | Life | `state.career`, `state.relationship`, `state.children` |
| `life.` | Life | `life.parental_divorce` = never / once / ...; `life.after_high_school` = university |
| `channel.` | Life | raw channel sums |
| `z.`, `wchannel.`, `hist.` | Adult | inputs to the adult profile |
| `adult.` | Adult | `adult.neuroticism`, `adult.attachment_style`, `adult.kinks.dominance` |

Rules can also use `self.` (the parent being rolled, inside per-parent groups)
and `any_parent.` (true if either parent matches).

## Determinism

* Every roll draws from its own random stream, seeded by a hash of the pawn
  seed and the roll's name (for example `hash(seed, "life", stage, age, item)`).
  Adding or removing an item leaves every other roll unchanged.
* The random generator is SplitMix64, not `System.Random`, so results do not
  change between .NET versions or platforms.
* Tracing and explanations do not consume random numbers, so turning them on
  never changes a pawn.

## Validation

`GameData.Load` reads every file and checks it before any pawn is generated. A
problem stops loading with a message naming the file and entry, for example:

> family.yaml: household.wealth mod 3: 'household.nope' is unknown or not
> generated yet (items can only read items above them)

Checks include unknown keys, unknown values, numeric facts compared like
categories (or the reverse), items reading items below them, invalid
correlation matrices, location tables that don't cover every birth year, and
effects that write facts they shouldn't. The debug app keeps running on the
last good data when a saved file is broken.

## Overrides and traces

* **Overrides.** `GenerateOptions.Overrides` forces starting-point facts (for
  example `mother.religion = muslim`). Everything downstream reacts. Life and
  adult facts can't be overridden; they are outcomes, not inputs.
* **Traces.** With `GenerateOptions.Trace`, every fact gets a `RollTrace`
  explaining how it was produced: weights, which modifiers fired, final odds,
  or the formula and its parts. With `LifeDetails`, each history entry also
  stores a step-by-step explanation of its roll.

## Code layout

| File | Role |
| ----- | ----- |
| `Sim/Generation/PawnGenerator.cs` | Runs the three parts. |
| `Sim/Generation/StartingPointGenerator.cs` | Spec steps 1–3. |
| `Sim/Life/LifeSimulator.cs` | Spec sections 4–5. |
| `Sim/Adult/AdultProfiler.cs` | Spec section 6, including calibration. |
| `Sim/Data/GameData*.cs` | Loading, validation and compiling of every data file. |
| `Sim/Data/TraitCatalog*.cs` | Describes every trait: how it's determined, what it reads, what reads it. |
| `Sim/Data/FamilyEdits.cs` | Renames that update every reference across the data files. |
| `Sim/Data/YamlIO.cs` | YAML reading and writing (used by the Traits tab when it saves). |
| `Sim/Generation/PopulationStats.cs` | Value shares and modifier fire rates across many pawns. |
| `Sim/Generation/PawnText.cs`, `TraceText.cs` | Plain-text output for the CLI and the inspector. |
| `Game/DataHost.cs` | Loads data and reloads it when a file is saved. |
| `Game/PawnInspector.cs` | The pawn inspector tab. |
| `Game/TraitManager*.cs` | The Traits tab. |
