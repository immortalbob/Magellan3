using System;
using System.Collections.Generic;
using Magellan.Mapping;

namespace Magellan.Tests
{
    /// <summary>Records every line/text the renderer emits, so we can assert on geometry.</summary>
    internal sealed class RecordingCanvas : IMapCanvas
    {
        public readonly List<(float x1, float y1, float x2, float y2, int argb)> Lines
            = new List<(float, float, float, float, int)>();
        public readonly List<(float x, float y, string s, int argb)> Texts
            = new List<(float, float, string, int)>();

        public void Line(float x1, float y1, float x2, float y2, int argb) { Lines.Add((x1, y1, x2, y2, argb)); }
        public void Text(float x, float y, string s, int argb) { Texts.Add((x, y, s, argb)); }
    }

    public static class RendererTests
    {
        public static void Register()
        {
            T.Suite("AutomapRenderer / projection");

            // The centre must be the player, always.
            T.Test("the player's own position projects to the screen centre", () =>
            {
                var r = new AutomapRenderer { CenterX = 150, CenterY = 190, PixelsPerMetre = 2f };
                float sx, sy;
                r.Project(50, 60, 50, 60, 123f, out sx, out sy);   // world point == player point
                T.Near(150, sx, 1e-4, "sx");
                T.Near(190, sy, 1e-4, "sy");
            });

            // THE HEADING BUG. Facing north (0 deg), a point due north of the player must be ABOVE
            // centre (smaller screen Y). Nothing controversial yet.
            T.Test("facing north, a point due north renders above centre", () =>
            {
                var r = new AutomapRenderer { CenterX = 150, CenterY = 190, PixelsPerMetre = 2f };
                float sx, sy;
                r.Project(0, 10, 0, 0, 0f, out sx, out sy);        // 10 m north
                T.Near(150, sx, 1e-4, "dead ahead: no sideways shift");
                T.True(sy < 190 - 1, "north is up (sy=" + sy + ")");
            });

            // The one that catches the sign error. Facing EAST (90 deg), a point due east of the
            // player is in front of him, so heading-up it must render ABOVE centre -- NOT below.
            // With the -heading sign the plan shipped, this point lands behind the player.
            T.Test("facing east, a point due east renders AHEAD (above centre), not behind", () =>
            {
                var r = new AutomapRenderer { CenterX = 150, CenterY = 190, PixelsPerMetre = 2f };
                float sx, sy;
                r.Project(10, 0, 0, 0, 90f, out sx, out sy);       // 10 m east; player faces east
                T.Near(150, sx, 1e-3, "dead ahead: no sideways shift");
                T.True(sy < 190 - 1, "forward is up (sy=" + sy + "); if this is >190 the rotation is mirrored");
            });

            // And the point to the player's LEFT while facing east (i.e. due north) must be on the
            // left half of the screen. This nails down the handedness, not just the sign.
            T.Test("facing east, a point due north renders to the LEFT", () =>
            {
                var r = new AutomapRenderer { CenterX = 150, CenterY = 190, PixelsPerMetre = 2f };
                float sx, sy;
                r.Project(0, 10, 0, 0, 90f, out sx, out sy);       // 10 m north; player faces east -> north is left
                T.True(sx < 150 - 1, "north is on the left when facing east (sx=" + sx + ")");
                T.Near(190, sy, 1e-3, "directly abeam: no forward/back shift");
            });

            T.Test("lock rotation: north is always up regardless of heading", () =>
            {
                var r = new AutomapRenderer { CenterX = 150, CenterY = 190, PixelsPerMetre = 2f, LockRotation = true };
                float sx, sy;
                r.Project(0, 10, 0, 0, 217f, out sx, out sy);      // arbitrary heading, ignored
                T.Near(150, sx, 1e-4, "no sideways shift");
                T.True(sy < 190 - 1, "north stays up");
            });

            T.Suite("AutomapRenderer / slicing & trail");

            T.Test("floor slice hides geometry outside the vertical slab", () =>
            {
                var edges = new List<MapEdge>
                {
                    new MapEdge(0, 0, 10, 0, 0f),      // player's floor (z=0)
                    new MapEdge(0, 0, 10, 0, 20f),     // a floor 20 m up
                };
                var geo = new DungeonGeometry(0x01E50000, edges);

                var r = new AutomapRenderer { SliceByFloor = true, SliceHeight = 4f };
                var c = new RecordingCanvas();
                r.Render(c, geo, 0, 0, 0, 0f, null);

                // Exactly one wall line drawn (the near floor); the marker adds its own 4 lines.
                int wall = 0;
                foreach (var l in c.Lines) if (l.argb == r.WallColor) wall++;
                T.Eq(1, wall, "only the floor within the slab is drawn");
            });

            T.Test("slice offset pages up to a higher floor", () =>
            {
                var edges = new List<MapEdge> { new MapEdge(0, 0, 10, 0, 20f) };
                var geo = new DungeonGeometry(0x01E50000, edges);

                var r = new AutomapRenderer { SliceByFloor = true, SliceHeight = 4f, SliceOffset = 20f };
                var c = new RecordingCanvas();
                r.Render(c, geo, 0, 0, 0, 0f, null);

                int wall = 0;
                foreach (var l in c.Lines) if (l.argb == r.WallColor) wall++;
                T.Eq(1, wall, "offset brings the upper floor into the slab");
            });

            T.Test("footstep trail dedupes points closer than 0.5 m and caps its length", () =>
            {
                var r = new AutomapRenderer { TrailLength = 3 };
                r.PushFootstep(0, 0, 0);
                r.PushFootstep(0.1f, 0, 0);     // within 0.5 m -> ignored
                T.Eq(1, r.TrailCount, "near-duplicate ignored");

                r.PushFootstep(1, 0, 0);
                r.PushFootstep(2, 0, 0);
                r.PushFootstep(3, 0, 0);
                r.PushFootstep(4, 0, 0);        // over cap -> oldest dropped
                T.Eq(3, r.TrailCount, "ring buffer capped at TrailLength");
            });

            T.Test("footstep trail is floor-sliced by Z, like the walls", () =>
            {
                // Two floors: three steps at Z=0, then three at Z=10 (a second level). Standing on the
                // lower floor (pz=0), only the lower-floor trail segments should draw; the upper floor
                // and the connecting climb should be culled.
                var r = new AutomapRenderer { SliceByFloor = true, SliceHeight = 4f, TrailLength = 100 };
                r.PushFootstep(0, 0, 0);
                r.PushFootstep(2, 0, 0);
                r.PushFootstep(4, 0, 0);     // lower floor: 2 segments
                r.PushFootstep(4, 0, 10);    // climb up (spans floors) -> culled
                r.PushFootstep(6, 0, 10);
                r.PushFootstep(8, 0, 10);    // upper floor: its segments culled while we're below

                var lower = new RecordingCanvas();
                r.Render(lower, DungeonGeometry.Empty, 0, 0, 0f, 0f, null);   // pz = 0
                int lowerTrail = 0;
                foreach (var l in lower.Lines) if (l.argb == r.TrailColor) lowerTrail++;
                T.Eq(2, lowerTrail, "only the 2 lower-floor segments draw when standing on the lower floor");

                // Now stand on the upper floor (pz=10): only the upper-floor segments draw.
                var upper = new RecordingCanvas();
                r.Render(upper, DungeonGeometry.Empty, 0, 0, 10f, 0f, null);  // pz = 10
                int upperTrail = 0;
                foreach (var l in upper.Lines) if (l.argb == r.TrailColor) upperTrail++;
                T.Eq(2, upperTrail, "only the 2 upper-floor segments draw when standing on the upper floor");

                // With slicing OFF, every segment draws (5 segments across 6 points).
                r.SliceByFloor = false;
                var all = new RecordingCanvas();
                r.Render(all, DungeonGeometry.Empty, 0, 0, 0f, 0f, null);
                int allTrail = 0;
                foreach (var l in all.Lines) if (l.argb == r.TrailColor) allTrail++;
                T.Eq(5, allTrail, "slicing off -> all 5 segments draw");
            });

            T.Test("caption is drawn at the top of the map (6, 4)", () =>
            {
                var r = new AutomapRenderer();
                var c = new RecordingCanvas();
                r.Render(c, DungeonGeometry.Empty, 0, 0, 0, 0f, "Green Mire Grave");
                T.Eq(1, c.Texts.Count, "one caption");
                T.Near(6, c.Texts[0].x, 0.01, "caption x");
                T.Near(4, c.Texts[0].y, 0.01, "caption y");
                T.Eq("Green Mire Grave", c.Texts[0].s, "caption text");
            });

            T.Test("null canvas is a no-op, not a crash", () =>
            {
                new AutomapRenderer().Render(null, DungeonGeometry.Empty, 0, 0, 0, 0f, "x");
                T.True(true, "did not throw");
            });
        }
    }
}
