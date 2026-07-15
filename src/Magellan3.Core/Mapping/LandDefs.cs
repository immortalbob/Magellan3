using System;
using System.Collections.Generic;

namespace Magellan.Mapping
{
    /// <summary>
    /// Canonical AC landblock constants, from DungeonViewer's LandDefs.cpp.
    /// Do not re-derive these and do not guess them.
    /// </summary>
    public static class LandDefs
    {
        public const uint BlockIdMask = 0xFFFF0000;
        public const uint CellIdMask = 0x0000FFFF;

        /// <summary>The LandBlock (terrain) record: 0xLLLLFFFF.</summary>
        public const uint BlockCellId = 0x0000FFFF;

        /// <summary>The LandBlockInfo record: 0xLLLLFFFE. Carries NumCells -- no probing required.</summary>
        public const uint LbiCellId = 0x0000FFFE;

        /// <summary>EnvCells run 0x0100 .. 0xFFFD.</summary>
        public const uint FirstEnvCellId = 0x00000100;
        public const uint LastEnvCellId = 0x0000FFFD;

        /// <summary>portal.dat Environment id base: file id = 0x0D000000 | EnvCell.EnvironmentId.</summary>
        public const uint EnvironmentBase = 0x0D000000;

        public const float BlockLength = 192.0f;
        public const float SquareLength = 24.0f;
        public const int SideVertexCount = 9;

        public static uint LandblockOf(uint landcell) { return landcell & BlockIdMask; }

        /// <summary>
        /// True when the landcell is an interior cell -- a building or a dungeon.
        /// (AC wiki: "when this value is >= 0x0100 you are inside".)
        /// </summary>
        public static bool IsInterior(uint landcell)
        {
            uint c = landcell & CellIdMask;
            return c >= FirstEnvCellId && c <= LastEnvCellId;
        }

        public static uint EnvCellId(uint landblock, uint index)
        {
            return LandblockOf(landblock) | (FirstEnvCellId + index);
        }
    }

    /// <summary>One map line, in landblock-local metres. Z is carried so the renderer can slice by floor.</summary>
    public struct MapEdge
    {
        public readonly float Ax, Ay, Bx, By, Z;

        public MapEdge(float ax, float ay, float bx, float by, float z)
        {
            Ax = ax; Ay = ay; Bx = bx; By = by; Z = z;
        }
    }

    /// <summary>
    /// Everything the renderer needs for one landblock. Built once (off the render thread),
    /// then read-only forever.
    /// </summary>
    public sealed class DungeonGeometry
    {
        public uint Landblock { get; private set; }
        public IReadOnlyList<MapEdge> Edges { get; private set; }

        /// <summary>Cells the DAT didn't have yet. Non-zero means the client is still streaming: rebuild on OnUpdateCell.</summary>
        public int MissingCells { get; private set; }

        public DungeonGeometry(uint landblock, IReadOnlyList<MapEdge> edges, int missingCells = 0)
        {
            Landblock = landblock;
            Edges = edges ?? new MapEdge[0];
            MissingCells = missingCells;
        }

        public bool IsEmpty { get { return Edges.Count == 0; } }

        public static readonly DungeonGeometry Empty = new DungeonGeometry(0, new MapEdge[0]);
    }
}
