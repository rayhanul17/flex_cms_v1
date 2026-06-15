using FlexCms.Framework.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlexCms.Tests.Unit.Helpers;

/// <summary>
/// Pins the contract on the shared controller base — TempData keys
/// stay stable, append concatenates instead of overwrites, duration
/// and close-button toggles round-trip. Any module that learns to
/// rely on these defaults must not be broken by a future refactor.
/// </summary>
public class BaseFcmsControllerTests
{
    // Expose the protected toast helpers for direct test invocation. In real
    // controllers these are called from action methods that DO live on the
    // subclass, so protected is the right access level on the base.
    private sealed class TestController : BaseFcmsController
    {
        public new void ShowSuccess(string message, bool appendMessage = false,
            bool showAfterRedirect = true, int durationSeconds = 5, bool showCloseButton = true)
            => base.ShowSuccess(message, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

        public new void ShowError(string message, bool appendMessage = false,
            bool showAfterRedirect = true, int durationSeconds = 7, bool showCloseButton = true)
            => base.ShowError(message, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

        public new void ShowWarning(string message, bool appendMessage = false,
            bool showAfterRedirect = true, int durationSeconds = 5, bool showCloseButton = true)
            => base.ShowWarning(message, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

        public new void ShowInfo(string message, bool appendMessage = false,
            bool showAfterRedirect = true, int durationSeconds = 5, bool showCloseButton = true)
            => base.ShowInfo(message, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);

        public new void ShowMessage(string message, FcmsMessageType messageType = FcmsMessageType.Success,
            bool appendMessage = false, bool showAfterRedirect = true, int durationSeconds = 5,
            bool showCloseButton = true)
            => base.ShowMessage(message, messageType, appendMessage, showAfterRedirect, durationSeconds, showCloseButton);
    }

    private static TestController NewController()
    {
        var ctrl = new TestController();
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = sp };
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };

        var provider = Substitute.For<ITempDataProvider>();
        ctrl.TempData = new TempDataDictionary(http, provider);
        return ctrl;
    }

    [Fact]
    public void ShowSuccess_writes_TempData_with_default_settings()
    {
        var c = NewController();
        c.ShowSuccess("Saved.");

        Assert.Equal("Saved.", c.TempData[BaseFcmsController.TempDataMessageKey]);
        Assert.Equal("success", c.TempData[BaseFcmsController.TempDataTypeKey]);
        Assert.Equal(5, c.TempData[BaseFcmsController.TempDataDurationKey]);
        Assert.Equal(true, c.TempData[BaseFcmsController.TempDataCloseButtonKey]);
    }

    [Fact]
    public void Append_concatenates_with_separator()
    {
        var c = NewController();
        c.ShowSuccess("First");
        c.ShowSuccess("Second", appendMessage: true);

        Assert.Equal("First | Second", c.TempData[BaseFcmsController.TempDataMessageKey]);
    }

    [Fact]
    public void Append_with_no_existing_message_writes_plain_text()
    {
        var c = NewController();
        c.ShowSuccess("Lonely", appendMessage: true);
        Assert.Equal("Lonely", c.TempData[BaseFcmsController.TempDataMessageKey]);
    }

    [Fact]
    public void Subsequent_call_without_append_overwrites()
    {
        var c = NewController();
        c.ShowSuccess("First");
        c.ShowSuccess("Second");

        Assert.Equal("Second", c.TempData[BaseFcmsController.TempDataMessageKey]);
    }

    [Fact]
    public void Duration_and_close_button_round_trip()
    {
        var c = NewController();
        c.ShowMessage("Bye", FcmsMessageType.Warning,
            durationSeconds: 12, showCloseButton: false);

        Assert.Equal(12, c.TempData[BaseFcmsController.TempDataDurationKey]);
        Assert.Equal(false, c.TempData[BaseFcmsController.TempDataCloseButtonKey]);
        Assert.Equal("warning", c.TempData[BaseFcmsController.TempDataTypeKey]);
    }

    [Fact]
    public void ShowAfterRedirect_false_writes_to_ViewBag_not_TempData()
    {
        var c = NewController();
        c.ShowSuccess("Inline", showAfterRedirect: false);

        Assert.Null(c.TempData[BaseFcmsController.TempDataMessageKey]);
        Assert.Equal("Inline", c.ViewBag.FcmsToastMessage);
        Assert.Equal("success", c.ViewBag.FcmsToastType);
    }

    [Fact]
    public void Each_variant_helper_maps_to_correct_type()
    {
        var c = NewController();
        c.ShowSuccess("a"); Assert.Equal("success", c.TempData[BaseFcmsController.TempDataTypeKey]);
        c.ShowError("a");   Assert.Equal("danger",  c.TempData[BaseFcmsController.TempDataTypeKey]);
        c.ShowWarning("a"); Assert.Equal("warning", c.TempData[BaseFcmsController.TempDataTypeKey]);
        c.ShowInfo("a");    Assert.Equal("info",    c.TempData[BaseFcmsController.TempDataTypeKey]);
    }

    [Fact]
    public void FcmsOk_returns_isSuccess_true_envelope()
    {
        var c = NewController();
        var json = c.GetType()
            .GetMethod("FcmsOk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(c, new object?[] { "msg", null }) as JsonResult;

        Assert.NotNull(json);
        var value = json!.Value!.GetType();
        var isSuccess = (bool)value.GetProperty("isSuccess")!.GetValue(json.Value)!;
        Assert.True(isSuccess);
    }
}
