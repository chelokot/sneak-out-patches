# Sneakout

A local working repository for `Sneak Out` reverse engineering and documentation.

Contents:

- `tools/` — working scripts and patchers
- `Source docs/` — structured documentation for files, logic, gameplay mechanics, and patch history

Quick start:

```bash
python3 tools/patch_sneak_out.py "/path/to/Sneak Out"
python3 tools/patch_sneak_out.py --rollback "/path/to/Sneak Out"
```

Current verified result:

- the patcher can now build a live `Normal / Berek` mode selector inside `PortalPlayView`
- the first-invite private party fix and the uniform hunter-random fix remain available in the same CLI
- the crown visual is still not fully fixed
