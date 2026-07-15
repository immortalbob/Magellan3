using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Magellan.Config
{
    /// <summary>
    /// The four options Magellan 2 persisted, in its own config.xml format.
    ///
    ///   <MAGELLAN2 version="1.0.0.0">
    ///     <CONFIG>
    ///       <VALUE name="ShowFootsteps">1</VALUE>
    ///       <VALUE name="LockRotation">1</VALUE>
    ///       <VALUE name="RelCoords">0</VALUE>
    ///       <VALUE name="ShowMap">1</VALUE>
    ///     </CONFIG>
    ///   </MAGELLAN2>
    ///
    /// These map 1:1 onto the four checkboxes in the recovered view XML
    /// (chkShowMap, chkFootsteps, chkLockRotation, chkRelCoords), which is a nice confirmation
    /// that the recovered config schema is the real one -- config.xml is NOT in the MSI payload;
    /// the plugin wrote it at first run.
    ///
    /// The format is kept verbatim so a 2003 config.xml still loads. New settings (slab height,
    /// map scale) go in as additional VALUE elements; unknown names are preserved on save.
    /// </summary>
    public sealed class Settings
    {
        public bool ShowMap = true;
        public bool ShowFootsteps = true;
        public bool LockRotation = false;

        /// <summary>
        /// The original's "Display Relative locations". What exactly it displayed is the one thing
        /// not recoverable from the artifacts. Here: search results show their offset from you
        /// ("3.2N 1.1W of you") instead of absolute coordinates.
        /// </summary>
        public bool RelCoords = false;

        // --- new, not in the 2003 schema; written as extra VALUE elements ---
        public float MapScale = 2.0f;        // pixels per metre
        public bool SliceByFloor = true;
        public float SliceHeight = 4.0f;

        private readonly Dictionary<string, string> _unknown = new Dictionary<string, string>();

        public static Settings Load(string path)
        {
            var s = new Settings();
            if (!File.Exists(path)) return s;
            try
            {
                var doc = XDocument.Load(path);
                foreach (var v in doc.Descendants("VALUE"))
                {
                    var name = (string)v.Attribute("name");
                    if (string.IsNullOrEmpty(name)) continue;
                    var val = v.Value.Trim();

                    switch (name)
                    {
                        case "ShowMap": s.ShowMap = Flag(val); break;
                        case "ShowFootsteps": s.ShowFootsteps = Flag(val); break;
                        case "LockRotation": s.LockRotation = Flag(val); break;
                        case "RelCoords": s.RelCoords = Flag(val); break;
                        case "MapScale": s.MapScale = Num(val, s.MapScale); break;
                        case "SliceByFloor": s.SliceByFloor = Flag(val); break;
                        case "SliceHeight": s.SliceHeight = Num(val, s.SliceHeight); break;
                        default: s._unknown[name] = val; break;   // forward-compatible: never drop a stranger's setting
                    }
                }
            }
            catch (Exception)
            {
                return new Settings();   // a corrupt config must not take the client with it
            }
            return s;
        }

        public void Save(string path)
        {
            var cfg = new XElement("CONFIG",
                Val("ShowFootsteps", ShowFootsteps),
                Val("LockRotation", LockRotation),
                Val("RelCoords", RelCoords),
                Val("ShowMap", ShowMap),
                Val("MapScale", MapScale.ToString(CultureInfo.InvariantCulture)),
                Val("SliceByFloor", SliceByFloor),
                Val("SliceHeight", SliceHeight.ToString(CultureInfo.InvariantCulture)));

            foreach (var kv in _unknown)
                cfg.Add(new XElement("VALUE", new XAttribute("name", kv.Key), kv.Value));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            new XDocument(new XElement("MAGELLAN2", new XAttribute("version", "1.0.0.0"), cfg)).Save(path);
        }

        private static XElement Val(string name, bool b) { return new XElement("VALUE", new XAttribute("name", name), b ? "1" : "0"); }
        private static XElement Val(string name, string v) { return new XElement("VALUE", new XAttribute("name", name), v); }
        private static bool Flag(string s) { return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase); }

        private static float Num(string s, float fallback)
        {
            float f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : fallback;
        }
    }
}
