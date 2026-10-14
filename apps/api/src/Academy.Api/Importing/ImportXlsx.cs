using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Academy.Api.Importing;

// Bounded SpreadsheetML transport, not an Excel calculation engine. No formulas execute.
public static class ImportXlsx
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    private const int MaxExpandedBytes = 16 * 1024 * 1024;
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace T = "http://schemas.openxmlformats.org/package/2006/content-types";
    public static byte[] Write(ImportWorkbook book)
    {
        // Reuse repeated labels/IDs instead of duplicating inline strings in
        // every reference cell. Keep every existing reader/security size limit.
        var strings = book.Sheets.SelectMany(s => s.Rows).SelectMany(r => r).Where(c => c.IsText).Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
        var stringIds = strings.Select((value, index) => (value, index)).ToDictionary(x => x.value, x => x.index, StringComparer.Ordinal);
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            void Xml(string path, XDocument doc) { var entry = zip.CreateEntry(path); entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); using var stream = entry.Open(); doc.Save(stream, SaveOptions.DisableFormatting); }
            Xml("[Content_Types].xml", new(new XElement(T + "Types", new XElement(T + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(T + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(T + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(T + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")),
                new XElement(T + "Override", new XAttribute("PartName", "/xl/sharedStrings.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml")),
                book.Sheets.Select((_, i) => new XElement(T + "Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i + 1}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))))));
            XElement Rel(string id, string type, string target) => new(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + type), new XAttribute("Target", target));
            Xml("_rels/.rels", new(new XElement(P + "Relationships", Rel("rId1", "officeDocument", "xl/workbook.xml"))));
            Xml("xl/workbook.xml", new(new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets", book.Sheets.Select((s, i) => new XElement(S + "sheet", new XAttribute("name", s.Name), new XAttribute("sheetId", i + 1), new XAttribute(R + "id", $"rId{i + 1}")))))));
            Xml("xl/_rels/workbook.xml.rels", new(new XElement(P + "Relationships", book.Sheets.Select((_, i) => Rel($"rId{i + 1}", "worksheet", $"worksheets/sheet{i + 1}.xml")), Rel("styles", "styles", "styles.xml"), Rel("strings", "sharedStrings", "sharedStrings.xml"))));
            Xml("xl/sharedStrings.xml", new(new XElement(S + "sst", new XAttribute("uniqueCount", strings.Length), strings.Select(value => new XElement(S + "si", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value))))));
            Xml("xl/styles.xml", new(new XElement(S + "styleSheet",
                new XElement(S + "fonts", new XAttribute("count", 2), new XElement(S + "font", new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "name", new XAttribute("val", "Arial"))), new XElement(S + "font", new XElement(S + "b"), new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "color", new XAttribute("rgb", "FFFFFFFF")), new XElement(S + "name", new XAttribute("val", "Arial")))),
                new XElement(S + "fills", new XAttribute("count", 3), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "none"))), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "gray125"))), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "solid"), new XElement(S + "fgColor", new XAttribute("rgb", "FF23395D")), new XElement(S + "bgColor", new XAttribute("indexed", 64))))),
                new XElement(S + "borders", new XAttribute("count", 1), new XElement(S + "border")),
                new XElement(S + "cellStyleXfs", new XAttribute("count", 1), new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
                new XElement(S + "cellXfs", new XAttribute("count", 3),
                    Style(0, 0, 49), Style(1, 2, 49), Style(0, 0, 2)),
                new XElement(S + "cellStyles", new XAttribute("count", 1), new XElement(S + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))))));
            for (var i = 0; i < book.Sheets.Count; i++)
            {
                var sheet = book.Sheets[i]; var columns = sheet.Rows.Count == 0 ? 1 : sheet.Rows.Max(r => r.Count);
                Xml($"xl/worksheets/sheet{i + 1}.xml", new(new XElement(S + "worksheet",
                    new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("rightToLeft", 1), new XAttribute("showGridLines", 0), new XElement(S + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                    new XElement(S + "cols", new XElement(S + "col", new XAttribute("min", 1), new XAttribute("max", columns), new XAttribute("width", sheet.Name == "Instructions" ? 60 : 25), new XAttribute("customWidth", 1), new XAttribute("style", 0))),
                    new XElement(S + "sheetData", sheet.Rows.Select((row, ri) => new XElement(S + "row", new XAttribute("r", ri + 1), new XAttribute("ht", ri == 0 ? 42 : Math.Max(28, Math.Ceiling(row.Max(c => c.Value.Length) / (sheet.Name == "Instructions" ? 45d : 22d)) * 18)), new XAttribute("customHeight", 1), row.Select((cell, ci) => cell.IsText
                        ? new XElement(S + "c", new XAttribute("r", Column(ci) + (ri + 1)), new XAttribute("s", ri == 0 ? 1 : 0), new XAttribute("t", "s"), new XElement(S + "v", stringIds[cell.Value]))
                        : new XElement(S + "c", new XAttribute("r", Column(ci) + (ri + 1)), new XAttribute("s", 2), new XElement(S + "v", cell.Value)))))))));
            }
        }
        return bytes.ToArray();
    }
    private static XElement Style(int font, int fill, int number) => new(S + "xf", new XAttribute("fontId", font), new XAttribute("fillId", fill), new XAttribute("borderId", 0), new XAttribute("numFmtId", number), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XElement(S + "alignment", new XAttribute("vertical", "center"), new XAttribute("wrapText", 1)));
    private static string Column(int index) { var name = ""; do { name = (char)('A' + index % 26) + name; index = index / 26 - 1; } while (index >= 0); return name; }
    private static int CellColumn(string address, int rowNumber)
    {
        // A bounded character check avoids regex timeouts when large sheets are
        // parsed on a busy runner, and rejects suffixes/leading-zero row aliases.
        var letters = 0;
        while (letters < address.Length && letters < 2 && address[letters] is >= 'A' and <= 'Z') letters++;
        if (letters is < 1 or > 2 || address.Length != letters + rowNumber.ToString(CultureInfo.InvariantCulture).Length ||
            !address.AsSpan(letters).SequenceEqual(rowNumber.ToString(CultureInfo.InvariantCulture).AsSpan()))
            throw new ImportFileException("عنوان الخلية لا يطابق صفها.");
        var column = 0;
        for (var i = 0; i < letters; i++) column = column * 26 + address[i] - 'A' + 1;
        return column;
    }
    public static ImportWorkbook Read(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxFileBytes) throw new ImportFileException("حجم الملف غير مسموح. الحد 5 ميجابايت.");
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > 128 || zip.Entries.Sum(e => e.Length) > MaxExpandedBytes || zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
                throw new ImportFileException("الملف المضغوط يتجاوز حدود الفحص أو يحتوي مسارات مكررة.");
            var xml = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var path = entry.FullName;
                if (path.Contains("..") || path.Contains('\\') || path.StartsWith('/') || path.Contains("vba", StringComparison.OrdinalIgnoreCase) || path.Contains("externalLinks", StringComparison.OrdinalIgnoreCase) || path.Contains("embedding", StringComparison.OrdinalIgnoreCase) || path.Contains("activeX", StringComparison.OrdinalIgnoreCase) || path.Contains("connections", StringComparison.OrdinalIgnoreCase)) throw new ImportFileException("الماكرو والمراجع الخارجية والمحتوى المضمّن غير مسموحة.");
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) { if (path.EndsWith('/')) continue; throw new ImportFileException("يحتوي الملف مكونات غير مدعومة. استخدم القالب دون مرفقات."); }
                if (entry.Length > 4 * 1024 * 1024) throw new ImportFileException("إحدى أوراق الملف أكبر من الحد المسموح.");
                using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
                var doc = XDocument.Load(reader);
                if (doc.Descendants().Any(e => e.Name.LocalName is "f" or "definedName" or "externalReference" || e.Attributes().Any(a => a.Name.LocalName == "TargetMode" && a.Value == "External")) || doc.Descendants().Attributes("ContentType").Any(a => a.Value.Contains("macro", StringComparison.OrdinalIgnoreCase))) throw new ImportFileException("الصيغ والماكرو والروابط الخارجية غير مسموحة في ملف الاستيراد.");
                xml.Add(path, doc);
            }
            XDocument Part(string path) => xml.GetValueOrDefault(path) ?? throw new ImportFileException("بنية Excel غير مكتملة.");
            var shared = xml.GetValueOrDefault("xl/sharedStrings.xml")?.Descendants(S + "si").Select(si => string.Concat(si.Descendants(S + "t").Select(t => t.Value))).ToArray() ?? [];
            var rels = Part("xl/_rels/workbook.xml.rels").Descendants(P + "Relationship").ToDictionary(e => (string)e.Attribute("Id")!, e => (string)e.Attribute("Target")!);
            var sheets = new List<ImportSheet>();
            foreach (var s in Part("xl/workbook.xml").Descendants(S + "sheet"))
            {
                if (sheets.Count >= 5) throw new ImportFileException("استخدم أوراق القالب الخمس فقط.");
                var name = (string)s.Attribute("name")!; var target = rels.GetValueOrDefault((string)s.Attribute(R + "id")!) ?? throw new ImportFileException("مرجع الورقة غير صحيح.");
                var path = target.StartsWith("/xl/") ? target[1..] : "xl/" + target;
                if (!path.StartsWith("xl/worksheets/", StringComparison.Ordinal) || path.Contains("..")) throw new ImportFileException("مرجع الورقة غير مسموح.");
                var rows = new List<IReadOnlyList<ImportCell>>();
                foreach (var row in Part(path).Descendants(S + "row"))
                {
                    if (!int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var rowNumber) || rowNumber <= rows.Count || rowNumber > 10001) throw new ImportFileException("ترتيب الصفوف أو عددها غير مسموح.");
                    while (rows.Count < rowNumber - 1) rows.Add([]);
                    var cells = new List<ImportCell>(); var occupied = new HashSet<int>();
                    foreach (var c in row.Elements(S + "c"))
                    {
                        var address = (string?)c.Attribute("r") ?? throw new ImportFileException("عنوان خلية مفقود.");
                        var col = CellColumn(address, rowNumber);
                        if (col is < 1 or > 32 || !occupied.Add(col)) throw new ImportFileException("عنوان خلية غير مسموح أو مكرر.");
                        while (cells.Count < col) cells.Add(new("", true));
                        var type = (string?)c.Attribute("t"); var value = type == "inlineStr" ? string.Concat(c.Descendants(S + "t").Select(t => t.Value)) : c.Element(S + "v")?.Value ?? "";
                        if (type == "s") { if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= shared.Length) throw new ImportFileException("مرجع نص غير صحيح."); value = shared[index]; }
                        if (type is not (null or "n" or "s" or "inlineStr" or "str")) throw new ImportFileException("نوع خلية غير مدعوم.");
                        if (value.Length > 2000) throw new ImportFileException("نص الخلية أطول من المسموح.");
                        cells[col - 1] = new(value.Trim(), type is "s" or "inlineStr" or "str");
                    }
                    rows.Add(cells);
                }
                sheets.Add(new(name, rows));
            }
            if (!sheets.Select(s => s.Name).Order().SequenceEqual(ImportSchema.Sheets.Order())) throw new ImportFileException("أسماء أوراق الملف لا تطابق القالب الحالي.");
            return new(sheets);
        }
        catch (Exception e) when (e is InvalidDataException or XmlException or ArgumentException or OverflowException)
        { throw new ImportFileException("الملف غير صالح أو ليس قالب Excel مدعومًا."); }
    }
}
