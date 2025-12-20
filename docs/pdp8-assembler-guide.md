# PDP-8 Assembler Guide

## Introduction

This guide provides a comprehensive overview of programming in PDP-8 assembly language, specifically tailored for the Olive Petrel PDP-8 Emulator. The PDP-8 is a classic 12-bit minicomputer architecture that uses a simple yet powerful instruction set.

## Basic Architecture

### Word Size and Registers
- **Word Size**: 12 bits
- **Accumulator (AC)**: Primary working register
- **Link (L)**: Carry/overflow bit
- **Program Counter (PC)**: Points to the next instruction to be executed

## Instruction Set Overview

### Instruction Types
1. **Memory Reference Instructions**
   - Load and Store operations
   - Arithmetic operations
   - Logical operations

2. **Operating System Instructions (IOT)**
   - Input/Output control
   - Device-specific operations

3. **Operate Instructions**
   - Register and condition code manipulation

## Instruction Formats

### Memory Reference Instructions
```
| Opcode (3 bits) | Indirect Bit (1 bit) | Page/Zeropage (1 bit) | Address (7 bits) |
```

### Instruction Mnemonics

#### Load and Store
- `TAD addr`: Two's complement Add (Load and Add)
- `DCA addr`: Deposit and Clear Accumulator (Store and Clear)

#### Arithmetic
- `ISZ addr`: Increment and Skip if Zero
- `SZA`: Skip if Accumulator is Zero

#### Logical
- `AND addr`: Bitwise AND
- `CMA`: Complement Accumulator

## Addressing Modes

### Direct Addressing
```assembly
TAD VARIABLE   ; Load value from VARIABLE directly into AC
```

### Indirect Addressing
```assembly
TAD I POINTER  ; Load value pointed to by POINTER
```

### Zero Page and Current Page Addressing
- Addresses 0-127 are in the current page
- Addresses 128-255 require page addressing

## Memory Layout

### Typical Memory Map
- `0000-0177`: Zero Page (frequently accessed data)
- `0200-0377`: Program Start (typical entry point)
- `0400-0777`: Subroutines and Extended Code
- `1000-7777`: Additional Program Space

## Common Assembler Directives

- `*`: Set location counter
- `0`: Define a word
- `TEXT`: Define a null-terminated string

## Input/Output Operations

### IOT (Input/Output Transfer) Instructions
```assembly
IOT 6046   ; Teleprinter output
IOT 6751   ; RX Floppy Disk Load Command
```

## Example Program

```assembly
/ Simple Hello World Program
    * 0200
START,  CLA CLL      / Clear Accumulator and Link
        TAD MSG      / Load message address
        JMS I PUTS   / Call print subroutine
        HLT          / Halt

MSG,    TEXT "Hello, PDP-8!"; 0

/ Subroutine to print null-terminated string
PUTS,   0            / Subroutine entry
        DCA STRPTR   / Save string pointer
PUTS_LOOP,
        TAD I STRPTR / Get character
        SZA          / Stop if zero
        JMS PUTCH    / Print character
        SZA
        JMP I PUTS   / Return if zero
        ISZ STRPTR   / Next character
        JMP PUTS_LOOP

PUTCH,  0            / Print character
        AND BIT7     / Mask to 7 bits
        IOT 6046     / Print to teleprinter
        JMP I PUTCH

BIT7,   0177         / 7-bit mask
STRPTR, 0            / String pointer
```

## Best Practices

1. Use Zero Page for frequently accessed variables
2. Leverage indirect addressing for pointer-based operations
3. Minimize instruction count for performance
4. Use meaningful labels and comments

## Common Pitfalls

- Forgetting to clear AC before operations
- Misunderstanding indirect vs. direct addressing
- Not handling overflow and link bit correctly

## Performance Optimization

- Use `ISZ` for loop counters
- Minimize memory references
- Leverage Zero Page for fast access

## Debugging Techniques

1. Use diagnostic print routines
2. Examine AC and status after critical operations
3. Use `HLT` and manual inspection for complex issues

## Tools and Emulation

- Olive Petrel PDP-8 Emulator
- Diagnostic programs like `flapper.pa`
- Assembler with good error reporting

## Resources

- Digital Equipment Corporation PDP-8 Manuals
- Vintage Computer Federation resources
- Online PDP-8 emulator communities

## Conclusion

The PDP-8 assembly language offers a window into early computing, with its elegant and minimalist design. Understanding its architecture provides insights into fundamental computer design principles.