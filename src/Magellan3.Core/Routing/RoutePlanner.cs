using System;
using System.Collections.Generic;
using Magellan.World;

namespace Magellan.Routing
{
    /// <summary>
    /// One portal from the 2.0.0.2 places database: you enter at (SrcNS, SrcEW) and are dropped at
    /// (DstNS, DstEW). Both coordinate pairs use the corrected axes (COORD_X = North, COORD_Y = East;
    /// same for DEST_COORD_*).
    ///
    /// The 2.2.0.0 release removed routing (and the DEST_COORD fields from its data). This model is
    /// populated only from places_2.0.0.2.xml, which carries 149 portals with destination coordinates.
    /// </summary>
    public sealed class Portal
    {
        public int Id;
        public string Name;
        public double SrcNS, SrcEW;   // where you click to use the portal
        public double DstNS, DstEW;   // where it drops you
    }

    /// <summary>One leg of a planned route.</summary>
    public sealed class RouteStep
    {
        public enum Kind { Walk, Portal }
        public Kind Type;
        public string Text;           // human-readable instruction
        public double NS, EW;         // the point you arrive at after this step
        public double Cost;           // walking metres for this leg (portals are ~free)
    }

    /// <summary>
    /// Plans a route from a start coordinate to a destination coordinate over the portal network,
    /// minimising total walking distance. The graph:
    ///   * nodes  = the start, the destination, and every portal's source and destination point
    ///   * portal edges  = Src -> Dst at (near-)zero cost (instant travel)
    ///   * walk edges    = between any two nodes within WalkLinkRadius, cost = ground distance
    /// Plain Dijkstra over that graph. Walking the whole way is always an option (a direct walk edge
    /// from start to end is included), so a route always exists; portals are used only when they
    /// actually shorten the walk.
    ///
    /// This is pure and deterministic -- no DAT, no client -- so it is fully unit-tested.
    /// </summary>
    public sealed class RoutePlanner
    {
        private readonly List<Portal> _portals;

        /// <summary>
        /// How close (coordinate units) a node must be to walk directly to another. Portals across the
        /// map are reached by walking to a nearby portal first; this bounds the walking graph so we
        /// don't add a walk edge between every pair (O(n^2) of ~300 nodes is fine, but long direct
        /// walks are never preferable to a portal hop anyway). Large enough that the graph stays
        /// connected in practice; the direct start->end walk edge guarantees connectivity regardless.
        /// </summary>
        public double WalkLinkRadius = 200.0;

        /// <summary>Cost charged for taking a portal. Tiny but non-zero so equal-length routes prefer fewer hops.</summary>
        public double PortalCost = 0.1;

        public RoutePlanner(IEnumerable<Portal> portals)
        {
            _portals = new List<Portal>(portals ?? new Portal[0]);
        }

        public int PortalCount { get { return _portals.Count; } }

        /// <summary>
        /// Plan a route from (startNS,startEW) to (endNS,endEW). Returns the ordered legs, or a single
        /// direct-walk step if that's shortest. Never returns null.
        /// </summary>
        public List<RouteStep> Plan(double startNS, double startEW, double endNS, double endEW)
        {
            // Build the node list. 0 = start, 1 = end, then two nodes per portal (src, dst).
            var nodes = new List<Node>();
            nodes.Add(new Node { NS = startNS, EW = startEW, Label = "start" });
            nodes.Add(new Node { NS = endNS, EW = endEW, Label = "end" });

            var portalSrcNode = new int[_portals.Count];
            var portalDstNode = new int[_portals.Count];
            for (int i = 0; i < _portals.Count; i++)
            {
                var p = _portals[i];
                portalSrcNode[i] = nodes.Count; nodes.Add(new Node { NS = p.SrcNS, EW = p.SrcEW, Label = p.Name, PortalIndex = i, IsPortalSrc = true });
                portalDstNode[i] = nodes.Count; nodes.Add(new Node { NS = p.DstNS, EW = p.DstEW, Label = p.Name, PortalIndex = i });
            }

            int n = nodes.Count;
            var adj = new List<Edge>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<Edge>();

            // Walk edges: between any two nodes within WalkLinkRadius (both directions). Always include
            // the direct start->end walk so a route exists no matter how sparse the portal net is.
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double d = Coords.Distance(nodes[i].NS, nodes[i].EW, nodes[j].NS, nodes[j].EW);
                    if (d <= WalkLinkRadius)
                    {
                        adj[i].Add(new Edge { To = j, Cost = d, Walk = true });
                        adj[j].Add(new Edge { To = i, Cost = d, Walk = true });
                    }
                }
            // Guarantee the direct walk is present even if start/end are far apart.
            {
                double d = Coords.Distance(startNS, startEW, endNS, endEW);
                adj[0].Add(new Edge { To = 1, Cost = d, Walk = true });
            }

            // Portal edges: src -> dst, near-free, one-directional (portals aren't two-way).
            for (int i = 0; i < _portals.Count; i++)
                adj[portalSrcNode[i]].Add(new Edge { To = portalDstNode[i], Cost = PortalCost, Walk = false, PortalIndex = i });

            // Dijkstra from node 0 (start) to node 1 (end).
            var dist = new double[n];
            var prev = new int[n];
            var prevEdge = new Edge[n];
            for (int i = 0; i < n; i++) { dist[i] = double.PositiveInfinity; prev[i] = -1; }
            dist[0] = 0;

            var pq = new SimplePriorityQueue();
            pq.Push(0, 0);
            var settled = new bool[n];

            while (pq.Count > 0)
            {
                int u = pq.Pop();
                if (settled[u]) continue;
                settled[u] = true;
                if (u == 1) break;

                foreach (var e in adj[u])
                {
                    double nd = dist[u] + e.Cost;
                    if (nd < dist[e.To])
                    {
                        dist[e.To] = nd;
                        prev[e.To] = u;
                        prevEdge[e.To] = e;
                        pq.Push(e.To, nd);
                    }
                }
            }

            var steps = new List<RouteStep>();
            if (double.IsInfinity(dist[1])) return steps;   // unreachable (shouldn't happen: direct walk exists)

            // Reconstruct the path start->end.
            var path = new List<int>();
            for (int at = 1; at != -1; at = prev[at]) path.Add(at);
            path.Reverse();

            for (int k = 1; k < path.Count; k++)
            {
                int to = path[k];
                var e = prevEdge[to];
                var nd = nodes[to];
                if (e.Walk)
                {
                    steps.Add(new RouteStep
                    {
                        Type = RouteStep.Kind.Walk,
                        Text = "Walk to " + Coords.Format(nd.NS, nd.EW),
                        NS = nd.NS, EW = nd.EW, Cost = e.Cost,
                    });
                }
                else
                {
                    var p = _portals[e.PortalIndex];
                    steps.Add(new RouteStep
                    {
                        Type = RouteStep.Kind.Portal,
                        Text = "Take portal: " + (p.Name ?? "(portal)") + " -> " + Coords.Format(nd.NS, nd.EW),
                        NS = nd.NS, EW = nd.EW, Cost = e.Cost,
                    });
                }
            }

            // Collapse consecutive walk steps into one (walking A->B->C with no portal between is just
            // "walk to C"); keeps the instruction list readable.
            return Collapse(steps, endNS, endEW);
        }

        private static List<RouteStep> Collapse(List<RouteStep> steps, double endNS, double endEW)
        {
            var outp = new List<RouteStep>();
            foreach (var s in steps)
            {
                if (s.Type == RouteStep.Kind.Walk && outp.Count > 0 && outp[outp.Count - 1].Type == RouteStep.Kind.Walk)
                {
                    var last = outp[outp.Count - 1];
                    last.Cost += s.Cost;
                    last.NS = s.NS; last.EW = s.EW;
                    last.Text = "Walk to " + Coords.Format(s.NS, s.EW);
                }
                else outp.Add(s);
            }
            return outp;
        }

        private struct Node { public double NS, EW; public string Label; public int PortalIndex; public bool IsPortalSrc; }
        private struct Edge { public int To; public double Cost; public bool Walk; public int PortalIndex; }

        // Minimal binary-heap priority queue (no external deps, matches the project's no-package rule).
        private sealed class SimplePriorityQueue
        {
            private readonly List<int> _ids = new List<int>();
            private readonly List<double> _keys = new List<double>();
            public int Count { get { return _ids.Count; } }

            public void Push(int id, double key)
            {
                _ids.Add(id); _keys.Add(key);
                int i = _ids.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_keys[parent] <= _keys[i]) break;
                    Swap(i, parent); i = parent;
                }
            }

            public int Pop()
            {
                int top = _ids[0];
                int last = _ids.Count - 1;
                _ids[0] = _ids[last]; _keys[0] = _keys[last];
                _ids.RemoveAt(last); _keys.RemoveAt(last);
                int i = 0, n = _ids.Count;
                while (true)
                {
                    int l = 2 * i + 1, r = 2 * i + 2, s = i;
                    if (l < n && _keys[l] < _keys[s]) s = l;
                    if (r < n && _keys[r] < _keys[s]) s = r;
                    if (s == i) break;
                    Swap(i, s); i = s;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                int ti = _ids[a]; _ids[a] = _ids[b]; _ids[b] = ti;
                double tk = _keys[a]; _keys[a] = _keys[b]; _keys[b] = tk;
            }
        }
    }
}
