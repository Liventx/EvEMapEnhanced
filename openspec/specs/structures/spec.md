# structures

User-entered structures (citadels, jump bridges, cyno beacons/jammers) and how they plug into
routing and map display.

## Requirement: Structure kinds and access levels
The system SHALL support entering a structure as one of: Ansiblex, CustomJumpBridge,
CynoBeacon, CynoJammer, Keepstar, Fortizar, Azbel, Athanor, or Tatara, each with a name, owning
system, optional owner tag, an access level (own alliance / own corporation / public /
blacklisted), and free-form notes.

#### Scenario: Structure is listed at its system
- GIVEN a structure entered at a given solar system
- WHEN the map or structure list is queried for that system
- THEN the structure appears among that system's structures

## Requirement: Structures are managed in a modal dialog
Structure entry and editing SHALL be available from a compact top menu bar button that opens a
modal dialog, rather than a dedicated tab in the main window.

#### Scenario: Opening structures from the menu
- GIVEN the main window is showing the map
- WHEN the user clicks the Structures button in the top menu
- THEN a modal dialog opens with the structure entry form and structure list

## Requirement: Jump bridges form routable edges
Ansiblex and CustomJumpBridge structures with a linked system SHALL create a bidirectional
routable edge between their system and the linked system, weighted by the real light-year
distance between them.

#### Scenario: Linked jump bridge is usable in both directions
- GIVEN an Ansiblex in system A linked to system B
- WHEN querying jump-bridge neighbors of either A or B
- THEN each lists the other as a reachable neighbor via that structure

#### Scenario: Unlinked structure creates no edge
- GIVEN a jump-bridge-kind structure with no `LinkedSystemId` set
- WHEN the universe map loads structures
- THEN no jump-bridge edge is created for that structure

## Requirement: Cyno jammers block cyno-based jump landings
A system containing a CynoJammer structure SHALL be treated as cyno-jammed: excluded as an
intermediate landing point for cyno-based jump routing (see jump-planning), even though it
remains a valid gate-routing waypoint.

#### Scenario: Cyno-jammed system still allows gate transit
- GIVEN a system with a CynoJammer structure
- WHEN computing a pure gate route through that system
- THEN the route may still pass through it

## Requirement: Structures persist across sessions
Entered structures SHALL be saved locally and reloaded automatically the next time the map
loads, without requiring re-entry.

#### Scenario: Structure survives an app restart
- GIVEN a structure was entered and saved in a previous session
- WHEN the app is restarted and the map is loaded
- THEN that structure is present on the map and in routing again
