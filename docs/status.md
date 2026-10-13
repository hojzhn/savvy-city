# Status

Last updated 2026-10-13.

## Built

| Spec part | Status | Where |
| ----- | ----- | ----- |
| 1. Seed | Done, plus affiliation and the current city | `StartingPointGenerator.cs`, `config.yaml`, `locations.yaml` |
| 2. Context | Done (birth context) | `locations.yaml`, `eras.yaml` |
| 3. Family and household | Done, plus parent ethnicity | `family.yaml` |
| 4. Life simulation | Done: all 4.7 items, 4.11 invariants | `LifeSimulator.cs`, `life.yaml` |
| 5. Event interpretation | Done as readings that write channels | `life.yaml` readings |
| 6. Adult profile | Done: 6.4–6.9, with calibration | `AdultProfiler.cs`, `adult.yaml` |
| Political Alignment | Done: axes, consistency, issues, stakes, salience | `adult.yaml` beliefs group |
| Lifestyle and gender roles | Added beyond the spec: alt / mainstream / trad, egalitarian / flexible / traditional | `adult.yaml` beliefs group |
| Social circle and health | Mostly done (see gaps below) | `adult.yaml` |
| Debug app | Pawn inspector and Traits tab | `Game/` |

92 tests pass (`dotnet test`).

## Population snapshot

From 3,000 pawns with the current data. These are plausibility checks, not
targets; the numbers behind them are placeholders.

| Measure | Share |
| ----- | ----- |
| Parents divorced during childhood | 22% |
| Married now | 50% |
| Bachelor's degree or higher | 31% |
| Left high school without graduating | 10% |
| Enlisted | 5% |
| Unemployed now | 6% |
| Wealth: poverty / wealthy | 11% / 0.7% |
| Secure attachment | 52% |
| Mental health stable | 72% |
| Self-rated health excellent / poor | 19% / 6% |
| Tiny social circle | 19% |

## Placeholder data

Every rate and weight in `Data/` is a rough estimate, chosen to give plausible
results, not researched figures. The locations are 8 sample US places. Things
to research and tune:

* Locations: real settings, ethnic and religious shares, norms by period.
* Base rates in `eras.yaml` (divorce, marriage, job loss, first sex) by year.
* Wealth at the top end: only 0.7% of pawns end up wealthy.
* Leaving home: few pawns live with family in their 20s.
* Kink and health condition prevalence.

## Known gaps

* **Attraction engine and compatibility.** Referenced throughout the spec but
  not specified there yet. Spec sections 7 and 8 are missing.
* **Section 9, presentation and starting state** (honesty, disclosure
  thresholds, mood, loneliness at the start of play): not built.
* **Social circle:** friend sources and close-friend records are not built.
* **Partner preferences:** the wound's "want" bonus (spec 6.9) is not applied;
  revealed preferences don't use past partners' attributes, because past
  partners are not simulated.
* **Golden-seed tests** (spec 4.12, 6.13): not added while the data is still
  being tuned.
* **Life and adult traits are read-only in the Traits tab;** edit them in YAML.
* **The game itself:** no player, dates or dialogue yet.

## Next steps

1. Write the attraction engine spec (sections 7 and 8), then build it.
2. Build section 9 (presentation and starting state).
3. Replace the placeholder locations and rates with researched data for the
   game's setting.
4. Add golden-seed tests once the numbers settle.
5. Start on the player-facing game.
