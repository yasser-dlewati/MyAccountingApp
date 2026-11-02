namespace MyAccountingApp.Core.Enums;

/// <summary>Dashboard range, ending today: Day = today by hour, Week / Month = last 7 / 30 days by day,
/// Year = last 12 months by month.</summary>
public enum ReportPeriod
{
    Day = 1,
    Week = 2,
    Month = 3,
    Year = 4
}
