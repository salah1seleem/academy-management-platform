namespace Academy.Api.Finance;

public sealed record ExpenseShare(Guid? GroupId, int Weight, decimal Amount, string ServiceKind = "Training");
public sealed record ExpenseBooking(Guid Id, string ServiceKind, Guid? GroupId, int Minutes);

public static class OperatingExpenseAllocation
{
    // Stable largest-remainder split in minor units. A saved run, not a report filter,
    // determines the denominator and gives every last piaster one owner.
    public static IReadOnlyList<ExpenseShare> Equal(decimal amount, IEnumerable<Guid> eligibleGroups)
    {
        if (amount <= 0 || amount > 999999999m || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount));
        var groups = eligibleGroups.Distinct().OrderBy(x => x.ToString("N"), StringComparer.Ordinal).ToArray();
        if (groups.Length == 0) return [new(null, 0, amount)];
        var cents = decimal.ToInt64(amount * 100m);
        var baseCents = cents / groups.Length;
        var remainder = cents % groups.Length;
        return groups.Select((id, index) => new ExpenseShare(id, 1, (baseCents + (index < remainder ? 1 : 0)) / 100m)).ToArray();
    }

    // Shared resource cost first follows actual reserved booking minutes by service.
    // The Training bucket then follows the owner's default: equal active groups.
    // Non-training costs are explicit service residuals, never silently charged to Training.
    public static IReadOnlyList<ExpenseShare> ByServiceMinutes(decimal amount, IEnumerable<ExpenseBooking> bookings)
    {
        _ = Equal(amount, []); // Same precise-money validation as direct allocation.
        var rows = bookings.ToArray();
        if (rows.Length == 0) return [new(null, 0, amount, "Unallocated")];
        if (rows.Any(x => x.Minutes <= 0 || string.IsNullOrWhiteSpace(x.ServiceKind) || x.ServiceKind.Length > 32))
            throw new ArgumentException("Invalid reserved booking for expense allocation.", nameof(bookings));
        var services = rows.GroupBy(x => x.ServiceKind, StringComparer.Ordinal)
            .Select(g => new { Kind = g.Key, Minutes = g.Sum(x => (long)x.Minutes), Rows = g.ToArray() })
            .OrderBy(x => x.Kind, StringComparer.Ordinal).ToArray();
        var totalMinutes = services.Sum(x => x.Minutes);
        var cents = decimal.ToInt64(amount * 100m);
        var floors = services.Select(s => decimal.ToInt64(decimal.Floor((decimal)cents * s.Minutes / totalMinutes))).ToArray();
        var allocated = floors.Sum();
        // Largest remainders; service-name ordering resolves exact ties deterministically.
        var extra = services.Select((s, i) => new { Index = i, Remainder = (decimal)cents * s.Minutes % totalMinutes })
            .OrderByDescending(x => x.Remainder).ThenBy(x => services[x.Index].Kind, StringComparer.Ordinal)
            .Take((int)(cents - allocated)).Select(x => x.Index).ToHashSet();
        var result = new List<ExpenseShare>();
        for (var i = 0; i < services.Length; i++)
        {
            var service = services[i];
            var serviceAmount = (floors[i] + (extra.Contains(i) ? 1 : 0)) / 100m;
            if (service.Minutes > int.MaxValue) throw new ArgumentException("Booking duration exceeds supported allocation weight.", nameof(bookings));
            if (service.Kind != "Training") { result.Add(new(null, (int)service.Minutes, serviceAmount, service.Kind)); continue; }
            var recipients = service.Rows.Select(x => x.GroupId).Distinct()
                .OrderBy(x => x.HasValue ? x.Value.ToString("N") : "zz", StringComparer.Ordinal).ToArray();
            if (recipients.Length == 1 && recipients[0] is null) { result.Add(new(null, 0, serviceAmount)); continue; }
            var trainingCents = decimal.ToInt64(serviceAmount * 100m);
            var each = trainingCents / recipients.Length;
            var remainder = trainingCents % recipients.Length;
            result.AddRange(recipients.Select((id, index) => new ExpenseShare(id, 1, (each + (index < remainder ? 1 : 0)) / 100m)));
        }
        return result;
    }
}
