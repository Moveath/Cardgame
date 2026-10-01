using CardGameClient;

namespace CardGame.Core.Tests;

public class GovnoGestureTests
{
    private static GovnoGestureState Press(int slot = 2, float x = 80, float y = -200)
    {
        var state = new GovnoGestureState();
        Assert.True(state.Begin(7, 10, true, slot, x, y));
        return state;
    }

    [Fact]
    public void CardTapIsReturnedOnlyOnReleaseAndOnlyOnce()
    {
        var state = Press();
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 83, -197, false).Action);
        Assert.True(state.IsCaptured);
        var decision = state.Track(7, 10, true, 83, -197, true);
        Assert.Equal(GovnoGestureAction.Tap, decision.Action);
        Assert.Equal(2, decision.Slot);
        Assert.False(state.IsCaptured);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 83, -197, true).Action);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void UpwardSwipeCanStartOnCardOrGapAndFiresExactlyOnce(int slot)
    {
        var state = Press(slot);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 85, -160, false).Action);
        Assert.Equal(GovnoGestureAction.SwipeUp, state.Track(7, 10, true, 85, -140, false).Action);
        Assert.False(state.IsCaptured);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 85, -100, false).Action);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 85, -100, true).Action);
    }

    [Fact]
    public void ReleaseRecognizesFastSwipeWithoutIntermediateDragEvent()
    {
        var state = Press();
        Assert.Equal(GovnoGestureAction.SwipeUp, state.Track(7, 10, true, 80, -100, true).Action);
        Assert.False(state.IsCaptured);
    }

    [Fact]
    public void TapInGapDoesNothing()
    {
        var state = Press(-1);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80, -200, true).Action);
        Assert.False(state.IsCaptured);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(0, -30)]
    [InlineData(30, 0)]
    [InlineData(-30, 0)]
    public void DragReturningToInitialCardNeverBecomesTap(float dx, float dy)
    {
        var state = Press();
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80 + dx, -200 + dy, false).Action);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80, -200, true).Action);
        Assert.False(state.IsCaptured);
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(-40, 0)]
    [InlineData(0, -40)]
    [InlineData(40, 40)]
    public void NonUpwardDragCannotTurnIntoSwipeLater(float dx, float dy)
    {
        var state = Press();
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80 + dx, -200 + dy, false).Action);
        Assert.False(state.IsCaptured);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80, -100, true).Action);
    }

    [Theory]
    [InlineData(14, 0, (int)GovnoGestureAction.Tap)]
    [InlineData(0, 14, (int)GovnoGestureAction.Tap)]
    [InlineData(0, 14.01f, (int)GovnoGestureAction.None)]
    [InlineData(0, 59.99f, (int)GovnoGestureAction.None)]
    [InlineData(0, 60, (int)GovnoGestureAction.SwipeUp)]
    [InlineData(25, 60, (int)GovnoGestureAction.SwipeUp)]
    [InlineData(50, 60, (int)GovnoGestureAction.None)]
    [InlineData(50, 60.1f, (int)GovnoGestureAction.SwipeUp)]
    public void ReleaseRespectsTapAndSwipeBoundaries(float dx, float dy, int expected)
    {
        var state = Press();
        Assert.Equal((GovnoGestureAction)expected, state.Track(7, 10, true, 80 + dx, -200 + dy, true).Action);
        Assert.False(state.IsCaptured);
    }

    [Fact]
    public void SecondPointerCannotStealOrReleaseFirstPointersGesture()
    {
        var state = Press();
        Assert.False(state.Begin(8, 10, true, 1, 500, 500));
        Assert.Equal(GovnoGestureAction.None, state.Track(8, 11, false, 500, 700, true).Action);
        Assert.True(state.HasPointer(7));
        Assert.Equal(GovnoGestureAction.Tap, state.Track(7, 10, true, 80, -200, true).Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewExchangeCancelsHeldPointerBeforeItCanTapOrSwipe(bool swipe)
    {
        var state = Press();
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 11, true, 80, swipe ? -100 : -200, true).Action);
        Assert.False(state.IsCaptured);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 11, true, 80, -100, false).Action);
        Assert.True(state.Begin(7, 11, true, 2, 80, -200));
        Assert.Equal(GovnoGestureAction.SwipeUp, state.Track(7, 11, true, 80, -100, true).Action);
    }

    [Fact]
    public void InteractionGatePreventsBeginAndCancelsExistingGesture()
    {
        var state = new GovnoGestureState();
        Assert.False(state.Begin(7, 10, false, 2, 80, -200));
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80, -100, true).Action);
        Assert.True(state.Begin(7, 10, true, 2, 80, -200));
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, false, 80, -100, false).Action);
        Assert.False(state.IsCaptured);
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80, -100, true).Action);
    }

    [Fact]
    public void ExplicitCancellationPreventsHeldPointerFromResumingAfterPause()
    {
        var state = Press();
        state.Cancel();
        Assert.Equal(GovnoGestureAction.None, state.Track(7, 10, true, 80, -100, true).Action);
        Assert.True(state.Begin(7, 10, true, 2, 80, -200));
        Assert.Equal(GovnoGestureAction.Tap, state.Track(7, 10, true, 80, -200, true).Action);
    }
}
