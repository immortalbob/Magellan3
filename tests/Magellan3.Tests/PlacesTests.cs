using System.Linq;
using Magellan.Data;
using Magellan.World;

namespace Magellan.Tests
{
    public static class PlacesTests
    {
        private static PlacesDb _db;
        private static PlacesDb Db { get { return _db ?? (_db = PlacesDb.FromFile(T.Data("places.xml"))); } }

        public static void Register()
        {
            T.Suite("PlacesDb");

            T.Test("loads all 3,307 places, ids unique", () =>
            {
                T.Eq(3307, Db.All.Count, "place count");
                T.Eq(3307, Db.All.Select(p => p.Id).Distinct().Count(), "distinct ids");
            });

            T.Test("the name element is <NAME>, not <n>", () =>
            {
                // 3,306 rows have one. If a parser bound to <n> it would load zero names,
                // and every search would silently return nothing.
                T.Eq(3306, Db.All.Count(p => !string.IsNullOrEmpty(p.Name) && p.Name != "(unnamed community)"),
                     "rows with a name");
            });

            T.Test("ID 4829 has no <NAME> element at all -- absent, not empty", () =>
            {
                var p = Db.ById(4829);
                T.NotNull(p, "row 4829 must still load");
                T.Eq("Community", p.Type, "type");
                T.Eq("(unnamed community)", p.Name, "placeholder name");
            });

            // ---------------------------------------------------------------- THE AXES
            //
            // COORD_X = North * 10, COORD_Y = East * 10.  The original plan had this
            // backwards. Ground truth below is a 2002 third-party location list (twarriors.com),
            // compiled from the live game and entirely independent of Magellan.
            //
            // Under the plan's reading, 0 of these 6 match. Under this one, 6 of 6.

            T.Suite("PlacesDb / axes (external ground truth, 2002)");

            CheckPlace("Yaraq to Samsur", -23.0, -0.2);        // 23.0S 00.2W  <- the plan's own worked example
            CheckPlace("Yaraq to Al-Arqas", -22.8, -0.7);      // 22.8S 00.7W
            CheckPlace("Al-Arqas to Samsur", -32.6, 14.4);     // 32.6S 14.4E
            CheckPlace("Samsur to Yaraq", -3.8, 18.6);         // 03.8S 18.6E
            CheckPlace("Arwic to Al-Jalima", 33.6, 57.1);      // 33.6N 57.1E
            CheckPlace("Holtburg to Rithwic", 40.8, 34.0);     // 40.8N 34.0E

            T.Test("Cragstone Lifestone is at 24.4N, 48.3E (AC wiki), not 48.3N 24.4E", () =>
            {
                // Two lifestone rows contain "Cragstone" (the other is "Cragstone Reach" at 24.7N);
                // match the exact name so the test pins the town lifestone the plan mis-verified.
                var p = Db.All.First(x => string.Equals(x.Name, "Cragstone Lifestone",
                                                        System.StringComparison.OrdinalIgnoreCase));
                T.Near(24.4, p.NS, 0.05, "north");
                T.Near(48.3, p.EW, 0.05, "east");
            });

            // ---------------------------------------------------------------- errata

            T.Suite("PlacesDb / errata");

            T.Test("all six off-map coordinates are repaired at load; none are dropped", () =>
            {
                T.Eq(6, Db.CorrectionsApplied, "corrections applied");
                T.Eq(0, Db.RowsRejected, "rows rejected");
                T.False(Db.All.Any(p => System.Math.Abs(p.NS) > 102.0 || System.Math.Abs(p.EW) > 102.0),
                        "no place remains off-map");
            });

            T.Test("every erratum is in COORD_Y (east/west); none in COORD_X", () =>
            {
                foreach (var p in Db.All.Where(x => x.CoordsCorrected))
                    T.True(System.Math.Abs(p.NS) <= 102.0, "COORD_X was never the broken axis for id " + p.Id);
            });

            T.Test("Samsur rows are a DOUBLE stray zero -> 19.5E, next to Samsur (~18.5E)", () =>
            {
                // The original left these as "needs a lookup". They were only unsolvable
                // because the axes were transposed: 1951.0 North is nonsense, 19.5 East is not.
                var collector = Db.ById(3188);
                var provisioner = Db.ById(3189);
                T.Near(-2.7, collector.NS, 0.05, "collector NS");
                T.Near(19.5, collector.EW, 0.05, "collector EW");
                T.Near(-2.8, provisioner.NS, 0.05, "provisioner NS");
                T.Near(19.5, provisioner.EW, 0.05, "provisioner EW");
            });

            T.Test("Sho Roadside Portal lands in Sho territory (SE) once repaired", () =>
            {
                var p = Db.ById(3111);
                T.Near(-64.5, p.NS, 0.05, "NS");
                T.Near(76.8, p.EW, 0.05, "EW");
            });

            // ---------------------------------------------------------------- queries

            T.Suite("PlacesDb / queries");

            T.Test("search is a case-insensitive name substring", () =>
            {
                var hits = Db.Search("cragstone").ToList();
                T.True(hits.Count > 10, "Cragstone should match many places, got " + hits.Count);
                T.True(hits.All(p => p.Name.IndexOf("Cragstone", System.StringComparison.OrdinalIgnoreCase) >= 0),
                       "every hit contains the fragment");
            });

            T.Test("search on an empty fragment returns nothing (not everything)", () =>
            {
                T.Eq(0, Db.Search("   ").Count(), "blank search");
            });

            T.Test("Nearby: radius 3 around Holtburg returns a cluster, nearest first", () =>
            {
                // Holtburg is around 42.5N, 33.5E.
                var near = Db.Near(42.5, 33.5, 3.0).ToList();
                T.True(near.Count >= 5, "expected a cluster near Holtburg, got " + near.Count);

                double prev = -1;
                foreach (var p in near)
                {
                    double d = Coords.Distance(42.5, 33.5, p.NS, p.EW);
                    T.True(d <= 3.0, "inside the radius");
                    T.True(d >= prev - 1e-9, "sorted nearest-first");
                    prev = d;
                }
                T.True(near.Any(p => p.Name != null &&
                                     p.Name.IndexOf("Holtburg", System.StringComparison.OrdinalIgnoreCase) >= 0),
                       "at least one result is actually named Holtburg");
            });

            T.Test("Nearby in genuinely empty deep ocean returns nothing", () =>
            {
                // NOT the SW corner -- that holds the Singularity Caul / Advocate-tower cluster (real
                // data, 17 rows near 95S 95W). Pick a point with nothing around it: mid deep-ocean.
                T.Eq(0, Db.Near(-10.0, 130.0, 3.0).Count(), "no places in the far eastern deep ocean");
            });

            T.Test("Nearby DOES find the Singularity Caul cluster in the far SW corner", () =>
            {
                // Regression guard: proves the previous 'empty ocean' assumption was wrong about the
                // data, not the query. The Caul asylum & advocate towers really are at ~95S, 95W.
                var caul = Db.Near(-95.0, -95.0, 3.0).ToList();
                T.True(caul.Count >= 10, "the Caul corner is populated, got " + caul.Count);
            });

            T.Test("118 rows carry a DUNGEONID; all are in the empty-landblock corner", () =>
            {
                var withDungeon = Db.All.Where(p => p.DungeonId.HasValue).ToList();
                T.Eq(118, withDungeon.Count, "rows with DUNGEONID");
                T.True(withDungeon.All(p => p.DungeonId.Value >= 0x0107 && p.DungeonId.Value <= 0x02F0),
                       "dungeon landblocks live off the map grid");
            });

            T.Test("4 rows carry a full <LOC> landcell", () =>
            {
                T.Eq(4, Db.All.Count(p => p.Landcell.HasValue), "rows with LOC");
            });

            T.Test("<LOC> rows agree with their own COORD_X/COORD_Y", () =>
            {
                // Belt and braces: the transform and the database must tell the same story.
                foreach (var p in Db.All.Where(x => x.Landcell.HasValue))
                {
                    double ns, ew;
                    Coords.FromSurfaceCell(p.Landcell.Value, out ns, out ew);
                    T.Near(p.NS, ns, 0.051, "NS for " + p.Name);   // inclusive half-cell
                    T.Near(p.EW, ew, 0.051, "EW for " + p.Name);
                }
            });
        }

        private static void CheckPlace(string name, double ns, double ew)
        {
            T.Test(name + " -> " + Coords.Format(ns, ew), () =>
            {
                var p = Db.All.FirstOrDefault(x => string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));
                T.NotNull(p, "place '" + name + "' must exist in places.xml");
                T.Near(ns, p.NS, 0.15, "north-south");
                T.Near(ew, p.EW, 0.15, "east-west");
            });
        }
    }
}
