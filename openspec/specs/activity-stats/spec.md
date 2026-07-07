# activity-stats

Recent-activity statistics for solar systems, sourced from zKillboard/ESI, used for the on-demand
stats panel, routing penalties, and (for NPC kills specifically) map plate coloring.

## Requirement: On-demand PvP kill statistics
The system SHALL compute, for a given solar system on request, kills in the last hour, kills in
the last 24 hours, an estimate of capital and pod kills within the last 24 hours (from
hydrating a bounded number of the most recent killmails), and total ISK destroyed in the last 24
hours, sourced from zKillboard's per-system feed and ESI killmail detail.

#### Scenario: Stats query returns a populated result
- GIVEN a valid solar system and network access to zKillboard/ESI
- WHEN a stats query is run for that system
- THEN the result includes non-negative kill counts for the last hour and last 24 hours and a
  computed activity score

#### Scenario: Capital/pod kill counts are bounded by the hydration limit
- GIVEN a system with more than the configured hydration limit of kills in the last 24 hours
- WHEN stats are computed for that system
- THEN only the most recent kills up to the hydration limit are inspected for capital/pod
  classification, and the rest are not individually hydrated

## Requirement: Stats are cached locally
Computed system stats SHALL be cached locally (keyed by solar system) so the last-known values
remain available for display even without a fresh network call, and SHALL be overwritten (not
duplicated) on each successful re-query of the same system.

#### Scenario: Cached stats survive a failed refresh
- GIVEN previously cached stats for a system and a subsequent stats query that fails (e.g.
  offline)
- WHEN the UI displays that system's stats
- THEN the last successfully cached values are shown instead of an error with no data

## Requirement: Activity score
A system's activity score SHALL weight capital kills and pod kills more heavily than plain kill
count, as a single number usable as a routing penalty input (see routing).

#### Scenario: Capital-heavy activity scores higher than an equal-count pod/ship mix
- GIVEN two systems with the same total 24h kill count, one with more capital kills than the
  other
- WHEN activity scores are compared
- THEN the system with more capital kills has the higher activity score

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
