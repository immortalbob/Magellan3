using System;
using System.Collections.Generic;
using System.Linq;
using Magellan.Routing;

namespace Magellan.Tests
{
    public static class RoutingTests
    {
        public static void Register()
        {
            T.Suite("RoutePlanner / Dijkstra over the portal graph");

            // With no portals at all, the only route is a direct walk.
            T.Test("no portals -> a single direct-walk step", () =>
            {
                var rp = new RoutePlanner(new Portal[0]);
                var route = rp.Plan(0, 0, 10, 0);
                T.Eq(1, route.Count, "one step");
                T.Eq(RouteStep.Kind.Walk, route[0].Type, "it's a walk");
                T.Near(10.0, route[0].Cost, 0.001, "walk cost = straight-line distance");
            });

            // A portal that jumps exactly from start to end should be taken (its ~0 cost beats a long walk).
            T.Test("a portal that shortcuts the whole trip is used", () =>
            {
                var portals = new List<Portal>
                {
                    // enter at the start point, arrive at the destination
                    new Portal { Name = "Start to End", SrcNS = 0, SrcEW = 0, DstNS = 0, DstEW = 100 },
                };
                var rp = new RoutePlanner(portals) { WalkLinkRadius = 5.0 };
                var route = rp.Plan(0, 0, 0, 100);   // 100 units apart -> walking is far
                // Should contain a portal step, and total cost should be ~PortalCost, not ~100.
                T.True(route.Any(s => s.Type == RouteStep.Kind.Portal), "route uses the portal");
                double total = route.Sum(s => s.Cost);
                T.True(total < 5.0, "total cost is tiny (portal), not a 100-unit walk: " + total);
            });

            // A portal going the wrong way must NOT be used; plain walk wins.
            T.Test("a useless portal is ignored in favour of walking", () =>
            {
                var portals = new List<Portal>
                {
                    new Portal { Name = "Wrong Way", SrcNS = 0, SrcEW = 0, DstNS = 0, DstEW = -500 },
                };
                var rp = new RoutePlanner(portals) { WalkLinkRadius = 20.0 };
                var route = rp.Plan(0, 0, 0, 10);   // destination is 10 east; portal goes 500 west
                T.False(route.Any(s => s.Type == RouteStep.Kind.Portal), "no portal used");
                double total = route.Sum(s => s.Cost);
                T.Near(10.0, total, 0.001, "just a 10-unit walk");
            });

            // Two-hop: walk to a portal, take it, walk to the destination.
            T.Test("walk -> portal -> walk chains correctly", () =>
            {
                var portals = new List<Portal>
                {
                    // portal entrance is 3 units from start; it drops you 2 units from the destination
                    new Portal { Name = "Hop", SrcNS = 3, SrcEW = 0, DstNS = 200, DstEW = 0 },
                };
                var rp = new RoutePlanner(portals) { WalkLinkRadius = 10.0 };
                var route = rp.Plan(0, 0, 202, 0);   // 202 units by foot; via portal ~5 units of walking
                T.True(route.Any(s => s.Type == RouteStep.Kind.Portal), "uses the portal");
                double walk = route.Where(s => s.Type == RouteStep.Kind.Walk).Sum(s => s.Cost);
                T.True(walk < 10.0, "total walking is small (~3 + ~2), not 202: " + walk);
            });

            // The final step always lands on the destination coordinate.
            T.Test("route ends at the destination", () =>
            {
                var portals = new List<Portal>
                {
                    new Portal { Name = "Near", SrcNS = 1, SrcEW = 1, DstNS = 50, DstEW = 50 },
                };
                var rp = new RoutePlanner(portals) { WalkLinkRadius = 10.0 };
                var route = rp.Plan(0, 0, 52, 52);
                T.True(route.Count > 0, "has steps");
                var last = route[route.Count - 1];
                T.Near(52.0, last.NS, 0.001, "ends at dest NS");
                T.Near(52.0, last.EW, 0.001, "ends at dest EW");
            });

            // Portals are one-directional: a portal Src->Dst can't be traversed Dst->Src.
            T.Test("portals are one-way (cannot be walked backwards through the edge)", () =>
            {
                var portals = new List<Portal>
                {
                    new Portal { Name = "OneWay", SrcNS = 0, SrcEW = 0, DstNS = 0, DstEW = 300 },
                };
                // Want to go from the portal's DESTINATION back to its SOURCE. The portal edge only
                // goes Src->Dst, so this must fall back to a 300-unit walk, not a free hop.
                var rp = new RoutePlanner(portals) { WalkLinkRadius = 5.0 };
                var route = rp.Plan(0, 300, 0, 0);
                double total = route.Sum(s => s.Cost);
                T.Near(300.0, total, 0.001, "no reverse portal; full walk back");
            });

            // Consecutive walk legs collapse into one instruction.
            T.Test("consecutive walks collapse into a single step", () =>
            {
                // Three collinear nodes within walk radius, no portals: start -> mid(via end) should
                // collapse to one walk to end.
                var rp = new RoutePlanner(new Portal[0]);
                var route = rp.Plan(0, 0, 3, 0);
                T.Eq(1, route.Count, "collapsed to a single walk step");
            });

            T.Suite("Coords.TryParseDisplay / EXITLOCATION parsing");

            T.Test("all four hemisphere combinations parse with correct signs", () =>
            {
                double ns, ew;
                T.True(Magellan.World.Coords.TryParseDisplay("38.6S, 82.1E", out ns, out ew), "SE parses");
                T.Near(-38.6, ns, 0.001, "S is negative NS"); T.Near(82.1, ew, 0.001, "E is positive EW");
                T.True(Magellan.World.Coords.TryParseDisplay("33.4N, 57.1E", out ns, out ew), "NE parses");
                T.Near(33.4, ns, 0.001, "N positive"); T.Near(57.1, ew, 0.001, "E positive");
                T.True(Magellan.World.Coords.TryParseDisplay("40.7n, 82.5w", out ns, out ew), "lowercase NW parses (real data has lowercase)");
                T.Near(40.7, ns, 0.001, "n positive"); T.Near(-82.5, ew, 0.001, "w negative");
                T.True(Magellan.World.Coords.TryParseDisplay("62.6S, 81.6W", out ns, out ew), "SW parses");
                T.Near(-62.6, ns, 0.001, "S negative"); T.Near(-81.6, ew, 0.001, "W negative");
            });

            T.Test("EW-first order and flexible separators are accepted", () =>
            {
                double ns, ew;
                T.True(Magellan.World.Coords.TryParseDisplay("82.1E, 38.6S", out ns, out ew), "EW-first parses");
                T.Near(-38.6, ns, 0.001, "NS still lands in ns"); T.Near(82.1, ew, 0.001, "EW still lands in ew");
                T.True(Magellan.World.Coords.TryParseDisplay("  10.0N   20.0E  ", out ns, out ew), "no comma, extra spaces");
                T.Near(10.0, ns, 0.001, "ns"); T.Near(20.0, ew, 0.001, "ew");
            });

            T.Test("garbage is rejected", () =>
            {
                double ns, ew;
                T.False(Magellan.World.Coords.TryParseDisplay(null, out ns, out ew), "null");
                T.False(Magellan.World.Coords.TryParseDisplay("", out ns, out ew), "empty");
                T.False(Magellan.World.Coords.TryParseDisplay("38.6S", out ns, out ew), "only one part");
                T.False(Magellan.World.Coords.TryParseDisplay("38.6S, 82.1N", out ns, out ew), "two NS parts");
                T.False(Magellan.World.Coords.TryParseDisplay("38.6X, 82.1E", out ns, out ew), "bad hemisphere letter");
                T.False(Magellan.World.Coords.TryParseDisplay("38.6S, 82.1E junk", out ns, out ew), "trailing junk");
            });

            T.Suite("PortalDb.FromPlaces over the real places.xml");

            T.Test("builds the expected graph from the shipped data", () =>
            {
                var places = Magellan.Data.PlacesDb.FromFile(T.Data("places.xml"));
                var db = PortalDb.FromPlaces(places);
                // 203 EXITLOCATION carriers in the 2.2.0.0 data: 167 Portal + 34 Random Portal
                // + 1 Dungeon + 1 Interest. Randoms are excluded by default -> 169 edges.
                T.Eq(169, db.Portals.Count, "169 deterministic portal edges");
                T.Eq(0, db.Skipped, "every EXITLOCATION parses");
                var withRandoms = PortalDb.FromPlaces(places, includeRandomPortals: true);
                T.Eq(203, withRandoms.Portals.Count, "203 with random portals included");
            });

            T.Test("forensic ground truth: Cragstone to Hebian-To lands at 38.6S, 82.1E", () =>
            {
                // This exact portal is the proof case for retiring the 2.0.0.2 DEST fields: they
                // store (821, 386) which the old loader read as 82.1N, 38.6E -- transposed AND
                // hemisphere-lost. Ground truth from the original 2.2.0.0 MSI: 38.6S, 82.1E.
                var places = Magellan.Data.PlacesDb.FromFile(T.Data("places.xml"));
                var db = PortalDb.FromPlaces(places);
                var p = db.Portals.Find(x => x.Id == 2);
                T.True(p != null, "portal id 2 exists");
                T.Near(-38.6, p.DstNS, 0.001, "destination is 38.6 SOUTH");
                T.Near(82.1, p.DstEW, 0.001, "destination is 82.1 EAST");
                // and its source coords are the same values the search tabs show (shared parse)
                var place = null as Magellan.Data.Place;
                foreach (var pl in places.All) if (pl.Id == 2) { place = pl; break; }
                T.True(place != null, "place id 2 exists in PlacesDb");
                T.Near(place.NS, p.SrcNS, 0.0001, "source NS identical to PlacesDb");
                T.Near(place.EW, p.SrcEW, 0.0001, "source EW identical to PlacesDb");
            });

            T.Test("southern destinations exist in the graph (the hemisphere the old data lost)", () =>
            {
                var places = Magellan.Data.PlacesDb.FromFile(T.Data("places.xml"));
                var db = PortalDb.FromPlaces(places);
                int south = 0;
                foreach (var p in db.Portals) if (p.DstNS < 0) south++;
                T.True(south > 30, "dozens of southern destinations present (got " + south + ")");
            });
        }
    }
}
