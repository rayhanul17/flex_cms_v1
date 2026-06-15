namespace FlexCms.Framework.Mvc;

/// <summary>
/// Toast variants surfaced by <see cref="BaseFcmsController.ShowMessage"/>.
/// The string form is the Bootstrap colour class fragment used by
/// <c>fcms.toast.&lt;type&gt;()</c> on the client.
/// </summary>
public enum FcmsMessageType
{
    Success,
    Info,
    Warning,
    Danger
}
