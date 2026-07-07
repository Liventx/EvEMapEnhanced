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
