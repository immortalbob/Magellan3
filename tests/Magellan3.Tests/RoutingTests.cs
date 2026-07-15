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
        }
    }
}
