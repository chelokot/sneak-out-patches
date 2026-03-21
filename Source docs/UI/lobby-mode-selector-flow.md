# Lobby Mode Selector Flow

## Current visible lobby UI

The active lobby screen is the newer portal play flow, not the old mode selection UI.

Observed active behavior:

- interacting with the portal opens the modern play popup
- that popup exposes role preference, public/private state, and the play action
- it does not expose a normal mode selector in the current build

## Hidden older mode selector

The client still contains older mode-selection views.

Confirmed pieces:

- `GameModeView`
- `GameModeViewV2`
- buttons for regular hide-and-seek and `Capture the Crown`

Practical conclusion:

- the older selector was not fully removed from the client
- it is present in the build, but no longer wired into the active flow

## Why enabling the old selector directly was not enough

Several early experiments tried to re-enable the old selector through scene changes or direct redirects.

Typical failure modes:

- black screen after opening the portal
- broken lobby initialization
- startup failures when forcing the wrong UI too early

This strongly suggests:

- the old selector depends on assumptions that are no longer true in the current client flow
- simply activating dormant scene objects is not enough

## Practical lesson

For this build, the stable route was not "restore the old selector UI."

The stable route was:

1. leave the visible lobby mostly intact
2. patch the host flow so it creates `Berek`
3. patch the state flow so it enters berek-specific startup
4. patch prefab wiring so berek components exist when the mode starts

## What to inspect in future UI work

If more UI recovery work is needed later, these areas are the most relevant:

- the portal play popup flow
- the event path that used to open `GameModeView`
- scene objects inside `level0`
- the point where the current lobby chooses what popup to open

