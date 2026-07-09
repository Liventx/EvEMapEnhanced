# map-rendering

The interactive 2D map (`MapControl`), its two display modes, and how systems/regions are laid
out and styled on screen.

## Requirement: Responsive top toolbars on narrow windows
The SDE status strip and main map toolbar SHALL adapt when the window is narrower than the
full single-row layout: controls SHALL wrap onto additional lines (logical groups stay together),
long status text SHALL truncate with an ellipsis instead of forcing horizontal overflow, and
fixed-width controls (combo boxes, profile dropdowns, zoom slider) SHALL use a flexible width
between a minimum and maximum so they can shrink on small monitors.

#### Scenario: Toolbar wraps on a narrow window
- GIVEN the main window is resized to a width typical of a small monitor (e.g. 1280 px or less)
- WHEN the user views the SDE strip and map toolbar
- THEN all controls remain reachable without clipping off-screen and related controls wrap as
  grouped rows rather than a single overflowing line

#### Scenario: Long status text does not stretch the toolbar
- GIVEN online tracking or SDE status reports a long message
- WHEN the message is shown in the top area
- THEN the text is truncated with an ellipsis within the available width instead of pushing
  other toolbar controls out of view

## Requirement: Main map zoom slider
The main map toolbar SHALL provide a horizontal zoom slider with +/− step buttons and a
numeric readout of the current zoom level (two decimal places, e.g. "3.00") so
the user can refer to a specific zoom when reporting expected behavior. The slider SHALL use a
logarithmic scale from the minimum to maximum zoom level. Moving the slider or clicking the
buttons SHALL update the main map zoom the same way as the mouse wheel (anchored to the viewport
center). The slider position and numeric readout SHALL stay in sync when zoom changes via the
wheel or programmatic fit operations.

#### Scenario: Slider zooms the main map
- GIVEN the main map is visible
- WHEN the user drags the zoom slider toward the + end
- THEN the map zooms in, the numeric readout updates, and system plates/markers grow accordingly

#### Scenario: Slider follows wheel zoom
- GIVEN the user zooms the main map with the mouse wheel
- WHEN the zoom level settles
- THEN the zoom slider thumb and numeric readout match the new zoom level

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

## Requirement: Schematic region placement matches the in-game universe map
In Schematic mode, whole regions SHALL be composed the way EVE's own in-game New Eden star map
arranges them. Each region SHALL be anchored using a bundled curated region-position grid extracted
from that in-game map (keyed by region name); a region absent from the grid SHALL be anchored from
its real in-game centroid mapped into the grid's frame via a best-fit transform, so it still lands
in the correct neighborhood. The whole anchor field SHALL be scaled up uniformly about its shared
center so each region's internal layout has room to render. When the curated grid carries an explicit
scale factor, that factor SHALL be used verbatim so a hand-tuned arrangement renders exactly as it
was authored (rather than being re-derived every build, which would let tighter packing paradoxically
inflate the gaps); only when no explicit scale is present SHALL the factor be derived from the
regions' own footprints and spacing (large enough that the great majority of regions clear their
neighbors purely by that uniform scaling, which preserves the arrangement), never a fixed constant.
Region-to-region placement SHALL NOT use Dotlan's universe-overview coordinates.

#### Scenario: Region ordering matches the in-game universe map
- GIVEN the set of all k-space regions
- WHEN the Schematic layout is built
- THEN each region's relative position (e.g. Delve south-west of The Forge, Cobalt Edge to the far
  east, Paragon Soul to the far south) matches the in-game star map's arrangement

#### Scenario: Region missing from the curated grid still lands correctly
- GIVEN a region with no entry in the bundled in-game region-position grid
- WHEN the Schematic layout is built
- THEN that region is placed by mapping its real in-game centroid through the best-fit transform
  between real and curated space, rather than being left unpositioned or dropped at the origin

#### Scenario: Uniform scaling preserves relative arrangement
- GIVEN two regions whose curated anchors place one due east of the other
- WHEN the Schematic layout scales the anchor field up to separate regions
- THEN that east/west relationship is preserved (the anchor field is scaled uniformly about its
  shared center, never re-ordered)

#### Scenario: Explicit grid scale is honored so tuning renders as authored
- GIVEN a curated region grid that carries an explicit scale factor
- WHEN the Schematic layout is built
- THEN the anchor field is scaled by exactly that factor (not a re-derived one), so the region
  spacing the user tuned is reproduced instead of the gaps changing between builds

## Requirement: Debug grid overlay for tuning region positions
The region-tuning developer tools SHALL live under their own top-level "Отладка" (Debug) menu,
placed immediately to the right of the "Карта" (Map) menu, rather than mixed into the Map menu.
This menu SHALL provide a toggle that, in Schematic mode, overlays the curated region grid's
coordinate space (the same 0-100 frame the curated region-position data is authored in) on the map:
grid lines with coordinate labels, each region's current curated (x, y) annotation, and a live
readout of the curated coordinate under the pointer. The overlay is a developer aid for hand-tuning
the curated region positions; it SHALL NOT alter the layout, and SHALL default to off.

#### Scenario: Region-tuning tools live under a dedicated Debug menu
- GIVEN the application menu bar
- WHEN it is displayed
- THEN a top-level "Отладка" menu appears immediately after the "Карта" menu and contains the
  debug-grid toggle, the region-edit toggle, and the export-region-positions action, and none of
  those three appear under the "Карта" menu

#### Scenario: Toggling the debug grid shows the coordinate overlay
- GIVEN the map is in Schematic mode and the curated region grid is in use
- WHEN the user enables the debug-grid toggle
- THEN the map draws the curated coordinate grid, per-region curated (x, y) annotations, and a
  pointer coordinate readout, without changing any system or region position

#### Scenario: Debug grid is off by default
- GIVEN the app has just launched
- WHEN the Schematic map is first displayed
- THEN no debug grid overlay is shown until the user enables the toggle

## Requirement: Interactive region-position editing and export
The "Отладка" (Debug) menu SHALL provide a "region edit" toggle that, in Schematic mode, lets the user reposition
whole regions by dragging them with the left mouse button, and an "export region positions" action
that serializes the current region grid to the same JSON shape as the bundled curated
region-position data, including the active uniform scale factor so pasting it back reproduces the
exact arrangement (a stable round-trip). While region edit mode is on, the coordinate grid overlay SHALL be shown, a
left-drag that starts on a region SHALL move that entire region (all its systems and its label)
together as one rigid group without disturbing the systems' internal arrangement or any other
region, and a left-drag that starts away from any region SHALL still pan the map. Dragging SHALL
update that region's curated (x, y) so it is reflected in the debug annotations and the exported
JSON. Region edit mode SHALL default to off. The export action SHALL write the JSON to a file the
user can retrieve and, when a clipboard is available, also copy it to the clipboard, so the tuned
coordinates can be pasted back into the project's region-position data.

#### Scenario: Dragging a region moves the whole cluster
- GIVEN region edit mode is on in Schematic mode
- WHEN the user presses the left mouse button over a region and drags
- THEN that region's systems and label move together by the drag delta, every other region stays
  put, and the region's curated (x, y) updates to match its new position

#### Scenario: Dragging empty space still pans
- GIVEN region edit mode is on in Schematic mode
- WHEN the user left-drags starting on empty space away from any region
- THEN the map pans as usual and no region is moved

#### Scenario: Exporting yields the curated JSON
- GIVEN the user has repositioned one or more regions in region edit mode
- WHEN the user invokes the export action
- THEN the app produces JSON in the curated region-position shape (normalized region name → [x, y])
  reflecting the current positions, saved to a file (and copied to the clipboard when available)

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

## Requirement: Inter-region gate connections are not shown
Schematic mode SHALL NOT draw connector lines between region anchor points. Regional stargates
are still indicated via the per-system gate lines described below; only the region-to-region
overview connectors are omitted.

#### Scenario: No region-to-region connector lines
- GIVEN two regions with at least one stargate crossing between them
- WHEN the Schematic map is rendered
- THEN no connector line is drawn between their region anchor points

## Requirement: Every regional gate is visible from at least one endpoint
Schematic mode SHALL indicate every stargate whose two endpoints are in different regions,
regardless of whether the neighboring system itself is currently on screen. Cross-region gate
lines SHALL be drawn in a light gray, thin pen style that is visually distinct from the darker,
thicker intra-region gate lines, and SHALL be painted before system plates and name labels so
they stay in the background and never obscure system names. A visible system SHALL never be
shown without any indication of one of its real stargates just because that gate happens to
cross a region boundary.

#### Scenario: Both endpoints of a regional gate are visible
- GIVEN two systems in different regions connected by a real stargate, both currently visible
- WHEN the Schematic map is rendered
- THEN a full line is drawn directly between them in the light gray, thin cross-region style

#### Scenario: Only one endpoint of a regional gate is visible
- GIVEN a visible system with a real stargate to a system in another region that is off screen
  (e.g. viewing a single region whose neighbor region isn't in view)
- WHEN the Schematic map is rendered
- THEN the visible system shows a full gate line from its plate edge toward the off-screen
  neighbor's projected position, in the same light gray, thin cross-region style (no arrowhead)

#### Scenario: The off-screen gate line survives zooming into a large plate
- GIVEN a visible system's off-screen regional gate line, at a zoom level where that system
  renders as a large Full-tier plate
- WHEN the Schematic map is rendered
- THEN the line is drawn in the background (before the plate) from the system's projected
  position toward the neighbor, so the plate and its name label paint over the line rather than
  the line crossing on top of them

## Requirement: Schematic region labels are muted and zoom-aware
Schematic mode SHALL draw each region's name in a muted blue color so it reads as a background
landmark rather than a bright overlay. At or below a fixed overview zoom threshold (zoom 5.00)
region labels SHALL be painted after system plates, labels, markers, beacons, PvP highlights, and
every other overlay so they overlap everything and help identify regions on the universe overview.
When the user zooms in past that threshold, region labels SHALL be painted before gate lines,
system plates, and system-name labels so every later opaque draw paints over any part of a region
label underneath it and system names stay legible.

#### Scenario: A region label never covers a system's name when zoomed in
- GIVEN the Schematic map is zoomed in past the overview threshold (zoom 5.00) and a region label's
  bounding box overlaps a system plate or label's position
- WHEN the map is rendered
- THEN the system plate/label is fully visible and the region label is only visible in the
  surrounding space, not on top of it

#### Scenario: Region labels overlap systems at wide zoom
- GIVEN the Schematic map is at or below the overview threshold (zoom 5.00)
- WHEN the map is rendered
- THEN region name labels are drawn on top of system plates, dots, beacons, and activity highlights
  where they overlap

## Requirement: Wide-zoom overlays shrink with zoom level
When the map is at wide overview zoom (at or below the default Schematic zoom level, or at or
below the Jump Range mini-map's auto-fit zoom), fixed-pixel overlays — pilot/cyno/SC beacons,
PvP activity outlines, search/hover highlights, route endpoint markers, structure icons, and
Schematic dot-tier markers — SHALL scale down proportionally as the user zooms out further, down
to a minimum of 12% of their normal size at extreme zoom-out, so the universe overview stays
legible instead of being cluttered by oversized highlights.

#### Scenario: Beacons shrink when zoomed out on the universe overview
- GIVEN a live pilot beacon is shown on the Schematic map at or below the default zoom level
- WHEN the user zooms out further toward the minimum zoom
- THEN the beacon renders smaller than it does at the default zoom level

#### Scenario: PvP highlights shrink when zoomed out on the universe overview
- GIVEN a system has a PvP activity highlight on the Schematic map at or below the default zoom
  level
- WHEN the user zooms out further toward the minimum zoom
- THEN the highlight outline and fill render smaller than they do at the default zoom level

## Requirement: Schematic system plates render at one of three detail tiers
Schematic mode SHALL render each visible system as a Dotlan-style plate rather than the small dot
markers used in Standard mode, at one of three detail tiers ordered from most to least detailed:
a rounded-rectangle plate with both the system name and its NPC-kill count, a smaller plate with
just the system name (colored the same as the full plate), or a plain dot colored only by
NPC-kill activity. The entire viewport SHALL use a single tier at once — every visible system
renders at the same level, regardless of region. The detail tier SHALL be chosen from the current
zoom level alone (not from viewport density or plate collisions) so panning between regions at
the same scale always shows the same tier. Below zoom 14.00 every visible system uses the dot
tier; from 14.00 up to (but not including) 22.00 every visible system uses compact name-only
plates; at 22.00 and above, full name+NPC-kill plates are used when NPC-kill labels are enabled
(otherwise compact remains the most detailed tier). Within the chosen tier, plate scale MAY
shrink only to prevent on-screen overlap, never to switch tiers.

#### Scenario: All regions share the same visual tier
- GIVEN two or more regions are visible on the Schematic map at the same zoom level
- WHEN the map is rendered
- THEN every visible system in every region renders at the same detail tier (full, compact, or dot),
  rather than one region showing plates while another shows only dots

#### Scenario: Zoom level selects the detail tier
- GIVEN the map is at zoom 10.00, 20.00, or 27.00
- WHEN the user pans between any two regions without changing zoom
- THEN every visible system renders at the dot tier (10.00), compact tier (20.00), or full tier
  (27.00) respectively, regardless of regional density

#### Scenario: Compact band sits between dots and full plates
- GIVEN NPC-kill labels are enabled and the map zoom is 19.00
- WHEN the Schematic map is rendered
- THEN every visible system renders as a compact name-only plate, not as a dot or a full
  name+kill plate

#### Scenario: Plates never overlap at overview zoom
- GIVEN any two visible systems in the same or different regions, at either the full or compact
  plate tier, and the map is at or below the default Schematic zoom level
- WHEN the Schematic map is rendered
- THEN their plate rectangles never intersect on screen

#### Scenario: Zooming out degrades detail before allowing overlap
- GIVEN visible systems no longer fit as full name+NPC-kill plates at the current zoom and the map
  is at or below the default Schematic zoom level
- WHEN the Schematic map is rendered
- THEN every visible system renders as name+color-only plates, or as NPC-kill-colored dots if even
  that doesn't fit, instead of shrinking indefinitely or overlapping

#### Scenario: All plates within a tier are visually consistent
- GIVEN two visible systems in the same region with different name lengths and NPC-kill counts,
  both rendering at the same tier
- WHEN their Schematic plates are drawn
- THEN both plates use the same font sizes and padding for that tier (only their content and
  resulting width differ), rather than one being rendered in a different style or size class

## Requirement: Full plates stay compact with minimal padding
Full-tier plates (system name plus NPC-kill count) SHALL be sized tightly around their text: the
rounded-rectangle box SHALL use minimal horizontal and vertical padding and a small minimum width,
and the vertical gap between the system name and the NPC-kill count SHALL be minimal, so the plate
occupies little more space than the two lines of text require.

#### Scenario: Full plate hugs its text
- GIVEN a full-tier plate showing a system name and its NPC-kill count
- WHEN the plate is drawn
- THEN the box padding around the text and the gap between the name and the kill count are minimal,
  so the box is only slightly larger than the text it contains

## Requirement: NPC-station systems are flagged with a corner marker
On the Schematic map, a system that contains at least one NPC station (per the SDE) SHALL be
flagged on its full- and compact-tier plate with a small gold square in the plate's bottom-left
corner. Systems without an NPC station, and systems rendered at the dot tier, SHALL NOT show the
marker.

#### Scenario: NPC-station system shows a gold corner square
- GIVEN a system that has an NPC station and is rendered as a full or compact plate
- WHEN the Schematic map is rendered
- THEN a small gold square is drawn in the bottom-left corner of that system's plate

#### Scenario: System without an NPC station has no marker
- GIVEN a system that has no NPC station rendered as a full or compact plate
- WHEN the Schematic map is rendered
- THEN no gold corner square is drawn on that system's plate

## Requirement: Plate size scales linearly with zoom level within each tier
Schematic plate dimensions and font sizes SHALL scale from a single shared scale factor clamped to
a shared minimum. On the universe overview (at or below the default Schematic zoom level) that
factor also has a shared maximum. When zoomed in past the default level, system positions spread
linearly with zoom but the plate scale factor SHALL grow sub-linearly (square root of the zoom
ratio, capped) so clusters open up faster than labels enlarge; if compact plates still collide at
that target scale, the scale SHALL shrink until they fit or the shared minimum is reached.

#### Scenario: Zooming out shrinks plates before dropping detail
- GIVEN the Schematic map at a close zoom level showing compact name plates
- WHEN the user zooms out toward the default Schematic zoom level
- THEN plates are rendered smaller than they were at the closer zoom level, continuing to shrink
  down to that tier's legibility minimum before the overview collision cascade may drop to dots

#### Scenario: Close zoom opens space between labels
- GIVEN the Schematic map at zoom 11.46 (past the default Schematic zoom level) showing several
  dense regional clusters
- WHEN the map is rendered
- THEN compact plates are sized so they do not overlap within each cluster (scale shrinks from
  the sqrt target if needed), rather than preserving the same overlap density as the overview

## Requirement: Click hit-testing matches rendered geometry
Left-click selection and right-click context-menu targeting SHALL hit-test against each system's
actually-rendered geometry: the drawn plate rectangle in Schematic mode, or a small fixed-radius
circle around the dot marker in Standard mode.

#### Scenario: Clicking a plate's edge selects that system
- GIVEN a Schematic-mode plate rendered wider than the old fixed hit-test radius
- WHEN the user clicks near the edge of that plate (but not on its center point)
- THEN that system is selected

## Requirement: Plate color reflects NPC kill activity
When the map's plate-color mode is set to NPC Kills and last-hour NPC-kill data is available
for a system, its Schematic plate fill color SHALL follow a muted white-to-red gradient keyed to
that kill count (white = none, through soft green, yellow and orange, to desaturated red for the
highest activity), matching Dotlan's "NPC Kills" map filter but with reduced brightness so busy
systems do not glare on the white map. A system with no NPC-kill data available yet SHALL fall
back to security-status coloring in that mode.

## Requirement: Plate color mode switches between NPC Kills and Security status
The main menu SHALL let the user choose how Schematic (and Standard) system markers are filled:
NPC Kills (gradient + kill count labels on full plates) or Security status (classic
security-color plates without kill counts). The choice SHALL take effect immediately on the main
map without restarting the app.

#### Scenario: NPC Kills mode shows gradient plates
- GIVEN plate-color mode is NPC Kills and NPC-kill data is available
- WHEN a Schematic plate is drawn
- THEN its fill uses the NPC-kills gradient and full-tier plates show the kill count label

#### Scenario: Security mode shows security colors
- GIVEN plate-color mode is Security status
- WHEN a Schematic plate is drawn
- THEN its fill uses the security-status palette and no NPC-kill count label is shown

#### Scenario: No NPC-kill data yet falls back to security color
- GIVEN NPC-kill data has not been fetched yet (e.g. app just started, offline)
- WHEN a Schematic plate is drawn
- THEN its fill color uses the security-status palette instead

## Requirement: Jump-range and route overlays work in both modes
Selecting a system, highlighting its gate neighbors, showing a jump-range ring, and drawing an
active route (gate legs as bright green animated dashed lines, jump legs as arcs) SHALL work
identically in both Standard and Schematic modes, using each mode's own projected coordinates.
Gate route legs SHALL march along the path so the active gate chain is visually distinct from
static stargate graph lines. A system within the active jump range (from the "Jump Range"
right-click menu or live pilot tracking) SHALL be marked
with a bold black outline traced directly on its own marker/plate boundary — matching Dotlan's own
jump-range map overlay — rather than a separate ring floating outside it or a recolored border.

#### Scenario: Jump-reachable systems get a bold black outline on their own boundary
- GIVEN a system within the active jump range
- WHEN the map is rendered
- THEN a bold black outline is traced directly on that system's own marker (Standard mode) or
  plate edge (Schematic mode, matching whatever tier it rendered at), not on a separate shape
  offset from it

#### Scenario: Hot PvP activity adds a red overlay inside jump range
- GIVEN a jump-reachable system classified as hot from recent zKillboard player kills
- WHEN the Schematic or Standard map is rendered
- THEN a red highlight outline is drawn on that system's plate or marker, visible on top of the
  jump-range black ring

#### Scenario: Recent PvP activity adds a yellow overlay inside jump range
- GIVEN a jump-reachable system classified as recently active from zKillboard player kills
- WHEN the map is rendered
- THEN a yellow highlight outline is drawn on that system's plate or marker

#### Scenario: NPC capital activity adds a purple overlay inside jump range
- GIVEN a jump-reachable system classified as having recent NPC dreadnought or titan activity
- WHEN the map is rendered
- THEN a purple highlight outline is drawn on that system's plate or marker, on top of any
  other activity highlight

#### Scenario: Context menu opens zKillboard for any system
- GIVEN the user right-clicks a solar system on the map
- WHEN they choose the zKillboard menu item
- THEN the app's default browser opens that system's zKillboard page

#### Scenario: Gate route legs are bright green and animated
- GIVEN an active route with at least one gate leg is shown on the map
- WHEN the map is rendered
- THEN each gate leg is drawn in bright green with a moving dash animation, distinct from the
  static gray stargate graph lines

## Requirement: Live pilot location has a persistent, always-visible beacon
When live "follow pilot" location tracking (see jump-planning) reports a system, the map SHALL
mark that system with a distinct "you are here" beacon. At wide overview zoom (at or below the
default Schematic zoom level) the beacon SHALL be drawn on top of every plate, label, and route
line and SHALL shrink proportionally with other fixed-pixel overlays (see wide-zoom overlay
scaling). When the user zooms in past the default level on the main map, every live location beacon (main
pilot, cyno profile, and SC profile) SHALL be painted before system plates and name labels —
including its full crosshair ticks and center dot — so plates and labels paint over any part of
the beacon that would otherwise obscure neighboring system names. The beacon SHALL render at a fixed
screen-pixel size at normal and close zoom levels, not scaled by the current zoom level, except
that in Schematic mode it SHALL grow just enough to fully encircle that system's own rendered
plate whenever the plate is larger than the beacon's fixed size. This beacon SHALL be tracked
independently of the click-driven system selection, so manually selecting a different system
to inspect it (e.g. for its own jump-range highlight) does not hide or move the pilot beacon.

#### Scenario: Beacon stays the same size at any zoom level while its plate stays smaller
- GIVEN a live-tracked pilot location beacon is shown on the map
- WHEN the user zooms in or out while that system's own plate stays smaller than the beacon
- THEN the beacon renders at the same fixed pixel size, remaining clearly visible rather than
  shrinking or blending into the underlying plate

#### Scenario: Beacon grows to clear an oversized plate
- GIVEN a live-tracked pilot's system is zoomed in far enough that its Schematic plate has grown
  larger than the beacon's fixed size
- WHEN the map is rendered
- THEN the beacon's ring grows just enough to fully encircle that plate instead of nesting inside
  it on top of the system's name text

#### Scenario: Close-zoom beacons stay behind neighboring system names
- GIVEN the main map is zoomed in past the default level and a live location beacon (pilot, cyno,
  or SC) is shown on a system whose beacon would overlap a neighboring system's plate or label
- WHEN the map is rendered
- THEN the neighboring system's plate and name are fully visible and the beacon (including its
  crosshair ticks) is only visible in open space around them, not drawn on top of their text

#### Scenario: Selecting another system does not hide the pilot beacon
- GIVEN live pilot tracking is showing a beacon on the pilot's current system
- WHEN the user clicks a different system to inspect it
- THEN the pilot beacon remains visible on the pilot's system alongside the newly selected
  system's own highlight

## Requirement: Live cyno pilot location has a distinct light-blue beacon
When one or more characters are selected in the "Cyno Profile" multi-select dropdown, the map
SHALL mark each selected character's last known (and live-updated) solar system with a crosshair
beacon identical in shape to the main pilot beacon but rendered in light blue (cyan) instead of
orange/red. These beacons SHALL be tracked independently of the main pilot beacon, SC beacons,
and click-driven selection. Multiple selected cyno pilots in the same system SHALL share one
light-blue beacon there.

#### Scenario: Cyno beacon uses light-blue crosshair styling
- GIVEN a character is selected in the Cyno Profile dropdown and their location is known
- WHEN the map is rendered
- THEN that system shows a light-blue (cyan) crosshair beacon (not the orange main-pilot beacon
  or the deep-blue SC beacon)

#### Scenario: Multiple cyno profiles show multiple beacons
- GIVEN two characters are selected in the Cyno Profile dropdown and their locations are known
- WHEN the map is rendered
- THEN each distinct system occupied by those characters shows a light-blue cyno beacon

#### Scenario: Cyno beacon stays visible while inspecting other systems
- GIVEN one or more cyno profiles are selected and their beacons are shown
- WHEN the user left-clicks a different system on the map
- THEN the light-blue cyno beacon(s) remain on the cyno pilots' system(s)

## Requirement: Live SC pilot location has a distinct deep-blue beacon
When one or more characters are selected in the "SC Profile" multi-select dropdown, the map
SHALL mark each selected character's last known (and live-updated) solar system with a crosshair
beacon identical in shape to the main pilot beacon but rendered in deep blue (not the cyno
profile's light-blue/cyan). These beacons SHALL be tracked independently of the main pilot
beacon, cyno beacons, and click-driven selection. Multiple selected SC pilots in the same system
SHALL share one deep-blue beacon there.

#### Scenario: SC beacon uses deep-blue crosshair styling
- GIVEN a character is selected in the SC Profile dropdown and their location is known
- WHEN the map is rendered
- THEN that system shows a deep-blue crosshair beacon distinct from the light-blue cyno beacon

#### Scenario: Multiple SC profiles show multiple beacons
- GIVEN two characters are selected in the SC Profile dropdown and their locations are known
- WHEN the map is rendered
- THEN each distinct system occupied by those characters shows a deep-blue SC beacon

#### Scenario: SC beacon stays visible while inspecting other systems
- GIVEN one or more SC profiles are selected and their beacons are shown
- WHEN the user left-clicks a different system on the map
- THEN the deep-blue SC beacon(s) remain on the SC pilots' system(s)

## Requirement: A dedicated Jump Range mini-map shows an accurately-scaled range circle
The main window SHALL provide a fixed-width right sidebar column (bordered as one panel) that
contains a system-search field at the top, the Jump Range mini-map below it, and route-planning
controls at the bottom. The mini-map
SHALL be 360×360 pixels, always renders in Standard mode (never Schematic), and always
highlights the Black Ops jump-range circle from the currently selected system — regardless of
whichever ship class the main map's own jump-range selector is set to — since Standard mode is
the only mode where the jump-range circle is drawn to true light-year scale (Schematic mode
clamps and compresses it, because its layout does not preserve real distances). Selecting a
system on the main map (by click or by live pilot tracking) SHALL update the mini-map's
selection and range highlight to match; the mini-map SHALL pan and zoom so the full range circle
fits within its viewport. The main map SHALL NOT show a system-information overlay panel;
per-system details (region, security, jump range, NPC kills) are not shown as floating text on
top of the map.

## Requirement: Right-panel system search focuses the main map
The right sidebar SHALL provide an autocomplete system-search field above the Jump Range
mini-map. When the user picks a system from the search field, the main map SHALL pan (without
changing zoom) to center on that system and draw an orange outline on its plate or marker.

#### Scenario: Search selection centers and highlights a system
- GIVEN the SDE is loaded and the user types or picks a valid system name in the right-panel
  search field
- WHEN the selection is committed
- THEN the main map pans to center that system at the current zoom and shows an orange outline
  on its rendered plate or marker

#### Scenario: Selecting a system updates the mini-map to its Black Ops range
- GIVEN a system is selected on the main map (by click or live pilot tracking)
- WHEN the mini-map renders
- THEN it is centered on that same system, zoomed so the full Black Ops jump-range circle
  (computed from the active pilot's skills) is visible, and systems within that range are marked
  the same way Standard mode marks jump-reachable systems elsewhere

#### Scenario: Mini-map range circle is not compressed
- GIVEN a selected system's Black Ops jump range
- WHEN the mini-map (Standard mode) and the main Schematic map are compared
- THEN the mini-map's circle radius is proportional to the true LY distance, unlike the
  Schematic map's clamped/scaled-down circle

#### Scenario: No floating system-info panel is shown
- GIVEN any system is selected or hovered on the main map
- WHEN the map is rendered
- THEN no persistent text overlay panel with that system's name, region, security, or jump-range
  details is drawn on top of the map (the transient wide-zoom hover name hint below is not such a
  panel)

## Requirement: Wide-zoom hover shows a floating system-name hint
On the main Schematic map, when the zoom level is wide enough that plates collapse to the dot tier
(system names are not otherwise drawn), hovering the pointer over a system SHALL show a small
floating hint near the pointer with that system's name (and its region name), and the hint SHALL
follow the pointer while it stays over the system and disappear when the pointer moves off it. At
zoom levels where plates render their names (compact or full tier), the main map SHALL NOT show
this hint, since the name is already visible on the plate.

#### Scenario: Hover hint appears at dot-tier zoom
- GIVEN the main Schematic map is zoomed out far enough that systems render as dots (no visible
  names)
- WHEN the user hovers the pointer over a system
- THEN a floating hint near the pointer shows that system's name and region name

#### Scenario: Hover hint is suppressed once names are on the plates
- GIVEN the main Schematic map is zoomed in far enough that systems render as compact or full
  plates with their names visible
- WHEN the user hovers the pointer over a system
- THEN no floating name hint is drawn (the plate already shows the name)

#### Scenario: Jump Range mini-map shows a hover tooltip with name and region
- GIVEN the Jump Range mini-map is visible and the pointer is over a solar system
- WHEN the mini-map is rendered
- THEN a floating tooltip near the pointer shows that system's name and its region name

#### Scenario: Mini-map hover highlights the system on the main Schematic map
- GIVEN the main map is in Schematic (Dotlan) mode
- WHEN the user hovers a system on the Jump Range mini-map
- THEN that same system on the main map is outlined with the green gate-neighbor highlight
- AND the highlight clears when the pointer leaves the mini-map or moves off the system

#### Scenario: Out-of-range systems stay legible on the Jump Range mini-map
- GIVEN the Jump Range mini-map is showing a Black Ops range circle
- WHEN systems outside that circle are rendered
- THEN they still show visible dot markers and white-backed name labels (muted text is acceptable)
- AND gate lines are drawn behind markers and labels; off-range connections use a thin muted
  pen so the graph stays visible without obscuring system names

#### Scenario: Mini-map shows universe overview before a system is selected
- GIVEN the Jump Range mini-map is visible and no jump-range origin is set
- WHEN the mini-map is rendered
- THEN it shows the universe overview with region labels and sparse system markers instead of
  a blank panel

#### Scenario: Zoomed-out mini-map avoids overlapping in-range labels
- GIVEN the Jump Range mini-map has an active jump-range origin and the user has zoomed out
  past the auto-fit level
- WHEN the mini-map is rendered
- THEN in-range system labels use the same collision avoidance as out-of-range labels, except
  that the origin system and any pinned/hovered systems always keep their labels

## Requirement: Jump Range mini-map shows zoom-aware region labels
The Jump Range mini-map SHALL draw each visible region's name at the centroid of that region's
real SDE positions, in a muted blue style matching the Schematic map's region labels. At the
auto-fit zoom level (when the mini-map first frames the full jump-range circle) or when zoomed
out further, region labels SHALL be painted after system markers and name labels so they overlap
systems and help orient the user. When the user zooms in past that auto-fit level, region labels
SHALL be painted before gate lines, system markers, and system-name labels so later opaque draws
paint over them and system names stay legible.

#### Scenario: Region labels appear on top when zoomed out on the mini-map
- GIVEN the Jump Range mini-map is showing a jump-range circle at or below its auto-fit zoom
- WHEN the mini-map is rendered
- THEN region name labels are drawn on top of system dots and labels where they overlap

#### Scenario: Region labels fall behind when zoomed in on the mini-map
- GIVEN the user has zoomed the Jump Range mini-map in past its auto-fit level
- WHEN a region label's bounding box would overlap a system marker or name label
- THEN the system marker/label is fully visible and the region label is only visible in the
  surrounding space, not on top of it

## Requirement: Jump-range ship class defaults to Black Ops
The map toolbar "Тип корабля" jump-range selector SHALL default to Black Ops (not "Свой
корабль") so the main map's jump-range overlay matches the mini-map's Black Ops range on first
load.

#### Scenario: Fresh launch uses Black Ops jump range
- GIVEN the application starts with SDE loaded
- WHEN the user has not changed the jump-range ship-class selector
- THEN the main map's jump-range highlight uses Black Ops range and the selector shows Black Ops

## Requirement: A "Focus" checkbox pins the jump-range origin
The map toolbar SHALL offer a "Focus" checkbox. When checked, left-clicking a system (or empty map
space) SHALL update the click-driven selection but SHALL NOT move the jump-range overlay's origin
system; live pilot tracking SHALL also leave the jump-range origin unchanged. The jump-range
origin SHALL only change via the right-click "Дальность прыжка (Jump Range)" context-menu pick
(or its clear action), or when Focus is unchecked and the user left-clicks a system.

#### Scenario: Focus prevents left-click from re-anchoring jump range
- GIVEN a jump-range overlay is shown from system A and the user checks Focus
- WHEN the user left-clicks system B on the map
- THEN system B becomes the click selection but the jump-range circle and reachability highlight
  remain anchored to system A

#### Scenario: Right-click jump-range menu still re-anchors while Focus is on
- GIVEN Focus is checked and the jump-range overlay is anchored to system A
- WHEN the user right-clicks system C and chooses a ship class under "Дальность прыжка"
- THEN the jump-range overlay re-anchors to system C

#### Scenario: Unchecking Focus restores left-click re-anchoring
- GIVEN Focus is checked
- WHEN the user unchecks Focus and left-clicks system D
- THEN the jump-range overlay re-anchors to system D the same as without Focus enabled

## Requirement: Jump-range origin pulses green when the pilot is elsewhere
When a jump-range origin is set and the live-tracked pilot (if any) is in a different system,
the map SHALL draw a pulsing green outline on the origin system's own marker/plate boundary.
When the tracked pilot is in the origin system, the green outline SHALL NOT be shown (the pilot
beacon already marks that system).

#### Scenario: Pinned origin shows a green pulse while the pilot is elsewhere
- GIVEN Focus is checked, the jump-range origin is system A, and live tracking reports the pilot
  in system B
- WHEN the map is rendered
- THEN system A shows a pulsing green outline and system B shows the pilot beacon, with no green
  pulse on system B

#### Scenario: No green pulse when pilot is in the origin system
- GIVEN the jump-range origin and the live-tracked pilot are both in system A
- WHEN the map is rendered
- THEN system A shows only the pilot beacon, not the pulsing green origin outline

## Requirement: Jump-range simulation overlays multiple origins and their intersection
The map toolbar SHALL offer a "Симуляция" toggle (jump-range simulation). While it is enabled, left-clicking
a solar system on the main map SHALL add (without removing prior picks) a jump-range overlay
anchored to that system, using the same range calculation as the main jump-range highlight
(ship class, pilot skills, jump method). Each simulation origin SHALL draw its range circle the
same way as the main jump-range circle. Systems reachable from a simulation origin but not from
every other active simulation origin SHALL be marked with the same bold black outline weight as
the main jump-range highlight, but drawn as a dashed line on their own marker/plate boundary.
When two or more simulation origins are active, systems reachable from all of them SHALL instead
be marked with a bold blue (#4F5AFF) outline on their own boundary. The simulation origin systems
themselves — the sources the intersections are measured from — SHALL be marked with a bold solid
orange (#FF8C00) outline that takes visual priority over the reachable (dashed black) and
intersection (blue) styling, so the fixed anchors are distinguishable from merely reachable or
overlapping systems. While simulation mode is
active on the main map, the solid main jump-range rings and main profile range circle SHALL be
hidden; simulation layers own those visuals instead. The anchored main jump-range origin itself
SHALL NOT change during simulation clicks. When the simulation toggle is turned on and a main
jump-range origin is already active, that origin SHALL immediately become the first simulation
layer without requiring an extra click, and every system in that seeded range SHALL use the bold
dashed simulation outline. From the second simulation pick onward, the app SHALL only add
the new origin when its jump range intersects every already-active simulation range; otherwise
it SHALL show a brief on-map notice "Пересечений нет" at the click location and leave the
existing simulation layers unchanged. Turning the toggle off SHALL remove every simulation overlay
and circle and restore the main profile jump-range highlight (solid rings, range circle, and
origin pulse animation) for the last anchored origin.

#### Scenario: Simulation clicks accumulate without clearing prior ranges
- GIVEN the simulation toggle is enabled and the user has left-clicked system A
- WHEN the user left-clicks system B on the main map
- THEN both A's and B's jump-range circles and reachable-system outlines remain visible

#### Scenario: Active jump range seeds the first simulation layer
- GIVEN a main jump-range overlay is already anchored to system A with reachable systems B and C
- WHEN the user turns the simulation toggle on
- THEN system A is treated as the first simulation origin without an additional click
- AND systems A, B, and C show the bold dashed black simulation outline instead of the solid
  main jump-range outline

#### Scenario: Disabling simulation restores the main jump-range styling
- GIVEN simulation mode was enabled from an existing main jump-range overlay anchored to system D
- WHEN the user turns the simulation toggle off
- THEN the solid main jump-range rings and range circle for system D return
- AND any origin pulse animation for system D resumes according to the normal jump-range rules

#### Scenario: Simulation reachable systems use a bold dashed black outline
- GIVEN the simulation toggle is enabled and exactly one simulation origin is set
- WHEN a system is reachable from that origin but is not part of the main profile jump range
- THEN that system shows a bold dashed black outline (same weight as the main jump-range ring)
  on its own marker/plate boundary

#### Scenario: Intersection of simulation ranges is blue
- GIVEN the simulation toggle is enabled with simulation origins at systems A and B
- WHEN a system C is reachable from both A and B under the current range rules
- THEN system C shows a bold blue (#4F5AFF) outline on its own marker/plate boundary instead of
  the dashed black simulation outline

#### Scenario: Simulation origin systems get an orange outline
- GIVEN the simulation toggle is enabled with simulation origins at systems A and B
- WHEN the overlay is drawn
- THEN systems A and B each show a bold solid orange (#FF8C00) outline on their own marker/plate
  boundary, even when they also fall inside another origin's range or the shared intersection

#### Scenario: A non-intersecting simulation pick is rejected
- GIVEN the simulation toggle is enabled and at least one simulation origin is already active
- WHEN the user left-clicks a system whose jump range shares no common reachable systems with
  every already-active simulation range
- THEN a brief on-map notice "Пересечений нет" appears at the click location
- AND the new origin is not added to the simulation overlay

#### Scenario: Disabling simulation clears overlays but keeps the main profile range
- GIVEN the simulation toggle is enabled with one or more simulation origins and a main profile
  jump-range overlay from system D
- WHEN the user turns the simulation toggle off
- THEN all simulation circles and dashed/blue outlines disappear
- AND the main profile jump-range overlay from system D remains unchanged
