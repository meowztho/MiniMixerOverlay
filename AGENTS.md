# Agent entry: Mini Mixer Overlay

Repository: `F:\mini_mixer_overlay` on SebPC (path is environment-specific); source root is this repository. Primary runtime: `src/MiniMixerOverlay.App`. This file only routes discovery; it is **not** a second product specification.

For a concise product mental model and scope-to-contract map, start with `BLUEPRINT.md` (navigational, non-authoritative); `PROJECT_INDEX.yaml` remains the canonical APC authority/workstream router.

1. Before work, check repository root, current instructions, Git status/diff, and `PROJECT_INDEX.yaml`. Load only authorities routed for the workstream.
2. Keep **Product Truth** (`Docs/00_VISION.md`, `Docs/01_PRODUCT_REQUIREMENTS.md`, `Docs/02_UX_UI_DESIGN.md`) distinct from **implementation observations** (`Docs/RORE_REFERENCE_TRUTH_2026-10-17.md`) and current verified runtime behavior (`ACCEPTANCE_MATRIX.yaml`).
3. `Docs/03_TECH_ARCHITECTURE.md` owns responsibility boundaries and contracts; `Docs/07_ROADMAP.md` owns dependency/admission stages; `Docs/10_DECISIONS_AND_EVIDENCE.md` owns decisions, uncertainties and provenance. Do not create competing authorities.
4. Preserve existing functioning behavior, especially user-reported experimental hook behavior; do not infer a broken feature merely from a static audit. Fix roots with the smallest tested change, no unauthorized architecture replacement.
5. No real game injection, audio-volume modification, install/uninstall, or anti-cheat interaction without separately scoped authorization. Isolated tests/static analysis may be used before that boundary. Product completion needs real-path evidence, not status labels.
6. This APC compilation changed documentation only. Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/verify-project-authorities.ps1 -SelfTest` to check authority links/acceptance consistency. The validator does not verify runtime behavior.

For optional, non-normative Windows-audio/Guard/overlay/IPC/GPU domain knowledge and focused agent prompts, use [`Docs/domain-knowledge/README.md`](Docs/domain-knowledge/README.md). Read only the relevant chapter **after** the routed product authorities. The catalog does not create new requirements, owners, implementation gates or runtime evidence; historical examples are revision-bound.

See `Docs/README.md` for a human navigation table.
