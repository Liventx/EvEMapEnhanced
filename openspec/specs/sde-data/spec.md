# sde-data

Downloading, importing and caching CCP's Static Data Export (SDE) as the local source of truth
for solar systems, stargates, regions and ship types.

## Requirement: First-run SDE acquisition
The system SHALL detect whether a usable local SDE cache exists and, if not, download and
import the latest SDE before the map can be shown.

#### Scenario: No local cache yet
- GIVEN the app is launched for the first time (no SDE SQLite cache file, or the cache exists
  but is empty)
- WHEN the user triggers the SDE download
- THEN the app downloads the latest SDE archive, imports it into a local SQLite database, and
  reports import progress/summary to the user

#### Scenario: Cache already present
- GIVEN a non-empty SDE SQLite cache already exists on disk
- WHEN the app starts
- THEN the app loads the universe map from the existing cache without re-downloading

## Requirement: Imported data completeness
The SDE import SHALL populate solar systems (with position, security status and region), the
stargate graph between them, region names, and a ship-type catalog needed to classify capital
hulls, pods and other ships used elsewhere in the app.

#### Scenario: Universe map is buildable after import
- GIVEN a freshly imported SDE cache
- WHEN the app builds the in-memory universe map from the cache
- THEN every solar system has a valid 3D position, security value and region ID, and every
  stargate connects two systems that both exist in the imported set

## Requirement: Accessible-space filtering
The system SHALL exclude space that isn't reachable by a normal capsuleer from the routable
universe: wormhole space, Abyssal Deadspace, CCP's internal test universes, and any
stargate-connected cluster smaller than a minimum size (treated as a disconnected
test/placeholder artifact), determined structurally rather than via hardcoded region
lists.

#### Scenario: Wormhole and internal regions are dropped
- GIVEN the raw SDE system list, including wormhole-space and internal CCP test regions
- WHEN the accessible-space filter runs
- THEN none of those systems appear in the resulting routable universe map

#### Scenario: A legitimately small but connected pocket is kept
- GIVEN a k-space region whose stargate-connected cluster meets the minimum accessible
  cluster size (e.g. Pochven)
- WHEN the accessible-space filter runs
- THEN that region's systems remain in the routable universe map

## Requirement: Re-import is idempotent
Re-running the SDE import against the same or a newer SDE archive SHALL fully replace prior
data without leaving stale or duplicate rows.

#### Scenario: Re-import after an SDE update
- GIVEN an existing populated SDE cache
- WHEN the user re-runs the download/import (e.g. after a new SDE release)
- THEN the resulting cache reflects only the newly imported data, with no leftover rows from
  the previous import
