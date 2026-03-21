# Sneakout

A local working repository for `Sneak Out` reverse engineering and documentation.

Contents:

- `tools/` — working scripts and patchers
- `Source docs/` — structured documentation for files, logic, gameplay mechanics, and patch history

Quick start:

```bash
python3 tools/patch_sneak_out_berek.py "/path/to/Sneak Out"
python3 tools/patch_sneak_out_berek.py --rollback "/path/to/Sneak Out"
```

Current verified result:

- a private lobby can be created in `Berek`
- the match launches on a berek map
- the mode plays from start to finish
- the crown visual is still not fully fixed
