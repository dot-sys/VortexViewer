#### Changelog v1.2
---
##### General
- Reduced AV/EDR false positives by refactoring API patterns
- Optimized Garbage Collection and memory management.

##### Timeline
- Improved signature status detection logic
- Implemented `StringPool` to reduce memory overhead during evidence aggregation.
- Hardened parsing logic for Event Logs and Registry Hives to eliminate runtime exceptions.

##### Journal & Drives
- Optimized parent-path construction for more reliable full path resolution

##### Processes
- Improved handling of SeDebugPrivilege and PPL constraints in memory scanner

#### Changelog v1.1
---
##### General
- Added Language support for EN, DE and RU from native speakers, as well as ES and PT from AI Translation.
Thanks to Andrew Gavrilenkov for the RU translations!
- Fixed a problem with forcing Admin correctly at startup
- Fixed a pixel bug with the top Logo
- Improved Garbage Collection for Memory efficiency
- Updated all libraries

##### System Info
- Added more GPU Names
- Fixed various issues with fetching the values on 64-Bit Systems
- Fixed "Oldest Prefetch" now showing in yyyy-MM-dd format

##### Journal
- Applied changes to Journal Parsing and Parent-Path-Construction for more consistent full paths
- Fixed a Bug where Parsing Journal from external NTFS Drives could cause infinite Loops

##### Processes
- Added visual warning for System Protected Processes
- Fixed an Overlay Problem on lower resolution

##### Timeline
- Added Filter to show only Not Signed Files
- Fixed an error with System and Software Hive Parsings
- Fixed an exception causing longer loading times during Status Checks

##### Drives
- Fixed various Exceptions in Drives Parser
