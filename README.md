
<p align="center">
  <img src="https://dot-sys.github.io/VortexCSRSSTool/Assets/VortexLogo.svg" alt="Vortex Logo" width="100" height="100">
</p>

<h2 align="center">Vortex Viewer</h2>

<p align="center">
  A lightweight Tool for quick triage in live Win10/11 Systems, extracting Journal, Execution Timeline and Drive-Logs, as well as an included Process Memory String Parsing Tool. <br><br>
  ⭐ Star this project if you found it useful.
</p>

---

### Overview

**Vortex Viewer** is a small Windows forensics and system triage tool built in C# and .Net 4.6.2. for maximum system compatibility, designed for rapid incident response and digital forensics analysis. It combines multiple specialized parsers into a single standalone executable. It can be run from any USB Stick or during Screenshare.

#### System Info

Provides system information focussed on Unique Identification of the System, including hardware details and OS configuration. It features a small variety of Log Tampering Overviews to quickly identify critical system characteristics during response.

#### USN Journal

Extracts and parses the NTFS Change Journal (USN Journal) from all mounted drives, capturing file system activity with timestamps, file operations (Creations, Modifications, Deletions) and uses MFT references to build Fullpaths. It displays entries in an interactive DataGrid with filtering and search capabilities for rapid investigation.

#### Execution Timeline

Constructs a chronological execution timeline by aggregating multiple data sources including:

- Registry Entries: AmCache, BAM, MuiCache, Shellbags, ShimCache, UserAssist and many more
- Event-Logs: Execution Traces from evtx Files
- Prefetch: Parsing of Prefetch-Files including its last Run-Time-Dates
- WER-Fault Logs

Presents a unified view of application execution history, enabling investigators to establish attack sequences and identify suspicious process activity.

#### Drive Logs

Aggregates filesystem metadata from all connected drives, including NTFS Master File Table (MFT) entries, file attributes and volume information. Provides comprehensive drive enumeration for forensic artifact collection of possibly connected Drives and Drive Operations.

#### Process Memory String Parser

Parses string data directly from process memory of running processes without writing to disk. Useful for extracting command-line arguments, execution traces, network connection, and other runtime artifacts from live memory - all captured and displayed within the GUI without CSV export overhead.

---

### Features

- **Single Standalone EXE**: All dependencies embedded
- **No Installation Required**: Run directly from USB
- **Live System Analysis**: Real-time triage without system modifications or pre-process dumping
- **Interactive DataGrids**: Filter, sort and search results in-app
- **Fast Triage**: Optimized for speed - complete incident response analysis in seconds

### Requirements

- .NET Framework 4.6.2
- Windows 10 or Windows 11 (64-bit)
- Administrator privileges (required for full journal and process memory access)
