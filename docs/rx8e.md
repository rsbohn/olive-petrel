# RX8E Floppy Disk Drive

The RX8E controller uses device code 75 and the following IOTs:

| IOT (octal) | Mnemonic | Description |
| --- | --- | --- |
| 06751 | LCD | Load command: first LCD loads sector and flags, second LCD loads track |
| 06752 | XDR | Transfer data word to/from AC |
| 06753 | STR | Skip on transfer ready |
| 06754 | SER | Skip on error |
| 06755 | SDN | Skip on done |
| 06756 | INTR | Initialize and read status (starts I/O, returns status in AC) |
| 06757 | INIT | Reset controller |

## Usage (in monitor)

    rx0 attach media/floppy.rx01 -- single density 256K
    rx1 attach media/floppy.rx02 -- double density 512k
    rx1 read 0 1 0400 -- read track 0 sector 1 to address 0400
    rx1 write 0 2 0400 -- write track 0 sector 2 from address 0400

In this emulator, `LCD` loads a word whose low 5 bits are the sector (0–25),
bit 5 (0020) selects unit 1, and bit 6 (0040) selects write mode (otherwise
read). The second `LCD` loads the track (0–76). `INTR` kicks off the operation,
`STR`/`SER`/`SDN` are ready/error/done skips, and `XDR` transfers one 12-bit
word at a time.

## Usage in guest system

### Sample PDP-8 Assembly: Read and Write a Floppy Sector

```assembly
/ Read sector 0 from RX8E into memory at 0200
/ Add 0020 to SECTOR access RX8E unit 1.
    CLA CLL             / Clear AC and Link
    TAD 0              / Sector number (0)
    DCA SECTOR
    TAD 0              / Track number (0)
    DCA TRACK
    TAD 0200            / Buffer address (0200)
    DCA BUF

/ Issue Load Command (read sector)
    TAD SECTOR
    IOT 06751             / LCD: Load sector
    TAD TRACK
    IOT 06751             / LCD: Load track
    CLA CLL
    IOT 06756             / INTR: Initialize and Read Status

/ Wait for Transfer Ready
WAIT,   IOT 06753             / STR: Skip on Transfer Ready
    JMP WAIT

/ Read 128 bytes (64 words)
    CLA CLL
    TAD BUF
    DCA PTR
    TAD 0100
    DCA COUNT
READ,   IOT 06752             / XDR: Read word from RX8E
    DCA I PTR
    ISZ PTR
    ISZ COUNT
    JMP READ

/ Write sector 1 from memory at 0300
    TAD 1
    DCA SECTOR
    TAD 0
    DCA TRACK
    TAD 0300
    DCA BUF

/ Issue Load Command (write sector)
    TAD SECTOR
    IOT 06751             / LCD: Load sector
    TAD TRACK
    IOT 06751             / LCD: Load track
    CLA CLL
    IOT 06756             / INTR: Initialize and Read Status

/ Wait for Transfer Ready
WAITW,  IOT 06753             / STR: Skip on Transfer Ready
    JMP WAITW

/ Write 128 bytes (64 words)
    CLA CLL
    TAD BUF
    DCA PTR
    TAD 0100
    DCA COUNT
WRITE,  TAD I PTR
    IOT 06752             / XDR: Write word to RX8E
    ISZ PTR
    ISZ COUNT
    JMP WRITE

UNIT1,  0020    / Set bit for unit 1
MODE8,  0100    / force 8-bit transfers
SECTOR, 0
TRACK,  0
BUF,    0
PTR,    0
COUNT,  0
```

> **Note:** This is a simplified example. Actual code should check for errors and handle controller status appropriately.

## Storage Format

RX01 floppy disks store data in 8-bit byte format, not 12-bit words. The actual capacity is:

RX01: 256,256 bytes = 77 tracks × 26 sectors × 128 bytes/sector

When accessed from the PDP-8, you'd typically read/write 128-byte sectors and convert between 8-bit bytes and 12-bit words as needed.

In SIMH's PDP-8 implementation, 12-bit words are packed into sector bytes using 1.5 bytes per 12-bit word. Concretely:

- RX01 (128-byte sector): 64 12-bit words are stored, packed into the first 96 bytes of the sector; the remaining 32 bytes are zeroed on writes.
- RX02/RX28 (256-byte sector): 128 12-bit words are stored, packed into the first 192 bytes of the sector; the remaining 64 bytes are zeroed on writes.

The byte index used by the simulator for word n is effectively floor(3*n/2), so pairs of words share bytes according to the standard 12-bit packing scheme. This matches the behavior implemented in `pdp8_rx.c` (see the `PTR12` packing macro and the FILL/EMPTY handling of 12-bit transfers).
