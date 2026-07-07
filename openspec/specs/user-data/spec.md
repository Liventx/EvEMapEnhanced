# user-data

Locally persisted user data that isn't part of the SDE: authenticated EVE characters, saved
routes, and per-system notes.

## Requirement: Multiple authenticated EVE characters
The system SHALL allow the user to sign in multiple EVE characters via ESI SSO, persist each
signed-in character's identity, encrypted refresh token, and last-fetched skills (see
jump-planning) locally, and let the user pick which authenticated character is active for
route/jump planning and location tracking via a pilot picker. The user SHALL be able to sign out
a character, removing its stored credentials.

#### Scenario: Switching active pilot changes route planning inputs
- GIVEN two authenticated characters with different jump-related skill levels
- WHEN the user switches the active pilot
- THEN subsequent route/jump-range calculations use the newly active character's skills

#### Scenario: Signing out removes stored credentials
- GIVEN a signed-in character
- WHEN the user signs that character out
- THEN its stored refresh token and cached skills are deleted and it no longer appears in the
  pilot picker

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

## Requirement: Saved routes
The user SHALL be able to save a computed route under a name, with its creation timestamp and
full step list, and reload it later without recomputing.

#### Scenario: Saved route reloads exactly
- GIVEN a route was computed and saved under a name
- WHEN the user reloads that saved route later
- THEN the same ordered list of route steps is restored

## Requirement: Per-system notes
The user SHALL be able to attach a free-form text note with optional tags to any solar system,
persisted locally and retrievable by system.

#### Scenario: Note persists across sessions
- GIVEN a note was added to a system
- WHEN the app is restarted
- THEN that system's note and tags are still retrievable

## Requirement: All user data survives SDE re-import
Authenticated characters, saved routes, system notes, and structures SHALL be stored
independently of the SDE cache (a separate local database), so re-downloading or re-importing
the SDE never deletes them.

#### Scenario: Re-importing the SDE keeps saved routes
- GIVEN saved routes exist and the user re-imports a newer SDE
- WHEN the import completes
- THEN the previously saved routes are still present and reference the same system IDs (SDE
  system IDs are stable across CCP's SDE releases)
