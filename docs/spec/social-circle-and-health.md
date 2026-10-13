# Social Circle, Physical Health, and Adult Mental Health

This document adds three parts to the adult profile (section 6 of *You will date*):

* **Physical health** describes the pawn's body: overall health, chronic conditions, fitness, and health habits.
* **Social circle** describes the pawn's friends: how many they have, who they are, and where the pawn stands among them.
* **Adult mental health** describes the pawn's own mental health today. The spec currently defines mental health only for parents (step 3), so the pawn needs its own.

The social circle affects mental health slightly. Physical health affects both the social circle and mental health. Section 6.1 conventions apply throughout: traits use the z-scale, intensities use 0 to 1, and every weight lives in a data file. Values marked (estimate) are starting values for tuning.

## 1. Where the parts fit

The three parts slot into the order table in 6.3. Physical health comes early, because sexuality and wants read it. The social circle and mental health come last, because they read almost everything.

| Step | Part | Reads |
| ----- | ----- | ----- |
| 1 | Personality | Base traits, channels |
| 2 | Attachment | Base traits, channels, relationship history |
| **3** | **Physical health** | Age, sex, history, wealth trajectory, personality, current city |
| 4 | Beliefs | Final state, history, personality |
| 5 | Sexuality | Personality, local norms, history, **physical health** |
| 6 | Wants | Channels, beliefs, attachment, sexuality, **physical health** |
| 7 | Inner life | History, personality, attachment, wants |
| **8** | **Social circle** | History, personality, attachment, final state, physical health, current city |
| **9** | **Adult mental health** | Personality, history, inner life, physical health, social circle, parents' mental health |

Each part reads only from the parts above it. In real life, mental health also shapes friendships. The life simulation carries that direction through history (an adult illness event can write `rupture` and lower close friendships), so the profile pass stays one-way.

## 2. Physical health

### 2.1 Health habits

Health habits are computed first, because overall health reads them.

| Field | Values | Main inputs |
| ----- | ----- | ----- |
| smoking | never, former, occasional, daily | Reward drive, self-control, parents' substance use, local norms by era, history (`legal_trouble`, peers) |
| drinking | none, light, moderate, heavy, dependent | Same categories as the parents' substance use. Reward drive, neuroticism, parents' substance use, religion and practice |
| activity | sedentary, light, active, athletic | Conscientiousness, reward drive, novelty seeking, current city urbanicity (walking cities raise light activity), income |
| sleep | poor, fair, good | Neuroticism, children under 5 in the household, career (shift work) |

The coping habit in 6.9 must agree with these. A pawn whose coping habit is drinking has drinking at heavy or above. The validator enforces this (see 6).

### 2.2 Overall health

The generator computes one health score on the z-scale, then buckets it into the five standard self-rated health levels.

raw\_health \= −0.03 × (age − 35) \+ 0.25 × ses \+ 0.20 × conscientiousness − 0.25 × adversity − 0.30 × habit\_load − 0.35 × illness\_events − 0.10 × premature \+ N(0, 0.7)

* **ses** is the mean of the income, education, and wealth z-scores from the final state.
* **adversity** is the stage-weighted sum of `scarcity`, `care_coldness`, and `care_inconsistency`. Childhood adversity predicts worse adult health.
* **habit\_load** sums smoking (daily 1.0, occasional 0.4), drinking (heavy 0.6, dependent 1.0), and sedentary activity (0.4).
* **illness\_events** counts illness entries from the adult events in 4.7.
* **premature** is 1 when the birth term was premature or very premature.

The coefficients are starting values (estimate). The score is normalized like other adult traits (6.1).

| general\_health | Cutoff on the z-scale | Target share (estimate) |
| ----- | ----- | ----- |
| excellent | above 0.85 | 20% |
| very good | 0.0 to 0.85 | 30% |
| good | −0.85 to 0.0 | 30% |
| fair | −1.6 to −0.85 | 15% |
| poor | below −1.6 | 5% |

### 2.3 Chronic conditions

The condition table works like the kink table (6.7). Each entry has these fields:

```yaml
id: type_2_diabetes
prevalence: 0.06            # base rate at age 45 (estimate)
age_curve: rising           # flat, rising, early_onset
sex: any                    # any, female, male
weights:
  health_score: -0.6
  habit_load: 0.4
  ses: -0.2
history_tags:
  scarcity: 0.2
visible: false              # can a stranger see it on a first date?
stigma_key: location.health_stigma.type_2_diabetes
affects:                    # all optional, each 0 to 1
  energy: 0.2
  libido: 0.1
  fertility: 0.0
  mobility: 0.0
```

For each condition, the generator rolls p \= sigmoid(logit(prevalence × age\_factor) \+ Σ w × x). A pawn keeps at most \[3\] conditions. When more fire, it keeps the ones with the highest p. Each kept condition stores these values:

* **Severity.** mild, moderate, or severe, drawn with the health score shifting the odds.
* **Onset age.** Drawn from the condition's age curve, at or below the current age. Early-onset conditions (type 1 diabetes, asthma) also write an entry into the history, so the text layer can say "has managed it since childhood."
* **Disclosure.** It uses the same formula as kink disclosure in 6.7, with the condition's stigma. Visible conditions always have disclosure 1.

Starting list (estimate; extend in data): asthma, migraine, type 1 diabetes, type 2 diabetes, hypertension, chronic back pain, IBS or Crohn's disease, autoimmune condition, epilepsy, hearing loss, visual impairment, mobility disability, endometriosis (female), PCOS (female), infertility, HIV.

Conditions are part of a person, not a punchline. The text layer should write them with the same care as the inner life fields.

### 2.4 Effects on other parts

| Reader | Effect |
| ----- | ----- |
| Sexuality (6.7) | libido \-= Σ condition.affects.libido × severity\_factor (0.5, 1.0, 1.5) |
| Wants (6.8) | A condition with fertility ≥ \[0.5\] marks the children goal as at risk. The text layer can use this; the goal score itself does not change. |
| Social circle (3) | Energy and mobility effects lower the circle size slightly (see 3.2). |
| Mental health (4) | Chronic burden raises the mental health risk (see 4.1). |
| Inner life (6.9) | Hideable conditions with disclosure below \[0.2\] become secret candidates, weighted by stigma. |
| Partner preferences (6.8) | New attribute `partner_smoking` (enum set). Smoking can become a dealbreaker: hardness \[0.6\] for never-smokers with high conscientiousness (estimate). |
| Presentation (9) | Each condition gets a disclosure threshold tied to trust, like other hidden fields. |

## 3. Social circle

### 3.1 Fields

The circle follows the familiar layers of social networks. A few close friends sit inside a wider group of friends, which sits inside a larger set of acquaintances.

| Field | Type | Meaning |
| ----- | ----- | ----- |
| close\_friends | integer, 0 to \[12\] | People the pawn would call in a crisis |
| circle\_size | enum: tiny, small, medium, large | Friends the pawn sees socially (tiny under 5, small 5 to 14, medium 15 to 49, large 50 or more) |
| circle\_status | z-scale | Social standing of the people the pawn spends time with |
| standing\_in\_circle | enum: peripheral, member, central, connector | The pawn's position among them |
| friend\_sources | list of {source, share} | Where the friends come from |
| close\_friend\_records | list (see 3.5) | One short record per close friend |
| social\_support | 0 to 1 | How much help the pawn can count on |
| loneliness | 0 to 1 | Gap between the company the pawn wants and the company it has |

### 3.2 Close friends and circle size

The close friend count is drawn from an overdispersed count model, so most pawns have a few close friends and a small share has none or many.

log\_mean \= ln(\[3.5\]) \+ 0.25 × extraversion \+ 0.15 × agreeableness − 0.20 × avoidance \+ 0.20 × friendship\_history \+ 0.10 × community\_ties \+ 0.20 × born\_locally − 0.15 × adult\_moves − 0.10 × young\_children − 0.10 × health\_limits − 0.01 × (age − 30)

close\_friends \= min(\[12\], Poisson(exp(log\_mean \+ N(0, 0.45))))

* **friendship\_history** is the close friendships condition from 4.7, averaged over adolescence and early adulthood.
* **community\_ties** is the community ties condition from 4.7 at the present stage.
* **born\_locally** is 1 when the birth location is the current city. Locals keep childhood and school friends nearby. Movers rebuild from work and hobbies.
* **adult\_moves** counts moves to a new city after age 18.
* **young\_children** is 1 when the household has a child under 5.
* **health\_limits** is the largest energy or mobility effect among the pawn's conditions.

Targets (estimate, roughly in line with recent US surveys): about 12% of pawns have no close friends, the median is 3, and about 10% have 10 or more. The base \[3.5\] and the noise \[0.45\] are tuned against these targets with the population test in 7. A Poisson count alone gives too few pawns with no friends, which is why the noise term is there.

circle\_size is drawn from the close friend count and extraversion. A pawn with 0 close friends can still have a small circle of casual friends.

### 3.3 Social standing

Friends tend to share the pawn's class, education, and age. The circle's status therefore starts from the pawn's own standing and adds noise.

own\_status \= 0.4 × income\_z \+ 0.3 × education\_z \+ 0.3 × occupation\_prestige\_z

circle\_status \= 0.6 × own\_status \+ 0.15 × family\_wealth\_z \+ 0.15 × extraversion \+ N(0, 0.5)

The occupation prestige score comes from a column in the career table. family\_wealth\_z reflects the household the pawn grew up in, so a pawn from money keeps some well-placed friends after a fall.

standing\_in\_circle comes from a softmax over the four positions:

| Position | Utility (estimate) |
| ----- | ----- |
| peripheral | 0.5 × avoidance − 0.4 × extraversion \+ 0.3 × shame\_sum |
| member | 0.5 (baseline) |
| central | 0.4 × extraversion \+ 0.3 × agreeableness \+ 0.2 × (own\_status − circle\_status) |
| connector | 0.5 × extraversion \+ 0.3 × novelty seeking \+ 0.2 × circle\_size\_index |

The text layer reads both fields. A pawn can be peripheral in a high-status circle (the plus-one at the gallery opening) or central in a modest one (the one everyone at the bar knows).

### 3.4 Friend sources

Each pawn has a share of friends from each source, drawn from a Dirichlet with these base weights:

| Source | Base weight | Raised by |
| ----- | ----- | ----- |
| childhood | 1.0 | born\_locally, extended family contact |
| school or university | 1.5 | education reached, age under 35 |
| work | 1.5 | career, age over 30 |
| neighborhood | 0.5 | small town or rural current city, owned housing |
| religious community | 0.5 | religiosity |
| hobby or scene | 1.0 | novelty seeking, urban current city |
| online | 0.5 | low extraversion, age under 35 |
| partner's friends | 0.5 | in a relationship |

### 3.5 Close friend records

Each close friend gets a small record so dialogue can mention real people ("my friend Dana from college").

```yaml
- source: school_or_university
  years_known: 12
  closeness: 0.8          # 0 to 1
  status_z: 0.4           # drawn around circle_status
  sex: female
  seed: 918273645         # derived: hash(pawn seed, "friend", index)
```

The derived seed lets the game promote a friend to a full pawn later without storing one now. A promoted friend must match the record (source, age within years known, sex).

### 3.6 Support and loneliness

social\_support \= sigmoid(0.7 × ln(1 \+ close\_friends) \+ 0.6 × mean\_closeness − 0.3 × avoidance − 0.2 × rupture\_recent − \[1.0\] \+ N(0, 0.3))

need \= sigmoid(0.6 × affiliation \+ 0.4 × extraversion \+ 0.3 × anxiety)

loneliness \= clamp(need − social\_support \+ N(0, 0.1), 0, 1)

Loneliness measures the gap, not the count. A low-affiliation pawn with one close friend is not lonely. An anxious pawn with eight friends can be. Section 9's starting loneliness starts from this value.

## 4. Adult mental health

### 4.1 Risk score

The pawn's mental health uses the same fields as the parents' mental health (severity, type, treatment), so the two can be compared and inherited. It is computed from a risk score:

risk \= 0.45 × neuroticism \+ 0.25 × wound\_impact\_z \+ 0.15 × parent\_history \+ 0.15 × chronic\_burden \+ 0.10 × substance\_load \+ **social\_term** \+ N(0, 0.7)

* **wound\_impact\_z** is the core wound's impact (6.9) on the z-scale, or 0 when there is no wound.
* **parent\_history** is 1 when either parent's severity is moderate or severe, and 0.5 when mild.
* **chronic\_burden** sums the severity of chronic conditions (mild 0.3, moderate 0.6, severe 1.0).
* **substance\_load** uses the same scale as habit\_load for drinking and drugs.

### 4.2 The social term

The social circle has a small effect, as intended:

social\_term \= 0.04 × loneliness\_z − 0.03 × social\_support\_z

Both inputs use the z-scale. The term is capped to \[−0.1, 0.1\].

Check: the other terms give raw risk a standard deviation of about 0.88. Moving from the most supported 10% of pawns (term about −0.09) to the loneliest 10% (about \+0.09) shifts normalized risk by about 0.2 SD. Against the 12% base rate of moderate or severe illness, that raises the rate from about 10% to about 14%, a gain of about 4 percentage points. That is enough to show in the population, but small next to neuroticism (0.45) and history. The sensitivity test in 7 holds the effect to \[3 to 5\] points.

### 4.3 Severity, type, and treatment

| Field | Rule |
| ----- | ----- |
| severity | Cutoffs on the normalized risk give the same base shares as the parents' table: stable 70%, mild 18%, moderate 9%, severe 3% (estimate). |
| type | Drawn from the parents' type list when severity is not stable. Threat sensitivity raises anxiety. Loneliness and chronic burden raise depression slightly. Physical violence in the home climate raises trauma. |
| treatment | untreated or treated. Income, education, current city urbanicity, and social support raise treated. |

## 5. Output record additions

```yaml
physical_health:
  general_health: enum
  health_score: float
  habits: { smoking: enum, drinking: enum, activity: enum, sleep: enum }
  conditions: [ { id, severity, onset_age, disclosure } ]
social_circle:
  close_friends: int
  circle_size: enum
  circle_status: float
  standing_in_circle: enum
  friend_sources: [ { source, share } ]
  close_friend_records: [ { source, years_known, closeness, status_z, sex, seed } ]
  social_support: float
  loneliness: float
mental_health:
  severity: enum
  type: enum
  treatment: enum
  risk: float
```

New data files:

* **Health habits table.** This file holds weights for smoking, drinking, activity, and sleep.
* **Condition table.** This file holds conditions with prevalence, age curves, weights, effects, and stigma keys.
* **Social table.** This file holds the count model, status weights, position utilities, and friend source weights.
* **Mental health table.** This file holds risk weights, the social term, cutoffs, and type and treatment weights.

## 6. Invariants

The validator rejects any profile that breaks these rules.

* close\_friends equals the number of close friend records.
* Every friend share is between 0 and 1, and the shares sum to 1.
* No friend's years known exceeds the pawn's age minus \[5\].
* A sex-specific condition matches the pawn's sex.
* Every condition's onset age is at or below the current age.
* A coping habit of drinking requires drinking at heavy or above. A coping habit of smoking requires occasional or daily smoking.
* Mental health type and treatment are none when severity is stable.
* The social term stays within its cap.

## 7. Testing

* **Population tests.** Run \[N\] pawns. The share with no close friends falls within \[12% ± 3\]. Median close friends is \[3\]. general\_health shares fall within \[tolerance\] of the targets in 2.2. Condition prevalence by age band falls within \[tolerance\] of the table.
* **Sensitivity tests.** Hold the seed fixed and move loneliness from its 10th to its 90th percentile. The chance of moderate or severe mental illness rises by \[3 to 5\] points, and never by more than \[6\]. Repeat for chronic burden, expecting a larger effect.
* **Gradient test.** Health score rises with ses, and the share in fair or poor health is at least \[2×\] higher in the bottom ses quintile than in the top.
* **Golden seeds.** Stored profiles include the new fields.

## 8. Building it in stages

The life simulation (section 4) does not exist yet, but the current table engine can already express most of this. Building in two stages lets the traits show up in the Traits tab now.

1. **Stage 1, tables only.** Add the three parts as groups in a new `adult.yaml` that the existing generator rolls after the family groups. The rules read what exists today: temperament, family, surroundings, home climate, `pawn.born_locally`, and `pawn.current_city`. Close friends uses buckets (0, 1, 2 to 3, 4 to 6, 7 or more) instead of a count. The social term becomes small modifiers on mental health severity, for example `loneliness: high` → `moderate x1.15, severe x1.15`.
2. **Stage 2, formulas.** Once sections 4 to 6 exist, switch each part to the formulas above, which read personality, channels, and history. The field names stay the same, so the Traits tab, the text layer, and the tests keep working.

## 9. Open questions

* Should close friends be promoted to full pawns when the player meets them, or stay as records?
* Should a partner's friends merge into the pawn's circle when they date, and split again after a breakup?
* How much should visible health affect first impressions in the attraction engine? This needs a deliberate, careful design choice rather than a default weight.
* Should sexual health (STI history) live in this section or in sexuality (6.7)?
