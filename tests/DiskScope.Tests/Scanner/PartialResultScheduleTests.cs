using DiskScope.Scanner;

namespace DiskScope.Tests.Scanner;

public sealed class PartialResultScheduleTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void NoInterval_IsNeverDue()
    {
        var schedule = new PartialResultSchedule(new ScanOptions());

        Assert.False(schedule.Enabled);
        Assert.False(schedule.IsDue(S(3600)));
    }

    [Fact]
    public void Interval_DoublesAfterEachSnapshot_UpToTheCap()
    {
        var schedule = new PartialResultSchedule(new ScanOptions
        {
            PartialResultInterval = S(1),
            MaxPartialResultInterval = S(3),
        });

        Assert.False(schedule.IsDue(S(0.9)));
        Assert.True(schedule.IsDue(S(1)));

        schedule.Published(S(1), TimeSpan.Zero);
        Assert.False(schedule.IsDue(S(2.9)));
        Assert.True(schedule.IsDue(S(3)));

        schedule.Published(S(3), TimeSpan.Zero);
        Assert.False(schedule.IsDue(S(5.9)));
        Assert.True(schedule.IsDue(S(6)));
    }

    [Fact]
    public void ExpensiveSnapshot_PushesTheNextOneToTenTimesItsCost()
    {
        var schedule = new PartialResultSchedule(new ScanOptions { PartialResultInterval = S(1) });

        schedule.Published(S(1), S(0.5));

        Assert.False(schedule.IsDue(S(5.9)));
        Assert.True(schedule.IsDue(S(6)));
    }
}
