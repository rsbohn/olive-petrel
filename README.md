# Olive Petrel

Olive Petrel is a PDP-8 emulator with a built-in monitor, a PAL assembler, and
device emulation for DECtape (TC08), RX01/RX02 floppy (RX8E), and a line printer.
It also includes a TNFS client for loading files over the network.

## Build

Requires .NET 9 SDK.

```bash
dotnet build src/OlivePetrel.csproj
```

## Run

```bash
dotnet run --project src/OlivePetrel.csproj
```

You should see a prompt like:

```
Olive Petrel PDP-8 Emulator
Type 'help' for commands.
>
```

## Monitor commands (quick reference)

General:
- `help` or `.help` open the help shell (topics in `docs/help`); inside the shell try `menu`, `search <word>`, or `random`
- `regs` show registers
- `mem <addr> [count]` dump memory (octal)
- `dep <addr> <word|string>..` deposit octal words or ASCII strings
- `load <file>` load an octal image (auto-detects S-records)
- `.a <src.pa> [dest]` assemble PAL source to an S-record file
- `save <file>` save core memory as a loadable image
- `pc <addr>` set the program counter
- `step [count]` execute instructions
- `run [max]` run up to max instructions (default 100000)
- `trace [count]` step and show registers after each instruction
- `reset` clear memory and registers
- `quit` exit the emulator

Devices:
- `show dev` show attached devices
- `dt0|dt1 att|attach <file> [new]` attach or create a DECtape image
- `rx0|rx1 att|attach <file> [new]` attach or create an RX01/RX02 image
- `lpt att|attach <file>` attach a line printer output file

## TNFS shell

Start the TNFS subshell with `tnfs`. Basic commands:

- `mount <host> <path> [user <id>] [pass <pwd>] [port <port>] [ver <major.minor>]`
- `ls [path]`
- `get <remote> [local]` (default local path is `./sd/<name>`)
- `umount`
- `status`
- `exit`

## Docs

Help topics live in `docs/help`. Protocol notes and device details are in
`docs/rx8e.md` and `docs/tnfs-protocol.md`.
