# Interactive Assembler

The Olive Petrel emulator includes an interactive assembler that allows you to enter PDP-8 assembly instructions one line at a time, with immediate feedback.

## Starting the Interactive Assembler

To start the interactive assembler, use the `.a` command with no arguments:

```
> .a
Interactive Assembler
Commands: q=quit, .list=show memory, .symbols=show symbols, .load=load to machine, .clear=reset

Start address [0200]?
```

You can specify a starting address or press Enter to use the default (0200 octal).

## Basic Usage

The assembler prompts you with the current address. Enter an assembly instruction and press Enter:

```
0200? CLA CLL
0200: 7300 CLA CLL
0201? TAD 0007
0201: 1007 TAD 0007
0202? IOT 6046
0202: 6046 IOT 6046
0203? HLT
0203: 7402 HLT
0204? q
```

Each line shows:
- The address where the instruction was assembled
- The octal machine code
- A disassembly of the instruction

## Supported Instructions

### Memory Reference Instructions
- `AND <addr>` - Bitwise AND
- `TAD <addr>` - Two's complement Add
- `ISZ <addr>` - Increment and Skip if Zero
- `DCA <addr>` - Deposit and Clear Accumulator
- `JMS <addr>` - Jump to Subroutine
- `JMP <addr>` - Jump

Add `I` for indirect addressing:
```
0200? JMP I 0010
0200: 5410 JMP I 0010
```

### Operate Instructions (Group 1)
Combine multiple operations:
```
0200? CLA CLL
0200: 7300 CLA CLL
0201? CMA IAC
0201: 7041 CMA IAC
```

Supported mnemonics: CLA, CLL, CMA, CML, RAR, RAL, RTR, RTL, BSW, IAC

### Operate Instructions (Group 2)
Skip instructions:
```
0200? SZA HLT
0200: 7442 SZA HLT
```

Supported mnemonics: SMA, SZA, SNL, SPA, SNA, SZL, CLA, OSR, HLT, ION, IOFF

### IOT Instructions
Enter device codes directly:
```
0200? IOT 6046
0200: 6046 IOT 6046
```

Or as octal:
```
0200? 6046
0200: 6046 IOT 6046
```

### Data Constants
Enter octal numbers:
```
0200? 1234
0200: 1234 1234
```

Hex (prefix with 0x):
```
0200? 0x1FF
0200: 0777 0777
```

Decimal (prefix with #):
```
0200? #100
0200: 0144 0144
```

Character literals:
```
0200? "A"
0200: 0101 "A"
```

## Labels and Symbols

Define labels by placing a comma after the label name:

```
0200? LOOP, CLA
0200: 7200 CLA
0201? JMP LOOP
0201: 5200 JMP 0200
```

You can reference symbols in instructions:
```
0202? TAD LOOP
0202: 1200 TAD 0200
```

## Special Commands

### `.list` - Show Memory
Display all assembled instructions:
```
0203? .list
Memory [0200-0202]:
0200: 7200 CLA
0201: 5200 JMP 0200
0202: 1200 TAD 0200
```

### `.symbols` or `=` - Show Symbols
Display the symbol table:
```
0203? .symbols
Symbols:
  LOOP         = 0200
```

### `.update` or `.u` - Update Machine Memory
Transfer assembled code into the emulator's memory:
```
0203? .update
Updated machine memory.
```

After updating, you can exit the interactive assembler and run your code with commands like `g` (go) or `s` (step).

### `.clear` - Reset Session
Clear all assembled memory and user-defined symbols:
```
0203? .clear
Memory cleared.
```

### Address Jump
Enter just an octal number to change the current address:
```
0200? 300
Address set to 0300
0300?
```

### Quit
Exit the interactive assembler with `q`, `quit`, or `exit`:
```
0200? q
Update machine memory with assembled code? (y/n) y
Updated machine memory.
>
```

If you've assembled code that hasn't been updated yet, you'll be prompted to update it before exiting.

## Example Session

Here's a complete example that creates a simple program:

```
> .a
Start address [0200]?

0200? CLA CLL
0200: 7300 CLA CLL
0201? TAD 0007
0201: 1007 TAD 0007
0202? LOOP, ISZ 0010
0202: 2010 ISZ 0010
0203? JMP LOOP
0203: 5202 JMP 0202
0204? HLT
0204: 7402 HLT
0205? .list
Memory [0200-0204]:
0200: 7300 CLA CLL
0201: 1007 TAD 0007
0202: 2010 ISZ 0010
0203: 5202 JMP 0202
0204: 7402 HLT
0205? .symbols
Symbols:
  LOOP         = 0202
0205? .update
Updated machine memory.
0205? q
> pc 0200
PC set to 0200
> g
```

## Tips

1. **Error Recovery**: If you make a mistake, the assembler will show an error but continue. You can re-enter the correct instruction.

2. **Forward References**: You can reference labels before they're defined, but you'll need to re-assemble that instruction after defining the label.

3. **Page Addressing**: Memory reference instructions use page-relative addressing. The assembler will warn if the target address is not on the current page (bits 5-11) or zero page.

4. **Comments**: Use `/` to add comments (not currently supported in interactive mode, use file assembly for commented code).

5. **Pseudo-ops**: Symbols defined in file assemblies (with `=`) are available in the interactive assembler.
7. **Update Memory**: Use `.update` or `.u` to transfer code to machine memory, or answer 'y' when prompted on exit.