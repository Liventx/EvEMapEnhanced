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
landmark rather than a bright overlay. At wide zoom (at or below the default Schematic
zoom level) region labels SHALL be painted after system plates and labels so they overlap systems
and help identify regions on the universe overview. When the user zooms in past the default level,
region labels SHALL be painted before gate lines, system plates, and system-name labels so every
later opaque draw paints over any part of a region label underneath it and system names stay
legible.

#### Scenario: A region label never covers a system's name when zoomed in
- GIVEN the Schematic map is zoomed in past the default level and a region label's bounding box
  overlaps a system plate or label's position
- WHEN the map is rendered
- THEN the system plate/label is fully visible and the region label is only visible in the
  surrounding space, not on top of it

#### Scenario: Region labels overlap systems at wide zoom
- GIVEN the Schematic map is at or below the default zoom level (universe overview)
- WHEN the map is rendered
- THEN region name labels are drawn on top of system plates and dots where they overlap

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
mark that system with a distinct "you are here" beacon, drawn on top of every plate, label, and
route line so it can never be covered or shrunk into illegibility. The beacon SHALL render at a
fixed screen-pixel size, not scaled by the current zoom level, except that in Schematic mode it
SHALL grow just enough to fully encircle that system's own rendered plate whenever the plate is
larger than the beacon's fixed size (e.g. a Full-tier plate at deep zoom-in), so the beacon is
never nested inside -- and visually lost against -- a plate bigger than itself. This beacon SHALL
be tracked independently of the click-driven system selection, so manually selecting a different
system to inspect it (e.g. for its own jump-range highlight) does not hide or move the pilot
beacon.

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
- THEN no text overlay panel with that system's name, region, security, or jump-range details is
  drawn on top of the map

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

#### Scenario: Mini-map prompts for a system before showing the universe
- GIVEN the Jump Range mini-map is visible and no jump-range origin is set
- WHEN the mini-map is rendered
- THEN it shows a short prompt to select a system on the main map instead of fitting the
  entire universe into the small viewport

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
