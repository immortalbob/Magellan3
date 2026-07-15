using System;
using System.Globalization;

namespace Magellan.World
{
    /// <summary>
    /// Landcell -> Dereth map coordinates.
    ///
    /// A landcell (CoreManager.Current.Actions.Landcell) is 0xXXYYCCCC:
    ///   XX   = landblock index, west -> east      (AC wiki: "F682 -> F6 = 246 landblocks from the left edge")
    ///   YY   = landblock index, south -> north    ("...82 = 130 landblocks from the bottom edge")
    ///   CCCC = cell index.  &lt; 0x0100 => outdoor surface cell (8x8 grid, cell = cx*8 + cy)
    ///                       >= 0x0100 => indoors (building interior OR dungeon)
    ///
    /// LocationX / LocationY (Actions) are metres inside the landblock:
    ///   X: 0 (west side) .. 192 (east side);  Y: 0 (south side) .. 192 (north side).
    ///   Unbounded indoors -- see <see cref="IsIndoors"/>.
    ///
    /// One coordinate unit = 240 m = 10 landcells. World centre = 255 * 192 / 2 = 24480 m.
    ///
    /// Verified against the four places in places.xml that carry a full 32-bit &lt;LOC&gt;
    /// landcell (agrees to within a half-landcell, the granularity limit) and against an
    /// independent 2002 third-party coordinate list.
    /// </summary>
    public static class Coords
    {
        /// <summary>Metres per landblock side.</summary>
        public const double BlockLength = 192.0;

        /// <summary>Metres per displayed coordinate unit (240 m = 10 landcells of 24 m).</summary>
        public const double UnitMetres = 240.0;

        /// <summary>Metres from the world's south-west origin to its centre (== 1020 cells * 24 m).</summary>
        public const double WorldCentre = 24480.0;

        /// <summary>Metres per landcell (the 8x8 grid inside a landblock).</summary>
        public const double CellLength = 24.0;

        /// <summary>Landblock index along the west-east axis (the high byte).</summary>
        public static int BlockEW(uint landcell) { return (int)((landcell >> 24) & 0xFF); }

        /// <summary>Landblock index along the south-north axis (the second byte).</summary>
        public static int BlockNS(uint landcell) { return (int)((landcell >> 16) & 0xFF); }

        /// <summary>The 16-bit cell index inside the landblock.</summary>
        public static int CellIndex(uint landcell) { return (int)(landcell & 0xFFFF); }

        /// <summary>The 16-bit landblock id (what dungeon_names.tsv is keyed on).</summary>
        public static ushort Landblock(uint landcell) { return (ushort)(landcell >> 16); }

        /// <summary>
        /// True when the player is inside a building or a dungeon.
        ///
        /// Surface coordinates are MEANINGLESS here: dungeons sit on otherwise-empty landblocks
        /// in the corner of the grid, so the transform below would report a position in the ocean.
        /// This is why the 2003 manual says "to find places near your character's current location,
        /// you must not currently be in a dungeon."
        /// </summary>
        public static bool IsIndoors(uint landcell) { return CellIndex(landcell) >= 0x0100; }

        /// <summary>
        /// Player position -> (north-south, east-west) in game coordinate units.
        /// Positive = North / East. Only meaningful when <see cref="IsIndoors"/> is false.
        /// </summary>
        public static void FromPosition(uint landcell, double locX, double locY,
                                        out double ns, out double ew)
        {
            ew = (BlockEW(landcell) * BlockLength + locX - WorldCentre) / UnitMetres;
            ns = (BlockNS(landcell) * BlockLength + locY - WorldCentre) / UnitMetres;
        }

        /// <summary>
        /// Cell-granularity form, for when you have a landcell but no metre offset
        /// (e.g. the four &lt;LOC&gt; records in places.xml). Accurate to a half-landcell.
        /// The cell index must be an outdoor one (&lt; 0x0100).
        /// </summary>
        public static void FromSurfaceCell(uint landcell, out double ns, out double ew)
        {
            int c = CellIndex(landcell);
            ew = (BlockEW(landcell) * 8 + (c >> 3) - 1019.5) / 10.0;
            ns = (BlockNS(landcell) * 8 + (c & 7) - 1019.5) / 10.0;
        }

        /// <summary>Great-circle-free plain distance between two coordinate pairs, in coordinate units.</summary>
        public static double Distance(double ns1, double ew1, double ns2, double ew2)
        {
            double dn = ns1 - ns2, de = ew1 - ew2;
            return Math.Sqrt(dn * dn + de * de);
        }

        /// <summary>Formats as AC does: "24.4N, 48.3E".</summary>
        public static string Format(double ns, double ew)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.0}{1}, {2:0.0}{3}",
                Math.Abs(ns), ns >= 0 ? "N" : "S",
                Math.Abs(ew), ew >= 0 ? "E" : "W");
        }
    }
}
