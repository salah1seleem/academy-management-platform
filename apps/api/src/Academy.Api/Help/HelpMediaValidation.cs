using System.Buffers.Binary;
using System.Text;

namespace Academy.Api.Help;

// Bounded container validation, not an antivirus or a transcoder. Accept only
// regular H.264 MP4 (not fragmented files) and PNG thumbnails for predictable clients.
public static class HelpMediaValidation
{
    private record struct Box(string Type, int Start, int End);
    private static List<Box> Boxes(ReadOnlySpan<byte> bytes, int start, int end)
    {
        var result = new List<Box>();
        while (start < end)
        {
            if (end - start < 8 || result.Count >= 4096) throw new InvalidDataException();
            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes[start..]); var header = 8;
            ulong length = size;
            if (size == 1) { if (end - start < 16) throw new InvalidDataException(); length = BinaryPrimitives.ReadUInt64BigEndian(bytes[(start + 8)..]); header = 16; }
            if (size == 0 || length < (ulong)header || length > (ulong)(end - start)) throw new InvalidDataException();
            result.Add(new(Encoding.ASCII.GetString(bytes.Slice(start + 4, 4)), start + header, start + (int)length)); start += (int)length;
        }
        return result;
    }
    public static int? Mp4Duration(byte[] bytes)
    {
        try
        {
            var top = Boxes(bytes, 0, bytes.Length);
            if (top.Count == 0 || top[0].Type != "ftyp" || top[0].End - top[0].Start < 8 || top.Any(x => x.Type is "moof" or "sidx") || !top.Any(x => x.Type == "mdat" && x.End > x.Start)) return null;
            var moov = top.Single(x => x.Type == "moov"); var children = Boxes(bytes, moov.Start, moov.End); var mvhd = children.Single(x => x.Type == "mvhd");
            var b = bytes.AsSpan(mvhd.Start, mvhd.End - mvhd.Start); if (b.Length < 20) return null;
            uint scale; ulong duration;
            if (b[0] == 0) { scale = BinaryPrimitives.ReadUInt32BigEndian(b[12..]); duration = BinaryPrimitives.ReadUInt32BigEndian(b[16..]); }
            else if (b[0] == 1 && b.Length >= 32) { scale = BinaryPrimitives.ReadUInt32BigEndian(b[20..]); duration = BinaryPrimitives.ReadUInt64BigEndian(b[24..]); }
            else return null;
            if (scale == 0 || duration == 0 || duration / (double)scale > 1800) return null;
            var hasVideo = false;
            foreach (var track in children.Where(x => x.Type == "trak"))
            {
                var mdia = Boxes(bytes, track.Start, track.End).Single(x => x.Type == "mdia"); var media = Boxes(bytes, mdia.Start, mdia.End);
                var handler = media.Single(x => x.Type == "hdlr"); if (handler.End - handler.Start < 12) return null;
                if (Encoding.ASCII.GetString(bytes, handler.Start + 8, 4) != "vide") continue;
                var minf = media.Single(x => x.Type == "minf"); var stbl = Boxes(bytes, minf.Start, minf.End).Single(x => x.Type == "stbl");
                var stsd = Boxes(bytes, stbl.Start, stbl.End).Single(x => x.Type == "stsd"); if (stsd.End - stsd.Start < 8) return null;
                var entries = Boxes(bytes, stsd.Start + 8, stsd.End);
                if (entries.Count != 1 || entries[0].Type != "avc1" || entries[0].End - entries[0].Start < 78) return null;
                if (!Boxes(bytes, entries[0].Start + 78, entries[0].End).Any(x => x.Type == "avcC" && x.End - x.Start >= 7)) return null;
                hasVideo = true;
            }
            return hasVideo ? (int)Math.Ceiling(duration / (double)scale) : null;
        }
        catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException) { return null; }
    }
    public static bool Png(byte[] b)
    {
        if (b.Length < 45 || !b.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return false;
        int at = 8, chunks = 0; bool data = false;
        while (at <= b.Length - 12 && chunks++ < 4096)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)); if (size > (uint)(b.Length - at - 12)) return false;
            var n = (int)size; var type = Encoding.ASCII.GetString(b, at + 4, 4);
            uint crc = uint.MaxValue; foreach (var value in b.AsSpan(at + 4, n + 4)) { crc ^= value; for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320U : crc >> 1; }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at + 8 + n))) return false;
            if (chunks == 1)
            {
                if (type != "IHDR" || n != 13) return false;
                var width = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at + 8)); var height = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at + 12));
                if (width is 0 or > 1920 || height is 0 or > 1920) return false;
            }
            if (type == "IDAT" && n > 0) data = true;
            at += n + 12;
            if (type == "IEND") return n == 0 && data && at == b.Length;
        }
        return false;
    }
}
