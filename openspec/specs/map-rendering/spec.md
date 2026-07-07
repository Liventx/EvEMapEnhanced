# map-rendering

The interactive 2D map (`MapControl`), its two display modes, and how systems/regions are laid
out and styled on screen.

## Requirement: Two display modes
The map SHALL support a Standard mode (systems placed at their real in-game 2D projection) and
a Schematic mode (Dotlan-style layout), switchable at runtime without reloading the underlying
universe data.

#### Scenario: Switching display mode preserves the loaded map
- GIVEN a universe map is already loaded and displayed in Standard mode
- WHEN the user switches to Schematic mode
- THEN the same systems/stargates are displayed, repositioned per the Schematic layout, and the
  view re-fits to show the whole universe

## Requirement: Standard mode uses real coordinates
In Standard mode, a system's screen position SHALL be its real SDE position projected to 2D
(X, -Z in light years), independent of which region it belongs to.

#### Scenario: Two systems in different regions but physically close stay close
- GIVEN two solar systems in different regions whose real 3D positions are near each other
- WHEN the map is rendered in Standard mode
- THEN their projected 2D screen positions are also near each other

## Requirement: Schematic region placement matches Dotlan's universe map
In Schematic mode, each region SHALL be anchored at the position Dotlan's own universe overview
map places it at (extracted from Dotlan's public world database), scaled up uniformly so a
region's internal layout has room to render without excessive distortion. A region absent from
that data SHALL fall back to its real in-game centroid so it still gets a sane anchor point.

#### Scenario: Region ordering matches Dotlan's universe map
- GIVEN the set of all k-space regions
- WHEN the Schematic layout is built
- THEN each region's relative position (e.g. Delve south-west of The Forge, Cobalt Edge to the
  far east) matches Dotlan's own universe overview map's relative arrangement

#### Scenario: Unknown region still gets a position
- GIVEN a region with no entry in the bundled Dotlan region-position data
- WHEN the Schematic layout is built
- THEN that region is anchored at the average real position of its own systems instead of being
  left unpositioned

## Requirement: Schematic in-region layout matches Dotlan's region maps
Within a region, when at least 60% of that region's current systems have a known position in
the bundled Dotlan per-system position data, the Schematic layout SHALL place those systems at
their exact Dotlan coordinates (scaled/translated as a rigid group, never individually
distorted). A system missing from that data SHALL be positioned at the average of its known
gate-neighbors, or the region's own centroid if it has none. Regions below the 60% coverage
threshold SHALL fall back to a gate-driven force-directed layout for every system in that
region.

#### Scenario: Well-covered region reproduces Dotlan's layout exactly
- GIVEN a region where at least 60% of its systems have a bundled Dotlan position
- WHEN the Schematic layout is built
- THEN every covered system's position relative to the others in its region matches Dotlan's
  own region map pixel-for-pixel (up to a uniform scale/translation)

#### Scenario: Sparsely-covered region falls back cleanly
- GIVEN a region where fewer than 60% of its current systems have a bundled Dotlan position
- WHEN the Schematic layout is built
- THEN every system in that region is placed by the force-directed fallback layout instead of
  mixing Dotlan and guessed positions

## Requirement: Regions never visually overlap
After initial placement, the Schematic layout SHALL push apart any regions whose bounding boxes
still overlap (plus a fixed padding), while preserving each region's internal (Dotlan or
force-directed) arrangement unchanged.

#### Scenario: No two regions overlap in the final layout
- GIVEN the full set of routable regions
- WHEN the Schematic layout finishes building
- THEN no two regions' bounding boxes (with padding) intersect

## Requirement: Inter-region gate connections are shown
Schematic mode SHALL draw one connector line between every pair of regions that share at least
one real stargate, from region anchor to region anchor, distinct from (and drawn beneath) the
intra-region gate lines and system plates.

#### Scenario: Connected regions show a line
- GIVEN two regions with at least one stargate crossing between them
- WHEN both regions are visible in the current viewport
- THEN a single connector line is drawn between their anchor points, regardless of how many
  individual stargates cross between them

#### Scenario: Unconnected regions show no line
- GIVEN two regions with no stargate directly connecting them
- WHEN the map is rendered
- THEN no connector line is drawn between them

## Requirement: Schematic system plates
Schematic mode SHALL render each visible system as a Dotlan-style rounded-rectangle plate
(system name plus a secondary NPC-kill-count line), rather than the small dot markers used in
Standard mode, with collision avoidance that thins out overlapping plates at low zoom while
always showing pinned systems (selected, hovered, or a route endpoint/step).

#### Scenario: Pinned system is always shown
- GIVEN a system that is currently selected, hovered, or part of the active route
- WHEN the visible plate count exceeds the always-draw-all threshold
- THEN that system's plate is still drawn even if it would otherwise be thinned out

## Requirement: Plate size scales with zoom level
Schematic plate dimensions and font sizes SHALL scale with the current zoom level (clamped to a
minimum so text stays legible and a maximum so plates don't balloon when zoomed in close),
rather than using a fixed pixel size regardless of zoom.

#### Scenario: Zooming out shrinks plates
- GIVEN the Schematic map at a close zoom level
- WHEN the user zooms out to a wider view
- THEN plates are rendered smaller than they were at the closer zoom level, down to the
  configured minimum size

## Requirement: Click hit-testing matches rendered geometry
Left-click selection and right-click context-menu targeting SHALL hit-test against each system's
actually-rendered geometry: the drawn plate rectangle in Schematic mode, or a small fixed-radius
circle around the dot marker in Standard mode.

#### Scenario: Clicking a plate's edge selects that system
- GIVEN a Schematic-mode plate rendered wider than the old fixed hit-test radius
- WHEN the user clicks near the edge of that plate (but not on its center point)
- THEN that system is selected

## Requirement: Plate color reflects NPC kill activity
When last-hour NPC-kill data is available for a system, its Schematic plate fill color SHALL
follow a white-to-red gradient keyed to that kill count (white = none, through green, yellow,
orange, to red for the highest activity), matching Dotlan's "NPC Kills" map filter. A system
with no NPC-kill data available yet SHALL fall back to security-status coloring.

#### Scenario: Busy ratting system is colored red/orange
- GIVEN a system with a high last-hour NPC kill count
- WHEN its Schematic plate is drawn
- THEN its fill color is toward the orange/red end of the gradient

#### Scenario: Quiet system is colored white/green
- GIVEN a system with zero or very low last-hour NPC kills
- WHEN its Schematic plate is drawn
- THEN its fill color is white or pale green

#### Scenario: No NPC-kill data yet falls back to security color
- GIVEN NPC-kill data has not been fetched yet (e.g. app just started, offline)
- WHEN a Schematic plate is drawn
- THEN its fill color uses the security-status palette instead

## Requirement: Jump-range and route overlays work in both modes
Selecting a system, highlighting its gate neighbors, showing a jump-range ring, and drawing an
active route (gate legs as straight lines, jump legs as arcs) SHALL work identically in both
Standard and Schematic modes, using each mode's own projected coordinates.
