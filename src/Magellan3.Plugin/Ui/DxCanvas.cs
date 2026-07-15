#if MAGELLAN_AUTOMAP
using System.Drawing;
using Magellan.Mapping;
using VirindiViewService;

namespace Magellan.Plugin.Ui
{
    /// <summary>
    /// The IMapCanvas implementation over VVS's DxTexture. This is the ONLY class in the map path
    /// that touches the game's renderer -- everything upstream (geometry, projection, slicing) is
    /// pure and tested.
    ///
    /// Immediate-mode discipline (from the VVS authors' own docs): all drawing is bracketed by one
    /// BeginRender()/EndRender() pair per frame -- NOT per primitive -- and DrawLines may need a
    /// FlushSprite() first for correct ordering. The overlay owns the Begin/End; this adapter just
    /// emits into the open render target.
    /// </summary>
    public sealed class DxCanvas : IMapCanvas
    {
        private readonly DxTexture _tex;
        private readonly float _ox;
        private readonly float _oy;

        public DxCanvas(DxTexture tex) : this(tex, 0f, 0f) { }
        public DxCanvas(DxTexture tex, float offsetX, float offsetY) { _tex = tex; _ox = offsetX; _oy = offsetY; }

        public void Line(float x1, float y1, float x2, float y2, int argb)
        {
            _tex.DrawLine(new PointF(x1 + _ox, y1 + _oy), new PointF(x2 + _ox, y2 + _oy), Color.FromArgb(argb), 1f);
        }

        public void Text(float x, float y, string s, int argb)
        {
            // Real VVS overload: BeginText(String font, Single size, Int32 weight, Boolean italic, Int32 outlineWidth, Int32 outlineColor)
            _tex.BeginText("Arial", 10f, 0, false, 0, 0);
            _tex.WriteText(s, Color.FromArgb(argb), WriteTextFormats.None,
                           new Rectangle((int)(x + _ox), (int)(y + _oy), 320, 20));
            _tex.EndText();
        }
    }
}

#endif
