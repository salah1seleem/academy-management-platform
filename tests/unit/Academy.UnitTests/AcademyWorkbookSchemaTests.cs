using Academy.Api.Importing;
using System.IO.Compression;
using System.Xml.Linq;

namespace Academy.UnitTests;
public sealed class AcademyWorkbookSchemaTests
{
    private static readonly Guid Academy=Guid.Parse("d3a6c13b-21dc-4f3a-92a6-015890000000");
    private static ImportWorkbook Sample()=>AcademyWorkbookSchema.BindSample(ImportXlsx.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Assets","academy-completed-sample.xlsx"))),Academy);
    private static ImportWorkbook Change(ImportWorkbook b,string sheet,int row,int col,string value)=>new(b.Sheets.Select(s=>s.Name!=sheet?s:s with{Rows=s.Rows.Select((r,i)=>i!=row?r:r.Select((c,j)=>j==col?new ImportCell(value,true):c).ToArray()).ToArray()}).ToArray());
    [Fact] public void Current_layout_has_50_short_codes_siblings_and_automatic_dates_and_balances()
    {
        var book=Sample();var input=FriendlyImportInput.Parse(book);Assert.Empty(input.Errors);Assert.Equal(50,input.Players.Count);Assert.Equal(25,input.Guardians.Count);Assert.Equal(50,input.Enrollments.Count);
        Assert.Equal(input.Players["PL01"]["Guardian"],input.Players["PL02"]["Guardian"]);Assert.Equal("2026-10-31",input.Enrollments["EN-PL01"]["End"]);Assert.Equal("12",input.Enrollments["EN-PL01"]["Entitled"]);Assert.Equal("11",input.Enrollments["EN-PL01"]["Remaining"]);Assert.Equal("12",input.Enrollments["EN-PL05"]["Remaining"]);
        Assert.Equal(50,input.Extras.Count);Assert.Equal(ImportXlsx.Write(book),ImportXlsx.Write(book));
        Assert.DoesNotContain(book.Sheets,s=>new[]{"أنماط التدريب","مستويات التدريب","العروض","الأرصدة الافتتاحية"}.Contains(s.Name));
    }
    [Fact] public void Guardian_phone_is_the_key_even_if_two_guardians_have_identical_names()
    {
        var book=Sample();book=Change(book,"أولياء الأمور",2,1,book.Sheet("أولياء الأمور").Rows[1][1].Value);var parsed=FriendlyImportInput.Parse(book);Assert.Empty(parsed.Errors);Assert.NotEqual(parsed.Players["PL01"]["Guardian"],parsed.Players["PL03"]["Guardian"]);
        var bad=Change(book,"اللاعبون",1,3,"01200009999");Assert.Throws<ImportFileException>(()=>FriendlyImportInput.Parse(bad));
        var duplicate=Change(book,"أولياء الأمور",2,2,book.Sheet("أولياء الأمور").Rows[1][2].Value);Assert.Throws<ImportFileException>(()=>FriendlyImportInput.Parse(duplicate));
        var photo=Change(book,"اللاعبون",1,6,"https://example.test/child.jpg");Assert.Throws<ImportFileException>(()=>FriendlyImportInput.Parse(photo));
    }
    [Fact] public void Xlsx_reader_accepts_native_excel_dates_without_executing_formulas()
    {
        var bytes=ImportXlsx.Write(Sample());using var stream=new MemoryStream();stream.Write(bytes);stream.Position=0;
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Update,true)) {
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var style=zip.GetEntry("xl/styles.xml")!;XDocument styles;using(var input=style.Open())styles=XDocument.Load(input);
            var xfs=styles.Root!.Element(ns+"cellXfs")!;xfs.Add(new XElement(ns+"xf",new XAttribute("numFmtId",14)));xfs.SetAttributeValue("count",6);style.Delete();using(var output=zip.CreateEntry("xl/styles.xml").Open())styles.Save(output);
            // Sheet 2 is the visible player sheet. E2 is birth date.
            var sheet=zip.GetEntry("xl/worksheets/sheet2.xml")!;XDocument doc;using(var input=sheet.Open())doc=XDocument.Load(input);var cell=doc.Descendants(ns+"c").Single(c=>(string?)c.Attribute("r")=="E2");cell.SetAttributeValue("t","n");cell.SetAttributeValue("s",5);cell.Element(ns+"v")!.Value=new DateTime(2009,3,2).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture);sheet.Delete();using(var output=zip.CreateEntry("xl/worksheets/sheet2.xml").Open())doc.Save(output);
        }
        Assert.Equal("2009-03-02",ImportXlsx.Read(stream.ToArray()).Sheet("اللاعبون").Rows[1][4].Value);
    }
}
