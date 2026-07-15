using System.Collections.Generic;
using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Magellan.Mapping;

// DatReaderWriter.Types also defines a LandDefs; ours is the one we mean throughout this file.
using LandDefs = Magellan.Mapping.LandDefs;

namespace Magellan.Plugin.Mapping
{
    /// <summary>
    /// The recovered Magellan 2 automap algorithm, re-expressed against DatReaderWriter.
    ///
    ///   landblock  = landcell &amp; 0xFFFF0000
    ///   info       = cell.dat[ landblock | 0xFFFE ]        (LandBlockInfo -- carries NumCells)
    ///   for i in 0 .. info.NumCells-1:
    ///       envCell = cell.dat[ landblock | (0x0100 + i) ] (EnvCell)
    ///       env     = portal.dat[ 0x0D000000 | envCell.EnvironmentId ]
    ///       cs      = env.Cells[ envCell.CellStructure ]
    ///       for poly in cs.Polygons: transform verts by envCell.Position, feed the OutlineBuilder
    ///
    /// Confirmed identical across three independent sources: magellan2.dll @ 0x10005fa0/0x10006140,
    /// DungeonViewer's DungeonMapScene::Init + CEnvCell::UnPack, and the DatReaderWriter records.
    ///
    /// This is the ONE place DAT types meet the pure geometry code. It is blocking (it reads the
    /// dats and allocates), so it runs off the render thread -- once per landblock change, or on
    /// FileService.OnUpdateCell. RenderFrame never calls it.
    /// </summary>
    public sealed class DungeonMapper
    {
        private readonly IDatSource _dat;

        /// <summary>A polygon is floor when its plane normal is within ~45 deg of vertical.</summary>
        public float FloorNormalZ = 0.70f;

        public DungeonMapper(IDatSource dat) { _dat = dat; }

        public static bool IsDungeon(uint landcell) { return LandDefs.IsInterior(landcell); }
        public static uint LandblockOf(uint landcell) { return LandDefs.LandblockOf(landcell); }

        /// <summary>
        /// Build the outline for the landblock the given landcell is in. Blocking. Off-thread.
        /// Returns <see cref="DungeonGeometry.Empty"/> if this isn't an interior or the LandBlockInfo
        /// isn't available yet.
        /// </summary>
        public DungeonGeometry Build(uint landcell)
        {
            uint lb = LandblockOf(landcell);

            LandBlockInfo info;
            if (!_dat.TryGetCell(lb | LandDefs.LbiCellId, out info) || info.NumCells == 0)
                return DungeonGeometry.Empty;

            var outline = new OutlineBuilder { FloorNormalZ = FloorNormalZ };
            var envCache = new Dictionary<ushort, Environment>();
            int missing = 0;

            for (uint i = 0; i < info.NumCells; i++)
            {
                uint cellId = lb | (LandDefs.FirstEnvCellId + i);

                EnvCell cell;
                if (!_dat.TryGetCell(cellId, out cell))   // client hasn't streamed this cell yet
                {
                    missing++;
                    continue;
                }
                if (cell.Position == null) { missing++; continue; }   // torn read guard (Frame is a class)

                Environment env;
                if (!envCache.TryGetValue(cell.EnvironmentId, out env))
                {
                    if (!_dat.TryGetPortal(LandDefs.EnvironmentBase | cell.EnvironmentId, out env))
                        continue;
                    envCache[cell.EnvironmentId] = env;
                }

                CellStruct cs;
                if (env.Cells == null || !env.Cells.TryGetValue(cell.CellStructure, out cs)) continue;

                EmitCell(cs, cell.Position, outline);
            }

            // ONE landblock-scoped flush. OutlineBuilder guarantees seams between adjacent cells
            // cancel -- see OutlineTests "two adjacent floor quads share a seam that CANCELS".
            return outline.Build(lb, missing);
        }

        private void EmitCell(CellStruct cs, Frame frame, OutlineBuilder outline)
        {
            var verts = cs.VertexArray != null ? cs.VertexArray.Vertices : null;
            if (verts == null || verts.Count == 0 || cs.Polygons == null) return;

            // scratch holds landblock-local points in Core's dependency-free Vec3. The Quaternion
            // transform stays in System.Numerics (this is net48; DatReaderWriter uses it), and we
            // convert to Vec3 at the boundary before handing off to the pure OutlineBuilder.
            var scratch = new List<Vec3>(8);

            foreach (var poly in cs.Polygons.Values)
            {
                var ids = poly.VertexIds;
                if (ids == null || ids.Count < 3) continue;

                scratch.Clear();
                bool ok = true;
                for (int k = 0; k < ids.Count; k++)
                {
                    SWVertex sw;
                    if (!verts.TryGetValue((ushort)ids[k], out sw)) { ok = false; break; }
                    // Cell-local -> landblock-local:  world = origin + rotate(orientation, v)
                    Vector3 world = frame.Origin + Vector3.Transform(sw.Origin, frame.Orientation);
                    scratch.Add(new Vec3(world.X, world.Y, world.Z));
                }
                if (!ok) continue;

                outline.AddPolygon(scratch);
            }
        }
    }
}
