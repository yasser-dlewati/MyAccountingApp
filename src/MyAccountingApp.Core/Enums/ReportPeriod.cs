namespace MyAccountingApp.Core.Enums;

/// <summary>Dashboard range = the current calendar period: Day = today by hour, Week (from Saturday) / Month (from the 1st)
/// by day, Year (from 1 January) by month.</summary>
public enum ReportPeriod
{
    Day = 1,
    Week = 2,
    Month = 3,
    Year = 4
}
