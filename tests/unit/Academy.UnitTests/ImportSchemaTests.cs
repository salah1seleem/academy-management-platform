using System.IO.Compression;
using System.Text;
using Academy.Api.Importing;
using Academy.Infrastructure.Subscriptions;

namespace Academy.UnitTests;

public sealed class ImportSchemaTests
{
    private static readonly Guid Academy = Guid.Parse("a2040000-0000-0000-0000-000000000001");
    private static readonly Guid Branch = Guid.Parse("a2040000-0000-0000-0000-000000000002");
    private static readonly Guid Sport = Guid.Parse("a2040000-0000-0000-0000-000000000003");
    private static readonly Guid Group = Guid.Parse("a2040000-0000-0000-0000-000000000004");
    private static readonly Guid Plan = Guid.Parse("a2040000-0000-0000-0000-000000000005");
    private static ImportReferences Refs(int capacity = 2000) => new(Academy, "EGP", [new("branch", Branch, "فرع اصطناعي"), new("sport", Sport, "سباحة"),
        new("group", Group, "مجموعة صناعية", Branch, Sport, MinYear: 2010, MaxYear: 2020, Capacity: capacity),
        new("product", Plan, "باقة صناعية", Sport: Sport, Revision: 3, PlanType: SubscriptionPlanType.Duration, AllBranches: true)], "stamp");
    private static IReadOnlyList<ImportCell> Cells(params string[] values) => values.Select(v => new ImportCell(v, true)).ToArray();
    private static ImportWorkbook Filled(ImportReferences? refs = null, int count = 1)
    {
        var template = (refs ?? Refs()).Template(new(2026, 9, 28));
        var rows = new List<IReadOnlyList<ImportCell>> { template.Sheet("Enrollments").Rows[0] };
        for (var i = 0; i < count; i++) rows.Add(Cells("P-" + i, "E-" + i, "", "لاعب صناعي " + i, "ولي اصطناعي " + i, "010" + i.ToString("00000000"), "2017-01-01", Branch.ToString(), Sport.ToString(), "", Group.ToString(), Plan.ToString(), "2026-09-01", "2026-09-30", "", "", "agreed_net", "", "", "540.00", "EGP", "existing", ""));
        return new(template.Sheets.Select(s => s.Name == "Enrollments" ? s with { Rows = rows } : s.Name == "Metadata" ? s with { Rows = s.Rows.Select(r => r[0].Value == "SourceSystem" ? Cells("SourceSystem", "legacy-test") : r).ToArray() } : s).ToArray());
    }
    private static ImportWorkbook Change(ImportWorkbook book, string column, string value, bool text = true, int row = 1)
    {
        var ci = Array.FindIndex(ImportSchema.Enrollments, c => c.Key == column);
        return new(book.Sheets.Select(s => s.Name != "Enrollments" ? s : s with { Rows = s.Rows.Select((r, i) => i != row ? r : r.Select((c, x) => x == ci ? new ImportCell(value, text) : c).ToArray()).ToArray() }).ToArray());
    }
    [Fact]
    public void Template_and_validator_share_headers_and_1000_historical_net_rows_are_preserved()
    {
        var source = Filled(count: 1000); var bytes = ImportXlsx.Write(source); var parsed = ImportXlsx.Read(bytes);
        var result = ImportPreview.Validate(parsed, Refs(), "hash"); Assert.True(result.CanApprove, string.Join(',', result.Errors.Select(e => e.Message))); Assert.Equal(1000, result.RowCount); Assert.Equal(1000, result.PlayerCount);
        Assert.All(result.Rows, r => { Assert.Equal("540.00", r.AgreedNet); Assert.Null(r.Discount); Assert.Equal(3, r.PriceRevision); Assert.StartsWith("010", r.GuardianPhone); });
        Assert.Equal(ImportSchema.Enrollments.Select(c => c.Key), parsed.Sheet("Enrollments").Rows[0].Select(c => c.Value));
    }
    [Theory]
    [InlineData("GuardianPhone", "1000000001", false)]
    [InlineData("DateOfBirth", "17/01/2017", true)]
    [InlineData("DateOfBirth", "2001-01-01", true)]
    [InlineData("EndDate", "2026-08-01", true)]
    [InlineData("BranchCode", "a2040000-0000-0000-0000-000000000999", true)]
    [InlineData("ProductCode", "a2040000-0000-0000-0000-000000000999", true)]
    [InlineData("RemainingSessions", "-1", true)]
    [InlineData("AgreedNet", "1.001", false)]
    [InlineData("LevelCode", "unknown", true)]
    public void Invalid_types_ownership_dates_or_unprepared_levels_are_rejected(string column, string value, bool text)
    {
        var result = ImportPreview.Validate(Change(Filled(), column, value, text), Refs(), "hash"); Assert.False(result.CanApprove); Assert.Contains(result.Errors, e => e.Column == column);
    }
    [Fact]
    public void Historical_base_minus_discount_must_reconcile_and_missing_money_is_not_zero()
    {
        var book = Change(Change(Change(Filled(), "PricingMode", "price_and_discount"), "BasePrice", "600.00"), "Discount", "60.00");
        Assert.True(ImportPreview.Validate(book, Refs(), "hash").CanApprove);
        Assert.False(ImportPreview.Validate(Change(book, "AgreedNet", "500.00"), Refs(), "hash").CanApprove);
        var missing = ImportPreview.Validate(Change(Change(Filled(), "PricingMode", ""), "AgreedNet", ""), Refs(), "hash"); Assert.True(missing.CanApprove); Assert.Equal(1, missing.FinanciallyIncomplete); Assert.Null(missing.Rows[0].AgreedNet);
    }
    [Fact]
    public void Same_child_in_two_sports_and_siblings_have_distinct_enrollment_keys_and_explicit_household()
    {
        var sport2 = Guid.NewGuid(); var group2 = Guid.NewGuid(); var plan2 = Guid.NewGuid();
        var refs = Refs() with { Values = Refs().Values.Concat(new ImportReference[] { new("sport", sport2, "كرة القدم"), new("group", group2, "مجموعة أخرى", Branch, sport2, MinYear: 2010, MaxYear: 2020, Capacity: 20), new("product", plan2, "باقة أخرى", Sport: sport2, Revision: 1, PlanType: SubscriptionPlanType.Duration, AllBranches: true) }).ToArray() };
        var book = Filled(refs, 3);
        foreach (var row in new[] { 1, 2, 3 }) { book = Change(book, "HouseholdRef", "H1", row: row); book = Change(book, "GuardianName", "ولي اصطناعي", row: row); book = Change(book, "GuardianPhone", "01000000001", row: row); }
        book = Change(Change(Change(Change(Change(book, "LegacyPlayerId", "P-0", row: 3), "PlayerName", "لاعب صناعي 0", row: 3), "SportCode", sport2.ToString(), row: 3), "GroupCode", group2.ToString(), row: 3), "ProductCode", plan2.ToString(), row: 3);
        var result = ImportPreview.Validate(book, refs, "hash"); Assert.True(result.CanApprove); Assert.Equal((3, 2, 1), (result.RowCount, result.PlayerCount, result.HouseholdCount));
    }
    [Fact]
    public void Signed_opening_balance_needs_cutoff_reference_and_approval_not_a_fake_payment()
    {
        ImportWorkbook WithBalance(string amount, string evidence) { var book = Filled(); return new(book.Sheets.Select(s => s.Name == "OpenBalances" ? s with { Rows = new[] { s.Rows[0], Cells("E-0", amount, "EGP", "2026-09-28", "OPEN-001", evidence) } } : s).ToArray()); }
        Assert.True(ImportPreview.Validate(WithBalance("500.00", "اعتماد اصطناعي"), Refs(), "hash").CanApprove);
        Assert.True(ImportPreview.Validate(WithBalance("-100.00", "اعتماد اصطناعي"), Refs(), "hash").CanApprove);
        Assert.False(ImportPreview.Validate(WithBalance("500.001", "اعتماد اصطناعي"), Refs(), "hash").CanApprove);
        Assert.False(ImportPreview.Validate(WithBalance("500.00", ""), Refs(), "hash").CanApprove);
    }
    [Fact]
    public void Capacity_stale_metadata_duplicate_ids_and_shared_phone_without_household_stop_preview()
    {
        Assert.False(ImportPreview.Validate(Filled(count: 2), Refs(1), "hash").CanApprove);
        Assert.False(ImportPreview.Validate(Filled(), Refs() with { Stamp = "changed" }, "hash").CanApprove);
        var duplicate = Change(Filled(count: 2), "LegacyEnrollmentId", "E-0", row: 2); Assert.Contains(ImportPreview.Validate(duplicate, Refs(), "hash").Errors, e => e.Column == "LegacyEnrollmentId");
        var siblings = Change(Filled(count: 2), "GuardianPhone", "01000000000", row: 2); Assert.Contains(ImportPreview.Validate(siblings, Refs(), "hash").Errors, e => e.Column == "HouseholdRef");
    }
    [Fact]
    public void Error_cap_never_makes_an_invalid_row_eligible_or_throws()
    {
        var book = Filled(count: 1000); for (var i = 1; i <= 1000; i++) book = Change(book, "DateOfBirth", "invalid", row: i);
        var result = ImportPreview.Validate(book, Refs(), "hash"); Assert.False(result.CanApprove); Assert.Equal(300, result.Errors.Count); Assert.Empty(result.Rows);
    }
    [Theory]
    [InlineData("formula")]
    [InlineData("macro")]
    [InlineData("external")]
    [InlineData("dtd")]
    [InlineData("duplicate")]
    [InlineData("expanded")]
    public void Unsafe_xlsx_components_are_rejected_before_data_use(string attack)
    {
        using var bytes = new MemoryStream(); bytes.Write(ImportXlsx.Write(Filled()));
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Update, true))
        {
            var path = attack switch { "macro" => "xl/vbaProject.bin", "external" => "xl/externalLinks/link.xml", "duplicate" => "xl/workbook.xml", _ => "xl/test.xml" };
            using var writer = new StreamWriter(zip.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(attack switch { "formula" => "<worksheet><f>HYPERLINK(\"https://example.invalid\")</f></worksheet>", "dtd" => "<!DOCTYPE d [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><d>&e;</d>", "expanded" => "<x>" + new string('x', 5 * 1024 * 1024) + "</x>", _ => "<x/>" });
        }
        Assert.Throws<ImportFileException>(() => ImportXlsx.Read(bytes.ToArray()));
    }
    [Fact]
    public void Invalid_zip_and_more_than_1000_rows_are_rejected()
    {
        Assert.Throws<ImportFileException>(() => ImportXlsx.Read(Encoding.UTF8.GetBytes("not Excel")));
        Assert.False(ImportPreview.Validate(Filled(count: 1001), Refs(), "hash").CanApprove);
    }
}
