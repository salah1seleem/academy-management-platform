using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

public sealed partial class CashRegisterService
{
    public async Task AuthorizeProductSale(bool hasPlayer = false, bool review = false)
    {
        var (t, _) = await Context(write: true);
        if (t.Role == AcademyRole.AcademyOwner) return;
        if (!await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.Module == "catalog" && g.IsActive && g.Read && (review ? g.Edit : g.Add)) ||
            hasPlayer && !await db.StaffActionGrants.AnyAsync(g => g.MembershipId == t.MembershipId && g.Module == "players" && g.IsActive && g.Read))
            throw new CashRejected("يلزم تفويض بيع المنتجات وقراءة اللاعب عند ربطه بالبيع.", 403);
    }

    public static string ProductHash(SaleWrite body) => AcademyConfigurationService.Hash(JsonSerializer.Serialize(body));

    public async Task<object> ProductOptions()
    {
        await AuthorizeProductSale();
        return new { branches = await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.ArabicName).Select(b => new { b.Id, b.ArabicName }).ToArrayAsync() };
    }

    public async Task<object> ProductQuote(Guid productId, Guid branchId, int quantity, Guid? playerId)
    {
        await AuthorizeProductSale(playerId.HasValue); var (t, _) = await Context(write: true);
        if (quantity is < 1 or > 100) throw new CashRejected("اختر كمية من 1 إلى 100.", 422);
        if (!await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive) || playerId.HasValue && !await db.Players.AnyAsync(p => p.Id == playerId && p.IsActive))
            throw new CashRejected("الفرع أو اللاعب غير متاحين.", 404);
        var product = await db.SportCatalogItems.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId && p.IsActive && p.Sport.IsActive);
        if (product?.DisplayPrice is null || string.IsNullOrWhiteSpace(product.Currency)) throw new CashRejected("المنتج غير متاح للبيع.", 404);
        if (product.StockQuantity < quantity) throw new CashRejected("الكمية المطلوبة غير متاحة حاليًا.");
        var unit = decimal.Round(product.DisplayPrice.Value * (100 - (product.DiscountPercentage ?? 0)) / 100, 2, MidpointRounding.AwayFromZero);
        Amount(unit * quantity, zero: false);
        var enabled = (await config.Active(t.AcademyId))?.CashEnabled == true;
        var sessions = await (from s in db.CashSessions.AsNoTracking()
                              join r in db.CashRegisters on s.RegisterId equals r.Id
                              where enabled && s.ClosedAtUtc == null && s.BranchId == branchId && s.OpenedByMembershipId == t.MembershipId && r.IsActive && r.Currency == product.Currency &&
                                  db.CashierAssignments.Any(a => a.RegisterId == r.Id && a.MembershipId == t.MembershipId && a.IsActive)
                              orderby r.Name select new { s.Id, name = r.Name }).ToArrayAsync();
        return new { unitPrice = Money(unit), amount = Money(unit * quantity), product.Currency, product.StockQuantity, cashEnabled = enabled, sessions };
    }

    private async Task<(CashSession Session, CashRegister Register)> ProductSession(Guid id)
    {
        await AuthorizeProductSale(); var (t, _) = await Context(write: true);
        var session = await Session(id); var register = await Register(session.RegisterId);
        await Enabled(t.AcademyId); await Usable(register); await Cashier(register, t);
        if (session.ClosedAtUtc.HasValue) throw new CashRejected("الوردية مقفلة. افتح وردية جديدة قبل التحصيل.");
        if (session.OpenedByMembershipId != t.MembershipId) throw new CashRejected("التحصيل من ورديتك المفتوحة فقط.", 403);
        return (session, register);
    }

    public async Task PrepareProductSale(Guid? sessionId, Guid branchId, string currency)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Product sale transaction required.");
        if (sessionId is not Guid id || id == Guid.Empty) throw new CashRejected("اختر ورديتك المفتوحة قبل بيع المنتج نقديًا.", 422);
        var (t, _) = await Context(write: true); await Lock(t.AcademyId, $"session:{id}");
        var (session, register) = await ProductSession(id);
        if (session.BranchId != branchId || register.Currency != currency) throw new CashRejected("اختر وردية من فرع البيع وبعملة المنتج.");
    }

    public async Task PostProductSale(Guid sessionId, Guid saleId, string payloadHash)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Product sale transaction required.");
        var (t, user) = await Context(write: true); var (session, register) = await ProductSession(sessionId);
        var sale = await db.ProductSales.SingleAsync(s => s.Id == saleId);
        if (sale.Status != "Confirmed" || sale.PaymentMethod != "Cash" || sale.ConfirmedByUserId != user || sale.BranchId != session.BranchId || sale.Currency != register.Currency)
            throw new CashRejected("بيانات بيع المنتج لا تطابق الوردية.");
        Amount(sale.Amount, zero: false); var at = time.GetUtcNow();
        db.CashProductPostings.Add(new() { Id = Guid.NewGuid(), AcademyId = t.AcademyId, SessionId = sessionId, BranchId = session.BranchId,
            ProductSaleId = saleId, Amount = sale.Amount, Currency = sale.Currency, ActorUserId = user, RequestHash = payloadHash, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync();
    }
}
