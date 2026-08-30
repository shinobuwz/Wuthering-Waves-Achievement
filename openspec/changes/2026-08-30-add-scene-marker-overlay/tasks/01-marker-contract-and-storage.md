# Task 01 — Add marker geometry and persistence seam

## Requirements

- Add source-pixel ROI and normalized ROI contracts.
- Map WPF display selections to source pixels without referencing WPF from Core.
- Crop packed/strided BGR frames exactly.
- Add identifier validation and an exe-relative storage service.
- Persist unique PNG/JSON pairs atomically enough to avoid overwrite or half-described output.

## Acceptance

- Focused tests cover mapping, crop, naming, and persistence behavior.
- Core remains independent of WPF and Infrastructure.
