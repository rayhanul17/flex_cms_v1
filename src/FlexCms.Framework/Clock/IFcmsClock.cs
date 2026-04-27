namespace FlexCms.Framework.Clock;

public interface IFcmsClock
{
    DateTime Now { get; }
    DateOnly Today { get; }
    TimeOnly TimeOfDay { get; }
}
