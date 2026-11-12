# FoxTable 0.5.0 map workshop

Additive terrain strokes, 64/128/256-cell map blueprints, 126 CC0 KayKit models rendered as transparent mobile sprites. Original geometry, materials and texture sources are retained in Art/KayKit/WorldModels.zip. WorldArt.zip is extracted and imported synchronously before the build. Provenance and CC0 license accompany both archives.

The cartographer interface uses charcoal/ink surfaces, parchment text and brass selection accents, large model previews, four primary navigation actions, floating object controls and stacked form labels.

Pre-export checks cover local campaign operations, grid/selection workspace, touch cancellation and movement, large-map construction and persistence, portrait/landscape UI layout. Offscreen Unity renders are emitted when a graphics device is available; null-graphics builds explicitly report the lack of visual render. Android hardware memory, gestures, pause/resume and frame rate still require device testing.

Server verification: PYTHONPATH=Server python3 -m unittest discover -s Server/tests -q — 68 tests passed.

Existing local campaign serialization version is unchanged; terrain is an additive field. Existing scenes remain present when creating a blueprint. Image import remains bounded to 2048 pixels per edge to avoid unbounded native memory use on mobile devices.
