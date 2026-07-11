# routing

Pathfinding over the universe graph: pure stargate routes, pure capital jump-drive chains,
jump-bridge chains, and the hybrid combination of gate + jump legs.

## Requirement: Route planning controls in the right sidebar
Route planning inputs (From/To, intermediate waypoints, mode, gate preference, ship class, hull,
jump method, Build route, and the route steps list) SHALL share a single bordered right sidebar
column with the Jump Range mini-map (mini-map on top, route controls below).

#### Scenario: Route controls are in the right sidebar with the mini-map
- GIVEN the main window is open
- WHEN the user looks for route planning controls
- THEN they appear below the mini-map inside the same right-hand bordered column, not under the
  main map or overlaid on it

## Requirement: Intermediate route waypoints
Route planning SHALL let the user insert an ordered list of intermediate waypoints between the
origin and destination. The computed route SHALL be the concatenation of per-leg routes
(origin → waypoint 1 → … → waypoint N → destination), each leg found with the currently selected
routing mode, preference, ship, and jump method. Waypoints SHALL be addable from the sidebar
("Add waypoint") and from a map system's right-click menu, individually
removable, and cleared together with the rest of the route by "Сбросить маршрут". Each waypoint
SHALL be marked on the map (ordered П1, П2, … markers) distinctly from the ОТ/ДО endpoints.

#### Scenario: Waypoint sets jump-range origin for planning the next leg
- GIVEN the origin system is set
- WHEN the user adds a waypoint from the map context menu
- THEN the jump-range overlay and mini-map re-center on that waypoint system so the next
  leg's jump range is visible

#### Scenario: Route to a waypoint is drawn before destination is set
- GIVEN only the origin system is set
- WHEN the user adds a waypoint from the map context menu
- THEN a route from the origin to that waypoint is drawn on the map even though the
  destination field is still empty

#### Scenario: Route passes through an added waypoint in order
- GIVEN From and To systems are set and one intermediate waypoint between them is added
- WHEN a route is built
- THEN the returned route reaches the waypoint before continuing to the destination, and the
  waypoint appears as a distinct ordered marker on the map

#### Scenario: A leg with no valid route reports failure
- GIVEN an intermediate waypoint for which no route of the selected mode exists to or from an
  adjacent chain point
- WHEN a route is built
- THEN no route steps are shown and the failure identifies the unreachable leg

#### Scenario: Empty waypoint fields are ignored
- GIVEN one or more waypoint rows left blank
- WHEN a route is built
- THEN the blank rows are skipped and the route is computed through only the filled-in waypoints

#### Scenario: Route changes preserve the main-map viewport
- GIVEN the main map is panned and zoomed to a chosen view
- WHEN the user sets "Маршрут от", "Маршрут до", adds a waypoint, or rebuilds the route
- THEN the route overlay updates without panning or zooming the main map

## Requirement: Route origin drives the active jump-range selection
Choosing a system as "Маршрут от" from the map SHALL select that system and make it the active
jump-range origin so its jump range is immediately visible on the main map and mini-map.

#### Scenario: Route origin is selected on the map
- GIVEN the user opens a system's map context menu
- WHEN the user chooses "Маршрут отсюда"
- THEN that system is highlighted as selected and both jump-range views use it as their origin

## Requirement: Clear active route from the sidebar
The route planning panel SHALL offer a "Сбросить маршрут" control that clears the From/To fields,
the route steps list, the route summary, and all route overlays (endpoints and gate/jump legs)
from the main map without changing other map state (jump-range overlay, pilot/cyno/SC beacons,
selection).

#### Scenario: Reset route removes map overlay and form fields
- GIVEN an active route is shown on the map with From/To filled in
- WHEN the user clicks "Сбросить маршрут"
- THEN the route lines and ОТ/ДО markers disappear, the From/To boxes and route list are cleared,
  and jump-range or location beacons remain unchanged

## Requirement: Shortest-path gate routing with security preference
Gate routing SHALL find a shortest path over the stargate graph using Dijkstra's algorithm,
with a selectable preference (Shorter / Safer / LessSecure) that biases edge cost by
destination system security without forcing large detours at equal hop count.

#### Scenario: Shorter preference ignores security
- GIVEN two systems connected by multiple gate paths of different security profile
- WHEN routing with the Shorter preference
- THEN the path with the fewest total jumps is returned regardless of the security of systems
  crossed

#### Scenario: Safer preference favors higher security at equal hop count
- GIVEN two equal-length gate paths between the same two systems, one through lower-security
  systems than the other
- WHEN routing with the Safer preference
- THEN the higher-security path is preferred

## Requirement: Hard avoidance filters
Gate and jump routing SHALL support hard avoidance of specific system IDs and, optionally, all
low-sec or all null-sec systems, excluding them from the route entirely except when they are
the origin or destination.

#### Scenario: Avoided system is never routed through
- GIVEN a system ID in the avoid list that lies on the only otherwise-shortest gate path
- WHEN a route is requested between two systems on either side of it
- THEN the returned route detours around the avoided system, or no route is returned if none
  exists

#### Scenario: Fallback when avoidance blocks every route
- GIVEN avoidance filters that block every possible path between origin and destination
- WHEN `AllowFallbackIfBlocked` is enabled
- THEN an unrestricted route (ignoring the filters) is returned instead of no route

## Requirement: Capital jump-drive pathfinding
Jump routing SHALL find a minimum-hop chain of jump-drive legs within the ship/skill jump
range, breaking ties by minimum total light-year distance, and SHALL reject any landing system
that isn't a valid landing spot for the chosen jump method (e.g. cyno fields require true
security &lt;= 0.4 and exclude Pochven; jump bridges are exempt) or that is cyno-jammed (for
cyno-based jumps, unless it is the final destination).

#### Scenario: Landing system must allow the jump method
- GIVEN a candidate landing system with security above 0.4
- WHEN jump routing via a standard or covert cyno
- THEN that system is never chosen as an intermediate landing point

#### Scenario: Cyno-jammed system is skipped except as final destination
- GIVEN an intermediate candidate system flagged as cyno-jammed
- WHEN jump routing via Cyno
- THEN that system is excluded as an intermediate hop, but still usable as the final
  destination

## Requirement: Jump-bridge (Ansiblex) pathfinding
The system SHALL be able to route purely across the player-entered jump-bridge network (paired
Ansiblex/custom jump-bridge structures), independent of gate or cyno-jump routing.

#### Scenario: Route uses only jump-bridge links
- GIVEN a chain of linked jump-bridge structures connecting origin to destination
- WHEN jump-bridge routing is requested
- THEN the returned route consists solely of jump-bridge legs between the linked systems

## Requirement: Hybrid gate + jump routing
The system SHALL evaluate pure-gate, pure-jump, pure-jump-bridge, and gate-then-jump hybrid
candidate routes between two systems, and return whichever has the fewest total steps (gate
jumps + capital jumps combined).

#### Scenario: Hybrid route beats either pure strategy
- GIVEN a destination unreachable in one jump-drive hop but reachable by gating partway and
  then jumping the remaining distance in fewer total steps than a pure-gate or pure-jump route
- WHEN hybrid routing is requested
- THEN the gate-then-jump combination is returned

#### Scenario: No viable route
- GIVEN no gate path, jump chain, or jump-bridge chain connects origin and destination within
  configured limits
- WHEN hybrid routing is requested
- THEN no route is returned

## Requirement: Optional per-system activity penalty
Route cost SHALL support an injectable per-system penalty function (e.g. derived from recent
kill activity), added on top of the base edge cost when routing through that system, without
being a hard block.

#### Scenario: High-activity system is avoided when a similar-length alternative exists
- GIVEN a system-penalty function that heavily penalizes a system on the shortest path, and an
  alternative path only slightly longer
- WHEN gate routing runs with that penalty configured
- THEN the alternative, lower-penalty path is preferred
