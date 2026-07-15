using System;
using System.Collections.Generic;

namespace Magellan.Mapping
{
    /// <summary>
    /// Turns dungeon polygons into a clean floor outline.
    ///
    /// THE RULE: a floor polygon edge used by exactly ONE floor polygon is a boundary -- a wall
    /// on the map. Edges shared by two floor polygons are interior and cancel.
    ///
    /// THE BUG THIS EXISTS TO PREVENT. The obvious implementation accumulates the edge table
    /// per EnvCell. That is wrong. A dungeon room spans many EnvCells and adjacent cells share a
    /// floor seam; within its own cell each side of the seam is a 1-use edge, so EVERY cell
    /// boundary draws as a wall and the dungeon comes out as a grid of sealed rooms with no
    /// doorways. The accumulator therefore lives for the whole LANDBLOCK -- which is why this is
    /// a class you feed, not a function you call per cell. The shape enforces the fix.
    ///
    /// Vertices must already be in landblock-local space (i.e. transformed by the EnvCell's Frame).
    /// The Z component is part of the key, so stacked levels of a multi-storey dungeon do not
    /// cancel against each other.
    ///
    /// Pure. No DAT types, no Decal, no game. This is where the bugs live, so this is what gets tested.
    /// </summary>
    public sealed class OutlineBuilder
    {
        /// <summary>A polygon counts as floor when its plane normal is within ~45 deg of vertical.</summary>
        public float FloorNormalZ = 0.70f;

        /// <summary>
        /// Vertex quantisation, metres. Cell geometry is modular and abuts exactly, but each cell's
        /// vertices go through a separate float Frame transform, so seams need a tolerance to meet.
        /// 5 cm is comfortably below AC's smallest feature and comfortably above float noise.
        /// </summary>
        public float QuantiseMetres = 0.05f;

        private readonly Dictionary<EdgeKey, Edge> _edges = new Dictionary<EdgeKey, Edge>();
        private int _floorPolys;
        private int _polys;

        public int PolygonsSeen { get { return _polys; } }
        public int FloorPolygonsSeen { get { return _floorPolys; } }

        /// <summary>
        /// Add one polygon, its vertices already in landblock-local space.
        /// Non-floor polygons (walls, ceilings) are counted and discarded: drawing them all is
        /// what produced the original's "too many trifans" failure.
        /// </summary>
        public void AddPolygon(IList<Vec3> worldPts)
        {
            if (worldPts == null || worldPts.Count < 3) return;
            _polys++;

            var n = Newell(worldPts);
            if (Math.Abs(n.Z) < FloorNormalZ) return;     // not a floor
            _floorPolys++;

            for (int k = 0; k < worldPts.Count; k++)
            {
                Vec3 a = worldPts[k];
                Vec3 b = worldPts[(k + 1) % worldPts.Count];

                long qa = Quantise(a), qb = Quantise(b);
                if (qa == qb) continue;                  // degenerate

                var key = new EdgeKey(qa, qb);
                Edge e;
                if (_edges.TryGetValue(key, out e))
                {
                    e.Uses++;
                    _edges[key] = e;
                }
                else
                {
                    _edges[key] = new Edge { A = a, B = b, Uses = 1 };
                }
            }
        }

        /// <summary>Emit the boundary edges. Call once, after every cell in the landblock has been added.</summary>
        public DungeonGeometry Build(uint landblock, int missingCells = 0)
        {
            var outEdges = new List<MapEdge>(_edges.Count);
            foreach (var e in _edges.Values)
            {
                if (e.Uses != 1) continue;               // interior seam -- cancels
                outEdges.Add(new MapEdge(e.A.X, e.A.Y, e.B.X, e.B.Y, (e.A.Z + e.B.Z) * 0.5f));
            }
            return new DungeonGeometry(landblock, outEdges, missingCells);
        }

        // -------------------------------------------------------------------------- internals

        private struct Edge { public Vec3 A, B; public int Uses; }

        /// <summary>Order-independent undirected edge key. A struct, so no allocation and no hash collisions to reason about.</summary>
        private struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly long _lo, _hi;

            public EdgeKey(long a, long b)
            {
                if (a <= b) { _lo = a; _hi = b; } else { _lo = b; _hi = a; }
            }

            public bool Equals(EdgeKey o) { return _lo == o._lo && _hi == o._hi; }
            public override bool Equals(object o) { return o is EdgeKey && Equals((EdgeKey)o); }

            public override int GetHashCode()
            {
                unchecked { return (_lo.GetHashCode() * 397) ^ _hi.GetHashCode(); }
            }
        }

        /// <summary>
        /// Pack a quantised vertex into 63 bits: 21 bits per axis, masked so negatives don't
        /// sign-extend into a neighbour's field. 21 signed bits at 5 cm = +/- 52 km -- Dereth is 49 km across.
        /// </summary>
        private long Quantise(Vec3 v)
        {
            long x = (long)Math.Round(v.X / QuantiseMetres) & 0x1FFFFF;
            long y = (long)Math.Round(v.Y / QuantiseMetres) & 0x1FFFFF;
            long z = (long)Math.Round(v.Z / QuantiseMetres) & 0x1FFFFF;
            return (x << 42) | (y << 21) | z;
        }

        /// <summary>Newell's method: a robust plane normal for an n-gon, including non-planar and degenerate ones. Public so tests in a separate assembly can verify it.</summary>
        public static Vec3 Newell(IList<Vec3> p)
        {
            Vec3 n = Vec3.Zero;
            for (int i = 0; i < p.Count; i++)
            {
                Vec3 c = p[i];
                Vec3 d = p[(i + 1) % p.Count];
                n.X += (c.Y - d.Y) * (c.Z + d.Z);
                n.Y += (c.Z - d.Z) * (c.X + d.X);
                n.Z += (c.X - d.X) * (c.Y + d.Y);
            }
            return n.LengthSquared() > 1e-9f ? n.Normalized() : Vec3.UnitZ;
        }
    }
}
