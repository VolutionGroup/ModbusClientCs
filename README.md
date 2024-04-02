# VVG.Modbus
A simple MODBUS-RTU client in c# - mainly written because other available alternatives didn't include read/write file record support.
Option to create MODBUS-TCP or MODBUS-ASCII (etc) at a later date using interface.

Supported functions:
* Read Coils (1)
* Read Discrete Input (2)
* Read Holding Registers (3)
* Read Input Registers (4)
* Write Coil (5)
* Write Holding Register (6)
* Write Coils (15)
* Write Holding Registers (16)
* Read File Records (20) - single block only
* Write File Records (21) - single block only

No enforced minimum idle time between last response and next command

Based on SharedCode C++ (internal)
