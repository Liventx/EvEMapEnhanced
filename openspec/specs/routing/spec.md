# routing

Pathfinding over the universe graph: pure stargate routes, pure capital jump-drive chains,
jump-bridge chains, and the hybrid combination of gate + jump legs.

## Requirement: Route planning controls in the right sidebar
Route planning inputs (From/To, mode, gate preference, ship class, Build route, clear-route,
and the route steps list) SHALL share a single bordered right sidebar column with the Jump
Range mini-map (mini-map on top, route controls below). Intermediate waypoints are map-only
and SHALL NOT appear as fields in the sidebar. Jump method (Cyno / Covert cyno) SHALL be
configured from «Карта» → «Настройки маршрута», not the sidebar.

#### Scenario: Route controls are in the right sidebar with the mini-map
- GIVEN the main window is open
- WHEN the user looks for route planning controls
- THEN they appear below the mini-map inside the same right-hand bordered column, not under the
  main map or overlaid on it

## Requirement: Subcapital ship class defaults to gate-only routing
The ship-class selector SHALL include a «Subcapital» entry as the default selection for
sub-capital route planning. When Subcapital is selected, routing mode SHALL be Gates only,
the mode selector SHALL be disabled, and jump/hybrid routing SHALL NOT be offered until a
capital class is chosen.

#### Scenario: Subcapital locks routing mode to gates
- GIVEN the ship class is Subcapital
- WHEN the user opens the route planning panel
- THEN «Тип маршрута» shows Gates only and cannot be changed to Jump only or Hybrid

#### Scenario: Capital class restores routing mode choice
- GIVEN Subcapital was selected with a locked Gates-only mode
- WHEN the user selects a capital ship class such as Carrier
- THEN the routing mode selector becomes enabled again

## Requirement: Route summary beside the route header
The gate/jump totals («Итого: …») SHALL appear on the same row as the «Маршрут» heading,
right-aligned, rather than in a separate row below the step list.

#### Scenario: Built route shows totals on the header row
- GIVEN a route has been built with gate and jump legs
- WHEN the route steps are displayed
- THEN the summary counts appear to the right of «Маршрут» on the same line

#### Scenario: Wormhole and Zarzakh hops are highlighted in the step list
- GIVEN a built route includes a wormhole hop or a gate hop touching Zarzakh
- WHEN the route step list is shown
- THEN those rows use a light-brown background distinct from ordinary gate hops

#### Scenario: Route step list fills the remaining sidebar height
- GIVEN a route with more steps than fit on screen
- WHEN the route step list is shown
- THEN the list occupies the remaining space below the route controls and scrolls internally

## Requirement: Intermediate route waypoints
Route planning SHALL let the user insert an ordered list of intermediate waypoints between the
origin and destination. The computed route SHALL be the concatenation of per-leg routes
(origin → waypoint 1 → … → waypoint N → destination), each leg found with the currently selected
routing mode, preference, ship class, and jump method. Waypoints SHALL be addable only from a
map system's right-click menu (not listed in the sidebar), individually cleared together with
the rest of the route by the clear-route control beside «Построить маршрут». Each waypoint
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
The route planning panel SHALL offer a clear-route control (cross icon) on the same row as
«Построить маршрут» that clears the From/To fields, the route steps list, the route summary,
and all route overlays (endpoints and gate/jump legs) from the main map without changing other
map state (jump-range overlay, pilot/cyno/SC beacons, selection).

#### Scenario: Reset route removes map overlay and form fields
- GIVEN an active route is shown on the map with From/To filled in
- WHEN the user clicks the clear-route control beside «Построить маршрут»
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

#### Scenario: Fallback when security avoidance blocks every route
- GIVEN only low-sec/null-sec avoidance filters block every possible path between origin and
  destination
- WHEN `AllowFallbackIfBlocked` is enabled
- THEN an unrestricted route (ignoring security filters only) is returned instead of no route

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

## Requirement: Optional wormhole shortcuts in gate routing
When the user enables «Использовать Wormhole» in «Карта» → «Настройки маршрута», gate routing and
hybrid routing gate legs SHALL treat active wormhole connections as additional one-hop edges:
EvE-Scout Turnur connections between the hub and remote system, EvE-Scout Thera connections as
mutual shortcuts among all current Thera remote exits, and manual markers with a resolved exit
system id. Wormhole hops SHALL appear in the route step list and on the map with distinct
styling. The routing preference SHALL persist across sessions and default to off. Wormhole
display on the map remains controlled separately from the Map menu «Wormhole» submenu.

#### Scenario: Wormhole routing toggle lives in route settings
- GIVEN the main window is open
- WHEN the user opens «Карта» → «Настройки маршрута»
- THEN «Использовать Wormhole» is available there, not under the Wormhole display submenu

#### Scenario: Turnur wormhole shortens a gate route
- GIVEN an active EvE-Scout Turnur connection between systems A and B and wormhole routing is
  enabled
- WHEN the user builds a gate route from A to B
- THEN the route may use a single wormhole hop instead of a longer gate path when that is shorter

#### Scenario: Manual wormhole with exit system is used in routing
- GIVEN a manual wormhole marker from system A to system B with a saved exit system id and
  wormhole routing is enabled
- WHEN the user builds a gate route that can benefit from the A↔B shortcut
- THEN the computed route may include a wormhole hop between A and B

## Requirement: Optional Zarzakh hub in gate routing
When «Использовать Zarzakh» in «Карта» → «Настройки маршрута» is enabled (the default), gate and
hybrid routing MAY route through the Zarzakh solar system as an ordinary stargate hop. When
disabled, Zarzakh SHALL be excluded as an intermediate system (origin and destination in Zarzakh
remain allowed).

#### Scenario: Zarzakh routing defaults on
- GIVEN a fresh install or no saved preference
- WHEN the user opens «Карта» → «Настройки маршрута»
- THEN «Использовать Zarzakh» is checked

#### Scenario: Disabling Zarzakh avoids the hub
- GIVEN «Использовать Zarzakh» is unchecked and the shortest gate path otherwise passes through
  Zarzakh
- WHEN a route is built
- THEN the returned route detours around Zarzakh or reports no route if none exists without it
