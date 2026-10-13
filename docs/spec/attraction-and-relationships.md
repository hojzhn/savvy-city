# Attraction Engine and Relationship Systems

This document fills sections 7 and 8 of *You will date*:

* **Section 7, attraction**, defines how one pawn scores another as a partner.
* **Section 8, relationships**, defines the relationship record, the mechanisms that move relationships over time, and how both run in the sim.

The two sections form one pipeline. Attraction (7) sets how much each pawn wants the other. The relationship pattern (8.3) turns that want into a desired shape of involvement. Tension between what each side wants and what they have produces talks, unilateral acts, and conflict (8.3 to 8.5). Those events write history (8.7), history moves traits, and traits feed back into attraction.

The rest of the document covers presentation, architecture, data files, invariants, testing, and build order.

## Ground rules

**Conventions.** Section 6.1 conventions apply throughout. Traits use the z-scale, clamped to [−3, 3]. Intensities, weights, and probabilities use the unit scale from 0 to 1. Every draw uses `hash(seed, "attraction" or "relationship", field_id, index)` as its sub-seed. All weights live in data files. Values marked (estimate) are starting values for tuning.

**Notation.** σ is the logistic function. `unit(v)` is v divided by its length. `x` and `y` are a pawn's attachment anxiety and avoidance on the z-scale.

**Inputs.** The engine reads the adult profile (6.10), the history log (section 4), the starting state (section 9), and the health record.

**Direction.** Every score runs one way, from an observer A to a target B. A's score for B and B's score for A are separate values.

**Causes, not behaviors.** The spec models the causes of behavior, not the behavior itself. No rule names a phenomenon such as ghosting, cheating, jealousy, or revenge as its trigger. Those come out of a small set of mechanisms. Data files hold content and constants but never say when something happens.

**The player watches.** The player watches pawns live, as in Roller Coaster Tycoon, and does not direct each step. See [Presentation and legibility](#presentation-and-legibility).

## 7. Attraction

### 7.1 Attraction model

Attraction has three parts and one multiplier. Each part has its own inputs and becomes visible at a different time.

| Part | What it measures | Visible when | Range |
| ----- | ----- | ----- | ----- |
| Physical (7.2, 7.3) | How B's aura lines up with A's taste | First sight | −1 to 1 |
| Societal (7.4) | How B rates in general and fits A on paper | As facts get disclosed | 0 to 1 |
| Emotional (7.5) | How B's inner life fits A's | Over repeated contact | 0 to 1 |
| Readiness (7.6) | How open A is to anyone right now | Always, as a multiplier | 0 to 1 |

**The player is a full pawn.** The player carries the complete profile record, including taste. NPCs run `attraction(npc, player)` to decide whether the player is dateable. The game runs `attraction(player, npc)` to decide which NPCs it marks as attractive to the player.

#### Combined score

The engine first maps physical attraction to the unit scale, `phys = (physical + 1) / 2`. Emotional attraction counts only the share A has discovered so far, `known` (7.6).

```
A(A→B) = R_A × (w_p × phys + w_s × soc + w_e × known × emo) − P_deal
```

R_A is readiness (7.6) and P_deal is the soft dealbreaker penalty (7.4). The weights depend on what A is deciding, and each row sums to 1.

| Decision | w_p (estimate) | w_s (estimate) | w_e (estimate) |
| ----- | ----- | ----- | ----- |
| Sex | 0.70 | 0.20 | 0.10 |
| Spend time together | 0.35 | 0.30 | 0.35 |
| The talk (accepting or proposing an agreement) | 0.20 | 0.35 | 0.45 |

Each pawn shifts its row before use. Libido adds 0.10 × libido_z to w_p, and self-insight adds 0.05 × self_insight to w_e (estimates). The engine then rescales the row to sum to 1.

#### Thresholds

| Decision | Rule (estimate) |
| ----- | ----- |
| The game marks an NPC as attractive to the player | physical(player, npc) ≥ 0.35 |
| NPC agrees to meet the player | A(npc, player) ≥ 0.50 in the spend-time context |
| NPC wants sex with the player | A(npc, player) ≥ 0.55 in the sex context, and libido ≥ 0.4 |
| NPC is open to an agreement with the player | A(npc, player) ≥ 0.65 in the talk context |
| NPC starts to pull away | A drops 0.15 below its peak and stays there for 2 weeks |

### 7.2 Physical attraction

Physical attraction compares two abstract waveforms called auras. No harmonic of the aura carries a name or a meaning. The aura is a fingerprint of the whole pawn, and taste is a rule for which fingerprints pull.

#### Aura

Each pawn's aura has K harmonics, with K = 6 (estimate). Each harmonic k has an amplitude amp_k ≥ 0 and a phase phase_k in [−π, π]. On screen, the aura can render as a closed radial shape built from these harmonics.

**Profile vector.** The generator builds a vector x of D values from the adult profile:

* Personality: the five adult traits and self-insight.
* Attachment: anxiety and avoidance.
* Sexuality: libido as a logit.
* Beliefs: the three political axes, religiosity, and money_saver_spender.
* History: the eleven channel sums from 6.2.
* Body: age as a z-score, plus two new body fields, height_z and build_z. Body fields inherit from both parents with a weight of 0.5 each, plus N(0, 0.6) (estimate).

The generator z-scores every entry against the calibration batch, so each entry has mean 0 and sd 1. The list above has D = 28 entries; aura.yaml holds the list, and D is however many entries it has.

**Projection.** At world start, the world seed fills a fixed matrix M with 2K rows and D columns, each entry drawn from N(0, 1/√D). Every pawn in the world uses the same M.

```
z       = M·x + ε,          ε_i ~ N(0, 0.3)
re_k    = z[2k]
im_k    = z[2k+1]
amp_k   = sqrt(re_k² + im_k²) / ā
phase_k = atan2(im_k, re_k)
```

ā is the calibration mean of the raw amplitude, so the population mean of amp_k is 1. The noise term ε keeps siblings with near-identical profiles from sharing an aura. Similar pawns still get similar auras, and a life event that moves a trait also moves the aura.

#### Taste

Taste uses two more fixed matrices from the world seed, T and W. Both have K rows and D columns, with entries from N(0, 1/√D).

```
offset_k = π × σ((T·x)_k + η_k),     η_k ~ N(0, 0.5)
weight_k = exp(0.5 × (W·x)_k) / Σ_j exp(0.5 × (W·x)_j)
```

An offset near 0 means A wants B's harmonic to align with A's own. An offset near π means A wants the opposite. Values in between mean A wants a partial shift.

#### Score

```
physical(A→B) = clamp( Σ_k weight_k × amp_k^A × s_k^B × cos(φ_k^B − phase_k^A − offset_k)
                       / Σ_k weight_k × amp_k^A ,  −1, 1 )
```

s_k^B and φ_k^B are B's perceived amplitude and phase from 7.3. For a healthy pawn, s_k^B = amp_k^B and φ_k^B = phase_k^B.

#### Worked example

This example uses K = 3 to keep the numbers short.

| Harmonic | A amp | A phase | A offset | A weight | B amp | B phase | Term |
| ----- | ----- | ----- | ----- | ----- | ----- | ----- | ----- |
| 1 | 1.2 | 0.5 | 0.2 | 0.5 | 1.0 | 0.8 | 0.597 |
| 2 | 0.8 | 2.0 | 3.0 | 0.3 | 1.1 | −1.2 | 0.263 |
| 3 | 1.0 | −1.0 | 1.5 | 0.2 | 0.6 | 0.3 | 0.118 |

The terms sum to 0.978, and the denominator is 0.6 + 0.24 + 0.2 = 1.04, so A's physical score for B is 0.94. A feels a strong pull. If B's first phase were 3.8 instead of 0.8, the first term would become −0.600 and the score would drop to −0.21. One heavily weighted harmonic can turn a pull into a mild repulsion.

#### Reference code

```csharp
public static double Physical(Pawn a, Pawn b)
{
    double num = 0, den = 0;
    for (int k = 0; k < Aura.K; k++)
    {
        double amp = b.Aura.Amp[k] * b.Health.Vitality;
        double phase = b.Aura.Phase[k] + b.Health.Jitter[k];
        double want = a.Aura.Phase[k] + a.Taste.Offset[k];
        double wA = a.Taste.Weight[k] * a.Aura.Amp[k];
        num += wA * amp * Math.Cos(phase - want);
        den += wA;
    }
    return den > 0 ? Math.Clamp(num / den, -1, 1) : 0;
}
```

### 7.3 Health effects on the aura

Health changes how others perceive the aura and leaves the stored aura untouched. Each active condition lowers vitality, shifts the phase of some harmonics, or both.

```
vitality = clamp(1 − Σ_c loss_c × active_c, 0.2, 1)
s_k^B    = amp_k^B × vitality
φ_k^B    = phase_k^B + Σ_c jitter_{c,k} × active_c
```

* **Vitality** scales every amplitude down. The shape fades and every alignment weakens. An observer whose taste is weak on a harmonic barely notices.
* **Jitter** shifts specific harmonics out of line. The world seed picks which harmonics a condition hits with `hash(world_seed, condition_id, n)`. The same condition always disrupts the same harmonics in a given world, so each condition has a consistent "look" that nobody authored.
* **active_c** runs from 0 to 1. It starts at the condition's severity and decays toward 0 with the condition's half-life. A condition with no half-life stays at full strength.
* **Rebasing.** A permanent condition folds into the aura after 12 months (estimate). The generator adds its jitter to the stored phases and removes it from the active list. After that, the change reads as part of who the pawn is, and new observers see no disruption.

Some observers will score B higher after a condition starts. That happens when the jitter moves one of B's phases toward what the observer wants.

#### Condition examples

All values are estimates.

| Condition | Vitality loss | Harmonics hit | Phase shift (radians) | Half-life |
| ----- | ----- | ----- | ----- | ----- |
| Bad night's sleep | 0.05 | 0 | 0 | 1 day |
| Hangover | 0.10 | 0 | 0 | 1 day |
| Acute stress (layoff, exam week) | 0.10 | 1 | 0.3 | 2 weeks |
| Flu | 0.25 | 0 | 0 | 5 days |
| Depressive episode | 0.30 | 2 | 0.5 | 3 months |
| Heavy drinking habit | 0.10 | 1 | 0.4 | 6 months after stopping |
| Chronic illness (generic) | 0.20 | 1 | 0.6 | None, rebases at 12 months |
| Visible facial injury | 0.05 | 2 | 0.8 | 3 months, 30% stays if it scars |
| Broken limb | 0.10 | 1 | 0.4 | 6 weeks |
| Permanent mobility disability | 0.00 | 2 | 0.7 | None, rebases at 12 months |

**Example.** B has the flu at full severity, so vitality = 0.75. In the worked example from 7.2, every term scales by 0.75, and A's score for B drops from 0.94 to 0.71.

### 7.4 Societal attraction

Societal attraction combines how the world rates B with how well B matches A's preferences on paper. The engine scores decisions with A's revealed preferences. Dialogue quotes A's stated preferences, so a pawn can say one thing and pick another.

```
soc(A→B) = c_A × appeal_B + (1 − c_A) × fit(A, B)
```

#### Conventionality

c_A sets how much A cares about general appeal. norm_strength comes from the location table, from 0 to 1. All coefficients are estimates.

```
c_A = clamp(0.4 + 0.15 × [money_meaning = status] − 0.10 × self_insight + 0.10 × norm_strength_loc, 0.1, 0.8)
```

#### Appeal

Appeal is the shared, observer-independent part: `appeal_B = σ(Σ weight × input)`. It is the same for every observer who knows the same facts about B. The location and era table can scale each weight, so income counts for more in a high-cost city.

| Input | Weight (estimate) |
| ----- | ----- |
| Income, z-scored within location and age band | 0.25 |
| Education level, z-scored | 0.15 |
| Job prestige, z-scored | 0.10 |
| Conscientiousness | 0.15 |
| Neuroticism | −0.15 |
| Extraversion | 0.10 |
| Mean aura amplitude × vitality, z-scored | 0.15 |
| Distance from the local peak dating age, in decades | −0.30 |
| Reputation loss (8.1) | −0.50 |
| Known criminal record (8.5) | −0.40 |

#### Fit

Fit scores B against each attribute in A's revealed preferences from 6.8. Each attribute has a target and an importance.

| Attribute | Score f from 0 to 1 (estimate) |
| ----- | ----- |
| Age | 1 inside the range, falling linearly to 0 over 5 years outside it |
| Income | 1 at or above the minimum, exp(−gap / 0.5 sd) below it |
| Education | 1 at or above the minimum, 0.5 one level below, 0 beyond that |
| Political distance | 1 within the maximum, falling linearly to 0 over 0.5 more distance |
| Religion match | 1 on a match, 0 otherwise |
| Wants children | 1 on a match, 0.5 if either side is unsure, 0 on a mismatch |
| Monogamy view | 1 if B's view is in A's allowed set, 0 otherwise |
| Kink overlap | Share of A's kinks B shares, divided by A's minimum, capped at 1 |
| Attachment style | A's weight for B's style |

```
fit(A, B) = Σ_i importance_i × f_i / Σ_i importance_i
```

**Unknown attributes.** A only scores attributes A knows about (8.1). An attribute A has not learned yet scores f = 0.6 (estimate). A pawn assumes the best a little, so early dates feel more promising than the facts support.

#### Dealbreakers

A dealbreaker counts only after A learns the attribute. A dealbreaker with hardness ≥ 0.8 (estimate) ends the relationship or date once revealed. A softer dealbreaker subtracts `P_deal = 0.5 × hardness` (estimate) from the combined score in 7.1.

**Example.** A has c_A = 0.4, and B's appeal is 0.62. A knows B's age, income, and politics, which score 1.0, 0.7, and 0.8 with importances 0.5, 0.3, and 0.6. B's religion is unknown, so it scores 0.6 at importance 0.4. Fit = (0.5 + 0.21 + 0.48 + 0.24) / 1.8 = 0.79, and soc = 0.4 × 0.62 + 0.6 × 0.79 = 0.72.

### 7.5 Emotional attraction

Emotional attraction scores how B's inner life fits A's. The engine computes the full value from the start, and 7.6 controls how much of it A feels at a given time.

```
emo(A→B) = 0.40 × form + 0.25 × wound + 0.20 × pull + 0.15 × res
```

The four weights are estimates.

#### Form fit

Form fit measures whether the two want the same shape of involvement. It compares the directions of desire u from 8.3, which come from each pawn's psyche.

```
form = (cos(u_A, u_B) + 1) / 2
```

Two novelty seekers fit because their desires point the same way. Two pawns who want very different forms feel it as friction no matter how attracted they are.

#### Wound and need

The belief table maps each want and need id from 6.9 to a rule over B's profile. A pawn with no core wound gets wound = 0.5.

| Belief (example) | Want rule | Need rule |
| ----- | ----- | ----- |
| "I have to earn love" | B's appeal ≥ 0.65 | B's avoidance ≤ −0.5 and conscientiousness ≥ 0 |
| "People leave" | B's anxiety ≥ 0.5 (intense, clingy) | B's avoidance ≤ −0.5 and a secure style |
| "I am too much" | B's extraversion ≤ −0.5 (quiet, contained) | B's agreeableness ≥ 0.5 |
| "I can only rely on myself" | B's avoidance ≥ 0.5 | B's anxiety ≤ 0 and conscientiousness ≥ 0.5 |

Each rule returns a score from 0 to 1 by how far B clears it. The want pulls early, and the need holds later.

```
wound = (1 − t) × want_match + t × need_match,     t = min(months_together / 6, 0.7)
```

#### Uncertainty pull

Unpredictable responsiveness pulls harder on a sensitive pawn. cv_B is the coefficient of variation of B's weekly contact with A over the last 8 weeks, or 0 before any contact. Sensitivity comes from 8.2.

```
pull = sensitivity_A × min(1, cv_B)
```

An avoidant partner fades and returns, which raises cv, and an anxious pawn has high sensitivity. The anxious-avoidant trap emerges without its own rule.

#### History resonance

Pawns who lived through similar things understand each other faster. `res = (cos(ch_A, ch_B) + 1) / 2`, where ch is the vector of channel sums from 6.2.

**Example.** A is anxious, with sensitivity 0.77, and B has been inconsistent, with cv = 0.8. The scores are form = 0.70, wound = 0.80, pull = 0.62, and res = 0.60. Then emo = 0.280 + 0.200 + 0.124 + 0.090 = 0.69.

### 7.6 Readiness and familiarity

Readiness scales the whole score by A's current state. Familiarity controls how much emotional attraction A has discovered and lets liking change how B looks to A.

#### Readiness

```
R_A = clamp(0.5 + 0.25 × lonely + 0.15 × goal_pressure + 0.10 × mood − 0.30 × recency, 0, 1) × open
```

| Term | Definition (estimate) |
| ----- | ----- |
| lonely | Loneliness from section 9, from 0 to 1 |
| goal_pressure | For marriage and children goals, the largest value of clamp(1 − (deadline − age) / 5, 0, 1) |
| mood | Current mood from −1 to 1 |
| recency | exp(−months since the last breakup / 3) |
| open | Openness to new people, below |

**Openness to others.** `open = clamp(1 − 0.8 × excl_max × (1 − diss_max), 0.05, 1)` (estimate). excl_max is the highest exclusivity level across the pawn's current agreements, and diss_max is its highest dissatisfaction in them (8.3). A pawn unhappy in an exclusive relationship becomes open to others, with no separate willingness-to-cheat roll.

#### Discovered emotional attraction

```
known = 1 − exp(−(contact_hours / 15 + 0.15 × deep_talks))
```

A deep talk is any conversation that discloses a field from beliefs, wants, or inner life. After one 2-hour date, known = 0.12. After ten 2-hour dates and three deep talks, known = 0.83.

#### Familiarity halo

Liking changes how B looks to A over time, in either direction.

```
physical_perceived = clamp(physical + 0.30 × (known × emo − 0.5) × fam, −1, 1)
fam                = 1 − exp(−weeks_known / 8)
```

**Example.** A's first-sight physical score for B is 0.10. After 16 weeks, fam = 0.86. With known = 0.83 and emo = 0.67, the halo adds 0.30 × 0.06 × 0.86 = 0.01, which barely moves the score. With emo = 0.95 the halo adds 0.07, and with emo = 0.30 it subtracts 0.06.

## 8. Relationships

Section 8 builds up in layers. 8.1 defines what is stored. 8.2 defines the four primitives every later mechanism uses. 8.3 is the core: a relationship as a pattern that both sides pull on. 8.4 to 8.6 extend it to other partners, conflict, and deception. 8.7 covers what happens over years, and 8.8 covers content settings.

### 8.1 Relationship record and knowledge model

Every relationship type uses one record. Each side of the record holds its own view, and most relationship types come from the two views disagreeing.

#### Record

```yaml
relationship:
  id: rel_000123
  members: [pawn_a, pawn_b]
  started_day: 412
  shared:
    pattern: [0.20, -0.10, 0.05, 0.31]    # P, see 8.3
    velocity: [0.0, 0.0, 0.0, 0.0]
    weeks_stable: 26
    agreements:                           # anchors, see 8.3
      - { op: define, anchor: [0.15, -0.05, 0.0, 0.30], mass: 1.0, made_day: 470, exclusivity: 0.0 }
    power: 0.2                            # -1 to 1, positive favors pawn_a
    secrecy: 0.0                          # 0 = public, 1 = nobody outside knows
    sex: { encounters: 14, last_day: 498 }
  sides:
    pawn_a:
      desire: [0.80, 0.50, 0.10, 0.40]    # D = mag x u, recomputed weekly
      drive: 0.25
      tolerance: 0.20                     # 8.4
      hidden_resentment: 0.00             # H, 8.3
      estimate_of_other: [0.30, 0.00, 0.00, 0.30]   # D-hat, 8.6
      investment: 0.55
      trust: 0.70
      resentment: 0.05
      grudge: 0.0
    pawn_b:
      desire: [0.10, -0.20, 0.00, 0.25]
      drive: 0.30
      tolerance: 0.55
      hidden_resentment: 0.40
      estimate_of_other: [0.40, 0.10, 0.00, 0.30]
      investment: 0.30
      trust: 0.60
      resentment: 0.00
      grudge: 0.0
  events: [evt_id, ...]                   # history entries, reused by section 4 channels
```

The record has no relationship type. The pattern, the agreements, and each side's desire are separate values, and any combination can exist. In this example, the two sides want different things, and nobody has said so.

Satisfaction is not stored. It is computed each week as 1 − diss (8.3).

#### Weekly updates

| Value | Weekly change (estimate) |
| ----- | ----- |
| investment | +0.03 × contact hours, capped at 1, then −0.02 a week without contact |
| trust | +0.01 a week without a negative event, minus each event's trust cost |
| resentment | + each event's resentment cost, then × 0.97 a week, or × 0.99 for neuroticism above 1 |
| satisfaction | 1 − diss (8.3) |

#### Knowledge model

Every pawn holds a set of known facts about other pawns. Each fact has a source and a day learned. A lie is stored as a claimed fact with a teller and a hearer.

* **Spread.** Each week, each fact a pawn knows has a chance to pass to each close friend: 0.05 × juiciness × (1 + extraversion of the teller). Juiciness is surprise against local norms × the listener's tie to the subject (8.2).
* **Contradiction.** When a pawn learns a fact that conflicts with a claimed fact it holds, the engine fires a discovery event. The liar loses trust equal to 0.3 + 0.5 × the fact's weight (estimate).
* **Reputation.** Each pawn has a reputation per social circle, from −1 to 1. Facts with a reputation cost lower it in each circle where enough members know them. Appeal (7.4) reads reputation loss as the most negative circle the observer belongs to.

### 8.2 Core primitives

Four primitives drive everything else in section 8: attachment tendencies, expectation and surprise, threat, and the choice model.

#### Attachment tendencies

Attachment works as continuous tendencies, never as switches. The style label from 6.5 feeds only the text layer.

| Tendency | Formula (estimate) | What it does |
| ----- | ----- | ----- |
| Voice | σ(0.8x − 1.0y) | Share of tension the pawn expresses through the talk. The rest is swallowed. |
| Sensitivity | σ(1.0x − 0.4y) | How strongly the pawn feels gaps, pain, and threat. |
| Exit floor | 0.35 − 0.10x − 0.10y | Attraction below which desire collapses toward the origin. Both axes keep a pawn in longer. |
| Reaction direction | 0.6x − 0.6y | Sign of the push under threat. Positive pursues, negative withdraws. Near 0, it swings with N(0, 0.3) noise. |
| Tolerance shift | +0.10y | Added to tolerance in 8.4. |

An avoidant pawn with y = 1.2 has voice 0.23. An anxious pawn with x = 1.2 has voice 0.72 and sensitivity 0.77. A pawn high on both has low voice, high sensitivity, and a reaction direction that swings, which produces push and pull with no rule for it.

#### Expectation and surprise

Each pawn keeps an expected value for each quantity it watches: the readouts of its own patterns, its estimate of each partner's desire D̂, and facts it holds about people it cares about.

```
E ← E + 0.2 × (obs − E)
surprise = |obs − E| / spread_q
```

spread_q is the population sd of that quantity. Sensitivity scales negative surprises up and leaves positive ones as they are, so anxious pawns read bad news louder. Surprise feeds three things:

* **Belief updates.** An observation about B moves A's estimate D̂_B, which changes A's tension and pain. A confession, an introduction to family, and cancelled plans all work this way.
* **Juiciness.** A fact's chance to spread (8.1) is surprise against the local norm × the listener's tie to the subject.
* **Blame.** Harm that A could have predicted from B's side counts as intended (8.5).

#### Threat activation

Each pawn has a threat level θ from 0 to 1. It sums current threats and decays by 30% a week (estimate).

| Source | Contribution (estimate) |
| ----- | ----- |
| A rise in overlap pain (8.4) | 1.0 × Δpain |
| A drop in a partner's contact readout | 0.5 × Δcontact / expected contact |
| A drop in a partner's vitality (7.3) | 0.8 × Δvitality |
| A history entry with threat_shift or scarcity deltas | 1.0 × the delta |
| A drop in reputation (8.1) | 0.5 × Δreputation |

A rival, a partner's illness, a death in the family, and a lost job all enter through these sources, so none needs its own rule. A rise in θ activates attachment by pushing the pattern (8.3):

```
Δv = 0.15 × Δθ × sensitivity × reaction × unit(D − P)
```

While θ is high, drive rises by 0.3 × θ × σ(x), and voice falls by 0.2 × θ × σ(y).

#### Choice model

Every deliberate act comes from one choice rule, the same one the life simulation uses for forks. Each option in the action catalog (8.5) gets a utility, and "do nothing" always has utility 0.

```
p(a) ∝ weight_a × exp(U_a / T),     T = 0.5 − 0.15 × conscientiousness
U_a  = gain_a − inhibition × (c_rep + c_legal + c_guilt + c_effort)
```

| Term | Definition (estimate) |
| ----- | ----- |
| gain | Expected drop in own dissatisfaction, plus grudge × expected harm to the target, plus social gain |
| c_rep | Reputation cost from tags × chance others learn of it |
| c_legal | Legal cost from tags × detection chance × (0.5 + σ(conscientiousness)) |
| c_guilt | Harm to others × σ(agreeableness) × (1 − grudge toward them) |
| c_effort | From the catalog |
| inhibition | 1 − 0.5 × intox − 0.2 × θ |

### 8.3 Relationship dynamics

The engine never defines what a relationship is. A relationship is an abstract pattern that each side pulls toward what it wants, against the pattern's own inertia. Spoken agreements anchor the pattern, and the talk changes them. Behavior, including sex, is read out of the pattern, and the text layer names what the player sees.

#### Pattern and readouts

Each relationship holds a pattern P, a vector of K_r = 4 unnamed values (estimate), each in [−1, 1]. A new acquaintance starts at the origin. P carries a velocity v and a count weeks_stable, the weeks since P last moved more than 0.05.

The world seed fills one fixed readout vector r per behavior, so nobody authors what a pattern means.

| Readout | Formula (estimate) |
| ----- | ----- |
| Contact hours per week | 12 × σ(r_contact · P) |
| sexrate | σ(r_sex · P) |
| Shown in public | σ(r_public · P) |
| Plans made ahead, in weeks | 26 × σ(r_plans · P) |
| Shared money and space | σ(r_shared · P) |

#### Desire: how much and what form

Each side wants P at a point D. Attraction sets how far from the origin D sits. The adult psyche alone sets its direction.

```
D_A   = mag_A × u_A
u_A   = Q·x_A / |Q·x_A|
mag_A = clamp((A(A→B) − floor_A) / (1 − floor_A), 0, 1)
```

Q is a fixed seed matrix, x_A is the profile vector from 7.2, and floor_A is the exit floor from 8.2. Two pawns equally drawn to each other can still want different forms, because their u vectors point different ways, and attraction cannot fix that. As attraction fades, mag shrinks and D moves toward the origin. Inertia makes the pair drift out of each other's lives over weeks instead of cutting off.

#### Drive and inertia

Drive g sets how hard each side pulls. Threat raises it as set in 8.2.

```
g = σ(0.8x + 0.5 × goal_pressure + 0.5 × lonely + 0.4 × known × emo − 0.6y − 1) × (1 + 0.5 × power)
```

The pattern resists change, more so the longer it has held and the more agreements anchor it:

```
S        = 1 − exp(−weeks_stable / 12)
m        = 1 + 3S + Σ mass
F_static = 0.10 × (1 + S) + 0.05 × Σ mass
F        = g_A × (D_A − P) + g_B × (D_B − P) + 0.3 × Σ_j mass_j × (anchor_j − P)
```

If |F| < F_static and |v| < 0.01, nothing moves. Otherwise, with F_kinetic = 0.5 × F_static:

```
a = (F − F_kinetic × F / |F|) / m
v ← 0.5 × v + a
P ← clamp(P + v, −1, 1)
```

Threat activation (8.2) and third parties (8.7) add to v directly.

#### Tension, dissatisfaction, and hidden resentment

```
τ    = g × |D − P| + 0.5 × pain
diss = sensitivity × (|D − P| + pain)
```

pain comes from 8.4. Dissatisfaction lowers mood by 0.3 × diss, sets satisfaction = 1 − diss, and raises openness to other people (7.6).

Each week, the swallowed share of tension accumulates out of sight as hidden resentment H:

```
H ← 0.98 × H + (1 − voice) × τ
```

H lowers satisfaction by 0.1 × H and shrinks emotional attraction by 0.05 × H a week. The partner never sees H. An avoidant pawn can stay below the attraction where a secure pawn would talk, look stable, and then leave once H crosses 1.

#### Talk, act alone, or wait

When τ ≥ 0.2, the pawn rolls each week (estimates):

```
p_talk  = voice × (0.5 × (τ − 0.2) + 0.05 × extraversion)
p_alone = σ(3(τ − 0.2) + 1.0y − 0.8 × agreeableness − 1.5 × A(A→B) + 0.5 × power + 1.5 × max(H − 1, 0) − 1)
```

**The talk** proposes a compromise C, weighted by drive and power, along with an agreement op:

```
C = (w_A × D_A + w_B × D_B) / (w_A + w_B),     w = g × (1 + 0.5 × power)
```

The responder accepts when |D_resp − C| − |D_resp − P| ≤ 0.4 × A(resp → init). Otherwise it refuses openly with chance voice_resp, or deflects. Either way, the initiator's resentment rises by 0.15.

**Acting alone** picks an act by weight. The direction of the pawn's desire decides the menu.

| Wants | Act | Weight (estimate) | Effect |
| ----- | ----- | ----- | ----- |
| Less (\|D\| < \|P\|) | Fade | 1 − voice | Drive locks to 0 and D collapses to the origin. P drains with no event. |
| Less | Ghost | (1 − voice) × (1 − investment) × (1 − σ(agreeableness)) | Dissolve, an impulse of 0.3 toward the origin, and this side's contact drops to 0 |
| Less | Dump | voice | Dissolve, announced, with no compromise |
| More (\|D\| > \|P\|) | Claim | g | Acts as if an anchor at D exists, on this side only |
| More | Pursue | g × θ × (1 − σ(agreeableness)), only after the other faded or ghosted | Opens the approach and control region of 8.5 |

Ghosting gives the other side surprise with no information. Its D decays only as fast as its attraction, and attraction decays slowly with no new facts, so closure takes a long time. Fading gives no event at all, and the other side learns only through readouts dropping. A claim adds to the other side's tension as pressure.

#### Agreement ops

Agreements are anchors in pattern space with a mass. The text layer names each one from data by op, by the agreements before it, and by era and location, so one op can read as "official," "engaged," "married," or "divorced."

| Op | Effect | Mass (estimate) | Requires |
| ----- | ----- | ----- | ----- |
| Define | Adds an anchor at C | 1.0 | Nothing |
| Renegotiate | Moves the newest anchor to C | Unchanged | An anchor |
| Formalize | Adds an anchor at C and records a legal or social fact | 3.0 | An anchor older than 12 months, and A ≥ 0.65 both ways |
| Set exclusivity | Sets the exclusivity level from 0 to 1, which lowers tolerance (8.4) | 0 | Nothing |
| Dissolve | Removes all anchors. P keeps its velocity and decays toward the origin by 10% of its distance a week | Removes all | Either side alone |

After Dissolve, P keeps its mass from S, so exes can keep seeing each other and sleeping together while the pattern drains.

#### Sex and consent

Whenever two pawns spend time together, each side rolls separately, and both must pass.

```
p = σ(base_setting + 4 × (A_sex − 0.55) + 0.8 × libido_z + 0.5 × intox − 1.2 × religiosity − 1.0 × excl_elsewhere + ln(2 × sexrate))
```

excl_elsewhere is the exclusivity level of the pawn's other agreements, scaled down by its dissatisfaction there. A pair with no pattern yet uses sexrate = 0.5. Settings carry attributes, and the base comes from them:

```
base_setting = −4 + 1.5 × privacy + 1.0 × disinhibition − 1.5 × sanction
```

| Setting (example) | privacy | disinhibition | sanction | base |
| ----- | ----- | ----- | ----- | ----- |
| Home, alone together | 1.0 | 0.2 | 0.0 | −2.3 |
| House party | 0.5 | 0.7 | 0.2 | −2.85 |
| Bar or club | 0.3 | 0.8 | 0.1 | −2.9 |
| Workplace | 0.2 | 0.0 | 0.9 | −5.05 |

How many eligible people a setting holds affects who meets whom, not this roll.

Intoxication at or above 0.8 sets a side's capacity to consent to 0. When either side fails, the encounter does not happen, unless the other side takes the Force sex action from the catalog (8.5). The choice model decides that like any other act, and the engine logs it as sexual assault.

#### Example

This example uses a 2-value pattern. P = (0.2, −0.1) has held for 26 weeks, so S = 0.89, m = 3.7, and F_static = 0.19. A wants D_A = (0.8, 0.5) with drive 0.25. B wants D_B = (0.1, −0.2) with drive 0.30. The force is (0.12, 0.12), with size 0.17, which falls below 0.19, so nothing moves.

A's tension is 0.25 × 0.85 = 0.21. A is secure, with voice 0.5, so A raises the talk. The compromise is C = (0.42, 0.12), which costs B 0.31. B's attraction allows 0.28, so B refuses. B is avoidant with voice 0.23, so B deflects rather than refusing openly. A's resentment rises by 0.15. B's own tension is small, but it lands mostly in B's H.

The player sees this through the feed and inspector described in [Presentation and legibility](#presentation-and-legibility).

### 8.4 Other relationships

Cheating, disappointment over casual dating, jealousy in poly, and neglect all come from one mechanism. Each pawn has a tolerance for how much of a partner's relational life sits elsewhere, and it feels pain when what it knows exceeds that tolerance.

#### Tolerance

Tolerance is part of the pawn's psyche. Agreements only adjust it. excl_AB is the exclusivity level set by agreements between A and B, from 0 to 1.

```
t_A = clamp(t_view + 0.10y − 0.3 × sensitivity + 0.15, 0, 1) × (1 − 0.8 × excl_AB)
```

| Monogamy view (6.6) | t_view (estimate) |
| ----- | ----- |
| strict | 0.10 |
| flexible | 0.30 |
| open | 0.60 |
| prefers_non_monogamy | 0.85 |

#### Overlap pain

A counts only B's other patterns that A knows about. Each one hurts in proportion to how much it resembles A's own pattern with B.

```
pain_A = max(0, Σ_j known_j × max(0, cos(P_AB, P̂_Bj)) × |P̂_Bj| − t_A)
```

P̂_Bj is A's estimate of B's pattern with person j, and known_j is A's confidence in it, from 0 to 1. A pattern that reads like a close friendship barely registers. One that reads like what A and B have hurts.

Pain feeds tension and dissatisfaction (8.3) and threat (8.2). Attachment then sets the response: an anxious pawn pushes toward B, and an avoidant pawn pulls back.

#### Breach

When an exclusivity level is set, overlap beyond tolerance also counts as a breach of the agreement:

```
trust_A −= 0.3 × pain_A × excl_AB × Σ mass_AB
```

Breaking a heavy agreement costs more trust than breaking a light one. Harm from a breach also feeds grudge (8.5).

#### Discovery

The knowledge model decides what A knows. Each week, each of B's other patterns that A does not know rolls to become known:

```
p = 1 − exp(−visibility_j × (0.01 + 0.03 × shared_friends + 0.02 × shared_places + 0.04 × suspicion))
```

visibility_j is the public readout of that pattern × (1 − secrecy). suspicion is A's accumulated surprise about B's readouts with A (8.2), so a drop in contact or a broken routine raises it without any rule about tells. Hiding a pattern is an action in the catalog (8.5), so a pawn conceals when the expected pain to its partner outweighs the effort and the guilt.

#### Cases

| Situation | What the engine does |
| ----- | ----- |
| Dating others with no agreement | Pain if it exceeds tolerance, no breach. This reads as disappointment. |
| The same with an exclusivity level set | Pain and a breach. This reads as cheating. |
| Poly with agreed low exclusivity | High tolerance absorbs most overlap. What is left reads as jealousy. |
| A partner's partner | A has its own pattern with that person, so kitchen-table and parallel setups emerge from those patterns. |
| The cheater | Needs no special logic. Its desire reaches toward another pattern, and secrecy decides whether its partner ever counts it. |

#### Time budget

Each pawn's patterns share what is left of its life load (8.7). When their contact readouts sum above that, the engine scales them all down to fit. The gap between D and P grows in each relationship, so neglect appears as tension with no rule for it.

### 8.5 Actions and conflict

Conflict, abuse, revenge, and crime all come from the choice model (8.2) run over one action catalog. The catalog says what each action is and what it does. It never says when it happens.

#### Action catalog

Each action has coordinates in a small response space, cost tags, and direct effects. Approach runs from −1 (withdraw) to 1 (approach). Hostility runs from −1 (warm) to 1 (hostile). Control runs from 0 to 1.

| Action (example) | Approach | Hostility | Control | Tags | Direct effect on the target (estimate) |
| ----- | ----- | ----- | ----- | ----- | ----- |
| Repair | 0.8 | −0.8 | 0.0 | None | trust +0.08, investment +0.05 |
| Complain | 0.6 | 0.2 | 0.1 | None | resentment +0.03 |
| Criticize | 0.6 | 0.5 | 0.2 | None | trust −0.02, resentment +0.05 |
| Mock | 0.3 | 0.9 | 0.2 | None | trust −0.05, resentment +0.10, shame +0.05 |
| Defend | 0.4 | 0.4 | 0.0 | None | resentment +0.04 |
| Shut down | −0.8 | 0.3 | 0.0 | None | trust −0.03, resentment +0.06 |
| Monitor | 0.7 | 0.2 | 0.6 | covert | trust −0.04, friend contact −5% |
| Isolate | 0.7 | 0.3 | 0.9 | covert | friend contact −10% |
| Control money | 0.5 | 0.4 | 1.0 | Needs power > 0 | financial dependence +0.1 |
| Threaten | 0.6 | 0.9 | 0.8 | illegal (minor) | fear +0.2, trust −0.10 |
| Hit | 0.8 | 1.0 | 0.7 | illegal, violent | fear +0.4, vitality injury, threat_shift +0.3 |
| Hide a pattern | −0.2 | 0.0 | 0.0 | covert | That pattern's secrecy +0.3 |
| Badmouth | 0.0 | 0.7 | 0.0 | public | Negative fact, spreads by surprise |
| Expose a secret | 0.0 | 0.9 | 0.0 | public | Releases a secret from 6.9 |
| Sex with the target's friend or ex | 0.0 | 0.6 | 0.0 | sexual | Overlap pain for the target, if known |
| Share intimate images | 0.0 | 1.0 | 0.0 | public, illegal | shame +0.6, reputation −0.3 |
| Follow and watch | 0.9 | 0.5 | 0.8 | covert, illegal | fear +0.3 once discovered |
| Take money | 0.2 | 0.4 | 0.3 | covert, illegal | wealth loss |
| Force sex | 0.9 | 1.0 | 1.0 | sexual, violent, illegal | See [Sexual assault](#sexual-assault) |

#### Conflict responses

When tension, threat, or grudge toward a partner is high, the pawn's state defines a point in the response space:

```
state = (reaction, clamp(resentment + grudge − 0.8 × σ(agreeableness), −1, 1), sensitivity × max(power, 0))
```

Each conflict action's gain in the choice model is 0.5 × (τ + θ) × (1 − |coords − state| / 2) (estimate). Actions near the pawn's state are the ones it reaches for, and their tags then decide whether inhibition stops them.

The text layer names actions by region. Criticize, Mock, Defend, and Shut down cover the four behaviors from Gottman's research that predict breakups, so those patterns appear without being scripted. Repair after a conflict restores trust and investment, so a cycle of harm and warmth raises investment over time, and that keeps people in.

#### Leaving

Leaving is Dissolve, Fade, or Ghost from 8.3. Its effort cost in the choice model comes from what the pawn would lose:

```
c_effort = 0.4 × shared_space + 0.5 × financial_dependence + 0.6 × fear + 0.3 × children − 0.25 × close_friends
```

The last term is capped at 1. Isolation cuts close friends, so it raises the cost of leaving. An abusive pawn can keep a partner for years when exit costs stay high, and the sim lets that happen. The player can be on either side.

#### Grudge: harm and blame

```
grudge += harm × blame
```

* **Harm** is the measured drop in the pawn's own satisfaction, overlap pain, reputation, and vitality that the event caused, summed with equal weights (estimate).
* **Blame** is how much the other side could foresee it. The choice model already holds the harm term the actor weighed, so blame = σ(4 × (foreseen harm / actual harm − 0.5)) (estimate). Ghosting someone who was clearly invested earns more blame than drifting from someone who seemed casual.

Grudge halves every 3 × (1 + 0.5 × neuroticism) months, with a minimum of 1 month. In the choice model, grudge turns harm to that target into gain.

#### Revenge and crime

No revenge ladder exists. A pawn with grudge weighs hostile actions by their gain against their costs. Harsher actions cost more reputation, legal risk, and guilt, so mild acts like badmouthing come first, and only large grudges in pawns with low inhibition reach exposure or crime.

Crime is not a category. Any action tagged illegal carries a legal cost and a detection chance from the crimes data file, by action, era, and location. Each act also creates a fact about the actor, and listeners closer to the target lower the actor's reputation in their circle.

#### Reporting and the legal pipeline

Reporting is the victim's own choice in the choice model:

```
U_report = harm × σ(institution_trust_loc) − 0.8 × shame − 0.6 × fear − 0.5 × closeness_perp + 0.3 × [friend encourages]
```

A report leads to an investigation. Charges and conviction follow with chances from the crimes file, by action and location, as researched data replaces the placeholders. A victim can seek a protective order as its own action after reporting. A conviction adds a criminal record fact, which appeal (7.4) reads.

#### Sexual assault

Force sex is the only route into this event, so it applies the same way to NPCs and to the player.

* **Victim effects.** The history log gets threat_shift +0.8, shame +0.6, and rupture +0.4 when the actor was a partner or friend, with the stage weights from 6.2. The event usually becomes the core wound. Trust toward the actor drops to 0.
* **Disclosure.** Telling a friend is an action with a shame cost and a support gain, so disclosure is slow for pawns with high shame. Each disclosure creates a fact the knowledge model can spread.
* **Reporting** uses the rule above.
* **Presentation.** The text layer covers the aftermath, the disclosure, and the consequences. It does not render the act. Content settings (8.8) can reduce or remove it.

#### Romance scams

A scam is a sequence of Take money actions wrapped in claimed facts (8.6). A pawn with low agreeableness and high scarcity finds the most gain in a target with high loneliness and low suspicion. Each money request is a new observation for the target, so surprise builds suspicion, and a target with friends who ask questions catches the contradictions sooner.

### 8.6 Perception and deception

Pawns never see each other directly. Each pawn holds estimates of the people it watches, built from projection, observation, and what others tell it. Deception works on those estimates through two mechanisms: claims for what is in a pawn's head, and fronts for verifiable facts.

#### Estimates

For each person B that A watches, A holds an estimate of B's desire (D̂_B), of B's other patterns (P̂_Bj), of B's taste, and of B's verifiable attributes.

* **Projection.** A new estimate starts from A's own values. A assumes B wants the form A wants (D̂_B starts along u_A) and likes what A likes. The weight on projection is 0.5 × (1 − σ(insight)) (estimate).
* **Observation.** What B does updates the estimate through the expectation rule in 8.2, at a rate of 0.2 × (0.5 + σ(insight)).
* **Claims.** What B says updates it at a rate of 0.3 × trust_A→B (estimate).

Each side assumes the other wants what it wants until the talk or a surprise corrects it, so misunderstandings appear without a rule for them.

#### Claims

A claim is an action in the catalog. It covers what is in the speaker's head: feelings, intentions, other relationships, and the past. A claim can be true or false.

* **Gain** is the expected effect of the listener's changed belief on the speaker's dissatisfaction.
* **Costs** are guilt (only for false claims), effort, and expected loss: chance of contradiction × the trust drop when caught.
* **Getting caught** uses the contradiction check in 8.1. The listener's trust in the speaker drops by 0.3 + 0.5 × the claim's weight, and the listener's blame for any harm rises, since the harm was foreseen.

Pawns with low conscientiousness or high scarcity lie more, because their costs are lower and their gains higher. Suspicious pawns catch more, because suspicion raises their discovery chances.

#### Fronts

Verifiable facts never go through claims. Profession, income, education, age, and relationship status use a presented profile, called a front.

* **Setting the front.** When a pawn first presents itself to someone, or writes a dating profile, it chooses a gap between the presented and true value for each attribute. The choice model weighs the appeal gain from 7.4 against upkeep and exposure risk, so pawns inflate what their local norms reward.
* **Upkeep.** A front costs effort each week: 0.02 × |gap| in z units (estimate). Lifestyle readouts have to match it, so spending above real income drains money through the life load (8.7).
* **Exposure.** Situations that touch reality are checks: a visit home, meeting coworkers, splitting a bill, and paperwork on a Formalize agreement. Each check exposes the front with chance |gap| × check strength (estimate). The shared money and space readout raises how often checks happen, so a front gets harder to hold as a relationship deepens.
* **Dropping it.** Confessing is an action. It costs trust of 0.1 × |gap| (estimate), which is less than being caught, and it ends upkeep and exposure risk.

#### Manipulativeness

Manipulativeness is a derived adult trait, computed as in 6.4:

```
mach = z(−0.6 × agreeableness + 0.3 × self_control − 0.3 × shame_sum + 0.2 × scarcity + N(0, 0.4))
```

Guilt already scales with agreeableness, so manipulative pawns lie and keep fronts more often without a separate rule. They also do it better. Front upkeep falls by a factor of (1 − 0.5 × σ(mach)). The chance of exposure per check and the detectability of a false claim both fall by (1 − 0.4 × σ(mach)). All coefficients are estimates.

#### Social insight and catering

Social insight is a new base trait on the z-scale, inherited as 0.5 × the parental average + N(0, 0.8) (estimate). The effective value is 0.7 × insight + 0.3 × self_insight.

* **Reading people.** Insight lowers the projection weight and raises the observation rate above. A's estimate of B's taste has noise with sd 0.6 × (1 − σ(insight)).
* **Catering** is an action. The pawn shifts how it presents toward its estimate of what the observer wants. It can rotate its perceived aura phases by up to 0.4 radians, and it can disclose first the attributes it thinks the observer values. A wrong estimate shifts the wrong way, so a pawn with low insight caters to its own taste.
* **Drift back.** The perceived shift fades as familiarity grows: perceived = true + cater × (1 − fam), with fam from 7.6. The gap then lands as a surprise: "they're not who I thought."

High insight with high manipulativeness produces a skilled manipulator. High insight with high agreeableness produces someone who makes people comfortable. Both use the same mechanism.

#### Dysphoria

Some pawns want to be someone else. Each pawn holds a wished-for self W, a point in the same profile space as its true self. W comes from history and chance, not from local appeal norms, so it can point in any direction.

```
dysphoria  = importance × |W − true|
importance = σ(0.5 × shame_sum + 0.3 × care_coldness + N(0, 0.5))
```

* **Presentation.** Fronts and claims move toward W, not toward what attracts the listener. A dysphoric pawn may claim a different background, interests, or past, even when that makes it less appealing. It reads as surprising rather than strategic.
* **Frequency.** The weekly chance of presenting this way scales with dysphoria. The gap itself drives it, so it happens even when guilt is high and the listener offers nothing to gain.
* **Exposure** uses the same checks as any front. Foreseen harm to the listener is low, so blame is low, and the listener feels more confusion than betrayal.

### 8.7 Long-run dynamics

Over years of play, relationships change the people in them, the people around a couple push on it, life competes for time, and old relationships leave memories. These four mechanisms keep a long run from settling into a fixed state.

#### Trait drift

The life simulation writes history channels only up to adulthood. During play, relationship actions and events keep writing to the same channels at the Present stage weight of 0.4.

| Event (example) | Channel deltas (estimate) |
| ----- | ----- |
| Each year in a relationship with no rupture | secure_bond +0.05 |
| Being ghosted | rupture +0.2 × investment |
| Discovering a breach (8.4) | rupture +0.3, shame +0.1 if self-blamed |
| Each conflict action received (8.5) with hostility above 0.7 | threat_shift +0.05, shame +0.03 |
| Dissolve | rupture +0.2 × investment |
| Formalize | constraint +0.1 |

Each month, the generator reruns the adult profile from 6.4 to 6.8 on the updated channel sums. The aura, taste, desire direction u, and tendencies then follow. Traits move at most 0.1 sd a month (estimate). Earned security, guardedness after a bad breakup, and people "changing" after marriage come out of this loop.

#### Third parties

Friends and family push on a relationship. Each close tie T of A computes an approval of A's partner B from its own psyche, using soc and emo from 7.4 and 7.5:

```
approval_T = soc(T→B) + 0.5 × emo(T→B) − 0.6
F         += 0.1 × sensitivity_A × Σ_T tie_TA × approval_T × unit(D_A − P)
```

The weights are estimates. Approval pushes P toward what A wants, and disapproval pushes it away. A parent's religion or politics against the partner's lowers soc, so family disapproval follows from beliefs. Isolation actions (8.5) cut ties, which removes their force.

#### Life load

Life load is one weekly budget of time, money, and energy, shared by every demand on the pawn.

| Demand (example) | Time (h/week) | Energy (0 to 1) | Money |
| ----- | ----- | ----- | ----- |
| Full-time job | 45 | 0.35 | Income |
| Second job | 20 | 0.25 | Income |
| Caring for a sick parent | 15 | 0.25 | Cost |
| Each child under 5 | 25 | 0.30 | Cost |
| Active illness (7.3) | 5 | 0.20 to 0.50 | Cost |

The pawn has 112 waking hours and 1.0 energy a week (estimate). Contact readouts across all patterns shrink to fit what is left (8.4). A money shortfall writes to the scarcity channel. Energy below 0.2 lowers vitality by 0.1. A new job, a sick parent, or a baby strains relationships through this budget with no extra rule.

#### Memory

Each relationship stores its peak and lowest satisfaction, with dates. After Dissolve, the remembered peak pulls the pawn back toward the ex:

```
mag_ex += 0.3 × peak × R × (1 − sat_best_current)
```

The pull halves every 18 months (estimate). R is readiness from 7.6. Running into an ex is a surprise against the remembered pattern. Going back to an ex, nostalgia, and rebounds come out of this.

### 8.8 Content and release

The systems always run at full fidelity. Content settings only change what the text layer shows.

#### Content settings

| Category | Options | Effect when reduced |
| ----- | ----- | ----- |
| Sexual content | Explicit, fade to black, summary | The text layer cuts from the setup to the aftermath |
| Abuse and violence | Full, summary | Events print as one-line outcomes |
| Sexual assault | Aftermath only, summary, off | Off removes the event from NPC histories at generation and blocks the player's step at the consent check |
| Crime | Full, summary | Events print as one-line outcomes |

A content warning shows the first time each category appears in a run.

#### Ratings and distribution

* Explicit sexual text puts the game at ESRB Mature or Adults Only.
* Steam requires an adult content survey and content descriptors before release.
* Valve removed the game Rape Day from Steam in 2019. If the player can commit sexual assault, plan for itch.io or direct sales as the primary storefront.

## Presentation and legibility

In an emergent sim, the player has to see causes, or results read as random. The player watches and does not direct each step, so four surfaces carry the story.

* **Feed.** The text layer posts lines when a readout crosses a threshold, threat jumps, or a talk or unilateral act happens.
* **Inspector.** Selecting a pair shows the readouts and a small plot of P with each side's D. The distance between the dots shows tension. H stays hidden.
* **Thoughts.** Each pawn surfaces its largest current derived term as a one-line thought, in the style of Roller Coaster Tycoon guests: "I wish he'd make plans" (a large gap between D and P), "Something feels off lately" (rising suspicion), "She hasn't texted back" (a drop in contact against expectation). The derived layer already computes these terms, so thoughts cost only templates.
* **Why view.** Selecting any change shows its top three contributing forces from the trace system. Example: "Pulled away: hidden resentment crossed 1, attraction fell 0.2, a better alternative appeared."

| Situation (example) | Example feed line |
| ----- | ----- |
| Contact low, sexrate high, public low | "They see each other when it suits them." |
| One side's tension above 0.2, the other's below 0.05 | "She keeps a toothbrush at his place now. He hasn't mentioned it." |
| Formalize anchor, sexrate below 0.1 | "They haven't touched in months, and neither brings it up." |
| Talk deflected | "She asked what they were doing. He changed the subject." |
| Ghost | "He hasn't replied in nine days." |
| Claim | "She told her mother they were moving in together. He found out from her mother." |
| Dissolve, sexrate still above 0.3 | "They broke up in March. He was at her place again on Friday." |

## Architecture

The sim is organized in layers by how fast each value changes and who decides it. One rule holds it together: pawns choose actions, events happen to pawns, and both write the same history entries.

### Layers

| Layer | Contents | Changes | Storage |
| ----- | ----- | ----- | ----- |
| Traits | Base traits, adult personality, attachment axes, manipulativeness, social insight, dysphoria and W, beliefs, aura, taste, desire direction u | Slowly, through history channels (8.7) | Stored |
| State | Mood, vitality, threat, loneliness, life load, money, location, active conditions | Each tick | Stored |
| Knowledge | Facts held, estimates, expectations, fronts seen, trust per source | When observed or heard | Stored, per pawn |
| Relationships | Pattern, velocity, anchors, each side's drive, H, trust, resentment, investment, grudge, memory peaks | Each tick | Stored, per pair |
| Derived | Attraction, tendencies, tension, dissatisfaction, pain, tolerance, readouts | On read | Never stored |
| Actions | The catalog in 8.5, plus talk, claims, fronts, catering, and the unilateral acts | Chosen by a pawn | actions.yaml |
| Events | Encounters, discoveries, exposure checks, consent outcomes, conditions, life events | Happen to a pawn | Data plus rolls |
| Presentation | Feed lines, thoughts, inspector, names for readouts and agreements | Reads everything, writes nothing | Templates |

Any value that can be computed from others is never stored. This keeps two copies from drifting apart and lets the why view trace every number to its inputs.

### The tick

One tick is one week (estimate).

1. **World.** The scheduler places pawns in settings, which decides who meets whom, and rolls events.
2. **Perception.** Each pawn observes what its situation shows, updates expectations and estimates, and computes surprise.
3. **Derive.** Tendencies, attraction, tension, pain, and threat are recomputed.
4. **Choose.** Each pawn's available actions, gated by setting and situation, go through the choice model.
5. **Apply.** Actions and events change state, relationships, and what others can observe.
6. **Record.** Every action and event writes a history entry with channel deltas.
7. **Drift.** Monthly, channel sums update traits, and the aura, taste, and u follow (8.7).

### Fact keys

The existing YAML reads the sim through fact keys such as `z.`, `wchannel.`, and `adult.`, with the existing condition system. Every new layer exposes its values the same way, for example `state.mood`, `rel.tension`, `know.suspicion`, and `tend.voice`. Action weights and conditions in actions.yaml are then written like the adult profile tables.

### Code layout

```
Sim/Psyche       traits, drift, derived tendencies
Sim/State        per-pawn state, conditions, life load
Sim/Knowledge    facts held, estimates, surprise, fronts
Sim/Relations    patterns, anchors, dynamics, tolerance
Sim/Actions      catalog loading, choice model, effects
Sim/World        scheduler, settings, events
Sim/History      log entries and channel sums (extends Sim/Life)
```

Each folder writes only its own layer. Actions and events change other layers by sending effects through Sim/Actions or Sim/World, never by writing to them directly.

## Data files

Data files hold content and constants. None of them says when something happens.

| File | Contents |
| ----- | ----- |
| attraction.yaml | Context weights, thresholds, readiness terms, familiarity constants |
| aura.yaml | K, noise sigmas, body inheritance weights |
| conditions.yaml | Vitality loss, harmonics hit, phase shift, half-life, rebase rule |
| appeal.yaml | Appeal weights and per-location multipliers |
| beliefs.yaml | Want and need rules per belief id (extends the 6.11 belief table) |
| tendencies.yaml | Coefficients for voice, sensitivity, exit floor, reaction direction, tolerance shift |
| threat.yaml | Threat sources, weights, and decay |
| relationships.yaml | K_r, readout scales, inertia constants, talk and unilateral weights, agreement ops and masses, time budget |
| tolerance.yaml | t_view per monogamy view, discovery terms |
| settings.yaml | Privacy, disinhibition, and sanction per setting |
| actions.yaml | The action catalog: coordinates, tags, effort, direct effects |
| crimes.yaml | Legal cost and detection per illegal action, era, and location, plus pipeline chances |
| norms.yaml | Local norm values that surprise and juiciness measure against |
| perception.yaml | Projection and update rates, front upkeep and exposure checks, insight and dysphoria coefficients |
| drift.yaml | Channel deltas per relationship event, drift cap, third-party and memory constants, life-load demands |
| thoughts.yaml | Thought templates keyed by derived term |

The world seed generates the fixed matrices and vectors at world start: M, T, and W for the aura and taste (7.2), Q for the form of desire (8.3), and the readout vectors r (8.3).

New profile fields: height_z and build_z (7.2), social insight (8.6), manipulativeness (8.6), and the wished-for self W (8.6).

## Invariants

The validator rejects any state that breaks these rules.

* Physical scores stay in [−1, 1]. All other scores stay in [0, 1].
* The taste weights of each pawn sum to 1, and each u vector has length 1.
* Vitality stays in [0.2, 1]. P stays in [−1, 1] on every value.
* No rule outside the choice model starts a catalog action.
* A sex event never exists without a passed consent check on both sides. A failed check followed by contact is always logged as Force sex.
* Every claimed fact has a teller and a hearer who both exist.
* Grudge and H never go below 0, and both decay in every week without new input.

## Testing

These are population tests over [N] simulated years. Since outcomes emerge, these tests are the main tuning tool. Targets are estimates until researched rates replace them.

| Test | Target (estimate) |
| ----- | ----- |
| Mean physical(A, B) over random pairs | 0.0 ± 0.05 |
| Share of random pairs with physical ≥ 0.35 | 15% to 25% |
| Correlation between physical(A, B) and physical(B, A) | Below 0.3 |
| Share of talks that end in a new or changed agreement | 40% to 60% |
| Share of exits that are fades or ghosts, for pawns with y ≥ 1 versus y ≤ 0 | Clearly higher for y ≥ 1 |
| Share of hidden overlap discovered within 1 year | 40% to 60% |
| Median length of relationships with 3+ control actions | Longer than those with none |
| Share of grudges that lead to an illegal action | Below 5% |
| Same seed, same world | Identical relationship histories |

### Tuning harness

A batch runner simulates the full population for decades and charts outcome distributions against the targets above. It also flags degenerate equilibria: almost everyone single, almost everyone formalized, or most relationships carrying repeated hostile actions. With this many coupled systems, the harness is the main tuning tool.

## Scope and build order

The current design puts its depth inside each pawn. Roller Coaster Tycoon does the opposite: its guests are shallow, and its depth comes from many guests reacting to what the player builds. The spec keeps its causal core, defers the outer layers, and treats the player's verbs as the open decision.

### Version 1 core

* Attraction (7.1 to 7.6)
* Attachment tendencies, expectation and surprise, and the choice model (8.2)
* Relationship dynamics: pattern, desire, drive, inertia, the talk, acting alone, agreements, sex and consent (8.3)
* Tolerance and overlap pain (8.4)
* Thoughts and the why view

### Deferred to version 2

* Fronts, catering, manipulativeness, social insight, and dysphoria (8.6)
* Romance scams and the detail of the legal pipeline (8.5)
* Most threat sources beyond overlap pain and partner withdrawal (8.2)
* Third parties and memory (8.7)

Each deferred layer goes in when the tuning harness shows a missing behavior that layer would produce.

## Open decision: the player's verbs

The player watches but also needs something to shape, the way RCT players build the park. The choice decides which systems need to be visible and tunable, and it needs a decision before tuning starts.

| Option | What the player does | Systems that matter most |
| ----- | ----- | ----- |
| Places | Builds and runs venues, events, and housing | Settings (8.3), the scheduler, life load (8.7) |
| Introductions | Chooses who meets whom | Attraction (7.1 to 7.6), readiness |
| Nudges | Influences one pawn's choices | The choice model, tendencies (8.2) |
