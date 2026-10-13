# Tools

## The debug app

Run it with `.\run.ps1` from the project folder (or double-click `run.cmd`).
The script builds the C# code first, because Godot only builds C# by itself
when you press Play in the editor.

| Option | Effect |
| ----- | ----- |
| `.\run.ps1` | Build and run the app. |
| `.\run.ps1 -Console` | Same, with logs and errors printed in the terminal. |
| `.\run.ps1 -Editor` | Build and open the project in the Godot editor (then press F5 to play). |
| `$env:GODOT = "C:\path\to\godot.exe"` | Use a specific Godot install. |

The bottom bar shows the data status. **Auto-reload** (on by default) reloads
the YAML files within a second of a save. If a saved file is broken, the error
is shown (file and entry) and the app keeps running on the last good data.

### Pawn inspector tab

One pawn at a time.

* **Seed controls.** Type a seed and press Go, step with < and >, or pick a
  random one.
* **Three tabs on the left:**
  * **Starting point:** seed draws, temperament, birth context, parents,
    household, surroundings, home climate.
  * **Life:** the life story (one row per event, choice or logged change),
    then the final state, events and choices, channels, where the pawn lives
    now, and the clock.
  * **Adult traits:** personality, attachment, physical health, beliefs and
    politics, sexuality, wants, inner life, social circle, mental health, and
    (collapsed) the adult inputs.
* **Detail panel.** Click any value to see how it was determined: base
  weights, which modifiers fired, final odds; or the formula and its parts. For
  a life story entry: what caused it, what it led to, and how the roll came out.
* **Overrides.** For starting-point values, pick a value to force it. The pawn
  regenerates. Orange marks overridden values; blue marks values that changed
  as a result (hover for the old value). Overrides persist when you change
  seeds. Life and adult values can't be overridden.
* **Edit this trait** opens the selected trait in the Traits tab.

### Traits tab

Every trait a pawn can have, grouped under Starting point, Life and Adult
traits.

* **Explanation:** how the trait is determined, what it depends on, and what
  uses it (click to jump).
* **Distribution:** value shares (or a histogram) across 2,000 sample pawns,
  and how often each modifier fires.
* **Editing** (starting-point traits): values, weights, conditions, modifiers,
  notes, the father-copies-mother chance; temperament traits and correlations;
  the current city and its origins table; age range and birth rate. Add, delete
  and reorder traits and groups.
* **Live preview:** while editing, the distribution shows the change from the
  saved version.
* **Saving:** Save is disabled while the draft has an error, which names the
  rule at fault. Renames update every rule that refers to the renamed trait or
  value, in all data files. The previous version of each written file is kept
  as `<file>.bak`. Discard throws away unsaved edits.
* Life and adult traits are shown read-only; edit them in `life.yaml` and
  `adult.yaml` and the tab reloads.

## PawnCli

```
dotnet run --project Tools/PawnCli -- <args>
```

| Arguments | Output |
| ----- | ----- |
| `42` | The pawn for seed 42: starting point, life story, adult traits. |
| `42 5` | Seeds 42 to 46. |
| `42 --why household.wealth` | Adds an explanation of how that value was rolled (repeatable). |
| `42 --set mother.religion=muslim` | Forces a starting-point value (repeatable). |
| `--stats 10000 household.shape state.career` | Value shares across 10,000 pawns. Works with `--set`. |

## Tests

`dotnet test` runs the suite in [Sim.Tests/](../Sim.Tests/) (about 20 seconds).

| File | Covers |
| ----- | ----- |
| `GameDataTests` | Data validation rejects bad tables. |
| `PawnGeneratorTests` | Determinism, schema, family invariants, temperament correlations, plausible shares. |
| `CurrentCityTests` | Current city, birth origins, birth context. |
| `DebugToolsTests` | Overrides and traces. |
| `TraitToolsTests` | YAML round trips, the trait catalog, population statistics. |
| `FamilyEditsTests` | Renames across family.yaml, life.yaml and adult.yaml. |
| `LifeTests` | Life simulation: spec 4.11 invariants, population rates, readings, validation. |
| `AdultTests` | Adult profile: spec 6.12 invariants, z-scale normalization, doc targets, the small social effect on mental health, validation. |
