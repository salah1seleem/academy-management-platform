using Academy.Api.Importing;

namespace Academy.UnitTests;

public sealed class FriendlyImportSchemaTests
{
    private static readonly Guid Academy = Guid.Parse("ec52f461-0a24-4343-94c0-184720300001");
    private static ImportWorkbook Change(ImportWorkbook book, string sheet, int row, string key, string value, bool text = true)
    {
        var column = Array.FindIndex(FriendlyImportSchema.Sheets[sheet], f => f.Key == key);
        return new(book.Sheets.Select(s => s.Name != sheet ? s : s with { Rows = s.Rows.Select((r, i) => i != row ? r : r.Select((cell, c) => c == column ? new ImportCell(value, text) : cell).ToArray()).ToArray() }).ToArray());
    }
    [Fact]
    public void Friendly_template_keeps_simple_identities_and_bounded_dropdowns_and_rehearsal_has_300_distinct_players()
    {
        var book = FriendlyImportRehearsal.Create(Academy, new(2026, 10, 25)); var bytes = ImportXlsx.Write(book); var loaded = ImportXlsx.Read(bytes); var parsed = FriendlyImportInput.Parse(loaded);
        Assert.Empty(parsed.Errors); Assert.Equal(300, parsed.Players.Count); Assert.Equal(305, parsed.Enrollments.Count); Assert.Equal(300, parsed.Guardians.Count); Assert.Equal(301, parsed.Rows["علاقات أولياء الأمور"].Count);
        Assert.Equal(6, FriendlyImportSchema.Sheets["اللاعبون"].Length); Assert.Equal("ابدأ هنا", loaded.Sheets[0].Name); Assert.Equal(bytes, ImportXlsx.Write(book)); Assert.True(bytes.Length < ImportXlsx.MaxFileBytes);
        Assert.True(parsed.Rows["حضور اختياري"].Count > 6000); Assert.True(parsed.Rows["حضور اختياري"].Count <= 20000);
        Assert.Equal(7, parsed.Rows["تحصيلات سابقة اختيارية"].Select(r => r["Date"][..7]).Distinct().Count());
    }
    [Theory]
    [InlineData("اللاعبون", 1, "Code", "", true)]
    [InlineData("اللاعبون", 2, "Code", "PL0001", true)]
    [InlineData("اللاعبون", 1, "Guardian", "UNKNOWN", true)]
    [InlineData("اللاعبون", 1, "Birth", "2027-02-30", true)]
    [InlineData("أولياء الأمور", 1, "Phone", "not-a-phone", true)]
    [InlineData("أولياء الأمور", 1, "Phone", "01500000001", false)]
    [InlineData("علاقات أولياء الأمور", 1, "Player", "OTHER-TENANT", true)]
    public void Invalid_identity_or_reference_reports_actual_arabic_sheet_row_and_column(string sheet, int row, string key, string value, bool text)
    {
        var book = Change(FriendlyImportRehearsal.Create(Academy, new(2026, 10, 25), 10), sheet, row, key, value, text); var parsed = FriendlyImportInput.Parse(book);
        Assert.NotEmpty(parsed.Errors); Assert.Contains(parsed.Errors, e => e.Sheet == sheet && e.Row == row + 1 && !string.IsNullOrEmpty(e.Column));
    }
    [Theory]
    [InlineData("٠١٥٠٠٠٠٠٠٠١")]
    [InlineData("+201500000001")]
    public void Phone_equivalents_stay_text_and_normalize_before_legacy_validation(string value)
    {
        var parsed = FriendlyImportInput.Parse(Change(FriendlyImportRehearsal.Create(Academy, new(2026, 10, 25), 10), "أولياء الأمور", 1, "Phone", value));
        Assert.Empty(parsed.Errors); Assert.Equal("+201500000001", parsed.Guardians["GU0001"]["Phone"]);
    }
    [Fact]
    public void Swimming_rehearsal_has_one_actual_sport_branch_and_water_criteria_with_explicit_existing_guardian_link()
    {
        var book=FriendlyImportRehearsal.Swimming(Academy,new(2026,10,25),"مالك خيالي قائم","01084720300");
        var parsed=FriendlyImportInput.Parse(ImportXlsx.Read(ImportXlsx.Write(book)));Assert.Empty(parsed.Errors);Assert.Equal(10,parsed.Players.Count);
        Assert.Single(parsed.ByCode("الرياضات"));Assert.Equal("سباحة",parsed.ByCode("الرياضات")["SP01"]["Kind"]);Assert.Single(parsed.ByCode("الفروع"));
        Assert.Equal(4,parsed.ByCode("معايير تقييم اختيارية").Count);Assert.All(parsed.Rows["معايير تقييم اختيارية"],r=>Assert.Equal("",r["Axis"]));Assert.All(parsed.Rows["درجات اختيارية"],r=>Assert.Contains(r["Criterion"],parsed.ByCode("معايير تقييم اختيارية").Keys));
        Assert.Equal("مالك خيالي قائم",parsed.Guardians["GU0001"]["Name"]);Assert.Equal("+201084720300",parsed.Guardians["GU0001"]["Phone"]);Assert.Equal("نعم",parsed.Guardians["GU0001"]["Associate"]);
        Assert.DoesNotContain(book.Sheets.SelectMany(s=>s.Rows).SelectMany(r=>r),c=>c.Value.Contains("كرة القدم"));
    }
}
