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
security &lt;= 0.4, except in the Pochven region where cyno fields cannot be lit at all;
jump-bridge landings are exempt from these restrictions.

#### Scenario: High-security system rejects cyno-based landing
- GIVEN a destination system with security above 0.4
- WHEN checking if it's a valid landing system for a cyno-based jump method
- THEN it is rejected

#### Scenario: Pochven rejects cyno-based landing despite null-sec security
- GIVEN a destination system in the Pochven region with security at or below 0.4
- WHEN checking if it's a valid landing system for a standard or covert cyno jump, or when
  computing jump-range reachability
- THEN it is rejected and not highlighted as reachable

#### Scenario: Jump bridge ignores the security restriction
- GIVEN a destination system with security above 0.4
- WHEN checking if it's a valid landing system for the JumpBridge method
- THEN it is accepted

## Requirement: ESI-authenticated pilot skills
Jump-relevant pilot skill levels (Jump Drive Calibration, Jump Fuel Conservation, Jump
Freighters, Capital Ships, Black Ops) SHALL be fetched from the signed-in character's ESI skill
sheet (`GET /characters/{id}/skills/`), mapped by the known static EVE skill type IDs, rather
than manually entered. When no authenticated character is selected as the active pilot, the
system SHALL assume all jump-relevant skills are trained to level 5 (maximum range and minimum
fuel) for route and jump-range calculations.

#### Scenario: No pilot selected uses max skill levels
- GIVEN no character is selected in the main pilot picker
- WHEN the user builds a route or views jump range
- THEN Jump Drive Calibration, Jump Fuel Conservation, Jump Freighters, Capital Ships and Black
  Ops are all treated as level 5

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
- GIVEN the "online" toggle is enabled for a signed-in character and the Focus checkbox is
  unchecked
- WHEN that character's ESI-reported solar system changes between polls
- THEN the map's jump-range highlight recenters on the newly reported system on the next poll

#### Scenario: Focus keeps jump-range anchored during live tracking
- GIVEN the "online" toggle is enabled and the Focus checkbox is checked with a jump-range
  overlay anchored to system A
- WHEN the tracked pilot's ESI-reported system changes to system B
- THEN the pilot beacon moves to system B but the jump-range overlay remains anchored to system A

#### Scenario: Switching pilots while tracking retargets polling
- GIVEN the "online" toggle is enabled and tracking character A
- WHEN the user selects character B in the pilot picker
- THEN subsequent location polls query character B instead of character A

#### Scenario: Enabling tracking immediately selects the pilot's system
- GIVEN a signed-in character with a previously known solar system (or none yet)
- WHEN the "online" toggle is turned on
- THEN the map immediately selects that character's system (using the cached location if one is
  known) and rebuilds the jump-range highlight for it, the same as if the user had clicked that
  system directly, rather than waiting for the next scheduled poll

#### Scenario: Polling failures are surfaced, not silently dropped
- GIVEN the "online" toggle is enabled
- WHEN a location poll fails (e.g. an expired token or a token missing the location scope)
- THEN the failure is shown next to the toggle instead of being silently ignored, so a pilot whose
  location stops updating has a visible reason instead of an unexplained stale jump-range overlay

## Requirement: Crosshair button centers the map on the main profile
The map toolbar SHALL provide a crosshair button between the "online" toggle and the Focus
checkbox. When clicked, the main map SHALL pan (without changing zoom) to center on the active
main profile's last known solar system.

#### Scenario: Centering on a profile with a known location
- GIVEN a main profile is selected and has a last-known solar system
- WHEN the user clicks the crosshair button
- THEN the main map pans so that system is centered at the current zoom level

#### Scenario: Centering without a known location shows feedback
- GIVEN no main profile is selected or the profile has no last-known system yet
- WHEN the user clicks the crosshair button
- THEN the map view does not move and a short status message explains why

## Requirement: Main profile auto-selects jump-range ship class from ESI
When the user selects a different main profile (or signs in and that character becomes the main
profile), the system SHALL query the character's current ship from ESI (`GET
/characters/{id}/ship/`) once and set the map toolbar "Тип корабля" jump-range selector to the
matching capital ship class when the hull is jump-capable. If the pilot is in a capsule or any
hull that is not a seeded jump-capable capital, the selector SHALL remain (or reset to) Black
Ops. This auto-selection SHALL happen only at profile change; the user MAY override the ship
class manually afterward until the main profile changes again.

#### Scenario: Switching to a pilot in a carrier selects Carrier jump range
- GIVEN the main profile is changed to a signed-in character currently flying a carrier hull
  recognized by the SDE ship catalog
- WHEN ESI reports that character's ship type
- THEN the jump-range ship-class selector shows Carrier, the route-panel ship picker shows the
  matching hull when known, and the main map jump-range overlay uses Carrier range

#### Scenario: Restored main profile auto-detects ship on launch
- GIVEN the app restarts with a previously selected main profile still active in the pilot picker
- WHEN startup finishes loading characters and the SDE ship catalog
- THEN the app queries ESI once for that pilot's current ship and applies the jump-range and
  route ship selectors the same as if the user had just picked that profile manually

#### Scenario: Switching to a pilot in a capsule keeps Black Ops
- GIVEN the main profile is changed to a signed-in character currently in a capsule
- WHEN ESI reports the capsule ship type
- THEN the jump-range ship-class selector shows Black Ops (the default)

#### Scenario: Manual ship-class override survives until the next profile change
- GIVEN the user changed the main profile and the jump-range selector auto-selected Carrier
- WHEN the user manually changes the selector to Titan and does not change the main profile again
- THEN subsequent jump-range calculations keep using Titan until another main-profile selection

#### Scenario: Missing ship-type scope prompts re-sign-in
- GIVEN a signed-in character whose stored ESI scopes do not include ship-type read access
- WHEN the user selects that character as the main profile
- THEN the app shows a short status message asking the user to sign in again instead of failing
  silently, and the jump-range selector is left unchanged until a successful ship lookup


## Requirement: Live cyno pilot location tracking
The map toolbar SHALL offer a "Cyno Profile" multi-select dropdown listing signed-in characters.
The user MAY select zero, one, or many characters at once. When one or more characters are
selected, the system SHALL periodically poll each selected character's current solar system from
ESI and update blue cyno beacons on the map automatically, independent of the main pilot's
"online" jump-range toggle. The selected cyno profiles SHALL be persisted across app restarts.

#### Scenario: Selecting cyno profiles starts location polling for each
- GIVEN at least two signed-in characters and ESI is configured
- WHEN the user selects both characters in the Cyno Profile dropdown
- THEN the map immediately shows a blue cyno beacon for each character's last known system (if
  any) and begins polling ESI for live location updates for every selected character

#### Scenario: Clearing all cyno profiles removes the beacons
- GIVEN one or more cyno profiles are selected and their beacons are visible
- WHEN the user clears every checkbox in the Cyno Profile dropdown
- THEN all blue cyno beacons are removed from the map and location polling stops

#### Scenario: Cyno profile selection persists across restarts
- GIVEN two cyno profiles were selected during a previous session
- WHEN the app is launched again
- THEN those same characters are pre-selected in the Cyno Profile dropdown and tracking resumes
  for both

## Requirement: Live SC pilot location tracking
The map toolbar SHALL offer a "SC Profile" multi-select dropdown listing signed-in characters,
with the same selection, persistence, and ESI polling behaviour as the Cyno Profile dropdown.
SC and Cyno selections are independent — the same character MAY appear in both lists at once.

#### Scenario: Selecting SC profiles starts location polling for each
- GIVEN at least two signed-in characters and ESI is configured
- WHEN the user selects both characters in the SC Profile dropdown
- THEN the map immediately shows an SC beacon for each character's last known system (if any)
  and begins polling ESI for live location updates for every selected character

#### Scenario: Clearing all SC profiles removes the beacons
- GIVEN one or more SC profiles are selected and their beacons are visible
- WHEN the user clears every checkbox in the SC Profile dropdown
- THEN all SC beacons are removed from the map and location polling for those profiles stops

#### Scenario: SC profile selection persists across restarts
- GIVEN two SC profiles were selected during a previous session
- WHEN the app is launched again
- THEN those same characters are pre-selected in the SC Profile dropdown and tracking resumes
  for both
