# CANape Online Measurement/Calibration Adder

This repository contains the source code for a Windows tool that adds CANape measurement and calibration objects online through the ASAP3 interface.

## Requirements

- Vector CANape with ASAP3 enabled.
- Vector ASAP2 Studio libraries available locally.
- A matching A2L and ELF supplied by the user at runtime.
- Measurement stopped before starting an online update.

The repository intentionally does not include proprietary A2L, ELF, MF4, CNA, Vector DLL, or compiled Release files.

## Workflow

1. Open the CANape project.
2. Open `Tools -> ASAP3` and keep `ASAP3 Active` running.
3. Stop measurement.
4. Select the local CANape project and ELF in the GUI.
5. Enter measurement and calibration object names.
6. The tool reads the active A2L, generates a candidate database, refreshes CANape, and verifies the requested objects.

The helper processes share a named Windows mutex so that ASAP3 sessions are serialized.

## Important

The source uses placeholder local installation paths for Vector components. Adjust them for the local CANape/ASAP2 Studio installation before building.
