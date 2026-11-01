using System.Globalization;

namespace Academy.Api.Importing;

// Deterministic fictional source records. This only produces a workbook; it never
// seeds, resets, activates, changes the server clock, or sends a notification.
public static class FriendlyImportRehearsal
{
    public static ImportWorkbook Correction(Guid academy, DateOnly reference, string variant = "corrected")
    {
        var book = Create(academy, reference, 10, 70_000_000 + (int)(BitConverter.ToUInt32(academy.ToByteArray(), 0) % 7_000_000));
        ImportWorkbook Set(ImportWorkbook b, string sheet, int row, string key, string value)
        {
            var column = Array.FindIndex(FriendlyImportSchema.Sheets[sheet], f => f.Key == key);
            return new(b.Sheets.Select(s => s.Name != sheet ? s : s with { Rows = s.Rows.Select((r, i) => i != row ? r : r.Select((cell, c) => c == column ? new ImportCell(value, true) : cell).ToArray()).ToArray() }).ToArray());
        }
        book = new(book.Sheets.Select(s => s.Name != "إعدادات الملف" ? s : s with { Rows = s.Rows.Select(r => r.Count == 2 && r[0].Value == "كود المصدر" ? ConnectedImportSchema.Row("كود المصدر", "correction-demo-" + academy.ToString("N")[..12]) : r).ToArray() }).ToArray());
        book = Set(book, "المجموعات", 1, "Name", "مجموعة التصحيح المصغرة الخيالية");
        // A valid Arabic-digit equivalent demonstrates normalization in the corrected file.
        var phone = book.Sheet("أولياء الأمور").Rows[1][2].Value;
        book = Set(book, "أولياء الأمور", 1, "Phone", string.Concat(phone.Select(c => c is >= '0' and <= '9' ? (char)('٠' + c - '0') : c)));
        if (variant == "identity")
        {
            book = Set(book, "اللاعبون", 2, "Code", "PL0001");
            book = Set(book, "اللاعبون", 1, "Guardian", "GU-UNKNOWN");
            book = Set(book, "أولياء الأمور", 2, "Phone", "رقم غير صالح");
        }
        else if (variant == "links")
        {
            book = Set(book, "المجموعات", 1, "Coaches", "CO-UNKNOWN");
            book = Set(book, "التسجيلات", 1, "Plan", "PK-UNKNOWN");
            book = Set(book, "علاقات أولياء الأمور", 2, "Player", "PL-UNKNOWN");
        }
        else if (variant == "history")
        {
            book = Set(book, "فترات سابقة اختيارية", 1, "End", "2026-01-01");
            book = Set(book, "تحصيلات سابقة اختيارية", 1, "Amount", "-100");
            book = Set(book, "درجات اختيارية", 1, "Score", "101");
            book = Set(book, "التسجيلات", 1, "Remaining", "-1");
        }
        else if (variant != "corrected") throw new ArgumentOutOfRangeException(nameof(variant));
        return book;
    }
    public static ImportWorkbook Swimming(Guid academy,DateOnly reference,string? guardianName=null,string? guardianPhone=null)
    {
        var book=Create(academy,reference,10,50_000_000+(int)(BitConverter.ToUInt32(academy.ToByteArray(),0)%7_000_000));
        var labels=new[]{"وضع الجسم","التنفس","التوافق","التحمل"};
        var sheets=new List<ImportSheet>();
        foreach(var sheet in book.Sheets) {
            if(!FriendlyImportSchema.Sheets.TryGetValue(sheet.Name,out var fields)){sheets.Add(sheet);continue;}
            string Field(IReadOnlyList<ImportCell> r,string key){var n=Array.FindIndex(fields,f=>f.Key==key);return n>=0&&n<r.Count?r[n].Value:"";}
            var rows=new List<IReadOnlyList<ImportCell>>{sheet.Rows[0]};
            foreach(var input in sheet.Rows.Skip(1)) {
                var code=Field(input,"Code");
                if(sheet.Name=="الفروع"&&code!="BR01"||sheet.Name=="الرياضات"&&code!="SP01"||sheet.Name=="الباقات"&&code.StartsWith("SP02")||sheet.Name=="معايير تقييم اختيارية"&&(!code.StartsWith("SP01")||code.EndsWith("5")||code.EndsWith("6"))||sheet.Name=="درجات اختيارية"&&(Field(input,"Criterion").EndsWith("5")||Field(input,"Criterion").EndsWith("6")))continue;
                var values=input.Select(c=>c.Value=="BR01,BR02,BR03"?"BR01":c.Value is "BR02" or "BR03"?"BR01":c.Value.Replace("كرة القدم","السباحة")).ToArray();
                void Set(string key,string value){var n=Array.FindIndex(fields,f=>f.Key==key);if(n>=0)values[n]=value;}
                if(sheet.Name=="الفروع")Set("Name","فرع الموج — عرض خيالي");
                if(sheet.Name=="الرياضات"){Set("Name","السباحة");Set("Kind","سباحة");}
                if(sheet.Name=="المجموعات")Set("Name","مجموعة الموج — عرض خيالي");
                if(sheet.Name=="معايير تقييم اختيارية"){Set("Name",labels[int.Parse(code[^1].ToString())-1]);Set("Axis","");}
                if(sheet.Name=="أولياء الأمور"&&code=="GU0001"&&!string.IsNullOrWhiteSpace(guardianName)&&!string.IsNullOrWhiteSpace(guardianPhone)){Set("Name",guardianName.Trim());Set("Phone",guardianPhone);Set("Associate","نعم");}
                if(sheet.Name=="إعدادات الملف"&&values[0]=="كود المصدر")values[1]="swimming-demo-"+academy.ToString("N")[..12];
                rows.Add(ConnectedImportSchema.Row(values));
            }
            sheets.Add(sheet with{Rows=rows});
        }
        return new(sheets);
    }
    public static ImportWorkbook Create(Guid academy, DateOnly reference, int count = 300, int phoneOffset = 0)
    {
        if (count is < 2 or > 300) throw new ArgumentOutOfRangeException(nameof(count));
        var source = "professionals-demo-" + academy.ToString("N")[..12];
        var template = FriendlyImportSchema.Template(academy, "EGP", reference, source);
        var data = template.Sheets.ToDictionary(s => s.Name, s => s.Rows.ToList());
        void Add(string sheet, params string[] values) => data[sheet].Add(ConnectedImportSchema.Row(values.Concat(Enumerable.Repeat("", Math.Max(0, FriendlyImportSchema.Sheets[sheet].Length - values.Length))).ToArray()));
        string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var month = new DateOnly(reference.Year, reference.Month, 1); var first = month.AddMonths(-6);
        var groups = (count + 24) / 25; var footballGroups = Math.Max(1, (groups * 2 + 2) / 3); if (footballGroups == groups && groups > 1) footballGroups--;
        Add("الفروع", "BR01", "فرع مدينة نصر — عرض خيالي"); Add("الفروع", "BR02", "فرع مدينتي — عرض خيالي"); Add("الفروع", "BR03", "فرع الشروق — عرض خيالي");
        Add("الرياضات", "SP01", "كرة القدم", "كرة قدم"); Add("الرياضات", "SP02", "السباحة", "سباحة");
        Add("الفئات العمرية", "AG01", "مواليد 2014 إلى 2018", "2014", "2018");
        var slots = new Dictionary<(int Group, int Month), List<(string Code, DateOnly Date)>>();
        for (var g = 1; g <= groups; g++)
        {
            var football = g <= footballGroups; var sport = football ? "SP01" : "SP02"; var branch = !football ? "BR03" : g <= Math.Max(1, footballGroups / 2) ? "BR01" : "BR02";
            var group = $"GR{g:00}"; var coach = $"CO{g:00}"; var start = $"{14 + (g - 1) % 5:00}:00"; var end = $"{15 + (g - 1) % 5:00}:00";
            Add("المدربون", coach, g == 1 ? "ولي ومدرب خيالي 001" : $"كابتن خيالي {g:000}", "015" + (phoneOffset + g).ToString("00000000"), branch, sport);
            Add("المجموعات", group, (football ? "مجموعة الكرة " : "مجموعة الموج ") + g.ToString("00"), branch, sport, "AG01", "30", "", "GROUP", coach);
            foreach (var weekday in new[] { 1, 3 }) Add("المواعيد الأسبوعية", $"SCH{g:00}-{weekday}", group, weekday.ToString(), start, end, Day(first), Day(month.AddMonths(2).AddDays(-1)));
            for (var index = 0; index < 7; index++)
            {
                var from = first.AddMonths(index); var limit = index == 6 ? reference : from.AddMonths(1).AddDays(-1);
                var days = new List<DateOnly>(); for (var d = from; d <= limit; d = d.AddDays(1)) if (d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Wednesday) days.Add(d);
                var selected = days.Take(index == 6 ? 6 : 8).ToArray(); slots[(g, index)] = [];
                for (var n = 0; n < selected.Length; n++)
                {
                    var code = $"TS{g:00}-{from:yyyyMM}-{n + 1:00}"; slots[(g, index)].Add((code, selected[n]));
                    Add("حصص فعلية اختيارية", code, group, Day(selected[n]), start, end, "منعقدة");
                }
            }
        }
        foreach (var (sp, name) in new[] { ("SP01", "كرة القدم"), ("SP02", "السباحة") })
        {
            Add("الباقات", sp + "-D", "مدة — " + name, sp, "BR01,BR02,BR03", "مدة", "600", "31", "", "GROUP");
            Add("الباقات", sp + "-S", "ثماني حصص — " + name, sp, "BR01,BR02,BR03", "حصص", "800", "", "8", "GROUP");
            Add("الباقات", sp + "-C", "مدة وثماني حصص — " + name, sp, "BR01,BR02,BR03", "مزدوجة", "1000", "31", "8", "GROUP");
            var labels = sp == "SP01" ? new[] { "تمرير", "مراوغة", "سرعة", "دفاع", "بدني", "تسديد" } : new[] { "وضع الجسم", "التنفس", "التوافق", "التحمل" };
            var axes = new[] { "Passing", "Dribbling", "Speed", "Defending", "Physical", "Shooting" };
            for (var c = 0; c < labels.Length; c++) Add("معايير تقييم اختيارية", $"{sp}-CR{c + 1}", sp, labels[c], "1", sp == "SP01" ? axes[c] : "");
        }
        var active = count == 300 ? 250 : count - 2;
        for (var i = 1; i <= count; i++)
        {
            var player = $"PL{i:0000}"; var guardian = i == 2 ? "GU0001" : $"GU{i:0000}";
            var join = Math.Min(5, (i - 1) / Math.Max(1, count / 6)); var registered = first.AddMonths(join).AddDays(-7 + i % 5);
            if (i != 2) Add("أولياء الأمور", guardian, i == 1 ? "ولي ومدرب خيالي 001" : $"ولي أمر خيالي {i:000}", i == 1 ? "015" + (phoneOffset + 1).ToString("00000000") : "010" + (phoneOffset + 1000 + i).ToString("00000000"), i == 1 ? "نعم" : "لا");
            Add("اللاعبون", player, $"لاعب خيالي {i:000}", $"{2014 + i % 5}-0{1 + i % 8}-12", i % 2 == 0 ? "أنثى" : "ذكر", guardian, Day(registered));
            Add("علاقات أولياء الأمور", $"REL{i:0000}", player, guardian, "ولي أمر");
            if (i == 1)
            {
                Add("أولياء الأمور", "GU-EXTRA", "ولية أمر خيالية إضافية", "011" + (phoneOffset + 2001).ToString("00000000"), "لا");
                Add("علاقات أولياء الأمور", "REL-EXTRA", player, "GU-EXTRA", "الأم");
            }
            var mainGroup = (i - 1) / 25 + 1; AddEnrollment($"EN{i:0000}", mainGroup);
            if (count == 300 && i <= 5) AddEnrollment($"EN{i:0000}-SW", groups);
            void AddEnrollment(string enrollment, int g)
            {
                var football = g <= footballGroups; var sp = football ? "SP01" : "SP02"; var branch = !football ? "BR03" : g <= Math.Max(1, footballGroups / 2) ? "BR01" : "BR02";
                var kind = i % 3; var package = sp + (kind == 0 ? "-D" : kind == 1 ? "-S" : "-C"); var net = kind == 0 ? "600" : kind == 1 ? "800" : "1000";
                var currentIndex = i <= active ? 6 : 5; var currentMonth = first.AddMonths(currentIndex); var currentCode = $"PER-{enrollment}-{currentMonth:yyyyMM}";
                var end = i > active ? currentMonth.AddMonths(1).AddDays(-1) : i <= Math.Min(50, active) ? month.AddDays(27) : month.AddMonths(1).AddDays(9);
                var remaining = i > active ? "0" : (8 - slots[(g, currentIndex)].Count).ToString();
                Add("التسجيلات", enrollment, player, branch, sp, $"GR{g:00}", package, "", currentCode, Day(currentMonth), Day(end), kind == 0 ? "" : "8", kind == 0 ? "" : remaining, net, "EGP");
                for (var index = join; index <= currentIndex; index++)
                {
                    var from = first.AddMonths(index); var period = $"PER-{enrollment}-{from:yyyyMM}";
                    if (index != currentIndex) Add("فترات سابقة اختيارية", period, enrollment, package, Day(from), Day(from.AddMonths(1).AddDays(-1)), kind == 0 ? "" : "8", kind == 0 ? "" : "0", net, "EGP");
                    Add("تحصيلات سابقة اختيارية", $"PAY-{enrollment}-{from:yyyyMM}", period, Day(from), net, "EGP", i % 2 == 0 ? "نقدي" : "إلكتروني", $"DEMO-SOURCE-{enrollment}-{from:yyyyMM}");
                    foreach (var (session, date) in slots[(g, index)])
                    {
                        var status = kind == 0 && (i + date.Day) % 11 == 0 ? "بعذر" : (i + date.Day) % 7 == 0 ? "متأخر" : "حاضر";
                        Add("حضور اختياري", $"AT-{enrollment}-{session}", session, enrollment, status, kind == 0 ? "" : period);
                    }
                    var assessment = $"EV-{enrollment}-{from:yyyyMM}"; var evaluated = slots[(g, index)].Last();
                    Add("تقييمات اختيارية", assessment, enrollment, $"GR{g:00}", $"CO{g:00}", Day(evaluated.Date), from.ToString("yyyy-MM"), (i + index) % 10 == 0 ? "مسودة" : "منشور", evaluated.Code, "متابعة تدريبية خيالية للتجربة");
                    for (var c = 1; c <= (football && i <= 10 ? 6 : 4); c++) Add("درجات اختيارية", $"SC-{assessment}-{c}", assessment, $"{sp}-CR{c}", (55 + (i * 3 + c * 7 + index * 2) % 41).ToString());
                }
            }
        }
        return new(template.Sheets.Select(s => s with { Rows = data[s.Name] }).ToArray());
    }
}
