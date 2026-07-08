# user-data

Locally persisted user data that isn't part of the SDE: authenticated EVE characters and
per-system notes.

## Requirement: Sign-in and skill refresh in the top menu
ESI sign-in and manual skill refresh for the active pilot SHALL be available from the compact
top menu bar, not in the route-planning sidebar column.

#### Scenario: Auth controls are in the top menu
- GIVEN the main window is open
- WHEN the user looks at the top of the window
- THEN sign-in and refresh-skills buttons are visible in the compact menu bar

## Requirement: Multiple authenticated EVE characters
The system SHALL allow the user to sign in multiple EVE characters via ESI SSO, persist each
signed-in character's identity, encrypted refresh token, and last-fetched skills (see
jump-planning) locally, and let the user pick which authenticated character is active for
route/jump planning and location tracking via a pilot picker.

#### Scenario: Switching active pilot changes route planning inputs
- GIVEN two authenticated characters with different jump-related skill levels
- WHEN the user switches the active pilot
- THEN subsequent route/jump-range calculations use the newly active character's skills

## Requirement: Sign-in and active pilot survive an app restart
Once a character has signed in via ESI SSO, the system SHALL NOT require the user to repeat the
browser sign-in flow on a later app launch merely to keep using that character -- the persisted
refresh token is used to obtain new access tokens as needed. The system SHALL also remember which
character was the active pilot and automatically restore that selection on the next launch,
instead of defaulting to "no pilot" and forcing the user to re-pick it every session.

#### Scenario: Restarting the app keeps the previously signed-in character usable
- GIVEN a character signed in during a previous session, with its refresh token persisted
- WHEN the app is closed and reopened
- THEN that character still appears in the pilot picker and can be used for route/jump planning
  without the browser sign-in flow running again

#### Scenario: Active pilot selection is restored on restart
- GIVEN a character was selected as the active pilot in a previous session
- WHEN the app is closed and reopened
- THEN that same character is automatically selected as the active pilot again

#### Scenario: Signing out clears a stale active-pilot selection
- GIVEN a character is both signed out and was the persisted active pilot
- WHEN the app is closed and reopened
- THEN the pilot picker defaults to "no pilot" instead of pointing at a character that no longer
  has stored credentials

## Requirement: Per-system notes
The user SHALL be able to attach a free-form text note with optional tags to any solar system,
persisted locally and retrievable by system.

#### Scenario: Note persists across sessions
- GIVEN a note was added to a system
- WHEN the app is restarted
- THEN that system's note and tags are still retrievable

## Requirement: All user data survives SDE re-import
Authenticated characters, system notes, and structures SHALL be stored independently of the SDE
cache (a separate local database), so re-downloading or re-importing the SDE never deletes them.

#### Scenario: Re-importing the SDE keeps structures
- GIVEN user-added structures exist and the user re-imports a newer SDE
- WHEN the import completes
- THEN the previously added structures are still present and reference the same system IDs (SDE
  system IDs are stable across CCP's SDE releases)

## Requirement: Single desktop instance
Launching the application while another instance is already running SHALL NOT open a second main
window. The existing instance SHALL be restored to the foreground (un-minimized if needed) and
the duplicate launch process SHALL exit immediately.

#### Scenario: Second launch activates the existing window
- GIVEN EvE Map Enhanced is already running with its main window open or minimized
- WHEN the user starts the application again (shortcut, installer, or command line)
- THEN no additional main window appears
- AND the already-running main window becomes the active foreground window

#### Scenario: Second launch preserves maximized window size
- GIVEN EvE Map Enhanced is already running with its main window maximized
- WHEN the user starts the application again
- THEN the main window stays maximized and only comes to the foreground

#### Scenario: First launch starts normally
- GIVEN no EvE Map Enhanced instance is currently running
- WHEN the user starts the application
- THEN a single main window opens as today
