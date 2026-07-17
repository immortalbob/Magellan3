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
    places.xml             -- 3,307 places; search, nearby, AND the routing
                              portal graph (via EXITLOCATION)          REQUIRED
    dungeon_names.tsv      -- 653 dungeon names (dungeon id)           REQUIRED

  A missing places.xml or dungeon_names.tsv aborts Startup entirely (no window,
  no map).

  RETIRED as of v1.2.0: places_2.0.0.2.xml. Routing now builds from the main
  places.xml. The old file's DEST_COORD fields were proven axis-transposed and
  hemisphere-stripped against the original 2003 MSIs -- do not ship it, and it
  is ignored if present.

Also required on the user's machine (NOT shipped here -- they install these):
    Decal 3                (provides Decal.Adapter.dll, Decal.FileService.dll)
    VirindiViewService     (provides VirindiViewService.dll)
    An End-of-Retail AC client (for the DAT files the automap reads)

To build the release zip: copy every DLL from bin\Release into this folder
alongside the data files, then zip the whole folder.

TROUBLESHOOTING: "the main window draws nothing" / "a window is gone"
=====================================================================

In-game, in this order:
  1. /mag diag   -- read the "main window backend" line. "legacy DecalInject" on a
     machine with a working dungeon map means VVS wasn't running when the plugin
     started: check Decal Agent > Services has 'Virindi View Service' enabled,
     and note that Decal's 'Disable View Rendering' option makes a legacy-backend
     window draw nothing at all.
  2. /mag reset  -- restores both windows' position, size, un-pins them, turns
     click-through off. (The title-bar thumbtack "pins" a window -- border
     removed, locked; un-pinning by hand needs LEFT-CTRL + click on it.)

If a window is still wrong, VVS's own saved state can be edited directly.
VVS persists per-window state (position, size, pinned, click-through, hidden,
theme) in an SQLite database in the VirindiViewService install folder:

    vvs.s3db  ->  table StoredViewInfo, keyed by "<Assembly>:<Window title>"

Magellan's rows are 'Magellan3:Magellan 3' and 'Magellan3:Magellan Map'.
WITH AC CLOSED, either fix the flags:
    UPDATE StoredViewInfo SET Ghost=0, ClickThrough=0, Enabled=1
     WHERE ViewKey LIKE 'Magellan3:%';
or delete the rows for factory-fresh windows:
    DELETE FROM StoredViewInfo WHERE ViewKey LIKE 'Magellan3:%';
This touches only Magellan's windows; other plugins keep their layouts.

If the window frame/text draws but BUTTONS, EDIT BOXES, LISTS, and the WINDOW
BACKGROUND are missing (world visible through the window body; a pink/magenta
box instead of the title-bar icon; controls flash into view under the mouse):
that is the D3D *texture-loading* layer failing while text/line drawing works
-- the dungeon map still draws because it is pure lines and text. Run
/mag diag and read the "texture self-test" line; then:
  1. Install the legacy "DirectX End-User Runtime (June 2010)" redistributable
     (provides the d3dx9_* components this era of plugins loads textures with).
  2. Re-run the Virindi Bundle installer (refreshes VVS and its theme assets --
     services do NOT auto-update).
  3. If a wrapper (dgVoodoo etc.) or overlay software is in use, test without.
