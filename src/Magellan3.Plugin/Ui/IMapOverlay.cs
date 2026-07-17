using System;
using Magellan.Mapping;

namespace Magellan.Plugin.Ui
{
    /// <summary>
    /// The seam between the plugin and the map drawing surface.
    ///
    /// Two implementations:
    ///   - <see cref="NullMapOverlay"/>  : no-op. The plugin builds and runs with ZERO VVS drawing
    ///     code, so Stages A-E (search, nearby, dungeon names, the phase check) work before you
    ///     have touched a single Direct3D call. This is the default build.
    ///   - VvsMapOverlay (guarded by #if MAGELLAN_AUTOMAP) : the real DxTexture overlay. Turn it on
    ///     for Stage F, once you can compile against your installed VirindiViewService.dll.
    ///
    /// Everything upstream of this interface (geometry, projection, slicing, dungeon naming) is pure
    /// and unit-tested; the interface exists precisely so the untested D3D half can be swapped in
    /// last, deliberately, without disturbing anything else.
    /// </summary>
    public interface IMapOverlay : IDisposable
    {
        bool Visible { get; set; }
        void CreateView();
        void SetGeometry(DungeonGeometry geo, float px, float py, float pz, float headingDeg);
        void SetFrameState(DungeonGeometry geo, float px, float py, float pz, float headingDeg, string caption);
        void SetTitle(string title);

        /// <summary>
        /// Restore the overlay window to a known-visible state (on-screen position, un-ghosted,
        /// opaque) -- the /mag reset escape hatch for per-window VVS state the plugin doesn't own.
        /// </summary>
        void ResetPresentation();
        string Status { get; }   // human-readable diagnostic: is the view created? did CreateView throw?
    }

    /// <summary>
    /// A map overlay that draws nothing. Lets the plugin load and every non-map feature work with no
    /// VirindiViewService drawing dependency at all. Swap in VvsMapOverlay (Stage F) when ready.
    /// </summary>
    public sealed class NullMapOverlay : IMapOverlay
    {
        public bool Visible { get; set; }
        public void CreateView() { }
        public void SetGeometry(DungeonGeometry geo, float px, float py, float pz, float headingDeg) { }
        public void SetFrameState(DungeonGeometry geo, float px, float py, float pz, float headingDeg, string caption) { }
        public void SetTitle(string title) { }
        public void ResetPresentation() { }
        public string Status { get { return "null overlay (automap not compiled)"; } }
        public void Dispose() { }
    }
}
