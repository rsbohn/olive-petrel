# IOT Codes

This document summarizes the IOTs implemented by the emulator for each
peripheral device.

## Console TTI (device 03)

| IOT (octal) | Mnemonic | Description |
| --- | --- | --- |
| 06031 | KCF | Clear keyboard flag |
| 06032 | KSF | Skip if keyboard flag set |
| 06034 | KRS | Read keyboard buffer into AC low byte |
| 06036 | KRB | Read buffer and clear flag |

## Console TTO (device 04)

| IOT (octal) | Mnemonic | Description |
| --- | --- | --- |
| 06041 | TCF | Clear output flag |
| 06042 | TSF | Skip if output flag set |
| 06044 | TLS | Output AC low byte |
| 06046 | TLSC | Output and clear flag |

## Line Printer LPT (device 60)

| IOT (octal) | Mnemonic | Description |
| --- | --- | --- |
| 06601 | LPCF | Clear printer flag |
| 06602 | LPSF | Skip if printer flag set |
| 06604 | LPT | Output AC low byte to printer |
| 06606 | LPTC | Output and clear flag |

## DECtape TC08 (control device 76, data device 77)

| IOT (octal) | Mnemonic | Description |
| --- | --- | --- |
| 06762 | DTCA | Clear transfer address |
| 06764 | DTSF | Skip if controller ready |
| 06766 | DTLB | Read block into memory at transfer address |
| 06771 | DTXA | Load transfer address from AC |

## RX8E Floppy (device 75)

| IOT (octal) | Mnemonic | Description |
| --- | --- | --- |
| 06751 | LCD | Load command: first LCD loads sector and flags, second LCD loads track |
| 06752 | XDR | Transfer data word to/from AC |
| 06753 | STR | Skip on transfer ready |
| 06754 | SER | Skip on error |
| 06755 | SDN | Skip on done |
| 06756 | INTR | Initialize and read status (starts I/O, returns status in AC) |
| 06757 | INIT | Reset controller |
