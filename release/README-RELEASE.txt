Magellan 3 -- release package contents
======================================

This folder is what an end user needs to run Magellan 3. Put ALL of these files
together in one folder (e.g. C:\Games\Magellan3\), then register Magellan3.dll
with the 32-bit RegAsm (see the main README's "Install" section).

FILES THAT MUST BE PRESENT:

  Built by you (from bin\Release after `dotnet build -c Release`):
    Magellan3.dll          -- the plugin
    Magellan3.Core.dll     -- its logic library (separate assembly; required)
    DatReaderWriter.dll    -- the DAT reader (from the Chorizite NuGet package)

  Data files (included in this folder):
    places.xml             -- 3,307 places (search / nearby)
    dungeon_names.tsv      -- 653 dungeon names (dungeon identification)
    places_2.0.0.2.xml     -- portal graph (route finding)

The DLL alone is NOT enough -- without the three data files, search returns
nothing, dungeon names are blank, and routing is disabled.

Also required on the user's machine (NOT shipped here -- they install these):
    Decal 3                (provides Decal.Adapter.dll, Decal.FileService.dll)
    VirindiViewService     (provides VirindiViewService.dll)
    An End-of-Retail AC client (for the DAT files the automap reads)

To build the release zip: drop the three built DLLs into this folder alongside
the data files, then zip the whole folder.
