## Political Alignment

Each person's politics has two layers. The core layer holds a few stable traits that describe how the person sees the world. The issue layer holds their positions on specific topics, and the game derives it from the core layer during generation.

### Core political traits

The core layer has three axes, each a value from −1 to 1\.

* **Economic** runs from redistribution and public services to markets and low taxes.  
* **Cultural** runs from progressive to traditional.  
* **Authority** runs from personal liberty to order and strong rule.

The game draws these values after it generates the person's background. Household politics, religion, family class, and local politics shift the draw for each axis. Temperament adds a small push. Research has linked high threat sensitivity with a preference for order and tradition, but the findings are mixed, so the effect should stay small. The rest of each value comes from chance, which lets two siblings from the same home end up with different politics.

### Issue positions

Each issue has a weight on each axis. Immigration leans on the cultural and economic axes. Gun policy leans on the authority and cultural axes. A person's position on an issue starts as the weighted sum of their axis values.

Two factors then move the position away from that starting point.

**Consistency** is a core trait from 0 to 1 that sets how much noise each issue receives. A person with high consistency holds positions that follow their axes closely. A person with low consistency holds a scattered set of views. Research since Philip Converse's 1964 study shows that most voters fall at the low end, so the default draw should sit there.

**Personal stake** shifts a position when the person's history touches the issue. A person with a gay sibling moves toward support for LGBTQ rights. A person whose family runs a small business moves toward lower taxes. A person who grew up near a factory that closed moves against free trade. These shifts give people views that make sense once the player learns their story.

The final position for each issue is:

`position = (axis weights × axis values) + stake modifiers + noise × (1 − consistency)`

### Salience

Each person also has a salience score for each issue. Salience starts low and rises with personal stake, so the issues a person cares about most come from their own life. Compatibility multiplies the gap on each issue by both people's salience, which means a large gap on a minor issue costs little and a small gap on someone's core issue can end a relationship.

### Example

Two people grow up in the same working-class Catholic household in a mixed suburb. Both draw axis values near the center, with a lean toward tradition.

The first has high consistency. Her positions line up with her axes, and she holds moderate, traditional views across the board.

The second has low consistency, and his older brother came out as gay when he was 15\. He holds traditional views on crime and immigration, supports same-sex marriage, and ranks it as his most important issue. A partner who opposes it fails his compatibility check even when they agree on almost everything else.

## **Pawn Generation**

A pawn is generated in a fixed order. Each step reads only from the steps before it. The order matters because temperament is drawn first, and it shapes how every later event lands. Two pawns with the same background diverge for this reason.

### **1\. Seed**

The generator starts with four independent draws.

* **Birth year** comes from an even spread across the game's age range.  
* **Birth location** comes from population weights.  
* **Sex** comes from the base rate.  
* **Temperament** sets three core traits, each from 0 to 1\. Threat sensitivity measures how strongly the pawn reacts to danger, stress, and loss, and how easily arousal shuts down. Reward drive measures how strongly the pawn pulls toward pleasure, novelty, people, and sex. Self-control measures whether the pawn acts on those pulls. The three traits carry mild correlations, so threat sensitivity and self-control lean slightly against each other.

### **2\. Context**

The generator looks up the world the pawn is born into from location and year. These values come from data tables and involve no random draw.

| Item | Values | Depends on |
| ----- | ----- | ----- |
| Urbanicity | urban core, inner suburb, outer suburb, small town, rural | location |
| Local politics | strongly left, left-leaning, mixed, right-leaning, strongly right | location, year |
| Ethnic and religious mix | diversity level (homogeneous, mostly homogeneous, mixed, highly diverse) and group shares by \[groups per location\] | location, year |
| Economic era | boom, steady growth, stagnation, recession, crisis | year, location |
| Norms on sex | restrictive, conservative, moderate, permissive | location, year |
| Norms on gender roles | traditional, mixed, egalitarian | location, year |
| Norms on family | extended-family centered, nuclear, individualist | location, year |

### **3\. Family and household**

**Parents.** Each value is drawn from a distribution set by the context. Values that apply to a person are drawn for each parent.

| Item | Values | Depends on |
| ----- | ----- | ----- |
| Immigrant generation | third generation or later, second generation, 1.5 generation, first generation, mixed | location, year |
| Languages at home | local only, bilingual, heritage only | immigrant generation |
| Education | none, primary, some secondary, secondary, trade, some college, bachelor's, master's, doctorate or professional | location, year, immigrant generation |
| Occupation | unemployed, homemaker, informal work, unskilled labor, skilled trade, service, clerical, agriculture, military, public sector, small business, creative, professional, academic, manager or executive | education, location, economic era |
| Religion | none, spiritual, Catholic, mainline Protestant, Evangelical, Orthodox, Latter-day Saint, Jewish, Muslim, Hindu, Buddhist, Sikh, folk or other | location, year, immigrant generation |
| Practice level | none, nominal, holidays only, regular, devout, strict | religion, location, year |
| Household politics | leaning (far left to far right, or apolitical), engagement (none, casual, engaged, activist), agreement (agree, split) | location, religion, education |
| Age at pawn's birth | under 20, 20 to 24, 25 to 29, 30 to 34, 35 to 39, 40 and over | year, education, religion |
| Mental health | severity (stable, mild, moderate, severe), type (anxiety, depression, bipolar, personality traits, psychotic, trauma), treatment (untreated, treated) | base rate |
| Substance use | none, social drinking, heavy drinking, alcohol dependence, recreational drugs, drug dependence, in recovery | mental health, location |

**Household at birth.** These values follow from the parents.

| Item | Values | Depends on |
| ----- | ----- | ----- |
| Household wealth | poverty, working class, lower middle, upper middle, affluent, wealthy | occupations, location |
| Marriage quality | warm and stable, cold and stable, strained, high conflict, separated | mental health, wealth, substance use |
| Household shape | two parents, single mother, single father, blended, grandparents, extended family, foster, adoptive | parents' ages, marriage quality, religion, year |
| Planned | planned, unplanned and welcomed, unplanned and resented | parents' ages, wealth, religion |
| Siblings | 0 to 5 or more | religion, wealth, year, location |
| Birth order | only, eldest, middle, youngest | siblings |
| Age gaps | close (under 3 years), moderate (3 to 6), wide (7 or more) | siblings, parents' ages |
| Twin | single, fraternal twin, identical twin | base rate |
| Birth term | full term, premature, very premature | wealth, mother's age |

**Surroundings.** These values follow from wealth and location.

| Item | Values | Depends on |
| ----- | ----- | ----- |
| Neighborhood wealth | poor, working class, middle, affluent, wealthy | household wealth, location |
| Neighborhood safety | dangerous, rough, average, safe, very safe | neighborhood wealth |
| Housing type | unstable or shelter, public housing, rented apartment, rented house, owned apartment, owned house, farm, estate | household wealth, location, urbanicity |
| School quality | failing, below average, average, good, elite | neighborhood wealth |
| School type | public, charter, private secular, religious, boarding, homeschool | household wealth, religion |
| Access to activities | none, few, some, many | household wealth, neighborhood wealth, urbanicity |

**Home climate.** These values come last in this step.

| Item | Values | Depends on |
| ----- | ----- | ----- |
| Parenting style | warm and firm, strict, permissive, neglectful, overprotective, inconsistent | mental health, education, religion, wealth, pawn's temperament |
| Domestic violence | none, verbal, physical between parents, physical toward children | substance use, marriage quality, mental health |
| Caregiving duties | none, light chores, sibling care, parent care, acting head of household | household shape, birth order, wealth, parents' health |

Parenting style depends on the pawn's own temperament, since children shape how their parents treat them. A child with high threat sensitivity tends to draw more protective parenting. A child with high reward drive and low self-control tends to draw stricter parenting.

### **4\. Life simulation**

#### **4.1 Purpose**

The life simulation turns a pawn’s starting state into a present-day profile and a life history. The attraction engine reads the profile. The text layer reads the history and fills prose templates from it.

#### **4.2 Inputs**

* **Seed.** A 64-bit integer. Every random draw derives from it.  
* **Birth year and birth location.**  
* **Starting conditions.** These cover parents’ occupations, parents’ education, parents’ ages, marriage quality, religion, immigrant generation, and starting wealth.  
* **Base traits.** These cover temperament, reward drive, self-control, threat sensitivity, and affiliation.  
* **Data tables.** Era, location, event, and fork tables load from data files (see 4.9).

#### **4.3 Stages**

| Stage | Ages |
| ----- | ----- |
| Early childhood | 0 to 5 |
| School age | 6 to 12 |
| Adolescence | 13 to 17 |
| Early adulthood | 18 to 25 |
| Present | 26 to current age |

The generator stops at the pawn’s current age. A 22-year-old pawn runs early adulthood up to age 22 and skips the present stage.

#### **4.4 Item types**

Each rolled item belongs to one of three types. The type sets how the generator resolves it.

* **Condition.** A state value that the generator recomputes at the start of each stage. Wealth and household shape are conditions.  
* **Event.** A yes-or-no roll with a rate. Parental divorce and job loss are events.  
* **Fork.** A choice among options, weighted by traits. Each option unlocks or blocks later items. Field of study and military service are forks.

#### **4.5 Stage loop**

Each stage runs these steps in order.

1. The generator updates all conditions for the stage.  
2. The generator rolls events in dependency order (see 4.8).  
3. Each event that fires passes through the interpretation step. That step writes trait and state changes back to the pawn.  
4. The generator resolves forks. The chosen option sets flags that gate items in later stages.  
5. The generator writes a stage record to the history.

#### **4.6 Rate and choice models**

**Event rate.** The generator computes each event’s chance with a logistic model:

p \= sigmoid(logit(base) \+ Σ wᵢ · xᵢ)

The base rate comes from the era and location tables for the pawn’s year and place. Each xᵢ is a normalized input from the item’s “depends on” list. Each wᵢ is a tunable weight stored in the event table. The logistic form keeps p between 0 and 1 without clamping.

**Fork choice.** The generator scores each option with a utility function of traits and conditions. It then picks with a softmax at temperature \[T\]. A low T makes pawns follow their traits closely. A high T adds noise.

**Repeat rolls.** Each event has a frequency. “Once” items roll a single time in their window and lock after they fire. “Per stage” items roll once per stage. “Per year” items roll once per simulated year.

#### **4.7 Item list**

| Item | Type | Window | Frequency | Depends on |
| ----- | ----- | ----- | ----- | ----- |
| Wealth trajectory | Condition | All stages | Per stage | Parents’ occupations, economic era, random shocks |
| Parental divorce | Event | Early childhood to adolescence | Once | Marriage quality, religion, year |
| Remarriage | Event | Early childhood to adolescence | Per stage | Divorce or parent death, parent age, wealth |
| Death of a family member | Event | All stages | Per stage | Parents’ ages, wealth |
| Move | Event | Early childhood to adolescence | Per stage | Parents’ occupations, wealth, divorce |
| Household shape | Condition | Early childhood to adolescence | Per stage | Divorce, death, remarriage |
| Peer acceptance | Condition | School age, adolescence | Per stage | Temperament, moves, wealth relative to school, fit with local mix |
| Bullying | Event | School age, adolescence | Per stage | Peer acceptance, threat sensitivity |
| Close friendships | Condition | School age onward | Per stage | Reward drive, moves |
| Mentor | Event | School age to early adulthood | Once | School quality, activities |
| Extended family contact | Condition | All stages | Per stage | Immigrant generation, household shape, moves |
| Community ties | Condition | All stages | Per stage | Religion, activities |
| Sex education | Condition | Adolescence | Once | Location, year, school type, religion |
| Age at first sexual experience | Event | Adolescence onward | Per year | Reward drive, self-control, parenting style, peers, religion, local norms |
| Education reached | Fork | Adolescence, early adulthood | At each level | Self-control, school quality, wealth, parents’ education |
| Field of study | Fork | Adolescence, early adulthood | Once | Temperament, education reached, parents’ occupations |
| Age at leaving home | Event | Adolescence onward | Per year | Wealth, local norms, family conflict |
| Military service | Fork | Late adolescence, early adulthood | Once | Wealth, location, education reached, economic era |
| Career path | Fork | Early adulthood onward | Per stage | Education, field, location, economic era |
| Income | Condition | Early adulthood onward | Per year | Career, economic era, location |
| Debt | Condition | Early adulthood onward | Per year | Education, wealth, self-control |
| Current city | Fork | Early adulthood onward | Per stage | Career, reward drive, family ties |
| Relationship history | Event | Adolescence onward | Per year | Temperament, affiliation, age, location, local norms |
| Children | Event | Early adulthood onward | Per year | Age, religion, relationship history, wealth |
| Current household | Condition | Early adulthood onward | Per year | Relationship history, children, income |
| Adult events (job loss, illness, windfall, legal trouble) | Event | Early adulthood onward | Per year | Career, economic era, self-control, reward drive |

Relationship history produces a list of past partners. Each entry records start age, end age, and end reason.

#### **4.8 Ordering and determinism**

The items inside a stage form a dependency graph. The build step sorts this graph and rejects any cycle. An item may read values from earlier in the same stage or from any earlier stage.

Each roll draws from its own sub-seed, computed as hash(seed, stage, year, item\_id). Adding or removing an item leaves all other rolls unchanged. The same seed and data files always produce the same pawn.

#### **4.9 Data files**

Designers edit these files. Code changes are not needed to tune or add items.

* **Era table.** This file holds base rates and economic conditions by year.  
* **Location table.** This file holds local norms, school quality ranges, and base rates by place.  
* **Event table.** Each entry defines id, type, window, frequency, base rate key, weights, effects, requires flags, and unlocks flags.  
* **Fork table.** Each entry defines options, utility weights, and the flags each option sets.

Example event entry:

yaml

id: parental\_divorce

type: event

window: \[early\_childhood, adolescence\]

frequency: once

base\_rate: era.divorce\_rate

weights:

  marriage\_quality: \-1.2

  religiosity: \-0.6

requires: \[parents\_married\]

effects:

  household\_shape: single\_parent

  family\_conflict: \+0.3

unlocks: \[remarriage, move\]

#### **4.10 Outputs**

* **Final state.** This record holds every condition, trait, and flag at the pawn’s current age.  
* **History.** This list holds one entry per fired event and resolved fork. Each entry stores stage, age, item id, outcome, trait changes, and the ids of the entries that caused it. The text layer uses the cause ids to explain a pawn’s traits in prose.

#### **4.11 Invariants**

The validator rejects any pawn that breaks these rules.

* A divorce requires married parents.  
* A remarriage requires an earlier divorce or parent death.  
* A parent’s age at the pawn’s birth falls within \[min\] to \[max\].  
* Military service starts at or after the legal minimum age for the location and year.  
* Income is zero when the pawn has no career and no other income source.  
* The era and location tables cover every year from birth to the present.

When both parents die, the generator assigns a guardian household. When the birth location differs from the current city, the generator applies the location table for each year the pawn lived in each place.

#### **4.12 Testing**

* **Population tests.** The suite generates \[N\] pawns per era and location. It compares marginal rates against reference data within a tolerance of \[x\]. Checked rates include divorce by year, education attainment, median age at leaving home, and median age at first sexual experience.  
* **Golden seeds.** A fixed set of seeds must reproduce stored pawns exactly. Any intended change to tables or code updates the stored pawns in the same commit.  
* **Performance.** One pawn generates in under \[N\] ms.

#### **4.13 Design intent**

Small differences in temperament grow over the stages, because each choice changes which events can happen next.

### **5\. Event interpretation**

Each event has a few possible readings, and the pawn's temperament weights which one it takes. Parental divorce is one example.

* A pawn with high threat sensitivity tends to read it as "people leave."  
* A pawn with low threat sensitivity and high reward drive tends to read it as "I can only rely on myself."  
* A pawn with high self-control and low reward drive tends to read it as "I have to keep everyone calm."

The reading is drawn with these weights, and chance still plays a part. The chosen reading shifts the pawn's affiliation, attachment, and beliefs. The generator logs each event with its reading and an impact score, and the inner life step uses this log later.

### **6\. Adult profile**

Once the life simulation ends, the generator computes the adult profile in one pass. The pass reads base traits, the final state, and the history log from section 4\. It writes one profile record that the attraction engine and the text layer read.

#### **6.1 Conventions**

* **Trait scale.** All trait and personality values are floats on a population z-scale, with mean 0 and standard deviation 1\. The generator clamps them to \[-3, 3\].  
* **Unit scale.** All intensities, weights, and probabilities are floats from 0 to 1\.  
* **Normalizing.** A calibration batch of \[N\] pawns runs at build time. The build stores each adult trait’s raw mean and standard deviation, and the generator uses them to rescale raw scores to the z-scale.  
* **Randomness.** Each draw uses hash(seed, “profile”, field\_id, index) as its sub-seed.  
* **Weights.** All weights in this section live in data files. The values shown are starting values for tuning (estimate).

  #### **6.2 Accumulator channels**

The interpretation step in section 5 writes deltas onto named channels in each history entry. The adult profile reads the channel sums. Each sum applies a stage weight to every delta.

| Stage | Stage weight (estimate) |
| ----- | ----- |
| Early childhood | 1.0 |
| School age | 0.8 |
| Adolescence | 0.9 |
| Early adulthood | 0.6 |
| Present | 0.4 |

| Channel | Meaning | Main writers |
| ----- | ----- | ----- |
| threat\_shift | Change to threat sensitivity | Bullying, parent death, illness |
| reward\_shift | Change to reward drive | Windfall, peer acceptance |
| control\_shift | Change to self-control | Mentor, military service, debt |
| affiliation\_shift | Change to affiliation | Close friendships, community ties |
| care\_inconsistency | Caregiving that came and went | Divorce, moves, parent addiction |
| care\_coldness | Caregiving that was distant or harsh | Parenting style, household conflict |
| rupture | Painful ends to close bonds | Breakups, betrayal, friend loss |
| secure\_bond | Long, stable close bonds | Long relationships, stable household |
| scarcity | Lack of money or safety | Wealth drops, job loss, debt |
| constraint | Loss of freedom to choose | Early marriage, early children, caregiving duty |
| shame | Events tied to self-worth | Bullying, legal trouble, failure at a fork |

#### **6.3 Order**

| Step | Part | Reads |
| ----- | ----- | ----- |
| 1 | Personality | Base traits, channels |
| 2 | Attachment | Base traits, channels, relationship history |
| 3 | Beliefs | Final state, history, personality |
| 4 | Sexuality | Personality, local norms, history |
| 5 | Wants | Channels, beliefs, attachment, sexuality |
| 6 | Inner life | History, personality, attachment, wants |

#### **6.4 Personality**

The generator computes five adult traits with this formula:

raw \= Σ (base weight × base trait) \+ Σ (channel weight × channel sum) \+ N(0, σ)

adult \= clamp((raw − calibration mean) / calibration sd, −3, 3\)

| Adult trait | Base weights (estimate) | Channel weights (estimate) | σ |
| ----- | ----- | ----- | ----- |
| Neuroticism | threat sensitivity 0.7 | threat\_shift 1.0, shame 0.4, secure\_bond −0.3 | 0.4 |
| Extraversion | reward drive 0.5, affiliation 0.3 | reward\_shift 0.8, affiliation\_shift 0.5 | 0.4 |
| Novelty seeking | reward drive 0.6 | reward\_shift 0.6, constraint 0.3 | 0.4 |
| Conscientiousness | self-control 0.7 | control\_shift 1.0, scarcity 0.2 | 0.4 |
| Agreeableness | affiliation 0.5, threat sensitivity −0.2 | affiliation\_shift 0.6, rupture −0.3 | 0.4 |

The generator also computes self-insight, which section 6.8 uses:

self\_insight \= clamp(0.4 × novelty seeking − 0.3 × neuroticism \+ 0.2 × conscientiousness \+ N(0, 0.5), −3, 3\)

#### **6.5 Attachment**

The generator scores attachment on two axes, anxiety and avoidance. Both use the z-scale.

anxiety \= 0.4 × threat sensitivity \+ 0.5 × care\_inconsistency \+ 0.3 × rupture − 0.4 × secure\_bond \+ N(0, 0.4)

avoidance \= −0.3 × affiliation \+ 0.5 × care\_coldness \+ 0.2 × rupture − 0.3 × secure\_bond \+ N(0, 0.4)

The coefficients above are starting values (estimate).

**Earned security.** A relationship that lasted at least \[Y\] years and ended without a rupture tag lowers both axes by \[d\].

**Style.** The generator assigns a style by quadrant, with a cutoff of \[c\] on each axis.

| Anxiety | Avoidance | Style |
| ----- | ----- | ----- |
| Below c | Below c | Secure |
| At or above c | Below c | Anxious |
| Below c | At or above c | Avoidant |
| At or above c | At or above c | Fearful-avoidant |

The profile stores both axis scores and the style. The attraction engine reads the scores. The text layer reads the style.

#### **6.6 Beliefs**

Political alignment, religion, and views on monogamy and money are drawn from conditioned distributions. The Political Alignment section describes the method in full. This section fixes the output fields.

| Field | Type | Values |
| ----- | ----- | ----- |
| political | Object | Defined in the Political Alignment section |
| religion\_id | Enum | Ids from the religion table, including none |
| religiosity | 0 to 1 | Strength of belief and practice |
| monogamy\_view | Enum | strict, flexible, open, prefers\_non\_monogamy |
| money\_saver\_spender | z-scale | Negative values save, positive values spend |
| money\_meaning | Enum | security, status, freedom, indifferent |

#### **6.7 Sexuality**

**Libido.**

libido \= sigmoid(0.5 × reward drive − 0.3 × threat sensitivity \+ age\_curve(age, sex) \+ N(0, 0.5))

The libido table stores age\_curve values. The coefficients are starting values (estimate).

**Kinks.** The kink table lists every kink with its fields.

yaml

* id: kink\_example  
* prevalence: 0.12  
* weights:  
*   reward\_drive: 0.5  
*   novelty\_seeking: 0.4  
*   neuroticism: \-0.1  
* history\_tags:  
*   rupture: 0.2  
  stigma\_key: location.kink\_stigma.kink\_example

For each kink, the generator rolls p \= sigmoid(logit(prevalence) \+ Σ w × x). A pawn keeps at most \[k\] kinks. When more fire, the generator keeps the ones with the highest p.

Each kept kink stores two values.

* **Intensity.** The generator draws it from Beta(α, β), with a mean of sigmoid(0.5 × novelty seeking \+ 0.3 × libido\_logit). Intensity from 0 to 0.33 means curious, 0.33 to 0.66 means enjoys, and 0.66 to 1 means needs.  
* **Disclosure.** disclosure \= sigmoid(base − stigma − 0.5 × neuroticism \+ 0.3 × extraversion \+ 0.1 × partner count). The location table supplies stigma for the pawn’s current city and era.

A kink with intensity of \[0.8\] or more becomes a dealbreaker candidate in 6.8.

#### **6.8 Wants**

**Life goals.** The goal table lists every goal with a base score, trait weights, channel weights, and a base deadline age. Examples include marriage, children, home ownership, financial target, career peak, creative work, and leaving the current city.

score \= base \+ Σ trait weight × trait \+ Σ channel weight × channel sum

The generator picks \[2 to 4\] goals by Gumbel-top-k sampling on the scores. Each goal gets a deadline:

deadline \= base deadline \+ local norm offset \+ N(0, 2\)

A deadline earlier than the current age marks the goal as missed. Section 6.9 reads missed goals as regret candidates.

Channel weights carry the history effects. Scarcity raises security goals. Constraint raises freedom goals. A pawn from an unstable home tends to want security, and a pawn who married young tends to want freedom.

**Partner preferences.** Stated and revealed preferences share one schema. Each attribute holds a target and an importance from 0 to 1\.

| Attribute | Target type |
| ----- | ----- |
| age | Range |
| income | Minimum |
| education | Minimum |
| looks | Minimum |
| political distance | Maximum |
| religion match | Boolean |
| wants children | Enum |
| monogamy view | Enum set |
| kink overlap | Minimum |
| attachment style | Enum weights |

* **Stated preferences** come from beliefs, local norms, and goals. They describe what the pawn says it wants.  
* **Revealed preferences** start from stated preferences and move toward a history pull. The history pull averages the attributes of past partners in relationship history, weighted by duration. Attachment adds a pull of its own. Anxious pawns lean toward avoidant partners, with a weight of \[a\].

revealed \= stated \+ (1 − sigmoid(self\_insight)) × (history\_pull − stated)

A pawn with low self-insight shows a wide gap between what it says and what it picks.

**Dealbreakers.** Each dealbreaker is a rule.

yaml

* attribute: monogamy\_view  
* operator: not\_in  
* value: \[open, prefers\_non\_monogamy\]  
* hardness: 0.9  
  source: beliefs.monogamy\_view

The generator builds dealbreakers from these sources:

| Source | Rule | Condition |
| ----- | ----- | ----- |
| Religion | religion match required | religiosity ≥ \[0.7\] |
| Monogamy view | partner view in allowed set | view is strict or prefers\_non\_monogamy |
| Children goal | partner child wish matches | goal score ≥ \[s\] |
| Kink | partner has the kink | intensity ≥ \[0.8\] |
| History | partner has no infidelity | history holds a cheated-on entry |

Hardness at or above \[h\] ends a date when the attribute is revealed. Lower hardness subtracts a penalty from attraction.

#### **6.9 Inner life**

**Impact score.** Each history entry gets an impact score.

impact \= Σ |channel deltas| × stage weight × valence factor

Negative entries use a valence factor of 1.0. Positive entries use 0.6 (estimate).

| Field | Rule |
| ----- | ----- |
| Core wound | The generator picks the negative entry with the highest impact. Ties go to the earlier age. If no entry reaches \[τ\], the pawn has no core wound and the next three fields stay empty. |
| Private belief | The wound’s interpretation id maps to a belief id in the belief table, such as “I have to earn love.” |
| Want | The belief table entry lists what the wound makes the pawn chase. The generator adds this as a bonus of \[b\] to the matching partner preference. |
| Need | The belief table entry lists what would heal the wound. The text layer uses it. The attraction engine uses it to score partners who meet the need. |
| Secret | Candidates are entries flagged hideable in the event table, plus kinks with disclosure below \[0.2\]. Weight \= stigma × (0.5 \+ sigmoid(threat sensitivity)). The generator draws 0 to \[2\] secrets. Each secret stores a discovery difficulty from 0 to 1\. |
| Regret | For each resolved fork, doubt \= adult utility of the best unchosen option − adult utility of the chosen option. Adult utility reruns the fork’s utility function with adult traits and the final state. Missed goals also enter as candidates. The generator picks the highest doubt above \[r\]. |
| Coping habit | The coping table scores each habit by traits, channels, and era availability. A parent with a matching habit tag in household history adds \[m\]. The generator draws one primary habit and, with probability \[q\], one secondary habit. Each habit stores a health flag. |

A pawn with high reward drive and a drinking parent leans toward drinking, because both the trait weight and the parent bonus raise the drinking score.

Each inner life field stores the ids of the history entries it came from.

#### **6.10 Output record**

yaml

* personality:  
*   neuroticism: float  
*   extraversion: float  
*   novelty\_seeking: float  
*   conscientiousness: float  
*   agreeableness: float  
*   self\_insight: float  
* attachment:  
*   anxiety: float  
*   avoidance: float  
*   style: enum  
* beliefs: { see 6.6 }  
* sexuality:  
*   libido: float  
*   kinks: \[ { id, intensity, disclosure } \]  
* wants:  
*   goals: \[ { id, score, deadline, missed } \]  
*   stated\_prefs: { attribute: { target, importance } }  
*   revealed\_prefs: { attribute: { target, importance } }  
*   dealbreakers: \[ { attribute, operator, value, hardness, source } \]  
* inner\_life:  
*   core\_wound: { entry\_id, impact } | null  
*   private\_belief: belief\_id | null  
*   want: id | null  
*   need: id | null  
*   secrets: \[ { entry\_id, discovery\_difficulty } \]  
*   regret: { entry\_id, doubt } | null  
    coping: \[ { id, healthy } \]

  #### **6.11 Data files**

* **Channel table.** This file holds channel ids and stage weights.  
* **Personality table.** This file holds base weights, channel weights, and σ for each adult trait.  
* **Attachment table.** This file holds axis coefficients, the earned security rule, and the cutoff.  
* **Libido table.** This file holds the age curve by age and sex.  
* **Kink table.** This file holds prevalence, weights, history tags, and stigma keys.  
* **Goal table.** This file holds base scores, weights, and base deadlines.  
* **Belief table.** This file maps interpretation ids to belief, want, and need ids.  
* **Coping table.** This file holds habit weights, health flags, and era availability.

  #### **6.12 Invariants**

The validator rejects any profile that breaks these rules.

* Every value sits inside its declared scale.  
* The attachment style matches the axis scores and the cutoff.  
* Every entry id in inner life exists in the history.  
* A deadline earlier than the current age has missed set to true.  
* Stated and revealed preferences hold the same attribute set.  
* No two dealbreakers contradict each other.  
* A pawn with no core wound has empty private belief, want, and need fields.

  #### **6.13 Testing**

* **Population tests.** The suite generates \[N\] pawns per era and location. Each adult trait has a mean within \[0 ± e\] and a standard deviation within \[1 ± e\]. Attachment style shares fall within \[target\] ± \[tolerance\]. Kink prevalence falls within \[tolerance\] of the table value.  
* **Sensitivity tests.** Raising one base trait by 1 while holding the seed fixed moves its adult trait by \[min\] to \[max\]. A run with history channels set to zero yields adult traits that match base traits within σ.  
* **Golden seeds.** A fixed set of seeds must reproduce stored profiles exactly.  
* **Performance.** One profile computes in under \[N\] ms.

### **9\. Presentation and starting state**

The last step sets how the pawn meets the world. Honesty level comes from temperament and history. Each field in beliefs, sexuality, wants, and inner life gets a disclosure threshold tied to trust. The starting state sets current mood, loneliness, and dating options.

### **Output**

The finished pawn is a record with two parts. The history log holds every event with its age and reading. The stat sheet holds every derived value. The game's prose templates read both, so a line of dialogue can refer to the pawn's past as easily as to its current mood.

