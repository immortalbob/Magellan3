# Magellan 3

A 2026 rebuild of **Magellan 2** (Adam Wright, 2003) for **Decal 3** on the **End-of-Retail**
Asheron's Call client: a places database, dungeon identification, on-screen coordinates, and a
live dungeon automap generated from the client's own DAT geometry.

The original was a native ATL/COM plugin that drew with GDI onto a device context Decal handed it.
That device context, that DAT format, and that plugin contract are all gone. This is a clean
reimplementation on the modern managed stack — but it starts from the original's data, window, and
algorithm, all recovered from the 2003 binary and verified against independent sources.

## Why it exists

GoArrow already does places and dungeon maps — but its dungeon maps are raster images downloaded
from a CDN that no longer exists. Magellan **generates** its map from `client_cell_1.dat` at
runtime, so it works offline and maps custom ACE-server dungeons nobody has ever drawn. That is the
differentiator; nostalgia is not.

## What's new in v1.2.2

Follow-up from the first v1.2.1 field diag, which cleared the backend/wireup/ghost/alpha layers on
the affected machine and taught us two things:

- **The invisible-window warning no longer fires on `ClickThrough`.** Cross-checking two machines'
  diags against a live `vvs.s3db` proved the runtime `HudView.ClickThrough` property reads True on
  healthy, un-pinned windows whose persisted `StoredViewInfo.ClickThrough` is 0 -- it does not
  mirror the user's click-through toggle, so flagging it was a false positive. It is still
  *reported*, just not alarmed on; the authoritative flag lives only in vvs.s3db.
- **`/mag diag` now reports the main window's active THEME** (reflected from the underlying view).
  With backend, wireup, ghost, and alpha all verified good, the per-window theme -- persisted
  per-machine in vvs.s3db (`ThemeID2`/`IsCustomTheme`) and capable of rendering control bodies
  blank while hover states still draw (cf. the tracker's `ghostytabs` theme knob) -- is the leading
  remaining suspect for "controls only visible under the mouse".
- **`/mag diag` runs a texture-pipeline self-test** (DxTexture create -> Fill -> ACImage from
  portal.dat, each step isolated). Screenshots from the affected machine showed the true
  fingerprint: text and line drawing fine (tabs, coords, the whole dungeon map) while every
  image-backed element was missing -- window background, buttons, edit/list bodies, and the
  magenta missing-texture box where the title-bar icon belongs. That is the D3DX/texture-load
  layer, not core D3D: the usual fix is installing the legacy DirectX End-User Runtime (June 2010)
  and re-running the Virindi Bundle installer, and the self-test names the first broken step in
  one chat line.

## What's new in v1.2.1

A diagnostics-and-visibility release, driven by one field report: "the dungeon map draws but the
main window draws nothing (unless I hover it)". Both windows render through the same VVS -> Direct3D
pipeline, so a working map exonerates the machine's DirectX stack -- the causes live in the windowing
layer, and none of them were observable in-game before this release:

- **The login banner and `/mag diag` now name the main window's renderer.** `ViewSystemSelector`'s
  auto-detect is a point-in-time snapshot at Startup (char-select): VVS must be **loaded and
  `Service.Running`** at that instant, or the window silently falls back to the legacy Decal-injected
  renderer. The automap overlay hard-references VVS and skips the check -- so a bad snapshot produces
  the machine-specific "map fine, main window broken" split. Worse, a legacy-renderer window is
  subject to Decal's "Disable View Rendering" option (common advice for VVS users): with that on, the
  fallen-back window draws NOTHING while the plugin runs normally. The banner reports the chosen
  backend plus the VVS state seen at startup vs now, so the fallback can never be silent again.
- **Automatic backend upgrade at login.** If Startup picked the legacy renderer but VVS is running by
  first login, the main window is rebuilt on VVS (one shot, fully guarded; control caches cleared and
  wireup re-run).
- **New `/mag reset`.** Restores both windows to a known-visible, clickable state: on-screen
  positions, un-pinned, click-through off, full alpha. VVS persists per-window presentation state
  *outside* the plugin -- the title-bar thumbtack pins ("hudifies") a window, stripping its border;
  pinned windows can be made click-through; un-pinning requires holding LEFT-CTRL while clicking the
  thumbtack (virindi.net wiki). A window pinned or faded months ago therefore looks dead through
  every update; the login banner now warns when that state is detected, and `/mag reset` clears it
  from chat -- the only recovery path that works on a click-through window. Reset restores window
  *size* too: VVS persists user-resized dimensions (vvs.s3db `UserW`/`UserH`), so a window once
  shrunk to its title bar stays that way until reset. `/mag diag` prints Magellan's exact vvs.s3db
  row keys for out-of-game repair (see README-RELEASE's troubleshooting section).
- **The map overlay no longer force-moves itself on first show.** It only rescues a window that is
  actually unplaced/off-screen, so a user's saved position is respected -- and the overlay can't park
  itself on top of the main window every session.

## What's new in v1.2.0

Routing was rebuilt after a forensic audit against the original 2003 installers, an original feature
came back, and a batch of polish landed:

- **Route planning is correct now.** It had been geometrically wrong since the feature first shipped:
  the old `places_2.0.0.2.xml` destination fields are axis-transposed *and* drop the north/south
  hemisphere entirely (verified against both 2003 MSIs — 132 of 133 cross-checkable portals).
  Routing now builds from the signed `EXITLOCATION` data in the main `places.xml`: correct
  destinations and a bigger graph — **169** deterministic portal edges, up from 149. Random portals
  are excluded (their in-game exit isn't predictable), and `places_2.0.0.2.xml` is retired.
- **Restored: "Show overland relative co-ordinates."** Recovered to its true 2003 behaviour — check
  it, click any place in Search or Nearby, and the top readout becomes a live
  `Distance to <place>: 2.3N, 1.1E` tracker that updates as you move and ticks toward zero on arrival.
- **The coordinate readout and About title render yellow** as intended (the old colour value was in
  the wrong byte order and displayed cyan), and the About tab no longer truncates mid-sentence.
- **Search and Nearby report their result count in chat** ("7 places for 'holt'" / "No places
  match…"), so an empty search is never mistaken for a dead button, and the login banner shows the
  version — a quick check that you're on the new build.

## Layout

```
Magellan3.sln
├── src/
│   ├── Magellan3.Core/        netstandard2.0 — PURE logic, zero dependencies, fully tested
│   │   ├── World/Coords.cs         landcell <-> Dereth coordinates (verified to a half-cell)
│   │   ├── Data/PlacesDb.cs        places.xml loader: search + radius query + errata
│   │   ├── Data/DungeonNames.cs    653 landblock -> dungeon-name lookups
│   │   ├── Mapping/OutlineBuilder  landblock-scoped floor-outline extraction (the seam fix)
│   │   ├── Mapping/AutomapRenderer heading-up projection + Z-slice (the rotation fix)
│   │   └── Config/Settings.cs      the 2003 config.xml format, round-tripped
│   └── Magellan3.Plugin/      net48, x86 — the Decal glue (Windows-only)
│       ├── PluginCore.cs           PluginBase; events, /mag command, view control wiring
│       ├── Mapping/DatSource.cs    FileService + offline DAT access (+ the phase check)
│       ├── Mapping/DungeonMapper   the recovered algorithm, driving OutlineBuilder
│       ├── Ui/DxCanvas.cs          IMapCanvas over VVS DxTexture
│       ├── Ui/MapOverlay.cs        borderless heading-up HUD, bake-once/blit-per-frame
│       ├── VirindiViews/           MetaViewWrappers shim (MIT, from Mag-nus/DecalPluginTemplates)
│       └── Resources/mainView.xml  the RECOVERED view, verbatim, embedded
├── tests/Magellan3.Tests/     net8.0 — 73 tests, no packages, runs anywhere
├── data/                      places.xml · dungeon_names.tsv · mainView.xml
└── run-tests.sh               offline test runner (no NuGet feed needed)
```

The dependency arrow points one way: `Core` knows nothing about Decal, VVS, or DatReaderWriter.
Everything that can be tested without a game is in `Core`, and it is.

## Test

```bash
./run-tests.sh        # 73/73, no NuGet feed required
```

Or, with a feed available:

```bash
dotnet test           # once tests/Magellan3.Tests is restored normally
```

The suite pins the things that were wrong or unknowable before, to ground truth that has nothing to
do with Magellan: the coordinate transform against the four `<LOC>` records and the Aphus Lassel
landblock; the `COORD_X`/`COORD_Y` axes against a 2002 third-party location list; the automap's
heading rotation against a "face east, a point due east must render ahead" check; and the
floor-outline seam-cancellation against a two-cell room that must read as one outline, not two boxes.

## Install (for users)

**Prerequisites** (install these first — Magellan does not bundle them):

- **Asheron's Call**, an **End-of-Retail** client (the automap reads its `client_cell_1.dat`).
- **Decal 3** — the plugin framework.
- **VirindiViewService (VVS)** — the UI/drawing toolkit the map and window use.

**Then install Magellan 3:**

1. Download the latest release archive from the [Releases](../../releases) page and unzip it. It
   contains everything you need:
   - `Magellan3.dll` — the plugin
   - `Magellan3.Core.dll` — its logic library
   - `DatReaderWriter.dll` — the DAT reader (for the automap)
   - `places.xml`, `dungeon_names.tsv` — the places and dungeon-name data (routing builds from `places.xml` too)
2. Put **all of those files together** in one folder (e.g. `C:\Games\Magellan3\`). They must sit
   side by side — the plugin loads the data files from beside the DLL, and won't work with just the DLL.
3. Register the plugin with the **32-bit** RegAsm, run from that folder as **Administrator**:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe /codebase Magellan3.dll
   ```
   (You must have `Decal.Adapter.dll`, `Decal.FileService.dll`, and `VirindiViewService.dll`
   reachable — the simplest way is to copy those three next to `Magellan3.dll` before registering.)
4. Enable **Magellan 3** in Decal's plugin list, then launch AC.
5. The Magellan window opens from its icon in the VVS bar. Search a place, enter a dungeon to see
   the automap, or run `/mag diag` to check status.

Settings you change (Show map, footsteps, lock rotation, relative co-ordinates) are saved automatically to
`%AppData%\Magellan3\config.xml` and persist across logins.

## Build from source (Windows)

Requires Visual Studio / `dotnet` on Windows, an installed Decal 3 and VVS, and an EoR client.

1. Edit the three `HintPath`s in `src/Magellan3.Plugin/Magellan3.Plugin.csproj` to point at your
   installed `Decal.Adapter.dll`, `Decal.FileService.dll`, and `VirindiViewService.dll`.
2. `dotnet build src\Magellan3.Plugin\Magellan3.Plugin.csproj -c Release` (or build the solution in
   VS; the plugin is `Release|x86`). `Chorizite.DatReaderWriter` restores from NuGet. The build copies
   the two data files (`places.xml`, `dungeon_names.tsv`) next to the DLL (routing builds from `places.xml`).
3. Copy `Decal.Adapter.dll`, `Decal.FileService.dll`, and `VirindiViewService.dll` next to the built
   `Magellan3.dll`, then register with the **32-bit** RegAsm as Administrator, from `bin\Release`:
   ```
   C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe /codebase Magellan3.dll
   ```
4. Enable **Magellan 3** in Decal's Manage Plugins UI, then launch AC.
5. In-game, run **`/mag diag`** for status, or **`/mag phase`** to verify the DAT read (see below).

The automap is on by default (`MAGELLAN_AUTOMAP` in `<DefineConstants>`); the logic layer is covered
by `run-tests.sh` (73 tests), which runs on any platform without Decal, VVS, or NuGet.

### One thing to verify in-game, once

DatReaderWriter's `Unpack` reads a leading id DWORD for `HasId` records (EnvCell, Environment,
LandBlockInfo all qualify). Whether Decal's `FileService.GetCellFile` returns that header or strips
it decides a 4-byte parse phase. On first run, confirm and set:

```csharp
byte[] b = CoreManager.Current.FileService.GetCellFile(knownCellId);
DecalDatSource.FileServiceIncludesIdHeader = DecalDatSource.HeaderPresent(b, (uint)knownCellId);
```

If it comes back wrong, geometry silently decodes into plausible garbage — so prove it once. See the
note in `Mapping/DatSource.cs`.

## Feature status — all working in-game

| Feature | State |
|---|---|
| Places database + search (3,307 entries) | **done, working in-game** |
| Click a result → coordinates in chat | **done, working in-game** |
| Dungeon identification (653 names) | **done, working in-game** |
| Always-on coordinate readout (top of window) | **done, working in-game** |
| "Relative co-ordinates" distance tracker (Options tab) | **done, working in-game** — live distance-to-selected-place readout |
| Route finding (169-portal Dijkstra) | **done, working in-game** |
| Automatic dungeon mapping (the automap) | **done, working in-game** — reads `client_cell_1.dat` at runtime |
| Footstep trail (Z-sliced by floor) | **done, working in-game** |
| Settings persist across logins | **done** — saved to `%AppData%\Magellan3\config.xml` |

Every original Magellan 2 feature is present, plus a 653-name dungeon database (vs the original's
118), Z-sliced trails, the restored "relative co-ordinates" distance tracker, and an About tab. The
full logic layer is unit-tested (`run-tests.sh`, 73 tests). The one thing that differs from the
original by necessity: the on-screen coordinate readout lives at the top of the plugin window rather
than as a separate floating overlay (a second VVS window proved unstable on the current client).

## Credits

- **Adam Wright** — Magellan 2 (2003): the original places database, window, and automap algorithm.
- **Immortalbob** — the 2026 resurrection: reverse-engineering the 2003 binary and rebuilding the
  whole plugin on the modern Decal 3 / End-of-Retail managed stack.
- **Chorizite** — `DatReaderWriter` (MIT): the DAT parsing.
- **DungeonViewer** authors — `dungeons.dvp` (653 dungeon names) and the DM→ToD format deltas, used
  as documentation.
- **Virindi Plugins / Mag-nus** — VirindiViewService and the MetaViewWrappers template.

## License

Released under the [MIT License](LICENSE).

`DatReaderWriter` (bundled at runtime) is also MIT-licensed by Chorizite. Decal and VirindiViewService
are separate installs with their own terms and are not distributed here.
