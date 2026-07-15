using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Magellan.Routing
{
    /// <summary>
    /// Loads the portal network from places_2.0.0.2.xml -- the ONLY Magellan data file that carries
    /// the destination coordinates (DEST_COORD_X/Y) needed for routing. The 2.2.0.0 release removed
    /// both the Route feature and these fields, so routing is built from the older 2.0.0.2 data.
    ///
    /// Axes match the rest of the codebase:
    ///     COORD_X / DEST_COORD_X = North * 10   (negative = South)
    ///     COORD_Y / DEST_COORD_Y = East  * 10   (negative = West)
    ///
    /// Only PLACEs of TYPE "Portal" that have BOTH a source (COORD_X/Y) and a destination
    /// (DEST_COORD_X/Y) become graph edges. In the 2.0.0.2 data that's 149 portals.
    /// </summary>
    public sealed class PortalDb
    {
        public readonly List<Portal> Portals = new List<Portal>();
        public int Skipped;   // portal-typed rows missing a usable dest, for diagnostics

        public static PortalDb LoadFromFile(string path)
        {
            using (var s = File.OpenRead(path)) return Load(s);
        }

        public static PortalDb Load(Stream stream)
        {
            var db = new PortalDb();
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root == null) return db;
            foreach (var el in root.Elements("PLACE")) db.Add(el);
            return db;
        }

        private void Add(XElement el)
        {
            var type = ((string)el.Element("TYPE") ?? "").Trim();
            // Only real portals with a destination are routable edges.
            int dstX, dstY;
            bool hasDest = TryInt(el.Element("DEST_COORD_X"), out dstX) & TryInt(el.Element("DEST_COORD_Y"), out dstY);
            if (!hasDest) return;   // not a portal edge (shop, dungeon, plain interest, etc.)

            int srcX, srcY;
            if (!TryInt(el.Element("COORD_X"), out srcX) || !TryInt(el.Element("COORD_Y"), out srcY))
            {
                Skipped++;
                return;
            }

            int id;
            TryInt(el.Element("ID"), out id);

            Portals.Add(new Portal
            {
                Id = id,
                Name = ((string)el.Element("NAME"))?.Trim(),
                SrcNS = srcX / 10.0,     // COORD_X = North
                SrcEW = srcY / 10.0,     // COORD_Y = East
                DstNS = dstX / 10.0,     // DEST_COORD_X = North
                DstEW = dstY / 10.0,     // DEST_COORD_Y = East
            });
        }

        private static bool TryInt(XElement e, out int v)
        {
            v = 0;
            if (e == null) return false;
            return int.TryParse((e.Value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }
    }
}
