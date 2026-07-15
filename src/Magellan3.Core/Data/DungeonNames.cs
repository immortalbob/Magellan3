using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Magellan.Data
{
    /// <summary>
    /// Landblock -> dungeon name. 653 entries, a strict superset of the 118 DUNGEONID rows
    /// Magellan 2 shipped (verified: 0 of Magellan's ids are absent here).
    ///
    /// Source: DungeonViewer's dungeons.dvp, merged with Magellan's own table; where the two
    /// disagree on a name (12 landblocks), Magellan's variant is preserved in a fourth column.
    ///
    /// TSV rather than JSON deliberately: netstandard2.0 has no JSON reader in the box, and this
    /// keeps Magellan3.Core free of every package dependency.
    /// </summary>
    public sealed class DungeonNames
    {
        private readonly Dictionary<ushort, Entry> _byLandblock = new Dictionary<ushort, Entry>();

        public sealed class Entry
        {
            public ushort Landblock;
            public string Name;
            public string Source;            // "dvp" | "magellan" | "both"
            public string MagellanVariant;   // set where the two tables disagree
        }

        public int Count { get { return _byLandblock.Count; } }
        public IEnumerable<Entry> All { get { return _byLandblock.Values; } }

        public static DungeonNames FromFile(string path)
        {
            using (var r = new StreamReader(path)) return FromReader(r);
        }

        public static DungeonNames FromStream(Stream s)
        {
            using (var r = new StreamReader(s)) return FromReader(r);
        }

        private static DungeonNames FromReader(TextReader r)
        {
            var db = new DungeonNames();
            string line = r.ReadLine();                       // header: landblock name source magellan_variant
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var f = line.Split('\t');
                if (f.Length < 2) continue;

                var s = f[0].Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);

                ushort lb;
                if (!ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out lb)) continue;

                db._byLandblock[lb] = new Entry
                {
                    Landblock = lb,
                    Name = f[1].Trim(),
                    Source = f.Length > 2 ? f[2].Trim() : null,
                    MagellanVariant = f.Length > 3 && !string.IsNullOrWhiteSpace(f[3]) ? f[3].Trim() : null,
                };
            }
            return db;
        }

        /// <summary>Name of the dungeon the given landcell is in, or null if we've never heard of it.</summary>
        public string Lookup(uint landcell)
        {
            Entry e;
            return _byLandblock.TryGetValue(Magellan.World.Coords.Landblock(landcell), out e) ? e.Name : null;
        }

        public bool TryGet(ushort landblock, out Entry entry)
        {
            return _byLandblock.TryGetValue(landblock, out entry);
        }

        /// <summary>
        /// The caption the original painted at screen (10, 80). Falls back to the raw landblock id
        /// for dungeons nobody has named yet -- which, on an ACE server running custom content,
        /// is most of them. That gap is the whole reason this plugin generates maps instead of
        /// downloading them.
        /// </summary>
        public string Caption(uint landcell)
        {
            var name = Lookup(landcell);
            return name ?? string.Format(CultureInfo.InvariantCulture,
                                         "Unknown dungeon (0x{0:X4})", Magellan.World.Coords.Landblock(landcell));
        }
    }
}
