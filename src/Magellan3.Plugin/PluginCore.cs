using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using Magellan.Config;
using Magellan.Data;
using Magellan.Mapping;
using Magellan.Plugin.Mapping;
using Magellan.Plugin.Ui;
using MyClasses.MetaViewWrappers;

namespace Magellan.Plugin
{
    /// <summary>
    /// Magellan 3 -- the plugin shell. Decal calls Startup/Shutdown; everything else hangs off events.
    ///
    /// The heavy lifting lives in Magellan3.Core (pure, tested) and in DungeonMapper / MapOverlay
    /// (DAT + VVS glue). This class is deliberately thin: load data, wire events, route control clicks,
    /// and keep the map fed -- with every Decal-facing method wrapped in try/catch, because an escaped
    /// exception takes the user's client down with it.
    ///
    /// Registration (managed, fresh GUID -- never reuse the 2003 CLSID {25846788-...}):
    ///   Software\Decal\Plugins\{THIS GUID}
    ///     (Default) = "Magellan"          Object    = Magellan.Plugin.PluginCore
    ///     Assembly  = <dir>\Magellan3.dll  Path      = <dir>
    ///     Surrogate = {71A69713-6593-47EC-0002-0000000DECA1}   (DecalAdapter.Surrogate)
    ///     Enabled   = 1
    ///   Build x86, [ComVisible(true)], register with the 32-bit RegAsm /codebase.
    /// </summary>
    [FriendlyName("Magellan")]
    [Guid("0B1E9E2C-4C2E-4E7B-9E9E-4D3A1B2C3D40")]   // fresh -- distinct from the native 2003 CLSID
    [WireUpBaseEvents]
    [MVView("Magellan.Plugin.Resources.mainView.xml")]
    [MVWireUpControlEvents]
    public class PluginCore : PluginBase
    {
        internal static PluginCore Instance;

        private PlacesDb _places;
        private DungeonNames _dungeons;
        private Magellan.Routing.RoutePlanner _planner;

        // Route tab state: start/end coordinates the user has set.
        private bool _haveRouteStart, _haveRouteEnd;
        private double _routeStartNS, _routeStartEW, _routeEndNS, _routeEndEW;
        private Settings _settings;

        private DungeonMapper _mapper;
        private AutomapRenderer _renderer;
        private Magellan.Plugin.Ui.IMapOverlay _overlay;
        private Decal.Filters.FileService _fileService;

        private string _pluginDir;
        private string _configPath;

        // Current landblock state (updated as the player moves).
        private uint _landblock;
        private DungeonGeometry _geo = DungeonGeometry.Empty;

        // ---------------------------------------------------------------- lifecycle

        protected override void Startup()
        {
            Instance = this;

            // Subscribe to the command handler FIRST, outside the main try, so /mag phase and
            // /mag diag are available even if something later in Startup throws -- they're our
            // in-game diagnostics, and they must not depend on a fully-successful init.
            try { CoreManager.Current.CommandLineText += CoreManager_CommandLineText; }
            catch (Exception ex) { Fail("hook CommandLineText", ex); }

            // RenderFrame drives the live automap: it fires every rendered frame, so we poll the
            // player's position/heading here (throttled, cheap-only) and the overlay repaints. Without
            // it the map draws once and goes static. Subscribed REFLECTIVELY and optionally: if this
            // Decal build names the event differently or uses a different delegate, we simply skip the
            // live-refresh feature (the map still updates on movement events) rather than crash.
            TryHookRenderFrame();

            try
            {
                _pluginDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                // config.xml lives in a stable, always-writable per-user location (%AppData%\Magellan3),
                // NOT next to the DLL: the bin folder may be read-only and is overwritten on every
                // rebuild, which silently lost settings. Migrate a config found beside the DLL once.
                _configPath = ResolveConfigPath();

                LoadData();

                _settings = Settings.Load(_configPath);
                _renderer = new AutomapRenderer
                {
                    LockRotation = _settings.LockRotation,
                    ShowTrail = _settings.ShowFootsteps,
                    PixelsPerMetre = _settings.MapScale,
                    SliceByFloor = _settings.SliceByFloor,
                    SliceHeight = _settings.SliceHeight,
                };

                // DAT access through Decal's FileService. CoreManager.Current.FileService returns the
                // base FilterBase; the concrete Decal.Filters.FileService (in Decal.FileService.dll)
                // is what actually has GetCellFile/GetPortalFile -- so cast once and cache it. Isolated
                // so a cast/type surprise can't take down the whole plugin or the command handler.
                try
                {
                    _fileService = CoreManager.Current.FileService as Decal.Filters.FileService;
                    if (_fileService == null)
                        Chat("warning: FileService is " +
                             (CoreManager.Current.FileService == null ? "null" : CoreManager.Current.FileService.GetType().FullName) +
                             " -- dungeon maps and /mag phase need Decal.Filters.FileService.");
                }
                catch (Exception ex) { Fail("FileService cast", ex); }

                var dat = new DecalDatSource(
                    id => _fileService != null ? _fileService.GetCellFile((int)id) : null,
                    id => _fileService != null ? _fileService.GetPortalFile((int)id) : null);
                _mapper = new DungeonMapper(dat);

                // The map overlay is the one part that draws with Direct3D via VVS. In the default
                // build it's a no-op (Stages A-E work without any VVS drawing); define MAGELLAN_AUTOMAP
                // to link the real DxTexture overlay (Stage F). A VVS API mismatch in the overlay must
                // not sink the whole plugin, so its view creation is isolated in try/catch.
#if MAGELLAN_AUTOMAP
                _overlay = new Magellan.Plugin.Ui.VvsMapOverlay(_renderer);
#else
                _overlay = new Magellan.Plugin.Ui.NullMapOverlay();
#endif
                try { _overlay.CreateView(); }
                catch (Exception ex) { Chat("automap overlay unavailable: " + ex.Message); }
                _overlay.Visible = _settings.ShowMap;

                // The always-on coordinate readout is now the txtCoordReadout StaticText at the top of
                // the main window (updated each frame in UpdateCoordReadout). A separate standalone VVS
                // HudView for it crashed the client, so it lives in the window VVS already manages.

                // MetaViewWrappers renders the recovered mainView.xml through VVS (or native
                // DecalControls if VVS is absent). Control clicks arrive via [MVControlEvent] below.
                // Isolated: if control wiring throws, the plugin and its commands still work.
                try { MVWireupHelper.WireupStart(this, Host); }
                catch (Exception ex) { Fail("view wireup", ex); }

                // OnUpdateCell (rebuild the map as the client streams cells in) is an OPTIMISATION,
                // not a requirement -- it removes the original's "map appears on second visit" quirk.
                // The concrete FileService in this Decal build may expose it as a COM event rather
                // than a managed one, so we wire it reflectively and simply skip it if absent. The
                // map still works without it (it just rebuilds on landblock change / a re-open).
                TryHookUpdateCell();

                // NOTE: the load banner is printed from OnLoginComplete, not here. Startup runs at the
                // character-select screen, before the in-world chat window exists, so AddChatText here
                // would go nowhere. We defer the banner to first login, when chat output actually shows.
                _startupOk = true;
            }
            catch (Exception ex)
            {
                Fail("Startup", ex);
            }
        }

        // Set true when Startup completes; the banner (deferred to login) reports this.
        private bool _startupOk;
        private bool _bannerShown;
        private bool _checkboxesInitialized;   // true once checkboxes have been set from loaded settings

        protected override void Shutdown()
        {
            try
            {
                if (_settings != null) { SyncSettingsFromUi(); _settings.Save(_configPath); }

                if (CoreManager.Current != null)
                {
                    CoreManager.Current.CommandLineText -= CoreManager_CommandLineText;
                }
                TryUnhookRenderFrame();
                TryUnhookUpdateCell();

                MVWireupHelper.WireupEnd(this);
                if (_overlay != null) _overlay.Dispose();
            }
            catch (Exception ex)
            {
                Fail("Shutdown", ex);
            }
        }

        // Decides where config.xml lives: %AppData%\Magellan3\config.xml (always writable, survives
        // rebuilds). If a config exists next to the DLL from an older build, migrate it once. Falls
        // back to the DLL folder only if AppData is somehow unavailable.
        private string ResolveConfigPath()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    string dir = System.IO.Path.Combine(appData, "Magellan3");
                    System.IO.Directory.CreateDirectory(dir);
                    string target = System.IO.Path.Combine(dir, "config.xml");

                    // One-time migration from the old location beside the DLL.
                    string legacy = System.IO.Path.Combine(_pluginDir, "config.xml");
                    if (!System.IO.File.Exists(target) && System.IO.File.Exists(legacy))
                    {
                        try { System.IO.File.Copy(legacy, target); } catch { }
                    }
                    return target;
                }
            }
            catch { }
            // Fallback: beside the DLL (previous behaviour).
            return System.IO.Path.Combine(_pluginDir, "config.xml");
        }

        private void LoadData()
        {            _places = PlacesDb.FromFile(System.IO.Path.Combine(_pluginDir, "places.xml"));
            _dungeons = DungeonNames.FromFile(System.IO.Path.Combine(_pluginDir, "dungeon_names.tsv"));

            // Routing data is optional: the portal graph (DEST_COORD_X/Y) only exists in the 2.0.0.2
            // places file. If it's shipped alongside the plugin, routing lights up; if not, the Route
            // tab reports that the data is missing and everything else works unchanged.
            try
            {
                string portalPath = System.IO.Path.Combine(_pluginDir, "places_2.0.0.2.xml");
                if (System.IO.File.Exists(portalPath))
                {
                    var pdb = Magellan.Routing.PortalDb.LoadFromFile(portalPath);
                    _planner = new Magellan.Routing.RoutePlanner(pdb.Portals);
                }
            }
            catch { _planner = null; }
        }

        // ---------------------------------------------------------------- world events

        [BaseEvent("LoginComplete", "CharacterFilter")]
        private void OnLoginComplete(object sender, EventArgs e)
        {
            try
            {
                if (!_bannerShown)
                {
                    _bannerShown = true;
                    if (_startupOk && _places != null && _dungeons != null)
                    {
                        Chat("Magellan 3 loaded. " + _places.All.Count + " places, " + _dungeons.Count + " dungeon names.");
                        if (_places.CorrectionsApplied > 0)
                            Chat("Repaired " + _places.CorrectionsApplied + " off-map coordinate(s) at load.");
                        Chat("Commands: /mag phase (verify DAT read), /mag diag (status), /mag map (toggle map).");
                    }
                    else
                    {
                        Chat("Magellan 3: startup did not complete cleanly -- try /mag diag for status.");
                    }
                }

                PushSettingsToCheckboxes();
                RefreshLandblock(force: true);
            }
            catch (Exception ex) { Fail("LoginComplete", ex); }
        }

        /// <summary>Reflect the loaded config in the Options tab (the view XML has no persisted state of its own).</summary>
        // The [MVControlReference] fields bind at wireup time -- but controls on tabs other than the
        // first (our checkboxes live on the Options tab) may not be instantiated yet then, so those
        // fields come back null ("NOT BOUND"). Resolve them lazily through the view indexer instead:
        // by the time we read/write a checkbox, the control exists and the indexer finds it. Cached.
        private ICheckBox Chk(ref ICheckBox cached, string name)
        {
            if (cached != null) return cached;
            try
            {
                var view = MVWireupHelper.GetDefaultView(this);
                if (view != null) cached = view[name] as ICheckBox;
            }
            catch { }
            return cached;
        }

        // Same lazy-resolution trick for the More Details static-text fields: they live on a non-default
        // tab, so their [MVControlReference] fields are null at wireup time. Resolve on demand instead.
        private IStaticText Txt(ref IStaticText cached, string name)
        {
            if (cached != null) return cached;
            try
            {
                var view = MVWireupHelper.GetDefaultView(this);
                if (view != null) cached = view[name] as IStaticText;
            }
            catch { }
            return cached;
        }

        // Lazy resolvers for the list and edit controls too. The wireup binding pass aborts on the
        // first control it can't resolve (a not-yet-realized tab control), so ANY reference -- even on
        // the default tab -- can end up null. Resolving on demand is immune to that ordering.
        private IList Lst(ref IList cached, string name)
        {
            if (cached != null) return cached;
            try
            {
                var view = MVWireupHelper.GetDefaultView(this);
                if (view != null) cached = view[name] as IList;
            }
            catch { }
            return cached;
        }

        private ITextBox Edt(ref ITextBox cached, string name)
        {
            if (cached != null) return cached;
            try
            {
                var view = MVWireupHelper.GetDefaultView(this);
                if (view != null) cached = view[name] as ITextBox;
            }
            catch { }
            return cached;
        }

        private void PushSettingsToCheckboxes()
        {
            var a = Chk(ref _chkShowMap, "chkShowMap");           if (a != null) a.Checked = _settings.ShowMap;
            var b = Chk(ref _chkFootsteps, "chkFootsteps");       if (b != null) b.Checked = _settings.ShowFootsteps;
            var c = Chk(ref _chkLockRotation, "chkLockRotation"); if (c != null) c.Checked = _settings.LockRotation;
            var d = Chk(ref _chkRelCoords, "chkRelCoords");       if (d != null) d.Checked = _settings.RelCoords;

            // Only allow the frame-loop poll to start reacting to checkbox changes once every checkbox
            // has actually been resolved AND set from the loaded settings. Until then the checkboxes may
            // read their XML defaults, and polling would corrupt the persisted config (see PollCheckboxes).
            if (a != null && b != null && c != null && d != null)
                _checkboxesInitialized = true;
        }

        // Reflectively attached OnUpdateCell delegate, if the event exists in this Decal build.
        private Delegate _updateCellHandler;

        /// <summary>
        /// Subscribe to FileService.OnUpdateCell if this Decal build exposes it as a managed event.
        /// Non-fatal: if the event isn't present (or is COM-only), we just don't get the streaming
        /// refresh -- the map still rebuilds on landblock change. Reflection keeps a hard reference
        /// to the event out of the compile, so a missing member can never break the build or load.
        /// </summary>
        private void TryHookUpdateCell()
        {
            try
            {
                if (_fileService == null) return;
                var ev = _fileService.GetType().GetEvent("OnUpdateCell");
                if (ev == null) return;

                var handlerType = ev.EventHandlerType;
                var mi = typeof(PluginCore).GetMethod(nameof(FileService_OnUpdateCell),
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _updateCellHandler = Delegate.CreateDelegate(handlerType, this, mi, throwOnBindFailure: false);
                if (_updateCellHandler != null)
                    ev.AddEventHandler(_fileService, _updateCellHandler);
                else
                    _updateCellHandler = null;
            }
            catch { _updateCellHandler = null; }   // never let an optional optimisation break Startup
        }

        private void TryUnhookUpdateCell()
        {
            try
            {
                if (_fileService == null || _updateCellHandler == null) return;
                var ev = _fileService.GetType().GetEvent("OnUpdateCell");
                if (ev != null) ev.RemoveEventHandler(_fileService, _updateCellHandler);
            }
            catch { }
            finally { _updateCellHandler = null; }
        }

        /// <summary>Fires when the client streams DAT cells in -- the fix for the 2003 "second visit" quirk.</summary>
        private void FileService_OnUpdateCell(object sender, EventArgs e)
        {
            try
            {
                if (_geo != null && _geo.MissingCells > 0)
                    RefreshLandblock(force: true);
            }
            catch (Exception ex) { Fail("OnUpdateCell", ex); }
        }

        /// <summary>
        /// Poll the player's landblock. Call from a movement/heartbeat hook (RenderFrame-throttled or a
        /// timer); when the landblock changes, rebuild the geometry off the render thread.
        /// </summary>
        // Reflective, fail-safe RenderFrame wiring. If the event is absent or its delegate isn't a
        // plain EventHandler on this Decal build, we skip it (no crash); the map still updates on
        // movement/landblock events. Reported via _renderFrameHooked in /mag diag.
        private Delegate _renderFrameDelegate;
        private bool _renderFrameHooked;

        private void TryHookRenderFrame()
        {
            try
            {
                var core = CoreManager.Current;
                var ev = core.GetType().GetEvent("RenderFrame");
                if (ev == null) return;
                // Only attach if the event's delegate type is compatible with our handler.
                Delegate d;
                try { d = Delegate.CreateDelegate(ev.EventHandlerType, this, "CoreManager_RenderFrame"); }
                catch { return; }   // signature mismatch -> skip the feature
                ev.AddEventHandler(core, d);
                _renderFrameDelegate = d;
                _renderFrameHooked = true;
            }
            catch { _renderFrameHooked = false; }
        }

        private void TryUnhookRenderFrame()
        {
            try
            {
                if (_renderFrameDelegate == null || CoreManager.Current == null) return;
                var ev = CoreManager.Current.GetType().GetEvent("RenderFrame");
                if (ev != null) ev.RemoveEventHandler(CoreManager.Current, _renderFrameDelegate);
            }
            catch { }
            finally { _renderFrameDelegate = null; _renderFrameHooked = false; }
        }

        // Throttle RenderFrame work: it fires every rendered frame, on AC's MAIN RENDER THREAD. The
        // reference is explicit that any slow synchronous work here (DAT reads, landblock rebuilds)
        // STALLS the frame. So this handler does ONLY cheap work: read the player's current
        // position/heading (already-in-memory Actions values) and push them to the overlay, then let
        // the overlay invalidate itself. Geometry rebuilds (DAT) happen on movement/landblock-change
        // events elsewhere -- NEVER here.
        private int _lastFrameTick;

        // Poll the Options checkboxes against settings and apply changes. Runs at 5 Hz from the frame
        // loop. This replaces the unreliable VVS checkbox Change event: reading Checked works fine, so
        // we detect the user toggling a box within ~200ms and act, no event needed.
        private void PollCheckboxes()
        {
            // CRITICAL: do not poll until the checkboxes have been initialized FROM settings at least
            // once (PushSettingsToCheckboxes at login). A freshly-created checkbox reads its XML default
            // (unchecked), and the frame loop starts at char-select -- before login. Without this guard,
            // the very first poll sees checkbox=false vs loaded-setting=true, mistakes it for the user
            // un-checking the box, overwrites the setting, and SAVES the wrong value -- destroying the
            // persisted config before login even finishes. This was the "settings don't persist" bug.
            if (!_checkboxesInitialized) return;
            try
            {
                bool changed = false;

                var a = Chk(ref _chkShowMap, "chkShowMap");
                if (a != null && a.Checked != _settings.ShowMap)
                {
                    _settings.ShowMap = a.Checked;
                    changed = true;
                    if (_overlay != null)
                    {
                        if (!_settings.ShowMap) _overlay.Visible = false;
                        else if (LandDefs.IsInterior(CurrentLandcell())) _overlay.Visible = true;
                    }
                }

                var b = Chk(ref _chkFootsteps, "chkFootsteps");
                if (b != null && b.Checked != _settings.ShowFootsteps)
                {
                    _settings.ShowFootsteps = b.Checked;
                    _renderer.ShowTrail = b.Checked;
                    if (!b.Checked) _renderer.ClearTrail();
                    changed = true;
                }

                var c = Chk(ref _chkLockRotation, "chkLockRotation");
                if (c != null && c.Checked != _settings.LockRotation)
                {
                    _settings.LockRotation = c.Checked;
                    _renderer.LockRotation = c.Checked;
                    changed = true;
                }

                var d = Chk(ref _chkRelCoords, "chkRelCoords");
                if (d != null && d.Checked != _settings.RelCoords)
                {
                    _settings.RelCoords = d.Checked;
                    changed = true;
                }

                // Persist the moment anything changes -- don't rely on Shutdown (a client crash or
                // force-close would otherwise lose the change). Cheap: a tiny XML file, only on a diff.
                if (changed) SaveSettings();
            }
            catch { }
        }

        // Writes config.xml. Safe to call often; guarded so a write failure never disrupts play.
        private void SaveSettings()
        {
            try { if (_settings != null && _configPath != null) _settings.Save(_configPath); }
            catch { }
        }

        // Builds and pushes the always-on coordinate readout text into the main window's top StaticText.
        // Cheap: reads position, no DAT. Resolved lazily (nested control) so it binds regardless of tab.
        private void UpdateCoordReadout()
        {
            var ctl = Txt(ref _txtCoordReadout, "txtCoordReadout");
            if (ctl == null) return;
            try
            {
                uint lc = CurrentLandcell();
                string text;
                if (LandDefs.IsInterior(lc))
                {
                    // Indoors: overland coords are meaningless (they'd map to ocean). Show the dungeon
                    // name if we know it, else the raw landcell.
                    string name = _dungeons != null ? _dungeons.Caption(lc) : null;
                    text = "In: " + (!string.IsNullOrEmpty(name) ? name : ("Landcell " + lc.ToString("X8")));
                }
                else
                {
                    double ns, ew;
                    Magellan.World.Coords.FromPosition(
                        lc,
                        CoreManager.Current.Actions.LocationX,
                        CoreManager.Current.Actions.LocationY,
                        out ns, out ew);

                    // Relative option: when RelCoords is on and a route start is set, show the offset
                    // from that start; otherwise absolute coords.
                    if (_settings.RelCoords && _haveRouteStart)
                        text = "Rel: " + Magellan.World.Coords.Format(ns - _routeStartNS, ew - _routeStartEW);
                    else
                        text = "Coords: " + Magellan.World.Coords.Format(ns, ew);
                }
                ctl.Text = text;
            }
            catch { }
        }

        private void CoreManager_RenderFrame(object sender, EventArgs e)
        {
            try
            {
                if (_overlay == null || _settings == null) return;

                int now = Environment.TickCount;
                if (unchecked(now - _lastFrameTick) < 200) return;   // ~5 Hz
                _lastFrameTick = now;

                // Poll the Options checkboxes and apply any changes. VVS's checkbox Change event
                // isn't reaching our handlers reliably, but the control's Checked state IS readable,
                // so we just diff it against our settings each tick -- robust regardless of events.
                // Until the checkboxes have been initialized from the loaded settings, keep trying to do
                // so (they're on the Options tab and resolve lazily, so it may take a few ticks after
                // login). PollCheckboxes is a no-op until this succeeds, so it can't corrupt the config.
                if (!_checkboxesInitialized) PushSettingsToCheckboxes();

                PollCheckboxes();

                // Update the always-on coordinate readout every tick, regardless of map state. On the
                // surface: overland coords (absolute, or relative-to-start when RelCoords is on). Indoors:
                // there are no meaningful overland coords, so show the dungeon name / landcell instead.
                UpdateCoordReadout();

                if (!_settings.ShowMap) { if (_overlay.Visible) _overlay.Visible = false; return; }

                uint lc = CurrentLandcell();
                uint lb = LandDefs.LandblockOf(lc);

                if (lb != _landblock)
                {
                    // Landblock changed (entered a dungeon, teleported to a new one, or stepped onto the
                    // surface). Rebuild geometry + clear the old trail + show/hide the map. The DAT Build
                    // happens ONCE per transition, not per frame, so it's fine to do here.
                    _landblock = lb;
                    if (LandDefs.IsInterior(lc))
                    {
                        _geo = _mapper.Build(lc);
                        _renderer.ClearTrail();
                        _overlay.SetGeometry(_geo, 0, 0, 0, 0);   // push the new walls to the overlay
                        _overlay.SetTitle(_dungeons.Caption(lc));  // dungeon name/hex in the title bar
                        _overlay.Visible = true;
                    }
                    else
                    {
                        _geo = DungeonGeometry.Empty;
                        _renderer.ClearTrail();
                        _overlay.SetGeometry(_geo, 0, 0, 0, 0);
                        _overlay.Visible = false;                 // no automap on the surface
                    }
                }

                // Cheap per-frame update: only meaningful indoors, but harmless otherwise.
                if (LandDefs.IsInterior(lc))
                {
                    float x = (float)CoreManager.Current.Actions.LocationX;
                    float y = (float)CoreManager.Current.Actions.LocationY;
                    float z = (float)CoreManager.Current.Actions.LocationZ;
                    float h = (float)CoreManager.Current.Actions.Heading;
                    if (_settings.ShowFootsteps) _renderer.PushFootstep(x, y, z);
                    _overlay.SetFrameState(_geo, x, y, z, h, _dungeons.Caption(lc));
                }
            }
            catch { /* never let a per-frame handler throw into the client's render loop */ }
        }

        public void RefreshLandblock(bool force = false)
        {
            uint landcell = unchecked((uint)CoreManager.Current.Actions.Landcell);
            uint lb = LandDefs.LandblockOf(landcell);

            if (!force && lb == _landblock) { UpdateFrameState(landcell); return; }
            _landblock = lb;

            if (LandDefs.IsInterior(landcell))
            {
                _geo = _mapper.Build(landcell);                       // blocking; acceptable on landblock change
                _renderer.ClearTrail();
                _overlay.Visible = _settings.ShowMap;
            }
            else
            {
                _geo = DungeonGeometry.Empty;
                _overlay.Visible = false;                              // no automap on the surface
            }

            UpdateFrameState(landcell);
        }

        private void UpdateFrameState(uint landcell)
        {
            float x = (float)CoreManager.Current.Actions.LocationX;
            float y = (float)CoreManager.Current.Actions.LocationY;
            float z = (float)CoreManager.Current.Actions.LocationZ;
            float h = (float)CoreManager.Current.Actions.Heading;

            if (LandDefs.IsInterior(landcell))
            {
                if (_settings.ShowFootsteps) _renderer.PushFootstep(x, y, z);
                string caption = _dungeons.Caption(landcell);
                _overlay.SetGeometry(_geo, x, y, z, h);
                _overlay.SetFrameState(_geo, x, y, z, h, caption);
            }
        }

        // ---------------------------------------------------------------- commands

        private void CoreManager_CommandLineText(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                var t = (e.Text ?? "").Trim();
                if (!t.StartsWith("/mag", StringComparison.OrdinalIgnoreCase)) return;
                e.Eat = true;

                var arg = t.Length > 4 ? t.Substring(4).Trim() : "";
                if (arg.Equals("map", StringComparison.OrdinalIgnoreCase))
                {
                    _settings.ShowMap = !_settings.ShowMap;
                    if (_settings.ShowMap)
                    {
                        // Force a geometry build + push for wherever we're standing, so toggling the
                        // map works even if we loaded while already inside this landblock (in which
                        // case RefreshLandblock never fired for a *changed* block).
                        uint lc = CurrentLandcell();
                        if (LandDefs.IsInterior(lc))
                        {
                            _landblock = LandDefs.LandblockOf(lc);
                            _geo = _mapper.Build(lc);
                            UpdateFrameState(lc);
                            _overlay.Visible = true;
                            Chat("Map on. geometry: " + (_geo != null ? _geo.Edges.Count : 0) + " edges, overlay.Visible=" + _overlay.Visible);
                            Chat("  overlay status: " + _overlay.Status);
                        }
                        else
                        {
                            _overlay.Visible = false;
                            Chat("Map on, but you're not in a dungeon -- nothing to draw here.");
                        }
                    }
                    else
                    {
                        _overlay.Visible = false;
                        Chat("Map off.");
                    }
                }
                else if (arg.Equals("phase", StringComparison.OrdinalIgnoreCase))
                {
                    RunPhaseCheck();
                }
                else if (arg.Equals("ctl", StringComparison.OrdinalIgnoreCase))
                {
                    // Report the ACTUAL VVS control type for each named control (via reflection, so this
                    // compiles without referencing the VVS-only shim namespace). Reveals why some
                    // controls resolve and some don't -- the shim's indexer maps by concrete VVS type.
                    try
                    {
                        object view = MVWireupHelper.GetDefaultView(this);
                        object underlying = view != null ? view.GetType().GetProperty("Underlying")?.GetValue(view) : null;
                        var idx = underlying != null ? underlying.GetType().GetProperty("Item", new[] { typeof(string) }) : null;
                        foreach (var nm in new[] { "txtQuery", "lstResults", "btnFind", "txtInfo", "chkShowMap", "txtCoordReadout", "txtAboutTitle" })
                        {
                            string tn;
                            try
                            {
                                object c = idx != null ? idx.GetValue(underlying, new object[] { nm }) : null;
                                tn = c == null ? "NULL" : c.GetType().Name;
                            }
                            catch (Exception ix) { tn = "EX:" + (ix.InnerException ?? ix).GetType().Name; }
                            Chat("  " + nm + " -> " + tn);
                        }
                    }
                    catch (Exception ex) { Chat("ctl probe failed: " + ex.Message); }
                }
                else if (arg.Equals("opt", StringComparison.OrdinalIgnoreCase))
                {
                    // Force-resolve (and thereby wire) the checkboxes through the view indexer, then report.
                    var a = Chk(ref _chkShowMap, "chkShowMap");
                    var b = Chk(ref _chkFootsteps, "chkFootsteps");
                    var c = Chk(ref _chkLockRotation, "chkLockRotation");
                    var d = Chk(ref _chkRelCoords, "chkRelCoords");
                    Chat("checkbox bindings: "
                        + "ShowMap=" + (a != null ? a.Checked.ToString() : "NOT BOUND")
                        + ", Footsteps=" + (b != null ? b.Checked.ToString() : "NOT BOUND")
                        + ", LockRot=" + (c != null ? c.Checked.ToString() : "NOT BOUND")
                        + ", RelCoords=" + (d != null ? d.Checked.ToString() : "NOT BOUND"));
                    Chat("settings now: ShowMap=" + _settings.ShowMap + ", Footsteps=" + _settings.ShowFootsteps
                        + ", LockRot=" + _settings.LockRotation + ", RelCoords=" + _settings.RelCoords);
                    // Also report the Search/Nearby/Details control bindings -- via the LAZY resolvers
                    // (same path the buttons use), so this tests what actually happens on a click.
                    var rl = Lst(ref _lstResults, "lstResults");
                    var rq = Edt(ref _txtQuery, "txtQuery");
                    var rp = Lst(ref _lstProxResults, "lstProxResults");
                    var ri = Txt(ref _txtInfo, "txtInfo");
                    Chat("controls (lazy): lstResults=" + (rl != null ? "OK" : "null")
                        + ", txtQuery=" + (rq != null ? "OK" : "null")
                        + ", lstProx=" + (rp != null ? "OK" : "null")
                        + ", txtInfo=" + (ri != null ? "OK" : "null"));
                }
                else if (arg.Equals("diag", StringComparison.OrdinalIgnoreCase))
                {
                    RunDiagnostics();
                }
                else
                {
                    Chat("Magellan: /mag map (toggle dungeon map)  /mag phase (verify DAT read)  /mag diag (status). Use the Magellan window for search.");
                }
            }
            catch (Exception ex) { Fail("CommandLineText", ex); }
        }

        // ---------------------------------------------------------------- view controls
        //
        // Control names and column layout come straight from the recovered mainView.xml
        // MetaViewWrappers auto-binds these fields by name.
        //
        // lstResults / lstProxResults columns:  [0]=IconColumn  [1]=TextColumn(name)  [2]=TextColumn(coords)
        // For an IconColumn cell the icon id is subval [1]; for a TextColumn the string is subval [0].

        [MVControlReference("txtQuery")]      private ITextBox _txtQuery = null;
        [MVControlReference("lstResults")]    private IList _lstResults = null;
        [MVControlReference("txtProxQuery")]  private ITextBox _txtProxQuery = null;
        [MVControlReference("lstProxResults")] private IList _lstProxResults = null;

        [MVControlReference("txtInfo")]        private IStaticText _txtInfo = null;
        [MVControlReference("txtLoc")]         private IStaticText _txtLoc = null;
        [MVControlReference("txtType")]        private IStaticText _txtType = null;
        [MVControlReference("txtRestriction")] private IStaticText _txtRestriction = null;
        [MVControlReference("txtNotes")]       private IStaticText _txtNotes = null;

        [MVControlReference("chkShowMap")]      private ICheckBox _chkShowMap = null;
        [MVControlReference("chkFootsteps")]    private ICheckBox _chkFootsteps = null;
        [MVControlReference("chkLockRotation")] private ICheckBox _chkLockRotation = null;
        // chkRelCoords checkbox was removed from the Options tab. The field stays (lazy-resolve call
        // sites null-guard it, so they harmlessly no-op) but it has NO [MVControlReference] -- otherwise
        // wireup would try to bind a control that no longer exists.
        private ICheckBox _chkRelCoords = null;

        // The always-on coordinate readout, now a StaticText at the top of the main window (a second
        // standalone HudView crashed the client, so it lives in the window VVS already manages).
        [MVControlReference("txtCoordReadout")] private IStaticText _txtCoordReadout = null;

        // Route page controls.
        [MVControlReference("txtRouteStart")]   private IStaticText _txtRouteStart = null;
        [MVControlReference("txtRouteEnd")]     private IStaticText _txtRouteEnd = null;
        [MVControlReference("lstRouteResults")] private IList _lstRouteResults = null;

        // ---------------------------------------------------------------- view control events

        [MVControlEvent("btnFind", "Click")]
        private void BtnFind_Click(object sender, MVControlEventArgs e)
        {
            try
            {
                var q = Edt(ref _txtQuery, "txtQuery");
                var lst = Lst(ref _lstResults, "lstResults");
                var frag = q != null ? q.Text : "";
                FillList(lst, _places.Search(frag));
            }
            catch (Exception ex) { Fail("btnFind", ex); }
        }

        [MVControlEvent("btnProxFind", "Click")]
        private void BtnProxFind_Click(object sender, MVControlEventArgs e)
        {
            try
            {
                uint landcell = CurrentLandcell();
                if (LandDefs.IsInterior(landcell))
                {
                    Chat("You must be on the surface (not in a dungeon) to search nearby places.");
                    return;
                }

                double radius;
                var pq = Edt(ref _txtProxQuery, "txtProxQuery");
                if (pq == null || !double.TryParse(pq.Text, out radius) || radius <= 0)
                    radius = 3.0;

                double ns, ew;
                Magellan.World.Coords.FromPosition(
                    landcell,
                    CoreManager.Current.Actions.LocationX,
                    CoreManager.Current.Actions.LocationY,
                    out ns, out ew);

                FillList(Lst(ref _lstProxResults, "lstProxResults"), _places.Near(ns, ew, radius));
            }
            catch (Exception ex) { Fail("btnProxFind", ex); }
        }

        [MVControlEvent("btnSetRouteSelf", "Click")]
        private void BtnSetRouteSelf_Click(object sender, MVControlEventArgs e)
        {
            try
            {
                uint landcell = CurrentLandcell();
                if (LandDefs.IsInterior(landcell))
                {
                    Chat("Go outdoors to set your location as the route start (routing is overland).");
                    return;
                }
                Magellan.World.Coords.FromPosition(
                    landcell,
                    CoreManager.Current.Actions.LocationX,
                    CoreManager.Current.Actions.LocationY,
                    out _routeStartNS, out _routeStartEW);
                _haveRouteStart = true;
                var rs = Txt(ref _txtRouteStart, "txtRouteStart");
                if (rs != null) rs.Text = "My location @ " + Magellan.World.Coords.Format(_routeStartNS, _routeStartEW);
                Chat("Route start set to " + Magellan.World.Coords.Format(_routeStartNS, _routeStartEW) + ".");
            }
            catch (Exception ex) { Fail("btnSetRouteSelf", ex); }
        }

        [MVControlEvent("btnPlanRoute", "Click")]
        private void BtnPlanRoute_Click(object sender, MVControlEventArgs e)
        {
            try
            {
                if (_planner == null)
                {
                    Chat("Routing data not installed. Place 'places_2.0.0.2.xml' next to the plugin DLL to enable route finding.");
                    return;
                }
                if (!_haveRouteStart)
                {
                    Chat("Set a start first: click \"Start = my location\" (outdoors), then Plan Route.");
                    return;
                }
                if (!_haveRouteEnd)
                {
                    Chat("Set a destination first: pick a place on the Search or Nearby tab, then Plan Route.");
                    return;
                }

                var steps = _planner.Plan(_routeStartNS, _routeStartEW, _routeEndNS, _routeEndEW);
                var list = Lst(ref _lstRouteResults, "lstRouteResults");
                if (list != null)
                {
                    list.Clear();
                    int i = 1;
                    double totalWalk = 0;
                    foreach (var s in steps)
                    {
                        IListRow row = list.Add();
                        row[0][1] = s.Type == Magellan.Routing.RouteStep.Kind.Portal ? 8072 : 0;   // portal icon vs none
                        row[1][0] = (s.Type == Magellan.Routing.RouteStep.Kind.Portal) ? "Portal" : "Walk";
                        row[2][0] = s.Text;
                        if (s.Type == Magellan.Routing.RouteStep.Kind.Walk) totalWalk += s.Cost;
                        i++;
                    }
                    int hops = 0;
                    foreach (var s in steps) if (s.Type == Magellan.Routing.RouteStep.Kind.Portal) hops++;
                    Chat("Route planned: " + steps.Count + " steps, " + hops + " portal(s), ~" + totalWalk.ToString("0.0") + " units of walking.");
                }
            }
            catch (Exception ex) { Fail("btnPlanRoute", ex); }
        }

        [MVControlEvent("lstResults", "Selected")]
        private void LstResults_Selected(object sender, MVListSelectEventArgs e)
        {
            try { OnRowSelected(_lastSearchResults, e.Row); }
            catch (Exception ex) { Fail("lstResults", ex); }
        }

        [MVControlEvent("lstProxResults", "Selected")]
        private void LstProxResults_Selected(object sender, MVListSelectEventArgs e)
        {
            try { OnRowSelected(_lastProxResults, e.Row); }
            catch (Exception ex) { Fail("lstProxResults", ex); }
        }

        [MVControlEvent("chkShowMap", "Change")]
        private void ChkShowMap_Change(object sender, MVCheckBoxChangeEventArgs e)
        {
            try
            {
                _settings.ShowMap = e.Checked;
                _overlay.Visible = e.Checked && LandDefs.IsInterior(CurrentLandcell());
                SaveSettings();
                Chat("[option] Show map = " + e.Checked);
            }
            catch (Exception ex) { Fail("chkShowMap", ex); }
        }

        [MVControlEvent("chkFootsteps", "Change")]
        private void ChkFootsteps_Change(object sender, MVCheckBoxChangeEventArgs e)
        {
            try { _settings.ShowFootsteps = e.Checked; _renderer.ShowTrail = e.Checked; SaveSettings(); Chat("[option] Footsteps = " + e.Checked); }
            catch (Exception ex) { Fail("chkFootsteps", ex); }
        }

        [MVControlEvent("chkLockRotation", "Change")]
        private void ChkLockRotation_Change(object sender, MVCheckBoxChangeEventArgs e)
        {
            try { _settings.LockRotation = e.Checked; _renderer.LockRotation = e.Checked; SaveSettings(); Chat("[option] Lock rotation = " + e.Checked); }
            catch (Exception ex) { Fail("chkLockRotation", ex); }
        }

        [MVControlEvent("chkRelCoords", "Change")]
        private void ChkRelCoords_Change(object sender, MVCheckBoxChangeEventArgs e)
        {
            try { _settings.RelCoords = e.Checked; SaveSettings(); Chat("[option] Relative coords = " + e.Checked); }
            catch (Exception ex) { Fail("chkRelCoords", ex); }
        }

        // ---------------------------------------------------------------- list helpers

        // We keep the Place backing each list so a row click can show full detail without re-parsing
        // the cells. MetaViewWrappers rows are write-oriented; this is simpler and allocation-light.
        private System.Collections.Generic.List<Place> _lastSearchResults = new System.Collections.Generic.List<Place>();
        private System.Collections.Generic.List<Place> _lastProxResults = new System.Collections.Generic.List<Place>();

        private void FillList(IList list, System.Collections.Generic.IEnumerable<Place> results)
        {
            if (list == null) return;
            // Pick the backing store by identity, but fall back to the search list if the lazily-resolved
            // wrapper isn't reference-equal to the cached field.
            var backing = ReferenceEquals(list, _lstProxResults) ? _lastProxResults : _lastSearchResults;
            backing.Clear();
            list.Clear();

            foreach (var p in results)
            {
                IListRow row = list.Add();
                row[0][1] = IconFor(p.Type);      // IconColumn: icon id at subval 1
                row[1][0] = p.Name ?? "(unnamed)";// TextColumn: string at subval 0
                row[2][0] = p.Coordinates;
                backing.Add(p);
            }
        }

        private void OnRowSelected(System.Collections.Generic.List<Place> backing, int row)
        {
            if (backing == null || row < 0 || row >= backing.Count) return;
            var p = backing[row];

            // Coordinate string: absolute world coords, OR (when "relative co-ordinates" is on and we
            // can read the player's position) the place's offset FROM the player -- e.g. "2.3N, 1.1E
            // from you". Offset = place coord - player coord.
            string coordText = p.Coordinates;
            if (_settings.RelCoords)
            {
                try
                {
                    uint lc = CurrentLandcell();
                    if (!LandDefs.IsInterior(lc))   // player world position only meaningful on the surface
                    {
                        double myNS, myEW;
                        Magellan.World.Coords.FromPosition(
                            lc,
                            CoreManager.Current.Actions.LocationX,
                            CoreManager.Current.Actions.LocationY,
                            out myNS, out myEW);
                        coordText = Magellan.World.Coords.Format(p.NS - myNS, p.EW - myEW) + " from you";
                    }
                    else
                    {
                        coordText = p.Coordinates + " (abs; go outdoors for relative)";
                    }
                }
                catch { coordText = p.Coordinates; }
            }

            // The original's behaviour: clicking a row prints the coordinates to chat.
            Chat((p.Name ?? "(unnamed)") + " @ " + coordText);

            // Populate the More Details tab. These controls live on a non-default tab, so resolve them
            // lazily (their startup [MVControlReference] binding was null -- same tab-lazy issue as the
            // checkboxes), otherwise the tab keeps its default "No results" text.
            var info = Txt(ref _txtInfo, "txtInfo");                if (info != null) info.Text = p.Name ?? "(unnamed)";
            var type = Txt(ref _txtType, "txtType");                if (type != null) type.Text = p.Type;
            var loc = Txt(ref _txtLoc, "txtLoc");                   if (loc != null) loc.Text = coordText;
            var restr = Txt(ref _txtRestriction, "txtRestriction"); if (restr != null) restr.Text = p.LevelRestriction ?? "";
            var notes = Txt(ref _txtNotes, "txtNotes");             if (notes != null) notes.Text = p.ExitLocation ?? "";

            // Selecting a place also arms it as the route DESTINATION (the Route tab's "Plan Route"
            // then finds a way there). Start defaults to your current location via the button.
            _haveRouteEnd = true;
            _routeEndNS = p.NS; _routeEndEW = p.EW;
            var rend = Txt(ref _txtRouteEnd, "txtRouteEnd");
            if (rend != null) rend.Text = (p.Name ?? "(place)") + " @ " + p.Coordinates;
        }

        private uint CurrentLandcell() { return unchecked((uint)CoreManager.Current.Actions.Landcell); }

        /// <summary>
        /// THE critical bring-up gate. Run once, in-game, before trusting any dungeon geometry.
        ///
        /// DatReaderWriter's Unpack reads a leading id DWORD for HasId records (EnvCell, Environment,
        /// LandBlockInfo all qualify). Whether Decal's FileService returns that header or strips it
        /// decides a 4-byte parse phase -- and getting it wrong yields structurally-valid, totally-
        /// wrong geometry with no crash. So we prove it, on a real file, and set the flag.
        ///
        /// Uses whatever landblock you're standing in: an EnvCell if you're in a dungeon (the exact
        /// record we parse), else the LandBlockInfo (present for almost every landblock).
        /// </summary>
        public void RunPhaseCheck()
        {
            try
            {
                uint landcell = CurrentLandcell();
                uint lb = LandDefs.LandblockOf(landcell);

                uint probeId;
                string kind;
                if (LandDefs.IsInterior(landcell)) { probeId = lb | LandDefs.FirstEnvCellId; kind = "EnvCell"; }
                else { probeId = lb | LandDefs.LbiCellId; kind = "LandBlockInfo"; }

                byte[] bytes = _fileService.GetCellFile(unchecked((int)probeId));
                if (bytes == null || bytes.Length < 4)
                {
                    Chat("phase check: no bytes for 0x" + probeId.ToString("X8") + " -- try again in a town or dungeon.");
                    return;
                }

                bool header = Magellan.Plugin.Mapping.DecalDatSource.HeaderPresent(bytes, probeId);
                Magellan.Plugin.Mapping.DecalDatSource.FileServiceIncludesIdHeader = header;

                uint first = BitConverter.ToUInt32(bytes, 0);
                Chat("phase check via " + kind + " 0x" + probeId.ToString("X8")
                     + ": first DWORD = 0x" + first.ToString("X8")
                     + " -> id header " + (header ? "PRESENT" : "ABSENT")
                     + "; FileServiceIncludesIdHeader = " + header);

                // End-to-end proof: parse this landblock and report edge/miss counts.
                if (LandDefs.IsInterior(landcell))
                {
                    var geo = _mapper.Build(landcell);
                    Chat("  parsed landblock 0x" + lb.ToString("X4") + ": "
                         + geo.Edges.Count + " wall edges, " + geo.MissingCells + " cells not yet streamed."
                         + (geo.Edges.Count == 0 ? "  (0 edges + header ABSENT usually means the phase is wrong.)" : ""));
                }
                else
                {
                    Chat("  stand in a dungeon and run /mag phase again to verify geometry parsing end-to-end.");
                }
            }
            catch (Exception ex) { Fail("phase check", ex); }
        }

        /// <summary>Quick status dump: data loaded, current landblock, dungeon name, coordinate readout.</summary>
        public void RunDiagnostics()
        {
            try
            {
                Chat("places=" + _places.All.Count + " (corrected " + _places.CorrectionsApplied
                     + ", rejected " + _places.RowsRejected + "), dungeon names=" + _dungeons.Count);

                uint landcell = CurrentLandcell();
                uint lb = LandDefs.LandblockOf(landcell);
                Chat("landcell=0x" + landcell.ToString("X8") + " landblock=0x" + lb.ToString("X4")
                     + " indoors=" + LandDefs.IsInterior(landcell));

                if (LandDefs.IsInterior(landcell))
                {
                    Chat("dungeon: " + _dungeons.Caption(landcell));
                }
                else
                {
                    double ns, ew;
                    Magellan.World.Coords.FromPosition(landcell,
                        CoreManager.Current.Actions.LocationX,
                        CoreManager.Current.Actions.LocationY, out ns, out ew);
                    Chat("position: " + Magellan.World.Coords.Format(ns, ew));
                }

#if MAGELLAN_AUTOMAP
                Chat("automap: COMPILED IN (MAGELLAN_AUTOMAP). Overlay visible=" + _overlay.Visible + "; renderFrameHook=" + _renderFrameHooked);
                Chat("  overlay status: " + _overlay.Status);
#else
                Chat("automap: not compiled (define MAGELLAN_AUTOMAP for the dungeon map).");
#endif
                try
                {
                    Chat("config: " + (_configPath ?? "(none)") + (System.IO.File.Exists(_configPath) ? " [exists]" : " [not yet written]"));
                }
                catch { }
            }
            catch (Exception ex) { Fail("diag", ex); }
        }

        private void SyncSettingsFromUi()
        {
            // The checkboxes drive _settings directly via their Change handlers, so nothing to pull
            // here beyond what the handlers already set. Present for symmetry / future controls.
        }

        /// <summary>Maps a place TYPE to a portal.dat icon id. Placeholder ids -- tune to taste.</summary>
        private static int IconFor(string type)
        {
            switch (type)
            {
                case "Lifestone": return 0x0600106A;
                case "Portal":
                case "Random Portal": return 0x06001238;
                case "Town":
                case "Community": return 0x060011F8;
                case "Shop": return 0x06001320;
                case "Dungeon": return 0x060026E4;
                default: return 0x06001336;
            }
        }

        private static void Chat(string s)
        {
            try { CoreManager.Current.Actions.AddChatText("[Magellan] " + s, 5); }
            catch { /* never let chat output crash us */ }
        }

        private static void Fail(string where, Exception ex)
        {
            try { CoreManager.Current.Actions.AddChatText("[Magellan] error in " + where + ": " + ex.Message, 5); }
            catch { }
        }
    }
}
