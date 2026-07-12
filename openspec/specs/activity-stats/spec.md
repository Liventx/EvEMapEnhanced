# activity-stats

Recent NPC-kill activity for solar systems, sourced from ESI's bulk system-kills feed, used for
Schematic map plate coloring and the plate's NPC-kill count label.

## Requirement: Bulk NPC-kill snapshot for map coloring
The system SHALL periodically fetch a last-hour NPC-kill count for every solar system in one
bulk request (ESI's system-kills feed), independent of the on-demand PvP stats query, and make
it available for the map's Schematic plate coloring (see map-rendering) without blocking map
interaction while the fetch is in flight.

#### Scenario: Initial fetch populates plate colors
- GIVEN the app has just started and NPC-kill data hasn't been fetched yet
- WHEN the bulk fetch completes successfully
- THEN the map's Schematic plates refresh to reflect the fetched NPC-kill counts

#### Scenario: Fetch failure keeps prior data
- GIVEN a previously successful NPC-kill fetch and a subsequent fetch that fails
- WHEN the periodic refresh runs
- THEN the previously fetched NPC-kill data is kept and used for plate coloring instead of
  being cleared

## Requirement: Jump-range PvP activity from zKillboard
For solar systems within the active jump range (reachable neighbors and the anchored origin
system), the system SHALL query zKillboard for killmails
in the last hour using a per-region feed (`regionID/{id}`, paginated; player and NPC kills),
classifying each jump-range system from kills in that regional slice, respecting zKillboard's
client rate limit, caching per-region results for a few minutes, and accepting that busy nullsec
regions may be under-counted compared with per-system queries. Capsule, shuttle and corvette
victims are excluded from player-death counts. A system with five or more valid player
deaths in the last hour SHALL receive a red highlight overlay; a system with one to four valid
player deaths in the last hour SHALL receive a yellow highlight overlay; a system with an NPC
dreadnought or titan kill in the last thirty minutes (victim or attacker hull) SHALL receive a
purple highlight overlay that takes priority over red and yellow. The UI SHALL show
zKillboard fetch progress in the right sidebar (above the Jump Range mini-map) with an
estimated time remaining while queries are in flight and SHALL not cancel an in-flight regional
batch when jump-range reachability flickers (e.g. live pilot tracking). The user SHALL be able
to choose between a polite pacing mode (~1 request/second, serial) and a faster mode (two
parallel requests, ~2/second) from the map menu, with the choice persisted across sessions.
The user SHALL also be able to switch to a global nullsec scope that queries zKillboard for
every nullsec region on the map and applies the same highlight rules to all nullsec systems,
independent of jump range.

#### Scenario: Global nullsec scope queries all nullsec regions
- GIVEN the user selects the global nullsec zKillboard scope and the SDE map is loaded
- WHEN a zKillboard refresh runs
- THEN the app queries each distinct nullsec region (paginated regional feed) and classifies
  every nullsec solar system on the map from those feeds

#### Scenario: Global scope highlights nullsec systems with activity
- GIVEN the global nullsec zKillboard scope is active and a nullsec system has qualifying
  recent activity in the regional feed
- WHEN the map is rendered
- THEN that system shows the appropriate red, yellow or purple highlight even when it is
  outside the active jump range

#### Scenario: Hot system gets red highlight in jump range
- GIVEN a jump-range overlay is active and zKillboard's regional feed includes five valid
  player deaths in the last hour for a reachable system
- WHEN the map is rendered
- THEN that system shows a red highlight outline in addition to the jump-range ring

#### Scenario: Moderate activity gets yellow highlight
- GIVEN a jump-range overlay is active and a reachable system has one to four valid player
  deaths in the last hour in the regional feed
- WHEN the map is rendered
- THEN that system shows a yellow highlight outline

#### Scenario: NPC capital activity gets purple highlight
- GIVEN a jump-range overlay is active and a reachable system has an NPC dreadnought or titan
  kill in the last thirty minutes in the regional feed (victim or attacker hull)
- WHEN the map is rendered
- THEN that system shows a purple highlight outline, taking priority over red or yellow

#### Scenario: Context menu opens zKillboard for any system
- GIVEN the user right-clicks a solar system on the map
- WHEN they choose the zKillboard menu item
- THEN the app's default browser opens that system's zKillboard page

## Requirement: Jump Range mini-map excludes zKillboard overlays
The Jump Range mini-map SHALL NOT draw red, yellow or purple zKillboard activity highlights;
those overlays are shown on the main map only.

#### Scenario: Mini-map shows jump range without PvP highlights
- GIVEN zKillboard data has classified systems with recent activity in the active scope
- WHEN the Jump Range mini-map is rendered
- THEN no red, yellow or purple zKillboard highlight rings are drawn on the mini-map

#### Scenario: Capsule and NPC kills are ignored for player PvP tiers
- GIVEN zKillboard killmails in the last hour include only capsule, shuttle, corvette or NPC
  victims for a system (with no NPC dreadnought or titan involvement in the last thirty minutes)
- WHEN PvP activity is classified for that system
- THEN no red or yellow highlight is applied

#### Scenario: Periodic refresh keeps prior highlights until each system is reclassified
- GIVEN zKillboard overlays are already showing red, yellow or purple highlights on the main map
- WHEN a periodic zKillboard refresh starts because regional cache entries expired
- THEN existing highlights stay visible on the map and a system's highlight level changes only
  after that system's region has been fetched and the system is reclassified from the new data

## Requirement: Sansha incursion systems highlighted on the main map
The system SHALL periodically fetch ESI's public incursions feed and mark every solar system
infested by an active Sansha Nation incursion with a muted salad-green glow on the main map.
When the map zoom is above 5.00 the glow SHALL breathe softly (no marching dashed border). At
overview zoom (5.00 and below) the highlight SHALL remain static with no animation. Fetch failures
SHALL keep the last-known incursion snapshot instead of clearing highlights.

#### Scenario: Infested system shows salad-green incursion highlight
- GIVEN ESI reports a Sansha Nation incursion infesting solar system A
- WHEN the main map is rendered at zoom above 5.00
- THEN system A shows a soft breathing salad-green glow on its plate or marker

#### Scenario: Incursion glow is static at overview zoom
- GIVEN ESI reports a Sansha Nation incursion infesting solar system A
- WHEN the main map is rendered at zoom 5.00 or below
- THEN system A shows a faint static salad-green halo with no animation

#### Scenario: Incursion fetch failure keeps prior highlights
- GIVEN a previously successful incursion fetch marked system A as infested and a subsequent
  fetch fails
- WHEN the periodic refresh runs
- THEN system A continues to show the salad-green incursion highlight

#### Scenario: Jump Range mini-map excludes incursion highlights
- GIVEN ESI reports Sansha incursions on the map
- WHEN the Jump Range mini-map is rendered
- THEN no salad-green incursion highlight is drawn on the mini-map

## Requirement: Thera and Turnur wormholes from EvE-Scout
The system SHALL periodically fetch the public EvE-Scout signatures API and expose active
completed wormhole connections for Thera and Turnur on the main map (see map-rendering). The
refresh interval SHALL be ten minutes. Fetch failures SHALL keep the last-known wormhole snapshot
instead of clearing markers. The Map menu SHALL provide a "Червоточины" submenu with a checkbox
to show or hide all wormhole markers (EvE-Scout and manual) and their hover hints; the choice
SHALL persist across sessions and default to on.

#### Scenario: Initial fetch populates wormhole markers
- GIVEN the app has just started and EvE-Scout data has not been fetched yet
- WHEN the first EvE-Scout fetch completes successfully
- THEN the main map refreshes to show Thera/Turnur wormhole markers and hover hints

#### Scenario: Wormhole fetch failure keeps prior data
- GIVEN a previously successful EvE-Scout fetch and a subsequent fetch that fails
- WHEN the periodic refresh runs
- THEN the previously fetched wormhole connections remain on the map

#### Scenario: Jump Range mini-map excludes wormhole markers
- GIVEN EvE-Scout reports active Thera or Turnur wormholes
- WHEN the Jump Range mini-map is rendered
- THEN no Thera/Turnur wormhole ripple markers are drawn on the mini-map

#### Scenario: Wormhole marker toggle persists
- GIVEN the user disables wormhole display in the Map menu "Червоточины" submenu
- WHEN the app is restarted
- THEN wormhole markers remain hidden until the user re-enables display in the Map menu

## Requirement: IHUB alliance names from ESI sovereignty
The system SHALL periodically fetch ESI's public sovereignty map, resolve alliance names for
systems with player alliance occupancy, and expose them for map hover tooltips. Fetch failures
SHALL keep the last-known snapshot.

#### Scenario: Sovereignty fetch populates IHUB alliance tooltips
- GIVEN ESI's sovereignty map lists alliance 99003581 in system A
- WHEN the sovereignty refresh completes and alliance names are resolved
- THEN hovering system A on compact or full schematic plates shows only that alliance name in
  the floating hint
