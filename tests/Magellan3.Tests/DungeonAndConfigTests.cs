using System.IO;
using System.Linq;
using Magellan.Config;
using Magellan.Data;

namespace Magellan.Tests
{
    public static class DungeonAndConfigTests
    {
        public static void Register()
        {
            // ---------------------------------------------------------------- dungeon names
            T.Suite("DungeonNames");

            var dn = DungeonNames.FromFile(T.Data("dungeon_names.tsv"));

            T.Test("653 dungeon names load", () =>
            {
                T.Eq(653, dn.Count, "entry count");
            });

            T.Test("lookup resolves a landcell to its dungeon name via the landblock", () =>
            {
                DungeonNames.Entry e;
                T.True(dn.TryGet(0x0003, out e), "0x0003 present");
                T.Eq("Niffis Fighting Pits", e.Name, "name");
                // And the full-landcell path (landblock 0x0003, some interior cell):
                T.Eq("Niffis Fighting Pits", dn.Lookup(0x00030137), "lookup by landcell");
            });

            T.Test("strict superset of Magellan's 118 DUNGEONIDs -- 0 missing", () =>
            {
                var places = PlacesDb.FromFile(T.Data("places.xml"));
                var magellanIds = places.All.Where(p => p.DungeonId.HasValue)
                                            .Select(p => p.DungeonId.Value).Distinct();
                foreach (var id in magellanIds)
                {
                    DungeonNames.Entry e;
                    T.True(dn.TryGet(id, out e), "landblock 0x" + id.ToString("X4") + " must be known");
                }
            });

            T.Test("unknown landblock falls back to a hex caption, never null", () =>
            {
                // 0xFFF0 is not a real dungeon -- on a custom server this is the common case,
                // and the whole reason this plugin generates maps rather than downloading them.
                var cap = dn.Caption(0xFFF00137);
                T.True(cap.Contains("FFF0"), "caption names the raw landblock: " + cap);
            });

            // ---------------------------------------------------------------- settings
            T.Suite("Settings");

            T.Test("defaults match the original's shipped config", () =>
            {
                var s = new Settings();
                T.True(s.ShowMap, "ShowMap default on");
                T.True(s.ShowFootsteps, "ShowFootsteps default on");
                T.False(s.LockRotation, "LockRotation default off");
                T.False(s.RelCoords, "RelCoords default off");
            });

            T.Test("round-trips through the 2003 config.xml format", () =>
            {
                var path = Path.Combine(Path.GetTempPath(), "mag_cfg_" + System.Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    var a = new Settings { ShowMap = false, LockRotation = true, RelCoords = true, MapScale = 3.5f };
                    a.Save(path);

                    var b = Settings.Load(path);
                    T.Eq(a.ShowMap, b.ShowMap, "ShowMap");
                    T.Eq(a.LockRotation, b.LockRotation, "LockRotation");
                    T.Eq(a.RelCoords, b.RelCoords, "RelCoords");
                    T.Near(a.MapScale, b.MapScale, 1e-4, "MapScale");
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            });

            T.Test("reads a verbatim 2003 config.xml", () =>
            {
                var path = Path.Combine(Path.GetTempPath(), "mag_cfg_" + System.Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(path,
                        "<?xml version=\"1.0\" ?>\r\n<MAGELLAN2 version=\"1.0.0.0\">\r\n  <CONFIG>\r\n" +
                        "    <VALUE name=\"ShowFootsteps\">1</VALUE>\r\n" +
                        "    <VALUE name=\"LockRotation\">1</VALUE>\r\n" +
                        "    <VALUE name=\"RelCoords\">0</VALUE>\r\n" +
                        "    <VALUE name=\"ShowMap\">1</VALUE>\r\n  </CONFIG>\r\n</MAGELLAN2>\r\n");

                    var s = Settings.Load(path);
                    T.True(s.ShowFootsteps && s.LockRotation && s.ShowMap && !s.RelCoords, "matches the 2.2.0.0 config");
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            });

            T.Test("unknown VALUE elements survive a load/save cycle", () =>
            {
                var path = Path.Combine(Path.GetTempPath(), "mag_cfg_" + System.Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(path,
                        "<MAGELLAN2 version=\"1.0.0.0\"><CONFIG>" +
                        "<VALUE name=\"ShowMap\">1</VALUE>" +
                        "<VALUE name=\"FutureThing\">42</VALUE>" +
                        "</CONFIG></MAGELLAN2>");

                    Settings.Load(path).Save(path);
                    T.True(File.ReadAllText(path).Contains("FutureThing"), "a setting we don't understand is preserved, not dropped");
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            });

            T.Test("a corrupt config falls back to defaults rather than throwing", () =>
            {
                var path = Path.Combine(Path.GetTempPath(), "mag_cfg_" + System.Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    File.WriteAllText(path, "this is not xml <<<");
                    var s = Settings.Load(path);
                    T.True(s.ShowMap, "defaulted safely");
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            });
        }
    }
}
