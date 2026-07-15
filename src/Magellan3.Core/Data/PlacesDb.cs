using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Magellan.World;

namespace Magellan.Data
{
    /// <summary>
    /// In-memory index over places.xml. Pure: no Decal, no game, fully unit-testable.
    ///
    /// Load once at startup (off the render thread), then query.
    /// </summary>
    public sealed class PlacesDb
    {
        private readonly List<Place> _places = new List<Place>();

        /// <summary>Every place, in file order.</summary>
        public IReadOnlyList<Place> All { get { return _places; } }

        /// <summary>Number of rows whose coordinates were repaired by <see cref="PlacesErrata"/> at load.</summary>
        public int CorrectionsApplied { get; private set; }

        /// <summary>Rows dropped because their coordinates were off-map and not in the errata table.</summary>
        public int RowsRejected { get; private set; }

        public static PlacesDb FromFile(string path)
        {
            using (var s = File.OpenRead(path)) return FromStream(s);
        }

        public static PlacesDb FromStream(Stream stream)
        {
            var db = new PlacesDb();
            var doc = XDocument.Load(stream);
            foreach (var el in doc.Descendants("PLACE"))
                db.Add(el);
            return db;
        }

        private void Add(XElement el)
        {
            int id;
            if (!TryInt(el.Element("ID"), out id)) return;

            int rawX, rawY;
            if (!TryInt(el.Element("COORD_X"), out rawX)) return;
            if (!TryInt(el.Element("COORD_Y"), out rawY)) return;

            // --- errata: six off-map COORD_Y values, all a stray trailing zero -------------
            int corrected;
            bool fixedUp = false;
            if (PlacesErrata.CorrectedCoordY.TryGetValue(id, out corrected) && rawY != corrected)
            {
                rawY = corrected;
                fixedUp = true;
                CorrectionsApplied++;
            }

            if (Math.Abs(rawX) > PlacesErrata.MaxTenths || Math.Abs(rawY) > PlacesErrata.MaxTenths)
            {
                RowsRejected++;   // still off-map after errata: refuse to index a lie
                return;
            }

            string name = (string)el.Element("NAME");           // <NAME>, not <n>
            if (string.IsNullOrWhiteSpace(name))
                PlacesErrata.MissingNames.TryGetValue(id, out name);

            var p = new Place
            {
                Id = id,
                Name = name,
                Type = ((string)el.Element("TYPE") ?? "").Trim(),
                NS = rawX / 10.0,                               // COORD_X = North * 10
                EW = rawY / 10.0,                               // COORD_Y = East  * 10
                LevelRestriction = Trim((string)el.Element("LEVELRESTRICTION")),
                // one row misspells the element as <EXITLOC>
                ExitLocation = Trim((string)el.Element("EXITLOCATION") ?? (string)el.Element("EXITLOC")),
                CoordsCorrected = fixedUp,
            };

            ushort dungeonId;
            if (TryHex16(el.Element("DUNGEONID"), out dungeonId)) p.DungeonId = dungeonId;

            uint landcell;
            if (TryHex32(el.Element("LOC"), out landcell)) p.Landcell = landcell;

            _places.Add(p);
        }

        // ------------------------------------------------------------------ queries

        /// <summary>
        /// Name-substring search, case-insensitive. This is what the original's "Search" tab did:
        /// "enter a fragment of the name you want to search for".
        /// </summary>
        public IEnumerable<Place> Search(string fragment, int limit = 200)
        {
            if (string.IsNullOrWhiteSpace(fragment)) return Enumerable.Empty<Place>();
            var f = fragment.Trim();
            return _places
                .Where(p => p.Name != null &&
                            p.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => p.Name.Length)          // exact-ish matches float up
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit);
        }

        /// <summary>
        /// Everything within <paramref name="radius"/> coordinate units of a point, nearest first.
        /// The original's default radius was 3 units (per the shipped manual).
        ///
        /// Callers MUST gate this on !Coords.IsIndoors(landcell) -- in a dungeon the player's
        /// surface position does not exist, which is exactly what the 2003 manual warns about.
        /// </summary>
        public IEnumerable<Place> Near(double ns, double ew, double radius = 3.0, int limit = 200)
        {
            return _places
                .Select(p => new { p, d = Coords.Distance(ns, ew, p.NS, p.EW) })
                .Where(x => x.d <= radius)
                .OrderBy(x => x.d)
                .Take(limit)
                .Select(x => x.p);
        }

        public Place ById(int id) { return _places.FirstOrDefault(p => p.Id == id); }

        // ------------------------------------------------------------------ parsing helpers

        private static string Trim(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }

        private static bool TryInt(XElement el, out int v)
        {
            v = 0;
            if (el == null) return false;
            return int.TryParse(el.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        private static bool TryHex16(XElement el, out ushort v)
        {
            v = 0;
            if (el == null) return false;
            uint u;
            if (!TryHex32(el, out u) || u > ushort.MaxValue) return false;
            v = (ushort)u;
            return true;
        }

        /// <summary>Accepts "0x01E5", "01E5" and "e53d003e".</summary>
        private static bool TryHex32(XElement el, out uint v)
        {
            v = 0;
            if (el == null) return false;
            var s = el.Value.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        }
    }
}
