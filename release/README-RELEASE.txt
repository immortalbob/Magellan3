Magellan 3 -- release package contents
======================================

This folder is what an end user needs to run Magellan 3. Put ALL of these files
together in one folder (e.g. C:\Games\Magellan3\), then register Magellan3.dll
with the 32-bit RegAsm (see the main README's "Install" section).

FILES THAT MUST BE PRESENT:

  Built by you -- EVERY .dll in bin\Release after `dotnet build -c Release`:
    Magellan3.dll          -- the plugin
    Magellan3.Core.dll     -- its logic library (separate assembly; required)
    DatReaderWriter.dll    -- the DAT reader (from the Chorizite NuGet package)
    System.Memory.dll               \
    System.Buffers.dll               \  DatReaderWriter's NuGet dependency chain.
    System.Runtime.CompilerServices.Unsafe.dll  /  NuGet copies these to bin\Release;
    System.Numerics.Vectors.dll     /   they MUST ship. (Exact set may vary by
                                        package version -- ship whatever is there.)

    RULE: the release DLL set is "everything in bin\Release", full stop. The
    Decal/VVS references are marked Private=false so they never land there --
    which means anything that DOES land there is required at runtime. Omitting
    a System.* DLL does NOT fail at load: the map is just silently empty and
    /mag diag shows FileNotFoundException on every rebuild (v0.8 beta finding).

  Data files (included in this folder):
    places.xml             -- 3,307 places (search / nearby)         REQUIRED
    dungeon_names.tsv      -- 653 dungeon names (dungeon id)         REQUIRED
    places_2.0.0.2.xml     -- portal graph (route finding)           optional

  A missing places.xml or dungeon_names.tsv aborts Startup entirely (no window,
  no map). places_2.0.0.2.xml only disables the Route tab.

Also required on the user's machine (NOT shipped here -- they install these):
    Decal 3                (provides Decal.Adapter.dll, Decal.FileService.dll)
    VirindiViewService     (provides VirindiViewService.dll)
    An End-of-Retail AC client (for the DAT files the automap reads)

To build the release zip: copy every DLL from bin\Release into this folder
alongside the data files, then zip the whole folder.
