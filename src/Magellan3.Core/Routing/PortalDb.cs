using System.Collections.Generic;
using Magellan.Data;
using Magellan.World;

namespace Magellan.Routing
{
    /// <summary>
    /// Builds the portal network for routing from the main places database (2.2.0.0 format), using
    /// each portal's EXITLOCATION -- a signed, display-style destination ("38.6S, 82.1E").
    ///
    /// HISTORY / WHY NOT places_2.0.0.2.xml: routing originally loaded the older 2.0.0.2 data file
    /// because it carried numeric DEST_COORD_X/Y fields. Forensic comparison against the original
    /// 2003 MSIs proved that data unusable in two independent ways:
    ///   1. The DEST fields are axis-TRANSPOSED relative to that same file's COORD fields
    ///      (DEST_COORD_X is East*10, DEST_COORD_Y is North*10 -- the opposite of COORD_X/Y), and
    ///      the two data-file generations also transpose COORD_X/Y relative to EACH OTHER
    ///      (1,706 of 1,713 name-matched places swap between versions; anchored absolutely by
    ///      converting LOC landcells through Coords.FromPosition).
    ///   2. DEST_COORD_Y stores |North| with the hemisphere STRIPPED -- of 133 portals present in
    ///      both files, 132 match "X = EW signed, Y = |NS|" and exactly one Y in the whole file is
    ///      negative. Southern destinations are unrecoverable from that file.
    /// The EXITLOCATION strings in the current places.xml are signed, carry explicit hemisphere
    /// letters, and cover 203 portals versus the old file's 149. One data file, one parse path.
    ///
    /// Sources come from PlacesDb's already-parsed (and errata-corrected) coordinates, so a portal's
    /// position here is byte-for-byte the same value the Search/Nearby tabs display.
    /// </summary>
    public sealed class PortalDb
    {
        public readonly List<Portal> Portals = new List<Portal>();
        public int Skipped;   // entries with an EXITLOCATION that didn't parse, for diagnostics

        /// <summary>
        /// Build graph edges from every place that has a parseable EXITLOCATION.
        /// "Random Portal" entries are EXCLUDED by default: their in-game destination is
        /// nondeterministic, so the single recorded exit would produce routes a player can't
        /// reliably follow. Pass includeRandomPortals=true to add them anyway (the original 2003
        /// graph mixed 40 of them in). Non-"Portal" types with an exit (one Dungeon, one Interest
        /// in the shipped data) are included -- both are portals mistyped by the original curator.
        /// </summary>
        public static PortalDb FromPlaces(PlacesDb places, bool includeRandomPortals = false)
        {
            var db = new PortalDb();
            foreach (var p in places.All)
            {
                if (string.IsNullOrEmpty(p.ExitLocation)) continue;
                if (!includeRandomPortals && p.Type == "Random Portal") continue;

                double dstNS, dstEW;
                if (!Coords.TryParseDisplay(p.ExitLocation, out dstNS, out dstEW))
                {
                    db.Skipped++;
                    continue;
                }

                db.Portals.Add(new Portal
                {
                    Id = p.Id,
                    Name = p.Name,
                    SrcNS = p.NS,
                    SrcEW = p.EW,
                    DstNS = dstNS,
                    DstEW = dstEW,
                });
            }
            return db;
        }
    }
}
