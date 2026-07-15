using System.Collections.Generic;
using Magellan.Mapping;

namespace Magellan.Tests
{
    /// <summary>
    /// The boundary-outline extractor -- and specifically the property that the per-EnvCell version
    /// gets wrong: a floor seam shared between two adjacent cells must cancel, so a room built from
    /// several cells reads as one outline, not a grid of sealed boxes.
    /// </summary>
    public static class OutlineTests
    {
        // A unit floor quad in the XY plane at height z, wound CCW.
        private static List<Vec3> FloorQuad(float x0, float y0, float x1, float y1, float z)
        {
            return new List<Vec3>
            {
                new Vec3(x0, y0, z),
                new Vec3(x1, y0, z),
                new Vec3(x1, y1, z),
                new Vec3(x0, y1, z),
            };
        }

        public static void Register()
        {
            T.Suite("OutlineBuilder");

            T.Test("a single floor quad yields its 4 boundary edges", () =>
            {
                var b = new OutlineBuilder();
                b.AddPolygon(FloorQuad(0, 0, 10, 10, 0));
                var geo = b.Build(0x01E50000);
                T.Eq(4, geo.Edges.Count, "four walls around one floor");
                T.Eq(1, b.FloorPolygonsSeen, "one floor polygon");
            });

            // THE BUG. Two quads sharing the edge x=10 (from (10,0) to (10,10)). The seam must
            // cancel, leaving a 6-edge outline of the combined 20x10 room -- NOT 8 edges.
            //
            // A per-EnvCell accumulator sees each quad in isolation, marks the shared edge as a
            // 1-use boundary in BOTH cells, and draws a wall straight down the middle of the room.
            T.Test("two adjacent floor quads share a seam that CANCELS (6 edges, not 8)", () =>
            {
                var b = new OutlineBuilder();
                b.AddPolygon(FloorQuad(0, 0, 10, 10, 0));    // left cell
                b.AddPolygon(FloorQuad(10, 0, 20, 10, 0));   // right cell, abutting at x=10
                var geo = b.Build(0x01E50000);

                T.Eq(6, geo.Edges.Count, "the shared seam cancels; the room is one outline");

                // And prove the seam itself is gone: no drawn edge runs along x=10 spanning y 0..10.
                foreach (var e in geo.Edges)
                {
                    bool onSeam =
                        System.Math.Abs(e.Ax - 10) < 0.01f && System.Math.Abs(e.Bx - 10) < 0.01f;
                    T.False(onSeam, "no phantom wall down the seam at x=10");
                }
            });

            T.Test("quantisation lets cells that abut with float noise still cancel", () =>
            {
                var b = new OutlineBuilder { QuantiseMetres = 0.05f };
                b.AddPolygon(FloorQuad(0, 0, 10, 10, 0));
                // Right cell nudged by 2 cm -- below the 5 cm quantum, so the seam must still meet.
                b.AddPolygon(new List<Vec3>
                {
                    new Vec3(10.02f, 0.00f, 0), new Vec3(20f, 0.00f, 0),
                    new Vec3(20f, 10.00f, 0),   new Vec3(10.02f, 10.00f, 0),
                });
                var geo = b.Build(0x01E50000);
                T.Eq(6, geo.Edges.Count, "sub-quantum gap still cancels the seam");
            });

            T.Test("walls and ceilings (non-floor polygons) are discarded", () =>
            {
                var b = new OutlineBuilder();
                b.AddPolygon(FloorQuad(0, 0, 10, 10, 0));            // floor -> kept
                // A vertical wall quad (normal is horizontal): the XZ plane at y=0.
                b.AddPolygon(new List<Vec3>
                {
                    new Vec3(0, 0, 0), new Vec3(10, 0, 0),
                    new Vec3(10, 0, 5), new Vec3(0, 0, 5),
                });
                var geo = b.Build(0x01E50000);
                T.Eq(1, b.FloorPolygonsSeen, "only the floor counts as floor");
                T.Eq(4, geo.Edges.Count, "the wall contributes no map edges");
            });

            T.Test("stacked floors at different Z do not cancel each other", () =>
            {
                var b = new OutlineBuilder();
                b.AddPolygon(FloorQuad(0, 0, 10, 10, 0));      // ground floor
                b.AddPolygon(FloorQuad(0, 0, 10, 10, 12));     // identical footprint, one storey up
                var geo = b.Build(0x01E50000);
                // Z is part of the key, so these are 8 distinct edges, not 0.
                T.Eq(8, geo.Edges.Count, "vertically stacked floors keep their own outlines");
            });

            T.Test("degenerate and sub-triangle polygons are ignored", () =>
            {
                var b = new OutlineBuilder();
                b.AddPolygon(new List<Vec3> { new Vec3(0, 0, 0), new Vec3(1, 1, 0) }); // 2 pts
                b.AddPolygon(null);
                var geo = b.Build(0x01E50000);
                T.Eq(0, geo.Edges.Count, "nothing drawn");
                T.Eq(0, b.PolygonsSeen, "sub-triangle polys not even counted");
            });

            T.Test("Newell normal points up for a CCW floor", () =>
            {
                var n = OutlineBuilder.Newell(FloorQuad(0, 0, 10, 10, 0));
                T.Near(1.0, n.Z, 1e-5, "floor normal is +Z");
            });
        }
    }
}
