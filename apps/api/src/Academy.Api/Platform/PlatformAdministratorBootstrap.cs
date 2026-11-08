using System.Security.Cryptography;
using System.Text;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Platform;

// Operator-only process, never an HTTP endpoint. No promotion or credential reset.
public static class PlatformAdministratorBootstrap
{
    public static async Task<Guid> CreateFirst(FoundationDbContext db, UserManager<ApplicationUser> users,
        string email, string displayName, string password, Func<string, Task<string>> confirmAuthenticator)
    {
        email = email.Trim(); displayName = displayName.Trim();
        if (email.Length > 256 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
            || displayName.Length is < 1 or > 200 || password.Length > 1024)
            throw new InvalidOperationException("راجع البريد والاسم وكلمة المرور.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(684239701)");
        if (await db.PlatformAdministrators.AnyAsync())
            throw new InvalidOperationException("يوجد مسؤول منصة بالفعل؛ التهيئة الأولى لا تعدل الحسابات القائمة.");
        if (await users.FindByEmailAsync(email) is not null || await users.FindByNameAsync(email) is not null)
            throw new InvalidOperationException("البريد مستخدم؛ لا يمكن ترقية حساب قائم بهذه الأداة.");
        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email,
            DisplayName = displayName, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        if (!(await users.CreateAsync(user, password)).Succeeded)
            throw new InvalidOperationException("تعذر إنشاء الحساب؛ تحقق من سياسة كلمة المرور.");
        if (!(await users.ResetAuthenticatorKeyAsync(user)).Succeeded)
            throw new InvalidOperationException("تعذر تجهيز تطبيق المصادقة.");
        var key = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("تعذر تجهيز تطبيق المصادقة.");
        var code = await confirmAuthenticator(key);
        if (code.Length != 6 || code.Any(c => !char.IsAsciiDigit(c))
            || !await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
            throw new InvalidOperationException("رمز المصادقة غير صحيح؛ لم تحفظ التهيئة.");
        if (!(await users.SetTwoFactorEnabledAsync(user, true)).Succeeded)
            throw new InvalidOperationException("تعذر تفعيل المصادقة الثنائية.");
        db.PlatformAdministrators.Add(new() { UserId = user.Id });
        // The enrollment code is consumed, just like a normal platform login.
        db.PlatformMfaUses.Add(new() { Id = Guid.NewGuid(), UserId = user.Id, AtUtc = DateTimeOffset.UtcNow,
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))) });
        db.PlatformAudits.Add(new() { Id = Guid.NewGuid(), ActorUserId = user.Id,
            Action = "platform.bootstrap", Details = "{\"authentication\":\"password-and-verified-totp\"}", AtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return user.Id;
    }

    public static async Task RunInteractive(IServiceProvider services, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Demo") || Console.IsInputRedirected || Console.IsOutputRedirected)
            throw new InvalidOperationException("التهيئة تحتاج جلسة طرفية تفاعلية خاصة خارج وضع العرض.");
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Console.WriteLine($"تهيئة أول مسؤول منصة فقط. الخادم: {connection.Host}، القاعدة: {connection.Database}");
        Console.WriteLine("لا تشغّل التهيئة على بيانات عملاء دون تفويض. اكتب اسم القاعدة لتأكيد الهدف:");
        if (string.IsNullOrWhiteSpace(connection.Database) || Console.ReadLine() != connection.Database)
            throw new InvalidOperationException("لم يتأكد الهدف؛ لم تنفذ التهيئة.");
        Console.Write("بريد المسؤول الجديد: "); var email = Console.ReadLine() ?? "";
        Console.Write("اسم المسؤول: "); var name = Console.ReadLine() ?? "";
        Console.Write("كلمة مرور جديدة (مخفية): "); var password = HiddenInput();
        Console.Write("تأكيد كلمة المرور: ");
        if (HiddenInput() != password) throw new InvalidOperationException("كلمتا المرور غير متطابقتين.");
        await CreateFirst(db, scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), email, name, password, key =>
        {
            Console.WriteLine("أضف حساب TOTP إلى تطبيق المصادقة على هاتفك باسم iAcademy One، بهذا المفتاح الخاص:");
            Console.WriteLine(key);
            Console.WriteLine("احفظ المفتاح في خزنتك الخاصة للاسترداد؛ لا ترسله أو تسجله أو تلتقط له صورة مشتركة.");
            Console.Write("اكتب الرمز من التطبيق (مخفي): ");
            return Task.FromResult(HiddenInput());
        });
        Console.WriteLine("تمت تهيئة المسؤول وتأكيد TOTP. انتظر تغير الرمز ثم استخدم صفحة دخول المنصة.");
    }

    private static string HiddenInput()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return text.ToString(); }
            if (key.Key == ConsoleKey.Escape) throw new InvalidOperationException("ألغيت التهيئة.");
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; }
            else if (!char.IsControl(key.KeyChar) && text.Length < 1024) text.Append(key.KeyChar);
        }
    }
}
