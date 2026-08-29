# WAA Implementation Status

## Current bounded phase

Phase 1A — report-contract and weighted-idle engine.

## Implemented in source

- .NET 10 core project and test project
- strict Driver Code + Driver Name parser using the confirmed first-whitespace rule
- Driver Leader parser as a separate alphanumeric organizational code
- dependency-free CSV reader with quoted-field support
- header normalization for BOM, non-breaking spaces, and repeated whitespace
- Rolling 7 Day required-header validation
- normalization of repeated `OOR %` and `Idle %` rows into one driver/week observation
- explicit conflict detection across repeated operational fields
- current report-cycle detection
- weighted driver 7-day calculation
- weighted driver 28-day calculation using four exact weekly periods
- incomplete 28-day coverage state
- weighted fleet 7-day and 28-day calculations with coverage
- strict configurable-threshold evaluation primitive
- tests for identity splitting, punctuation rejection, duplicate-row normalization, conflict rejection, truck reassignment, weighted math, incomplete coverage, and strict threshold comparison
- Windows GitHub Actions build/test workflow

## Not yet implemented

- WPF application shell
- Downloads known-folder discovery
- launch/manual update orchestration
- stable-file read and SHA-256 idempotency
- SQLite persistence and migrations
- virtualized fleet list
- threshold editor and automatic priority ordering UI
- idle-conversation persistence and work-through actions
- handoff generation

The next bounded slice is Phase 1B: persistence, controlled report update, and the first professional WPF fleet screen. No code from the retired implementation is to be consulted.
