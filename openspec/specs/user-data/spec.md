# user-data

Locally persisted user data that isn't part of the SDE: pilot profiles, saved routes, and
per-system notes.

## Requirement: Multiple named pilot profiles
The system SHALL allow the user to maintain multiple named pilot profiles, each with its own
skills and routing preferences (see jump-planning), and to select which profile is active for
route/jump planning.

#### Scenario: Switching active profile changes route planning inputs
- GIVEN two pilot profiles with different jump-related skill levels
- WHEN the user switches the active profile
- THEN subsequent route/jump-range calculations use the newly active profile's skills

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
Pilot profiles, saved routes, system notes, and structures SHALL be stored independently of the
SDE cache (a separate local database), so re-downloading or re-importing the SDE never deletes
them.

#### Scenario: Re-importing the SDE keeps saved routes
- GIVEN saved routes exist and the user re-imports a newer SDE
- WHEN the import completes
- THEN the previously saved routes are still present and reference the same system IDs (SDE
  system IDs are stable across CCP's SDE releases)
