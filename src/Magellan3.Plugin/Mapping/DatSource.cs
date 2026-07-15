using System;
using DatReaderWriter;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Options;
using DatReaderWriter.Types;

namespace Magellan.Plugin.Mapping
{
    /// <summary>
    /// Where dungeon geometry comes from. Two implementations:
    ///   - <see cref="DecalDatSource"/>  : in-game, through Decal's FileService (preferred)
    ///   - <see cref="FileDatSource"/>   : offline, straight off client_cell_1.dat / client_portal.dat
    ///
    /// Both hand back DatReaderWriter DBObjs. The geometry builder does not know or care which.
    /// </summary>
    public interface IDatSource
    {
        bool TryGetCell<T>(uint id, out T obj) where T : DBObj, new();
        bool TryGetPortal<T>(uint id, out T obj) where T : DBObj, new();
    }

    /// <summary>
    /// Preferred source: go through <c>CoreManager.Current.FileService</c> so we see the DAT exactly
    /// as the client sees it -- including cells the client has only just streamed in. Pair with
    /// <c>FileService.OnUpdateCell</c> to rebuild on the fly, which kills the original's
    /// "the map isn't there until you enter the dungeon a second time" quirk.
    ///
    /// This class takes two delegates so it has NO compile-time dependency on Decal -- the plugin
    /// wires them to FileService at startup, and the offline tests never touch it.
    /// </summary>
    public sealed class DecalDatSource : IDatSource
    {
        private readonly Func<uint, byte[]> _cell;
        private readonly Func<uint, byte[]> _portal;

        public DecalDatSource(Func<uint, byte[]> getCellFile, Func<uint, byte[]> getPortalFile)
        {
            _cell = getCellFile;
            _portal = getPortalFile;
        }

        /// <summary>
        /// THE PHASE QUESTION -- run this ONCE, in-game, on a known cell, before trusting any geometry.
        ///
        /// DatReaderWriter's DBObj.Unpack reads a leading id DWORD whenever the type's HeaderFlags has
        /// HasId -- and EnvCell, Environment and LandBlockInfo ALL have HasId (confirmed from the
        /// library source). If Decal's FileService returns the file WITH that id header, Unpack is
        /// happy as-is. If it strips the header, every field decodes four bytes out of phase and you
        /// get structurally-valid, totally-wrong geometry -- the worst failure mode there is.
        ///
        /// So we prove which it is, once, and set <see cref="FileServiceIncludesIdHeader"/> accordingly.
        /// A cell file's own id header equals the file id, so:
        /// </summary>
        public static bool HeaderPresent(byte[] fileBytes, uint requestedId)
        {
            return fileBytes != null
                && fileBytes.Length >= 4
                && BitConverter.ToUInt32(fileBytes, 0) == requestedId;
        }

        /// <summary>
        /// True if FileService hands back the id DWORD header. Determined once via <see cref="HeaderPresent"/>
        /// against a known cell at startup; defaults to true (the common case for raw DAT file access).
        /// </summary>
        public static bool FileServiceIncludesIdHeader { get; set; } = true;

        public bool TryGetCell<T>(uint id, out T obj) where T : DBObj, new()
        {
            return TryUnpack(_cell(id), id, out obj);
        }

        public bool TryGetPortal<T>(uint id, out T obj) where T : DBObj, new()
        {
            return TryUnpack(_portal(id), id, out obj);
        }

        private static bool TryUnpack<T>(byte[] bytes, uint id, out T obj) where T : DBObj, new()
        {
            obj = null;
            if (bytes == null || bytes.Length == 0) return false;

            try
            {
                if (!FileServiceIncludesIdHeader)
                {
                    var withHdr = new byte[bytes.Length + 4];
                    BitConverter.GetBytes(id).CopyTo(withHdr, 0);
                    bytes.CopyTo(withHdr, 4);
                    bytes = withHdr;
                }

                var o = new T();
                o.Unpack(new DatBinReader(bytes));
                obj = o;
                return true;
            }
            catch
            {
                // Torn / absent / not-yet-streamed cell -- treat as "not there yet", never crash.
                return false;
            }
        }
    }

    /// <summary>
    /// Offline source: read the dats directly. For the out-of-game map baker and for cutting unit-test
    /// fixtures. In-game it can lag the client's own writes -- the exact 2003 quirk -- so prefer
    /// <see cref="DecalDatSource"/> when a game is attached.
    /// </summary>
    public sealed class FileDatSource : IDatSource, IDisposable
    {
        private readonly DatCollection _dats;

        public FileDatSource(string acDirectory)
        {
            _dats = new DatCollection(acDirectory, DatAccessType.Read);
        }

        public bool TryGetCell<T>(uint id, out T obj) where T : DBObj, new()
        {
            T v;
            bool ok = _dats.Cell.TryGet(id, out v);
            obj = v;
            return ok;
        }

        public bool TryGetPortal<T>(uint id, out T obj) where T : DBObj, new()
        {
            T v;
            bool ok = _dats.Portal.TryGet(id, out v);
            obj = v;
            return ok;
        }

        public void Dispose()
        {
            if (_dats != null) _dats.Dispose();
        }
    }
}
