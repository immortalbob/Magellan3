using Magellan.World;

namespace Magellan.Tests
{
    /// <summary>
    /// The landcell -> coordinate transform, pinned to ground truth that has nothing to do with
    /// Magellan. If these ever fail, do not "fix" the expected values -- go and re-derive.
    /// </summary>
    public static class CoordsTests
    {
        public static void Register()
        {
            T.Suite("Coords");

            // The four places in places.xml carrying a full 32-bit <LOC> landcell. The cell index gives
            // position only to the nearest landcell (24 m = 0.1 units), so the value lands on a cell
            // CENTRE -- exactly a half-cell (0.05 units) off the store's rounded tenth. Tolerance is that
            // half-cell, inclusive; use 0.051 so the exact boundary passes rather than tripping on '>'.
            const double HalfCell = 0.051;

            T.Test("LOC e53d003e -> 52.6S, 82.0E  (Nanto Provisioner -- Sho lands, far SE)", () =>
            {
                double ns, ew;
                Coords.FromSurfaceCell(0xE53D003E, out ns, out ew);
                T.Near(-52.6, ns, HalfCell, "NS");
                T.Near(82.0, ew, HalfCell, "EW");
            });

            T.Test("LOC b96f002e -> 12.6S, 46.6E", () =>
            {
                double ns, ew;
                Coords.FromSurfaceCell(0xB96F002E, out ns, out ew);
                T.Near(-12.6, ns, HalfCell, "NS");
                T.Near(46.6, ew, HalfCell, "EW");
            });

            T.Test("LOC da55001b -> 33.7S, 72.8E", () =>
            {
                double ns, ew;
                Coords.FromSurfaceCell(0xDA55001B, out ns, out ew);
                T.Near(-33.7, ns, HalfCell, "NS");
                T.Near(72.8, ew, HalfCell, "EW");
            });

            T.Test("LOC 7d64001c -> 21.6S, 1.6W  (Rahira bint Hisan -- Gharu'ndim, S/SW)", () =>
            {
                double ns, ew;
                Coords.FromSurfaceCell(0x7D64001C, out ns, out ew);
                T.Near(-21.6, ns, HalfCell, "NS");
                T.Near(-1.6, ew, HalfCell, "EW");
            });

            // The metre-based form (what Actions.LocationX/Y give you) must agree with the
            // cell-index form when the player stands at the centre of that cell.
            T.Test("metre form == cell form at cell centre", () =>
            {
                const uint lc = 0xE53D003E;
                int cell = Coords.CellIndex(lc);
                double locX = (cell >> 3) * Coords.CellLength + 12.0;   // cx -> east metres
                double locY = (cell & 7) * Coords.CellLength + 12.0;    // cy -> north metres

                double ns1, ew1, ns2, ew2;
                Coords.FromSurfaceCell(lc, out ns1, out ew1);
                Coords.FromPosition(lc, locX, locY, out ns2, out ew2);

                T.Near(ns1, ns2, 1e-9, "NS agreement");
                T.Near(ew1, ew2, 1e-9, "EW agreement");
            });

            // Byte order. AC wiki, on landblock F682: "F6 -> 246 landblocks from the LEFT edge,
            // 82 -> 130 landblocks from the BOTTOM edge." So the high byte is east-west.
            T.Test("high byte is east-west, second byte is south-north", () =>
            {
                T.Eq(0xF6, Coords.BlockEW(0xF6820000), "BlockEW");
                T.Eq(0x82, Coords.BlockNS(0xF6820000), "BlockNS");
            });

            T.Test("Aphus Lassel landblock F682 lands in the far eastern sea, near the equator", () =>
            {
                double ns, ew;
                Coords.FromPosition(0xF6820000, 96, 96, out ns, out ew);   // mid-landblock
                T.Near(2.5, ns, 0.5, "NS");
                T.Near(95.3, ew, 0.5, "EW");
            });

            // Indoors: cell index >= 0x0100. Surface coordinates do not exist here.
            T.Test("IsIndoors: surface cells are outdoors, 0x0100+ is indoors", () =>
            {
                T.False(Coords.IsIndoors(0xC6A9003E), "surface cell 0x003E");
                T.False(Coords.IsIndoors(0xC6A90000), "surface cell 0x0000");
                T.True(Coords.IsIndoors(0x01E50100), "first interior cell");
                T.True(Coords.IsIndoors(0x01E50137), "interior cell");
            });

            T.Test("dungeon landblocks are geographically meaningless -- proof by absurdity", () =>
            {
                // Green Mire Grave, landblock 0x01E5. Run the surface transform on it anyway and you
                // land in the ocean off the north-west coast. This is exactly why the 2003 manual says
                // "Nearby" does not work in a dungeon, and why the readout must be gated on IsIndoors.
                double ns, ew;
                Coords.FromPosition(0x01E50100, 96, 96, out ns, out ew);
                T.True(ew < -100.0, "east-west is off the west edge of the world (" + ew.ToString("0.0") + ")");
                T.True(ns > 80.0, "north-south is off in the far north (" + ns.ToString("0.0") + ")");
            });

            T.Test("Landblock() strips the cell index", () =>
            {
                T.Eq((ushort)0x01E5, Coords.Landblock(0x01E50137), "landblock of a dungeon cell");
            });

            T.Test("Format matches AC's display", () =>
            {
                T.Eq("24.4N, 48.3E", Coords.Format(24.4, 48.3), "north-east");
                T.Eq("23.0S, 0.2W", Coords.Format(-23.0, -0.2), "south-west");
            });
        }
    }
}
