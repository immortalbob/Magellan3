using System;
using System.Collections.Generic;

namespace Magellan.Mapping
{
    /// <summary>
    /// Where map lines land on the screen. Implement once against VVS's DxTexture; mock it in tests.
    /// </summary>
    public interface IMapCanvas
    {
        void Line(float x1, float y1, float x2, float y2, int argb);
        void Text(float x, float y, string s, int argb);
    }

    /// <summary>
    /// Pure transform + emit. Allocates nothing per frame, touches no DAT, blocks on nothing.
    /// Safe to call from the render thread; nothing else here is.
    /// </summary>
    public sealed class AutomapRenderer
    {
        // Recovered from magellan2.dll: the player dot was Ellipse(148,188,152,192) -> centre (150,190);
        // the dungeon name was TextOutA(hdc, 10, 80, ...).
        public float CenterX = 150f;
        public float CenterY = 190f;
        public float PixelsPerMetre = 2.0f;

        /// <summary>The original's "Lock map rotation" option: map stays north-up, the marker turns instead.</summary>
        public bool LockRotation;

        // Z-slab. DungeonViewer does this with an orthographic camera's near/far planes (R/F/T/G keys);
        // filtering edges is the same thing. Magellan 2 drew every level superimposed.
        public bool SliceByFloor = true;
        public float SliceHeight = 4.0f;    // metres either side of the slab centre
        public float SliceOffset = 0.0f;    // metres above/below the player -- lets the user page floors

        public int WallColor = unchecked((int)0xFFB0B0B0);
        public int TrailColor = unchecked((int)0xFF4080FF);
        public int PlayerColor = unchecked((int)0xFFFFFF00);
        public int TextColor = unchecked((int)0xFFFFFFFF);

        /// <summary>"Shadow footsteps": a ring buffer of past positions, drawn as a polyline.</summary>
        public int TrailLength = 300;
        public bool ShowTrail = true;

        /// <summary>A recorded footstep. Z is kept so the trail can be floor-sliced exactly like the
        /// walls -- without it, a multi-level dungeon's trail draws every floor at once ("blue spaghetti").</summary>
        private struct TrailPoint { public float X, Y, Z; public TrailPoint(float x, float y, float z) { X = x; Y = y; Z = z; } }

        private readonly Queue<TrailPoint> _trail = new Queue<TrailPoint>();

        /// <summary>Call on position change, not per frame.</summary>
        public void PushFootstep(float x, float y, float z)
        {
            if (_trail.Count > 0)
            {
                TrailPoint last = LastTrailPoint();
                float dx = last.X - x, dy = last.Y - y, dz = last.Z - z;
                // Dedupe in 3D: a step that barely moves horizontally AND vertically is a duplicate.
                // Checking Z too means climbing straight up a ladder/stairs still records points.
                if (dx * dx + dy * dy < 0.25f && dz * dz < 0.25f) return;
            }
            _trail.Enqueue(new TrailPoint(x, y, z));
            while (_trail.Count > TrailLength) _trail.Dequeue();
        }

        private TrailPoint LastTrailPoint()
        {
            TrailPoint last = default(TrailPoint);
            foreach (var v in _trail) last = v;      // Queue has no O(1) tail; the queue is small
            return last;
        }

        public int TrailCount { get { return _trail.Count; } }

        public void ClearTrail() { _trail.Clear(); }

        /// <summary>
        /// Landblock-local world point -> screen point.
        ///
        /// HEADING. Actions.Heading is compass degrees: North = 0, East = 90, increasing CLOCKWISE.
        /// The player's forward vector in (east, north) is therefore (sin h, cos h) and his right is
        /// (cos h, -sin h). Heading-up means forward maps to screen-up, so:
        ///
        ///     sx = C + (dx*cos h - dy*sin h)      <- component along "right"
        ///     sy = C - (dx*sin h + dy*cos h)      <- component along "forward", negated: screen Y grows DOWN
        ///
        /// i.e. the rotation angle is +h, not -h. Getting that sign wrong yields a transform that is
        /// correct at heading 0 and MIRRORED at every other heading -- face east and something due
        /// east of you renders behind you. That is the 2003 changelog's "Unreliable map rotation".
        /// The test suite pins it (RendererTests.FacingEast_PointDueEast_ProjectsAhead).
        /// </summary>
        public void Project(float wx, float wy, float px, float py, float headingDeg,
                            out float sx, out float sy)
        {
            float h = LockRotation ? 0f : (float)(headingDeg * Math.PI / 180.0);
            float cos = (float)Math.Cos(h), sin = (float)Math.Sin(h);

            float dx = (wx - px) * PixelsPerMetre;
            float dy = (wy - py) * PixelsPerMetre;

            sx = CenterX + (dx * cos - dy * sin);
            sy = CenterY - (dx * sin + dy * cos);
        }

        /// <param name="px">Player LocationX (landblock-local metres, east).</param>
        /// <param name="py">Player LocationY (landblock-local metres, north).</param>
        /// <param name="pz">Player LocationZ.</param>
        /// <param name="headingDeg">Actions.Heading: degrees, North = 0, East = 90.</param>
        public void Render(IMapCanvas g, DungeonGeometry geo,
                           float px, float py, float pz, float headingDeg, string caption)
        {
            if (g == null) return;

            float slabZ = pz + SliceOffset;

            if (geo != null)
            {
                var edges = geo.Edges;
                for (int i = 0; i < edges.Count; i++)
                {
                    MapEdge e = edges[i];
                    if (SliceByFloor && Math.Abs(e.Z - slabZ) > SliceHeight) continue;

                    float x1, y1, x2, y2;
                    Project(e.Ax, e.Ay, px, py, headingDeg, out x1, out y1);
                    Project(e.Bx, e.By, px, py, headingDeg, out x2, out y2);
                    g.Line(x1, y1, x2, y2, WallColor);
                }
            }

            if (ShowTrail)
            {
                bool have = false;
                TrailPoint prev = default(TrailPoint);
                foreach (var f in _trail)
                {
                    if (have)
                    {
                        // Floor-slice the trail exactly like the walls: draw a segment only when BOTH
                        // its endpoints are within the current floor slab. A segment that climbs between
                        // floors (stairs/ramp) is skipped, so you see only the trail on the floor you're
                        // on -- no "blue spaghetti" from every level drawn at once. When slicing is off,
                        // everything draws as before.
                        bool onFloor = !SliceByFloor
                            || (Math.Abs(prev.Z - slabZ) <= SliceHeight && Math.Abs(f.Z - slabZ) <= SliceHeight);
                        if (onFloor)
                        {
                            float x1, y1, x2, y2;
                            Project(prev.X, prev.Y, px, py, headingDeg, out x1, out y1);
                            Project(f.X, f.Y, px, py, headingDeg, out x2, out y2);
                            g.Line(x1, y1, x2, y2, TrailColor);
                        }
                    }
                    prev = f;
                    have = true;
                }
            }

            // Heading-up: the map turns and the marker points up. Rotation locked: the map is
            // north-up and the marker turns instead.
            float markerRot = LockRotation ? (float)(headingDeg * Math.PI / 180.0) : 0f;
            DrawMarker(g, CenterX, CenterY, markerRot, PlayerColor);

            if (!string.IsNullOrEmpty(caption))
                g.Text(6, 4, caption, TextColor);      // top-left of the map (title bar carries it too)
        }

        private static readonly Vec2[] Marker =
        {
            new Vec2(0, -6), new Vec2(4, 5), new Vec2(0, 2), new Vec2(-4, 5)
        };

        private static void DrawMarker(IMapCanvas g, float cx, float cy, float rot, int argb)
        {
            float c = (float)Math.Cos(rot), s = (float)Math.Sin(rot);
            for (int i = 0; i < Marker.Length; i++)
            {
                Vec2 u = Marker[i];
                Vec2 v = Marker[(i + 1) % Marker.Length];
                g.Line(cx + u.X * c - u.Y * s, cy + u.X * s + u.Y * c,
                       cx + v.X * c - v.Y * s, cy + v.X * s + v.Y * c, argb);
            }
        }
    }
}
