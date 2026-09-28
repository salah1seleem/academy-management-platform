using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options)
    : DbContext(options)
{
}
