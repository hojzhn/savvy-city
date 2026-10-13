# Decisions

Choices made while building the generator, and every place where the build
departs from or adds to the spec ([you-will-date.md](spec/you-will-date.md)).

## Technology (2026-10-12)

* **C# with Godot 4 (.NET)** rather than C++. The game is simulation logic and
  text UI; C# is faster to write and debug, and Godot's C# support needs no
  separate build toolchain. Godot is used only for its 2D/UI side.
* **Simulation kept free of Godot.** `Sim` is a plain library so it can be
  tested and run in batches from the command line.
* **Data in YAML.** The spec asks for designer-editable data files; YAML keeps
  them readable and allows comments in file headers.
* **Own random generator** (SplitMix64) so seeds stay stable across .NET versions.

## Starting point

* **Affiliation added as a fourth temperament trait.** Spec 4.2 lists it as a
  base trait, but step 1 draws only threat sensitivity, reward drive and
  self-control.
* **Temperament is a 0–1 percentile** (0.7 = higher than 70% of people). The
  adult profile converts it to a z-score, the spec 6.1 trait scale.
* **Temperament traits are configurable** in `config.yaml`, with their
  correlations.
* **Parent ethnicity added,** drawn from the location's population shares. The
  spec has an ethnic mix only at the location level, and religion and
  immigration need it.
* **Current city versus birthplace (2026-10-12).** Every pawn lives in the
  game's city (`current_city`). The birth location is drawn from that city's
  origins table, and the birth context comes from the birth location. This
  replaces the earlier version, where `pawn.location` was drawn by population
  and doubled as the only location.
* **Single-parent homes tied to marriage quality.** Testing showed "single
  mother" households with "cold stable" marriages; the shape table now makes
  single-parent homes rare in stable marriages.

## Life simulation (spec section 4)

* **Order within a year is file order, not a topological sort.** Spec 4.8 asks
  for a dependency sort that rejects cycles, but relationships cycle naturally
  (dating leads to breakups, which lead back to dating). An item sees this
  year's changes from items above it and last year's from items below it.
* **"Current city" is an event, not a fork.** Spec 4.7 lists it as a fork, but
  the city is fixed by the game world. `move_to_city` brings pawns born
  elsewhere to the city at a realistic age, and is forced by the present if it
  hasn't happened.
* **Once and per-stage items** roll at one random age in their window (or
  stage). Open-ended windows stop at `age_max`.
* **Spec section 5 (interpretation)** is part of the stage loop: readings
  chosen by temperament write channel deltas.
* **Remarriage needs a prior split.** A `parents_split` state enforces spec
  4.11; a separate `parent_partners` event covers single parents who were never
  with the other parent.
* **Relationship years are counted at the end of each year,** after that
  year's relationship events (testing found a breakup left the counter
  running).
* **Summaries** (`life.<id>`) record how often each event fired and each fork's
  last choice, so adult traits and the Traits tab can use them.
* **Tuning after first runs.** Wealth drifted to the extremes (24% poverty,
  16% wealthy), so adults now drift toward the standard of living their income
  supports. Income was too sticky, dropouts and enlistment too common; all were
  adjusted.

## Adult profile (spec section 6)

* **Calibration runs at load,** on 600 pawns, the first time an adult profile
  is needed, rather than at build time as spec 6.1 says. It takes about a second.
* **Political axes use -1..1** via tanh of a weighted sum, as the Political
  Alignment section asks, rather than the z-scale used for personality.
* **Issue positions run from -1 (progressive) to +1 (traditional)** for every
  issue, so positions compare across issues.
* **Revealed preferences** use spec 6.8's formula, but the history pull comes
  from channels and attachment rather than past partners' attributes, because
  past partners are not simulated as people.
* **The wound's "want" is not yet added to partner preferences** (spec 6.9 asks
  for a bonus); it is stored for the attraction engine.
* **Regret** counts only the latest choice of a re-chosen fork (like a career),
  and credits staying in the current career. Before this, 61% of pawns
  regretted a career change.
* **Lifestyle and gender roles added (2026-10-13),** beyond the spec.
  `lifestyle` runs from alternative to traditional ways of living and
  presenting (visible on a first date); `gender_roles` from egalitarian to
  traditional views on roles at home. Both are kept separate from the political
  `cultural` axis, and from each other, so an alt pawn can still want
  traditional roles. Each has a label (alt / mainstream / trad; egalitarian /
  flexible / traditional), with cutoffs set for about 20% at each end.
* **Physical health, social circle and adult mental health** follow
  [social-circle-and-health.md](spec/social-circle-and-health.md), written for
  this project. Friend sources and close-friend records from that document are
  not built yet.

## Tools

* **Notes instead of comments.** The Traits tab rewrites data files when it
  saves, keeping each file's header comment but not comments below it. Groups,
  items, modifiers and options have a `note:` field instead.
* **Life and adult traits are read-only in the Traits tab;** they are edited in
  YAML with live reload. Starting-point traits are fully editable in the tab.
* **Life and adult facts can't be overridden** in the inspector. They are
  outcomes; overriding the inputs (the starting point) shows how they react.
* **The UI separates a pawn into three parts** (starting point, life, adult
  traits), in both the inspector tabs and the Traits tab tree.
