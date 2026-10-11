namespace Academy.Infrastructure.People;
public static class CompensationLimits
{
    public static bool Validity(DateOnly today, DateOnly from, DateOnly to) => from >= today && from <= today.AddYears(1) && to >= from && to <= from.AddDays(90);
    public static int Units(CompensationUnit unit, TimeOnly start, TimeOnly end) => start >= end ? 0 : unit == CompensationUnit.Session ? 1 : (int)(end - start).TotalMinutes;
    public static bool CanAllocate(int total, int allocated, int requested) => total > 0 && allocated >= 0 && requested > 0 && requested <= total - allocated;
}
