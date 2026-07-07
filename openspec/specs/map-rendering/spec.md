# map-rendering

The interactive 2D map (`MapControl`), its two display modes, and how systems/regions are laid
out and styled on screen.

## Requirement: Two display modes
The map SHALL support a Standard mode (systems placed at their real in-game 2D projection) and
a Schematic mode (Dotlan-style layout), switchable at runtime without reloading the underlying
universe data. Schematic mode SHALL be the default mode shown when the map first loads.

#### Scenario: Switching display mode preserves the loaded map
- GIVEN a universe map is already loaded and displayed in Standard mode
- WHEN the user switches to Schematic mode
- THEN the same systems/stargates are displayed, repositioned per the Schematic layout, and the
  view re-fits to show the whole universe

#### Scenario: Map opens in Schematic mode by default
- GIVEN the app has just launched and a universe map has loaded
- WHEN the map is first displayed
- THEN it is shown in Schematic (Dotlan) mode without the user having to switch to it manually

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

## Requirement: Schematic region labels are prominent but never obscure system names
Schematic mode SHALL draw each region's name large and brightly colored so it reads clearly as a
background landmark, while guaranteeing it never visually covers a system plate, dot, or label:
region labels SHALL be painted before any gate line, system plate, dot, or system-name label, so
every one of those later, opaque draws paints over any part of a region label underneath it,
regardless of how large the region label's font grows.

#### Scenario: A region label never covers a system's name
- GIVEN a region label whose bounding box overlaps a system plate or label's position
- WHEN the Schematic map is rendered
- THEN the system plate/label is fully visible and the region label is only visible in the
  surrounding space, not on top of it

## Requirement: Schematic system plates render at one of three detail tiers
Schematic mode SHALL render each visible system as a Dotlan-style plate rather than the small dot
markers used in Standard mode, at one of three detail tiers ordered from most to least detailed:
a rounded-rectangle plate with both the system name and its NPC-kill count, a smaller plate with
just the system name (colored the same as the full plate), or a plain dot colored only by
NPC-kill activity. Within a single region, every system SHALL render at the same tier — a region
is never shown with some systems at one tier (or missing) and others at a different tier. A tier
SHALL only be used for a region once every system in that region has been confirmed to fit at
that tier without its plate overlapping any other already-placed plate anywhere in the viewport
(including other regions' plates); the region falls back to the next less detailed tier otherwise.
The dot tier has no further fallback and SHALL always be drawn for every system, matching Standard
mode's underlying dot markers.

#### Scenario: A region is never partially shown
- GIVEN a region with systems close enough together that not all of them fit as full plates
- WHEN the Schematic map is rendered
- THEN every system in that region renders at the same (possibly less detailed) tier, rather than
  some of that region's systems showing a plate while others are skipped

#### Scenario: Plates never overlap
- GIVEN any two visible systems in the same or different regions, at either the full or compact
  plate tier
- WHEN the Schematic map is rendered at any zoom level
- THEN their plate rectangles never intersect on screen

#### Scenario: Zooming out degrades detail before allowing overlap
- GIVEN a region whose systems no longer fit as full name+NPC-kill plates at the current zoom
- WHEN the Schematic map is rendered
- THEN that region's systems render as name+color-only plates, or as NPC-kill-colored dots if even
  that doesn't fit, instead of shrinking indefinitely or overlapping

#### Scenario: All plates within a tier are visually consistent
- GIVEN two visible systems in the same region with different name lengths and NPC-kill counts,
  both rendering at the same tier
- WHEN their Schematic plates are drawn
- THEN both plates use the same font sizes and padding for that tier (only their content and
  resulting width differ), rather than one being rendered in a different style or size class

## Requirement: Plate size scales linearly with zoom level within each tier
Schematic plate dimensions and font sizes SHALL scale linearly with the current zoom level within
whichever tier is in use: every dimension (font sizes, padding, minimum width) is derived from a
single shared scale factor, clamped to one shared minimum and maximum, so the whole plate shrinks
or grows proportionally instead of individual dimensions hitting their own floor/ceiling at
different zoom levels and distorting the plate's proportions.

#### Scenario: Zooming out shrinks plates before dropping detail
- GIVEN the Schematic map at a close zoom level showing full name+NPC-kill plates
- WHEN the user zooms out to a wider view but systems still fit at the full tier
- THEN plates are rendered smaller than they were at the closer zoom level, continuing to shrink
  down to that tier's legibility minimum before any region needs to drop to a less detailed tier

#### Scenario: Every dimension of a plate scales together
- GIVEN the Schematic map at two different zoom levels, both still within the full tier
- WHEN plates are compared between the two zoom levels
- THEN the font sizes, padding, and minimum width have all changed by the same proportion, rather
  than some dimensions staying fixed while others shrink

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
Standard and Schematic modes, using each mode's own projected coordinates. A system within the
active jump range (from the "Jump Range" right-click menu or live pilot tracking) SHALL be marked
with a bold black outline traced directly on its own marker/plate boundary — matching Dotlan's own
jump-range map overlay — rather than a separate ring floating outside it or a recolored border.

#### Scenario: Jump-reachable systems get a bold black outline on their own boundary
- GIVEN a system within the active jump range
- WHEN the map is rendered
- THEN a bold black outline is traced directly on that system's own marker (Standard mode) or
  plate edge (Schematic mode, matching whatever tier it rendered at), not on a separate shape
  offset from it

## Requirement: Live pilot location has a persistent, always-visible beacon
When live "follow pilot" location tracking (see jump-planning) reports a system, the map SHALL
mark that system with a distinct "you are here" beacon, rendered at a fixed screen-pixel size
(not scaled by the current zoom level) and drawn on top of every plate, label, and route line so
it can never be covered or shrunk into illegibility. This beacon SHALL be tracked independently of
the click-driven system selection, so manually selecting a different system to inspect it (e.g.
for its own jump-range highlight) does not hide or move the pilot beacon.

#### Scenario: Beacon stays the same size at any zoom level
- GIVEN a live-tracked pilot location beacon is shown on the map
- WHEN the user zooms in or out
- THEN the beacon renders at the same fixed pixel size, remaining clearly visible rather than
  shrinking or blending into the underlying plate

#### Scenario: Selecting another system does not hide the pilot beacon
- GIVEN live pilot tracking is showing a beacon on the pilot's current system
- WHEN the user clicks a different system to inspect it
- THEN the pilot beacon remains visible on the pilot's system alongside the newly selected
  system's own highlight
