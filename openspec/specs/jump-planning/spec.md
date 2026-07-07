# jump-planning

Capital ship jump-drive mechanics: range, fuel (isotope) consumption, and jump fatigue, plus the
pilot skill/ship profiles that parameterize them.

## Requirement: Jump range by hull and skills
Maximum jump range in light years SHALL be computed from the capital ship class's base range
and the pilot's relevant jump-drive skills (e.g. Jump Drive Calibration), for both a specific
`ShipHull` and a bare `CapitalShipClass` override.

#### Scenario: Higher skill level increases range
- GIVEN two pilot skill sets differing only in jump-range-affecting skill level
- WHEN maximum jump range is computed for the same hull
- THEN the higher-skill pilot has a greater or equal maximum range

## Requirement: Isotope fuel consumption formula
Fuel cost for a jump SHALL be `distanceLy * hull.BaseFuelPerLyIsotopes`, reduced by Jump Fuel
Conservation skill (10% per level), further reduced by Jump Freighters skill (10% per level,
Jump Freighter hulls only), and further reduced by any Economizer implant/skill bonus, never
going below zero.

#### Scenario: Jump Fuel Conservation reduces cost
- GIVEN a jump of a fixed distance with Jump Fuel Conservation at level 5 versus level 0
- WHEN isotope cost is computed for the same hull
- THEN the level-5 cost is 50% lower than the level-0 cost (fuel conservation only)

#### Scenario: Jump Freighters skill only applies to Jump Freighter hulls
- GIVEN a non-Jump-Freighter capital hull
- WHEN isotope cost is computed with a non-zero Jump Freighters skill level
- THEN that skill has no effect on the computed cost

## Requirement: Jump fatigue and cooldown
Jump fatigue mechanics SHALL follow CCP's documented formula: effective light years scale by a
per-ship-class, per-method (standard vs. covert) fatigue multiplier; cooldown in minutes is
`max(currentFatigue/10, 1 + effectiveLy)`; next fatigue in minutes is
`min(300, max(currentFatigue, 10) * (1 + effectiveLy))`, with fatigue capped at 300 minutes.

#### Scenario: Fatigue never exceeds the cap
- GIVEN any sequence of jumps regardless of distance or starting fatigue
- WHEN fatigue is recomputed after each jump
- THEN the resulting fatigue value never exceeds 300 minutes

#### Scenario: Covert jump method uses the covert multiplier
- GIVEN the same ship class and distance jumped via a standard cyno versus a covert cyno
- WHEN effective light years are computed for each
- THEN the covert-cyno value uses the ship class's covert fatigue multiplier, not the standard
  one

## Requirement: Cyno field placement rules
A cynosural field (standard or covert) SHALL only be considered valid in a system with true
security &lt;= 0.4; jump-bridge landings are exempt from this restriction.

#### Scenario: High-security system rejects cyno-based landing
- GIVEN a destination system with security above 0.4
- WHEN checking if it's a valid landing system for a cyno-based jump method
- THEN it is rejected

#### Scenario: Jump bridge ignores the security restriction
- GIVEN a destination system with security above 0.4
- WHEN checking if it's a valid landing system for the JumpBridge method
- THEN it is accepted

## Requirement: ESI-authenticated pilot skills
Jump-relevant pilot skill levels (Jump Drive Calibration, Jump Fuel Conservation, Jump
Freighters, Capital Ships, Black Ops) SHALL be fetched from the signed-in character's ESI skill
sheet (`GET /characters/{id}/skills/`), mapped by the known static EVE skill type IDs, rather
than manually entered. A pilot with no authenticated character selected SHALL use all-zero
skill levels as the default.

#### Scenario: Re-authenticating/refreshing updates subsequent range calculations
- GIVEN a signed-in character whose ESI skill sheet has since changed (e.g. a skill finished
  training)
- WHEN the character's skills are refreshed (on sign-in, on demand, or via the periodic refresh)
- THEN subsequent jump-range and route calculations for that character use the newly-fetched
  skill levels

## Requirement: Live "follow pilot" location tracking
When the "online" jump-range toggle is enabled, the system SHALL periodically poll the selected
authenticated character's current solar system from ESI (`GET
/characters/{id}/location/`) and recenter the map's jump-range overlay on that system
automatically, without requiring the user to manually report their location. Switching the
active character while tracking is enabled SHALL switch which character's location is polled.

#### Scenario: Location changes recenter the jump-range overlay
- GIVEN the "online" toggle is enabled for a signed-in character
- WHEN that character's ESI-reported solar system changes between polls
- THEN the map's jump-range highlight recenters on the newly reported system on the next poll

#### Scenario: Switching pilots while tracking retargets polling
- GIVEN the "online" toggle is enabled and tracking character A
- WHEN the user selects character B in the pilot picker
- THEN subsequent location polls query character B instead of character A
