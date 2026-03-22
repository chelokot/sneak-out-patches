# Documentation Index

## gameplay

- `gameplay/tasks-and-task-steps.md`
  Why the end-of-match task stats look strange.
- `gameplay/hunters-modes-and-berek.md`
  Hunters, abilities, modes, and confirmed `Berek` facts.
- `gameplay/jugmaking-camera-indicator-bug.md`
  Why the danger indicator appears inverted during the jug-making task camera.
- `gameplay/seeker-selection.md`
  The seeker selection algorithm in the default mode.
- `gameplay/crown-visual-pipeline.md`
  How the visible crown is wired and what is still missing.
- `gameplay/locker-open-attack-cooldown.md`
  Why seekers cannot attack immediately after opening a locker.
- `gameplay/mummy-unlock-research.md`
  Runtime facts and entry points for restoring Mummy as a selectable hunter.

## patching

- `patching/working-berek-patch.md`
  The currently documented working patch set and its constraints.
- `patching/patch-history.md`
  What worked, what failed, and why.

## reverse-engineering

- `reverse-engineering/client-structure.md`
  Main client files, where things live, and what they are responsible for.
- `reverse-engineering/function-reference.md`
  Key functions, offsets, and why they matter.
- `reverse-engineering/berek-startup-flow.md`
  The startup chain that had to be repaired to make `Berek` playable.
- `reverse-engineering/private-party-invite-bug.md`
  Why private party invites only worked on the second accept.
- `reverse-engineering/install-and-runtime-layout.md`
  Steam app paths, library locations, and runtime artifacts.
- `reverse-engineering/evidence-sources.md`
  Where current conclusions came from and how reliable each source is.
- `reverse-engineering/backend-transition.md`
  Confirmed backend seams and the current runtime-mod redirect strategy.
- `reverse-engineering/community-backend-scope.md`
  The first-pass scope for a self-hosted private-lobby backend.
- `reverse-engineering/community-backend-live-test.md`
  The first install-ready runbook for testing the redirector mod against the community backend.
- `reverse-engineering/patching-methodology.md`
  Practical rules for safer IL2CPP and asset patching in this project.

## ui

- `ui/lobby-mode-selector-flow.md`
  How the current lobby UI works and where the hidden mode selector still exists.

## history

- `history/experiment-log.md`
  Chronological notes about successful and failed patch attempts.
