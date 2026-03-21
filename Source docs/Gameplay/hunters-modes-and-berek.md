# Hunters, Modes And Berek

## Hunters

Confirmed playable hunters:

- Clown
- Ripper
- Butcher
- Scarecrow
- Mummy
- Dracula

Additional seeker-type:

- `dracula_bat`

## Hunter abilities

### Ripper

- Blink
- Flames

### Scarecrow

- Pumpkin bomb
- Raven scout

### Butcher

- Hook
- Fog

### Mummy

- Sand trap
- Sarcophagus

### Clown

- Hammer hit
- Balloon

### Dracula

- Bat change
- Fly

## Mummy

An important cross-layer split was confirmed:

- in runtime, Mummy is still a full hunter
- in the meta-layer character list, Mummy is already gone

Practical conclusion:

- Mummy is not fully removed
- but for regular character selection it looks hidden or disabled

## Modes

Confirmed match modes in the current code:

- `Default`
- `Berek`

`Berek` corresponds to `Capture the Crown`.

## Confirmed Berek facts

- the mode has its own crown logic
- it has separate match states
- it has separate berek maps
- the old mode selector still exists in the client, but is not exposed in the active UI

## Current Berek patch state

Confirmed locally:

- the room is really created as `Berek`
- the map really switches into a berek map
- the match completes successfully

Known remaining issue:

- the crown visual is still not fully fixed
