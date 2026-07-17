#if MAGELLAN_AUTOMAP
using System;
using System.Drawing;
using Magellan.Mapping;
using VirindiViewService;
using VirindiViewService.Controls;

namespace Magellan.Plugin.Ui
{
    /// <summary>
    /// The dungeon map as a screen overlay -- a second, borderless, click-through HudView, the way
    /// the original drew (player dot fixed at screen (150,190), dungeon name at (10,80)). Separate
    /// from the main tabbed window (the recovered mainView.xml), which stays as-is.
    ///
    /// PERFORMANCE. Dungeon geometry is static per landblock, so the wall lines are baked into an
    /// offscreen DxTexture ONCE when the landblock changes, then blitted as a single rotated quad
    /// per frame (DrawTextureRotated). Only the footstep trail, player marker and caption are drawn
    /// live each frame. The geometry the renderer draws is the same output /mag phase verifies.
    ///
    /// All VVS signatures here were taken from the installed VirindiViewService.dll via reflection:
    ///   DxTexture(Size); DrawTextureRotated(DxTexture, Rectangle, Point, int alpha, float angle);
    ///   HudEmulator() with 'event Draw : delDraw' and DrawNow(DxTexture); no Texture property;
    ///   HudView(ViewProperties, ControlGroup); ViewProperties(title, w, h, ACImage); ACImage(int portalfile).
    /// </summary>
    public sealed class VvsMapOverlay : IMapOverlay
    {
        private const int Size_ = 300;         // overlay is Size_ x Size_, player at the centre
        private const int ViewIcon = 9311;     // 0x245F -- the map overlay's own viewbar icon

        private readonly AutomapRenderer _renderer;
        private HudView _view;
        private HudEmulator _emu;
        private DungeonGeometry _liveGeo;      // current landblock geometry, drawn directly each frame
        private uint _geoLandblock;
        private bool _geoValid;

        public bool Visible
        {
            get { return _view != null && _view.Visible; }
            set
            {
                if (_view == null) return;
                if (value && !_placed)
                {
                    _placed = true;
                    // First show: rescue ONLY a window that is actually unplaced or off-screen (a
                    // fresh HudView with no saved settings lands at (0,0); a stale save can be
                    // negative). The old code force-moved to a fixed point every session, which
                    // (a) clobbered the position LoadUserSettings had just restored and (b) could
                    // park the map squarely on top of the main window -- which then reads as "the
                    // main window draws nothing" whenever a dungeon map is up. Respect a real save.
                    try
                    {
                        var loc = _view.Location;
                        if (loc.X <= 0 && loc.Y <= 0) _view.Location = DefaultLocation;
                    }
                    catch { }
                }
                _view.Visible = value;
                // The map's viewbar icon should only appear while the map itself is shown -- so the
                // bar isn't cluttered with a map button when the map is off. ShowInBar tracks Visible.
                try { _view.ShowInBar = value; } catch { }
                if (value && _emu != null) { try { _emu.Invalidate(); } catch { } }
            }
        }
        private bool _placed;

        // Default spot: clear of the top-left region where the main tabbed window typically sits,
        // still on-screen at 800x600 (the overlay is Size_ wide; 420 + 300 = 720 < 800).
        private static readonly Point DefaultLocation = new Point(420, 100);

        /// <summary>
        /// /mag reset: put the window somewhere visible and strip any persisted ghost/zero-alpha
        /// state. Ghosted/Alpha are set by REFLECTION: they exist on HudView per the reflected
        /// metadata (research file 03 sec 10.7), but their presence/numeric type may vary by VVS
        /// build, and a reset helper must never be the thing that fails to compile or throws.
        /// </summary>
        public void ResetPresentation()
        {
            if (_view == null) return;
            try { _view.Location = DefaultLocation; } catch { }
            // VVS persists user size (vvs.s3db UserW/UserH) -- restore the designed dimensions too.
            try { _view.ClientArea = new Size(Size_, Size_); } catch { }
            try { var p = _view.GetType().GetProperty("Ghosted"); if (p != null && p.CanWrite) p.SetValue(_view, false, null); } catch { }
            try { var p = _view.GetType().GetProperty("ClickThrough"); if (p != null && p.CanWrite) p.SetValue(_view, false, null); } catch { }
            try { var p = _view.GetType().GetProperty("Alpha"); if (p != null && p.CanWrite) p.SetValue(_view, Convert.ChangeType(255, p.PropertyType), null); } catch { }
            if (_emu != null) { try { _emu.Invalidate(); } catch { } }
        }

        private string _status = "not created";
        public string Status { get { return _status + (_view != null ? "; view=OK" : "; view=NULL") + (_emu != null ? "; emu=OK" : "; emu=NULL") + "; drawCalls=" + _drawCalls + "; drawErr=" + _drawErrors + (_lastDrawErr.Length > 0 ? "(" + _lastDrawErr + ")" : "") + "; geo=" + (_liveGeo != null ? _liveGeo.Edges.Count + " edges" : "none"); } }

        public VvsMapOverlay(AutomapRenderer renderer) { _renderer = renderer; }

        public void CreateView()
        {
            // Build the view entirely in code. (An earlier attempt used VVS's Decal3XMLParser, but
            // Parse() returned a null ControlGroup for our minimal XML -- hence a NullReference on the
            // next line. new ControlGroup()/HeadControl and the HudView(props,group) ctor are all
            // confirmed-present API, so we construct directly.)
            try
            {
                _status = "creating emulator";
                _emu = new HudEmulator();

                _status = "new ControlGroup()";
                var group = new ControlGroup();

                _status = "creating view properties";
                var props = new ViewProperties("Magellan Map", Size_, Size_, new ACImage(ViewIcon));

                // Build the HudView from the (still-empty) group FIRST. The HudView ctor wires the
                // group's ViewParentP; ControlGroup.HeadControl's setter dereferences that, so it must
                // be assigned only AFTER the view exists -- otherwise NullReferenceException.
                _status = "creating HudView";
                _view = new HudView(props, group);

                _status = "group.HeadControl = emu (post-view)";
                group.HeadControl = _emu;

                _status = "view settings";
                _view.UserResizeable = true;
                _view.UserAlphaChangeable = true;
                _view.UserGhostable = true;
                try { _view.ShowInBar = false; } catch { }   // no viewbar icon until the map is enabled
                try { _view.LoadUserSettings(); } catch { }

                _status = "hooking draw";
                _emu.Draw += Emulator_Draw;
                _status = "created";
            }
            catch (Exception ex)
            {
                _status = "FAILED at [" + _status + "]: " + ex.GetType().Name + ": " + ex.Message;
                throw;
            }
        }

        /// <summary>
        /// Rebuild the baked wall texture for a landblock. Call off the render thread, on landblock
        /// change (or a streamed-cell update). Cheap to call again -- early-outs if unchanged and no
        /// cells were missing last time.
        /// </summary>
        public void SetGeometry(DungeonGeometry geo, float playerX, float playerY, float playerZ, float headingDeg)
        {
            // Just store the geometry; it's drawn directly in Emulator_Draw via the tested renderer.
            // (No offscreen baking -- the DxTexture->target blit didn't paint on this VVS build, and
            // direct DrawLine works, so we render walls live like the trail/marker.)
            _liveGeo = geo;
            _geoLandblock = geo != null ? geo.Landblock : 0u;
            _geoValid = geo != null && !geo.IsEmpty;
            if (_emu != null) { try { _emu.Invalidate(); } catch { } }
        }

        // Live per-frame inputs, set from the game thread.
        private float _px, _py, _pz, _heading;
        private string _caption;

        public void SetFrameState(DungeonGeometry geo, float px, float py, float pz, float headingDeg, string caption)
        {
            if (geo != null) _liveGeo = geo;   // keep the geometry current as the frame updates
            _px = px; _py = py; _pz = pz; _heading = headingDeg; _caption = caption;
            if (_emu != null) { try { _emu.Invalidate(); } catch { } }
        }

        private string _lastTitle;
        public void SetTitle(string title)
        {
            if (_view == null || title == null || title == _lastTitle) return;
            _lastTitle = title;
            try { _view.Title = title; } catch { }
        }

        // HudEmulator.Draw delegate 'delDraw' is:
        //   void(HudEmulator Caller, DxTexture Target, Rectangle TargetRegion, delClearRegion dClearOp)
        // We draw into Target; TargetRegion is where in the surface we own; the clear-op callback is
        // VVS's optional "clear my region for me" -- we don't need it (we overwrite every pixel).
        private int _drawCalls;
        private int _drawErrors;
        private string _lastDrawErr = "";

        private void Emulator_Draw(HudEmulator caller, DxTexture tex, Rectangle region, HudEmulator.delClearRegion clearOp)
        {
            _drawCalls++;
            if (tex == null) { return; }
            bool opened = false;
            try
            {
                // The emulator hands us the Target but does NOT open the render pass ("BeginRender not
                // called" otherwise). Open it ourselves, draw, then close it.
                tex.BeginRender();
                opened = true;

                // Center the map on the ACTUAL draw region each frame: the player marker sits at the
                // true centre (both axes), and the map fills the whole window -- so resizing the VVS
                // window bigger reveals more of the dungeon instead of clipping the left/top.
                _renderer.CenterX = region.Width / 2f;
                _renderer.CenterY = region.Height / 2f;

                // Draw walls + trail + marker + caption in ONE pass through the tested renderer, using
                // the real geometry. (An earlier design baked walls into an offscreen DxTexture and
                // blitted it per frame; that blit never landed -- DrawTexture onto the emulator target
                // didn't paint -- while direct DrawLine calls clearly do. At 5 Hz, drawing ~1.5k lines
                // directly is cheap, and it uses the exact primitive path that works for the marker/trail.)
                var geo = _liveGeo ?? DungeonGeometry.Empty;
                _renderer.Render(new DxCanvas(tex, region.Left, region.Top),
                                 geo, _px, _py, _pz, _heading, _caption);

                try { tex.FlushSprite(); } catch { }
            }
            catch (Exception ex) { _drawErrors++; _lastDrawErr = "draw: " + ex.GetType().Name + ": " + ex.Message; }
            finally { if (opened) { try { tex.EndRender(); } catch { } } }
        }

        public void Dispose()
        {
            try { if (_emu != null) _emu.Draw -= Emulator_Draw; } catch { }
            if (_view != null) { _view.Dispose(); _view = null; }
        }
    }
}

#endif
