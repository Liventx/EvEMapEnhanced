# user-data

Locally persisted user data that isn't part of the SDE: authenticated EVE characters, saved
routes, and per-system notes.

## Requirement: Multiple authenticated EVE characters
The system SHALL allow the user to sign in multiple EVE characters via ESI SSO, persist each
signed-in character's identity, encrypted refresh token, and last-fetched skills (see
jump-planning) locally, and select which authenticated character is active for route/jump
planning and location tracking. The user SHALL be able to sign out a character, removing its
stored credentials.

#### Scenario: Switching active pilot changes route planning inputs
- GIVEN two authenticated characters with different jump-related skill levels
- WHEN the user switches the active pilot
- THEN subsequent route/jump-range calculations use the newly active character's skills

#### Scenario: Signing out removes stored credentials
- GIVEN a signed-in character
- WHEN the user signs that character out
- THEN its stored refresh token and cached skills are deleted and it no longer appears in the
  pilot picker

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
