namespace FlexCms.Framework.Clock;

public sealed class FcmsClock : IFcmsClock
{
    public static readonly FcmsClock Instance = new();

    public DateTime Now => DateTime.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(DateTime.Now);
}
