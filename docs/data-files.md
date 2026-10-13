# Data Files

All game rules live in YAML files in [Data/](../Data/). The debug app reloads
them as soon as one is saved, and the loader rejects anything inconsistent with
a message naming the file and entry. Each file's header comment repeats the key
points of its format.

**Most numbers are placeholders.** Rates and weights are rough estimates tuned
to be plausible, not researched figures. See [status.md](status.md).

**Notes, not comments.** The Traits tab can rewrite `family.yaml`,
`config.yaml`, `locations.yaml`, `life.yaml` and `adult.yaml` when it saves. It
keeps each file's header comment block but not comments further down. Use the
`note:` field on groups, items, modifiers and options instead.
`relationships.yaml` and `aura.yaml` are never rewritten, so comments there are
safe; renames in the Traits tab don't reach them either.

## Shared syntax

### Conditions

Used in `when`, `only_if`, `requires`, `achieved` and rules.

| Form | Meaning |
| ----- | ----- |
| `key: a` | the fact is `a` |
| `key: [a, b]` | the fact is one of these |
| `key: "!a"` | the fact is anything but `a` |
| `key: ">=0.7"`, `"<1990"`, `"1990..1999"` | numeric comparisons |
| `key: 18` | a numeric fact is exactly 18 |

All conditions in one map must hold.

### Weights (life.yaml and adult.yaml)

| Form | Meaning |
| ----- | ----- |
| `fact: 0.5` | numeric fact: adds 0.5 × value. Temperament percentiles (`pawn.*`) are centred to -1..1 first. |
| `fact: { a: 0.5, b: -1 }` | categorical fact: adds the weight for its current value |

### Fact keys

See the table in [architecture.md](architecture.md#facts). Inside a per-parent
group, `self.x` is the parent being rolled; elsewhere use `mother.x`,
`father.x` or `any_parent.x`.

## config.yaml

Global settings.

| Field | Meaning |
| ----- | ----- |
| `current_city` | Location id of the city the game takes place in. Every pawn lives there. |
| `present_year`, `age_min`, `age_max` | Pawn ages; birth years spread evenly over the range. |
| `male_birth_rate` | Chance a pawn is male. |
| `temperament.traits` | Temperament trait ids. Each becomes the 0–1 percentile fact `pawn.<id>`. |
| `temperament.correlations` | `{ a, b, r }` pairs. The set must form a valid correlation matrix. Add new traits at the end of the list so existing traits keep their draws. |
| `body.traits` | Inherited body fields. Each becomes `pawn.<id>_z` (z-scale, within sex), with the parents' values in `mother.<id>_z` and `father.<id>_z`. |
| `body.parent_weight`, `body.noise`, `body.assort` | The pawn's value is `parent_weight × (mother + father) + N(0, noise)`; `assort` is the correlation between the parents' values. |

## locations.yaml

Places a pawn can be born and live.

| Field | Meaning |
| ----- | ----- |
| `id`, `name` | Identifier and display name. |
| `population` | Relative weight for birth locations when the current city has no origins table. |
| `urbanicity` | urban_core, inner_suburb, outer_suburb, small_town or rural. |
| `origins` | When this is the current city: where adults living here were born, as weights by location id. |
| `periods` | Conditions from a year onward (see below). Must cover every birth year. |

Each period has `from`, `local_politics`, `diversity`, `norms_sex`,
`norms_gender`, `norms_family`, optional `economic_era` (overrides eras.yaml),
`groups` (ethnic shares, the base weights for parent ethnicity), `religions`
(religious shares, the base weights for parent religion), and optional `rates`
(local overrides of eras.yaml base rates).

The periods describe a place as somewhere to grow up (the birth context) and,
during the life simulation, as the place the pawn lives that year.

## eras.yaml

| Field | Meaning |
| ----- | ----- |
| `economic_eras` | `{ from, era }` entries: boom, steady_growth, stagnation, recession or crisis. |
| `base_rates` | `{ from, rates: { name: value } }` entries. `life.yaml` uses them as `base: rate.<name>`. An entry applies until a later one sets the same rate. |

## family.yaml

The family and household (spec step 3), as groups of categorical items rolled
in order.

```yaml
groups:
  - name: parents
    per_parent: true          # roll each item for the mother and the father
    items:
      - id: education
        values: [none, primary, ...]
        weights: { none: 1, primary: 2, ... }   # values left out weigh 1
        assort: 0.4           # chance the father copies the mother's value
        mods:
          - when: { year: "<1990" }
            mul: { bachelors: 0.8 }
```

| Field | Meaning |
| ----- | ----- |
| `values`, `weights` | Outcomes and base weights. |
| `base_from` | Take base weights from the location's `groups` or `religions` shares instead. |
| `only_if`, `default` | Roll only when the conditions hold; otherwise use the default. |
| `assort` | Per-parent groups: chance the father copies the mother's value. |
| `mods` | Each modifier multiplies weights when all its conditions hold. |

After a per-parent group, `parents.same.<item>` (yes/no) says whether both
parents share a value.

The last group, `identity`, holds the pawn's own traits that are set before
life begins: `identity.orientation` (straight, bisexual, gay). The life
simulation reads it when a relationship starts, to set `state.partner_sex`.

## life.yaml

The life simulation (spec section 4). The header lists every field; this is
the summary.

```yaml
stages:          # contiguous, starting at 0; weight is used by the adult profile
  - { id: early_childhood, from: 0, to: 5, weight: 1.0 }
channels: [threat_shift, ..., shame]
items:
  - id: parental_divorce
    type: event
    window: [early_childhood, adolescence]
    frequency: once
    requires: { state.parents_together: "yes" }
    rate:
      base: rate.divorce_childhood
      weights:
        household.marriage_quality: { high_conflict: 1.2 }
    outcomes:
      - id: mother_custody
        weight: 75
        effects: { set: { state.household_shape: single_mother } }
    readings:
      - id: people_leave
        weights: { pawn.threat_sensitivity: 2.0 }
        effects: { channels: { care_inconsistency: 0.3 } }
```

**Item types.**

| Type | Behaviour |
| ----- | ----- |
| `condition` | A state value `state.<id>`. `kind` is one of: `state` (changed only by effects), `number` (numeric, changed by effects), `roll` (weighted roll on its schedule), `drift` (moves one step up or down, with `up`/`down` rates), `derive` (first matching rule, every year), `counter` (+1 a year while `while` holds, else 0). |
| `event` | Fires with probability `sigmoid(logit(base) + weights + mods)`. Optional `outcomes` (one is chosen), `readings` (chosen by temperament), `effects`, `valence` (negative or positive, for the impact score), `tags`. |
| `fork` | Chooses one `option`: chance ∝ `weight × exp(utility / temperature)`. `sticky_key` and `sticky_bonus` make a re-chosen fork (like a career) favour its current choice. |

**Schedule.** `window` ([stage] or [first, last]), `from_age`/`to_age`,
`frequency` (once, per_stage, per_year), `ages` (exact ages), `max` (most times
an event fires), `must_happen` (force it in the last simulated year if it has
not fired).

**Effects.** `set` (categorical state; `$current_city` for `state.location`),
`add` (numeric state), `reset` (set to 0), `record_first_age` (set to the
current age the first time), `channels` (add deltas).

**Order within a year.** Items run in file order. An item sees changes made
this year by items above it, and changes made by items below it as of last
year.

**Involvements (relationships).** A relationship is not stored as a label. A
pawn can be involved with several people at once, and each involvement has its
own fields, declared in the `involvement` section:

```yaml
involvement:
  fields:
    - { id: agreement, values: [none, defined, formalized] }
    - { id: living_together, values: ["no", "yes"] }
    - { id: partner_sex, values: [opposite, same] }
  label:                       # derives state.relationship
    values: [single, dating, cohabiting, married]
    rules:
      - { when: { any_inv.agreement: formalized }, value: married }
      - { when: { any_inv.living_together: "yes" }, value: cohabiting }
      - { when: { state.involvements: ">=1" }, value: dating }
      - { value: single }
```

| Piece | Meaning |
| ----- | ----- |
| `per_involvement: true` | An event rolls once a year for each ongoing involvement. Inside it, `inv.<field>` and `inv.years` are that involvement's. Leave out frequency, ages, max and must_happen. |
| `any_inv.<field>` | Elsewhere: true if any ongoing involvement matches. |
| `start_involvement: true` | In an event's own effects: start an involvement. `inv.<field>` sets in the same event (including its outcomes) apply to it. |
| `end_involvement: true` | In a per-involvement event: the involvement ends. The outcome id is how it ended. |
| `state.relationship` | Derived from the label rules whenever an involvement starts, changes or ends. No effect can set it. |
| `state.involvements`, `state.relationship_years` | Built in: how many involvements are ongoing, and the longest one's years (counted at the end of each year, including the current one). |

Each involvement becomes a partner record. The agreement field `formalized`
marks a marriage and the partner_sex field `same` a same-sex partner. Each
partner also gets an age gap, a standing and an availability from
`relationships.yaml` `past_partners`. Without an involvement section, the old
tags (`relationship_start`, `relationship_married`, `relationship_end`) still
build a one-at-a-time partner list.

**Summaries.** At the end, each event writes `life.<id>` (never, once, twice,
3_plus) and each fork writes its last choice (or not_reached). Adult traits can
read these; life items can't.

## adult.yaml

The adult profile (spec section 6). Items are grouped for display and run in
file order. The header lists every field.

| Type | Writes | Behaviour |
| ----- | ----- | ----- |
| `score` | `adult.<id>` | raw = base + weights + mods + N(0, sigma). `scale`: z (normalized and clamped to ±3), z_raw, unit (sigmoid), signed (tanh), clamp01. |
| `count` | `adult.<id>` | Poisson draw with log mean = base + weights + noise, capped at `cap`. |
| `blend` | `adult.<id>` | Revealed preference: stated + (1 − sigmoid(self_insight)) × (pull − stated). |
| `roll` | `adult.<id>` | Weighted roll over `values` with `odds`, plus `inherit_weight` for the value of each `inherit` fact, times `mul` mods. |
| `choice` | `adult.<id>` | Softmax over `options`, like a fork. |
| `derive` | `adult.<id>` | First matching rule. |
| `list` | `adult.<id>.<entry>` | Each entry fires with `sigmoid(logit(prevalence) + weights)`; the `max` most likely are kept with an intensity level (`levels`) and a disclosure. |
| `goals` | `adult.<id>.<goal>` | Gumbel-top-k pick of `pick_min`–`pick_max` goals; each gets a deadline and a status (none, active, achieved, missed). |
| `issues` | `adult.<id>.<issue>`, `..._salience` | Positions from -1 (progressive) to +1 (traditional), from the political axes, personal `stakes`, and noise × (1 − consistency). |
| `dealbreakers` | `adult.<id>.<rule>` | none, soft or hard (hardness at or above `hard_at`). |
| `inner_life` | `adult.core_wound`, `wound_impact`, `private_belief`, `want`, `need`, `regret`, `secret_count` | The core wound is the negative event with the highest impact above `wound_threshold`; `beliefs` map its reading to a belief, want and need. `hideable` lists events (or `item:outcome`) a pawn may keep secret, with their stigma. |
| `friend_sources` | `adult.<id>.<source>`, `adult.friend_closeness` | Friend source shares from a Dirichlet (each source's `weight × exp(weights + mods) × concentration`), then one record per close friend (`count`): a source the pawn was old enough for (`met_from`, `met_to`), years known, closeness (logit from `closeness`), standing around `status` (SD `status_sd`), and sex (the pawn's own with chance `same_sex`). |

`calibration_size` sets how many pawns are generated to normalize z-scale scores.

The adult step also prepares `hist.ex_count`, `hist.ex_status` and
`hist.ex_availability` from past partners (weighted by years together), for the
revealed-preference history pull (spec 6.8).

## relationships.yaml

Relationships after the life simulation: past partners, linking pawns into one
city, and the starting state the runtime begins from (attraction spec section
8, spec section 9). Formulas are `base + weights + mods + N(0, sigma)`, read from
the pawn's facts like adult scores.

| Section | Meaning |
| ----- | ----- |
| `pattern_size`, `readouts` | 8.3: a relationship is a pattern P of `pattern_size` unnamed values. Each readout is `scale × sigmoid(r · P)`, with r drawn from the world seed. |
| `tendencies` | 8.2: voice, sensitivity, exit floor and reaction, as `base + x × anxiety + y × avoidance`. |
| `tolerance` | 8.4 `t_view` for each monogamy view. |
| `past_partners` | Who each partner in the life history was: age gap (`male_older_years`, `age_gap_sd`), standing (around `own_status` by `status_match`, SD `status_sd`) and availability (a logit formula). These formulas run at the end of the life simulation, so they can only read starting-point and life facts. |
| `city` | Pairing couples: `years_tolerance` (largest gap in relationship years), costs per year of relationship gap, year of unexpected age gap and child of difference, `max_cost`, and `overflow_factor` (extra candidates searched for missing partners, as a multiple of the city size). Linking: `ex_link_chance` and `friend_link_chance` per record (the linked share comes out at about twice this), and the largest friend age gap by source. |
| `start` | Spec section 9. Satisfaction (logit), the provisional size of desire, exclusivity by monogamy view, agreement masses, investment, trust, open and hidden resentment, goal pressure, how fast projection gives way to observation, standing and power, friendship desire, harm and blame by how a relationship ended, mood, starting threat, and `knowledge` (fields, kinds, weights, lists and the secret rate). |

## aura.yaml

The aura and taste (attraction spec 7.2).

| Field | Meaning |
| ----- | ----- |
| `harmonics` | K, harmonics per aura. |
| `aura_noise`, `taste_noise` | SD of the per-pawn noise in the aura projection and in each taste offset. |
| `calibration_size` | Pawns generated to z-score the profile vector and measure the mean amplitude. |
| `profile_vector` | Numeric facts making up x. `logit(key)` takes the logit of a 0–1 value. The vector's length D is the number of entries. |
