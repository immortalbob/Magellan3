using System.Collections.Generic;
using Magellan.World;

namespace Magellan.Data
{
    /// <summary>
    /// One row of Adam Wright's places database (places.xml, 3,307 entries, 2003).
    ///
    /// THE AXES. The shipped file stores tenths of a coordinate unit:
    ///     &lt;COORD_X&gt; = North * 10   (negative = South)
    ///     &lt;COORD_Y&gt; = East  * 10   (negative = West)
    /// The axes were verified against reverse-portal pairs and an independent 2002 coordinate list.
    ///
    /// THE NAME ELEMENT is &lt;NAME&gt; (3,306 of them), not &lt;n&gt; (zero of those).
    /// One row (ID 4829) has no &lt;NAME&gt; element at all -- absent, not empty.
    /// </summary>
    public sealed class Place
    {
        public int Id;
        public string Name;              // may be null/empty -- ID 4829
        public string Type;              // Interest | Portal | Community | Dungeon | Shop | Lifestone | Random Portal | Town | Wandering NPC
        public double NS;                // coordinate units, + = North
        public double EW;                // coordinate units, + = East
        public ushort? DungeonId;        // 16-bit landblock, present on 118 rows
        public uint? Landcell;           // full 32-bit landcell, present on 4 rows (<LOC>)
        public string LevelRestriction;  // 692 rows
        public string ExitLocation;      // 202 rows -- human-readable destination, e.g. "38.6S, 82.1E"
        public bool CoordsCorrected;     // true if an errata fix was applied at load

        public string Coordinates { get { return Coords.Format(NS, EW); } }

        public override string ToString()
        {
            return (Name ?? "(unnamed)") + " [" + Type + "] " + Coordinates;
        }
    }

    /// <summary>
    /// The six off-map coordinates in places_2.2.0.0.xml, and the one nameless row.
    ///
    /// Every one of the six is a stray trailing zero in COORD_Y (the east/west axis); none are
    /// in COORD_X. Keyed on ID, not name, because the shipped file spells one of them
    /// "North Lytlethrope Villas".
    ///
    /// The two Samsur rows are a DOUBLE stray zero, not a single one -- 19510 -> 195 -> 19.5E,
    /// which puts them next to Samsur (~1.6-3.8S, 18.4-18.6E). The original left
    /// these as "needs a lookup"; they were unsolvable only because the axes were transposed.
    ///
    /// The shipped data file is NEVER modified. Corrections are applied at load and counted.
    /// </summary>
    public static class PlacesErrata
    {
        /// <summary>ID -> corrected raw COORD_Y (tenths of a coordinate unit, East positive).</summary>
        public static readonly Dictionary<int, int> CorrectedCoordY = new Dictionary<int, int>
        {
            { 3111,   768 },   // Sho Roadside Portal      7680  -> 768   =>  64.5S,  76.8E  (Sho lands, SE)
            { 3188,   195 },   // Collector - Samsur      19510  -> 195   =>   2.7S,  19.5E
            { 3189,   195 },   // Provisioner - Samsur    19540  -> 195   =>   2.8S,  19.5E
            { 3244,  -655 },   // Tuskers Camping Spot    -6550  -> -655  =>  71.3S,  65.5W
            { 3330,   324 },   // Two Pillars              3240  -> 324   =>  26.5N,  32.4E
            { 4202,   504 },   // North Lytlethrope Villas 5040  -> 504   =>   2.2N,  50.4E
        };

        /// <summary>Rows whose &lt;NAME&gt; element is missing entirely.</summary>
        public static readonly Dictionary<int, string> MissingNames = new Dictionary<int, string>
        {
            { 4829, "(unnamed community)" },   // TYPE=Community, 56.5S 53.0E
        };

        /// <summary>|value| beyond this (in tenths) cannot be a real Dereth coordinate.</summary>
        public const int MaxTenths = 1020;
    }
}
