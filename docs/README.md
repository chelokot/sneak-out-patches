# Documentation Index

## installation

- `installation/one-line-installer.md`
  Cross-platform `npx` install/uninstall commands, release payloads, compatibility checks, and rollback ownership.

## performance

- `performance/performance-overhaul.md`
  Measured startup, frame pacing, memory, rendering and Fusion results for client 1.1.10, including rejected experiments and the automated test harness.

## runtime mods

- `reverse-engineering/runtime-mod-catalog.md`
  Current runtime mod responsibilities, stability categories, and why some broad mods are intentionally not split yet.
- `reverse-engineering/unlock-everything-layering.md`
  Rules for keeping `Unlock Everything` on stable apply, persistence, and live-sync layers instead of UI wrappers.

## gameplay

- `gameplay/runtime-minimap.md`
  Runtime-only floor-plan generation, local-player projection, configuration, and validation evidence for the minimap mod.
- `gameplay/proximity-voice-chat.md`
  Architecture, privacy model, Steam transport, adaptive playback, and release validation status for proximity voice.
- `gameplay/geometry-fixes.md`
  Exact client paths and safety constraints for chair release, pumpkin radius, and Ripper shared-corner blink fixes.
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
- `gameplay/locker-stun-after-seeker-open.md`
  Why `IsOpen` cannot distinguish a normal exit from a seeker-forced exit, and how the stun fix tracks the actual opener event.
- `gameplay/mummy-unlock-research.md`
  Runtime facts and entry points for restoring Mummy as a selectable hunter.

## patching history

- `patching/working-berek-patch.md`
  Historical working patch sets and their constraints.
- `patching/patch-history.md`
  What worked, what failed, and why during the binary-patch era.

## reverse-engineering

- `reverse-engineering/client-structure.md`
  Main client files, where things live, and what they are responsible for.
- `reverse-engineering/function-reference.md`
  Key functions, offsets, and why they matter.
- `reverse-engineering/berek-startup-flow.md`
  The startup chain that had to be repaired to make `Berek` playable.
- `reverse-engineering/install-and-runtime-layout.md`
  Steam app paths, library locations, and runtime artifacts.
- `reverse-engineering/evidence-sources.md`
  Where current conclusions came from and how reliable each source is.
- `reverse-engineering/backend-transition.md`
  Confirmed backend seams and the current runtime-mod redirect strategy.
- `reverse-engineering/patching-methodology.md`
  Practical rules for safer IL2CPP and asset patching in this project.

## ui

- `ui/lobby-mode-selector-flow.md`
  How the current lobby UI works and where the hidden mode selector still exists.

## history

- `history/experiment-log.md`
  Chronological notes about successful and failed patch attempts.
