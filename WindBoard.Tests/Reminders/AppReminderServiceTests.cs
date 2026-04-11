using System;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using WindBoard.Reminders;
using WindBoard.Reminders.Channels;
using Xunit;

namespace WindBoard.Tests.Reminders;

public sealed class AppReminderServiceTests
{
    [Fact]
    public void RemindOncePerSignature_DoesNotConsumeSignature_WhenAllChannelsFail()
    {
        var toastChannel = new TestReminderChannel();
        var bannerChannel = new TestReminderChannel();
        var service = new AppReminderService(toastChannel, bannerChannel, static _ => false);
        Window window = CreateWindowStub();
        var message = new AppReminderMessage { Title = "title", Body = "body" };

        bool firstShown = service.RemindOncePerSignature(window, "UpdateAvailable:v2.0.0", message);

        bannerChannel.ShouldSucceed = true;
        bool secondShown = service.RemindOncePerSignature(window, "UpdateAvailable:v2.0.0", message);

        Assert.False(firstShown);
        Assert.True(secondShown);
        Assert.Equal(2, toastChannel.CallCount);
        Assert.Equal(2, bannerChannel.CallCount);
    }

    [Fact]
    public void RemindOncePerSignature_DoesNotConsumeSignature_WhenFullScreenBannerFails()
    {
        var toastChannel = new TestReminderChannel();
        var bannerChannel = new TestReminderChannel();
        var service = new AppReminderService(toastChannel, bannerChannel, static _ => true);
        Window window = CreateWindowStub();
        var message = new AppReminderMessage { Title = "title", Body = "body" };

        bool firstShown = service.RemindOncePerSignature(window, "UpdateAvailable:v2.0.0", message);

        bannerChannel.ShouldSucceed = true;
        bool secondShown = service.RemindOncePerSignature(window, "UpdateAvailable:v2.0.0", message);

        Assert.False(firstShown);
        Assert.True(secondShown);
        Assert.Equal(0, toastChannel.CallCount);
        Assert.Equal(2, bannerChannel.CallCount);
    }

    private static Window CreateWindowStub()
    {
        return (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
    }

    private sealed class TestReminderChannel : IAppReminderChannel
    {
        public bool ShouldSucceed { get; set; }

        public int CallCount { get; private set; }

        public bool TryShow(Window window, AppReminderMessage message, out Exception? error)
        {
            CallCount++;
            error = ShouldSucceed ? null : new InvalidOperationException("channel failed");
            return ShouldSucceed;
        }
    }
}
