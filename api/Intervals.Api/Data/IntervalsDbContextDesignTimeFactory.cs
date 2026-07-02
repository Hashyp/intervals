using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Intervals.Api.Data;

public sealed class IntervalsDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<IntervalsDbContext>
{
    public IntervalsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IntervalsDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=intervals-design;Username=postgres;Password=postgres"
            )
            .Options;

        return new IntervalsDbContext(options);
    }
}
