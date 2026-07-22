using System.Runtime.ExceptionServices;
using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialMenuControlTests
{
    [Fact]
    public void KeyboardFocus_RemainsEnabledWithoutDefaultFocusRectangle()
    {
        Exception? failure = null;
        var completed = false;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new RadialMenuControl();
                Assert.True(control.Focusable);
                Assert.Null(control.FocusVisualStyle);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed = true;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(completed);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
