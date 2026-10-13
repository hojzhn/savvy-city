# Status

Last updated 2026-10-13.

## Built

| Spec part | Status | Where |
| ----- | ----- | ----- |
| 1. Seed | Done, plus affiliation, social insight, body fields and the current city | `StartingPointGenerator.cs`, `config.yaml`, `locations.yaml` |
| 2. Context | Done (birth context) | `locations.yaml`, `eras.yaml` |
| 3. Family and household | Done, plus parent ethnicity and orientation | `family.yaml` |
| 4. Life simulation | Done: all 4.7 items, 4.11 invariants; relationships as involvements with agreements and a level that drains after a dissolve, several at once (label derived); past-partner records | `LifeSimulator.cs`, `life.yaml`, `relationships.yaml` |
| 5. Event interpretation | Done as readings that write channels | `life.yaml` readings |
| 6. Adult profile | Done: 6.4–6.9, with calibration; revealed preferences read past partners | `AdultProfiler.cs`, `adult.yaml` |
| Political Alignment | Done: axes, consistency, issues, stakes, salience | `adult.yaml` beliefs group |
| Lifestyle and gender roles | Added beyond the spec: alt / mainstream / trad, egalitarian / flexible / traditional | `adult.yaml` beliefs group |
| Social circle and health | Done, including friend sources and close-friend records | `adult.yaml` |
| Linked city | Done: couples by pool matching, households, ex and friend links | `Sim/Population/`, `relationships.yaml` |
| 7.2 Profile vector, aura, taste | Fields and world matrices done; no attraction scores yet | `World.cs`, `aura.yaml` |
| 9. Starting state | Done as the bridge into the runtime (rewritten in the spec) | `StartingState.cs`, `relationships.yaml` |
| Debug app | Pawn inspector, Traits tab, City tab | `Game/` |

118 tests pass (`dotnet test`).

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

A city of 1,000 (world seed 1, `PawnCli --city 1000 1`): 1,093 residents, 385
couples (5% same-sex), 6 partnered pawns dropped for want of a partner. 20% of
past partners and 42% of close friends are residents. Built in about 6 seconds
after a 2-second calibration.

## Placeholder data

Every rate and weight in `Data/` is a rough estimate, chosen to give plausible
results, not researched figures. The locations are 8 sample US places. Things
to research and tune:

* Locations: real settings, ethnic and religious shares, norms by period.
* Base rates in `eras.yaml` (divorce, marriage, job loss, first sex) by year.
* Orientation shares, and same-sex marriage law by place and year (one
  placeholder mod for New York).
* Wealth at the top end: only 0.7% of pawns end up wealthy.
* Leaving home: few pawns live with family in their 20s.
* Kink and health condition prevalence.
* Everything in `relationships.yaml` and `aura.yaml`.

## Known gaps

* **Hidden resentment at the start.** With 8.3's rule, H settles at
  50 × (1 − voice) × τ, so 95% of couples start above 0.5 and nearly all would
  pass 1 (and leave) without the starting cap. The spec needs a decision before
  the runtime is tuned.
* **Desire size at the start is provisional** (from starting satisfaction)
  until the attraction engine exists.
* **Couples are reconciled, not identical:** the two histories can differ by up
  to 2 years in relationship length and in children.
* **Roommates and partners' friends** are not linked into shared households or
  merged circles.
* **Renames in the Traits tab** don't reach relationships.yaml or aura.yaml.
* **The wound's "want" bonus** (spec 6.9) is not applied to partner preferences.
* **Golden-seed tests** (spec 4.12, 6.13): not added while the data is still
  being tuned.
* **The game itself:** no player, dates or dialogue yet.

## Next steps

1. City matching and the starting state read agreements and levels instead of
   the label. Overlapping patterns (casual, affairs, non-monogamy) come from the
   runtime's desires, not new life events.
2. Decide how hidden resentment should behave over long relationships (8.3).
3. Build attraction (7.1–7.6) and the version 1 runtime core (8.2–8.4, thoughts
   and the why view), with the tuning harness running on the linked city.
4. Check that the life simulation's marriages and divorces land near what the
   runtime produces from the same pawns.
5. Replace the placeholder locations and rates with researched data.
6. Add golden-seed tests once the numbers settle.
