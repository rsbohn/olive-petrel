# PDP-8 PAL Routine Library

This directory is intended for PAL assembly routines that can be reused
across PDP-8 programs.

## Conventions (proposed)

- Source files end in `.pa`.
- Each routine file documents:
  - Entry label(s)
  - Inputs/outputs (AC, MQ, L, PC, memory)
  - Clobbered registers/memory
  - Expected origin (if any)
- Routines should be position-independent unless stated otherwise.

## Next steps

Tell me which routines you want first (math, string, I/O helpers, device
drivers, etc.) and any calling convention you prefer. I can add the initial
PAL sources and examples here.

## Library ROM workflow

This repo includes a minimal ROM builder + linker:

- `bin/build-rom.sh [lib/*.pa ...]` -> `lib.rom` and `lib.sym`
- `bin/link.sh <app.pa>` -> `<app>.rom` (library merged + LINKs resolved)

### Routine requirements

- No `*` origin directives inside library routines (the builder places them).
- Routines must fit within a single page (0200 words).
- Labels must be unique across all library files (symbol table is global).

### LINK placeholder

Use `LINK <symbol>` on a single statement line to create an address constant:

```
OCTOPRINT_PTR, LINK OCTOPRINT
    ...
    JMS I OCTOPRINT_PTR
```

`bin/link.sh` replaces `LINK OCTOPRINT` with the octal address from `lib.sym`.
