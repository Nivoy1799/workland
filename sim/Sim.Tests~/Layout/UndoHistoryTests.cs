using Worktown.Sim.Layout;
using Xunit;

namespace Worktown.Sim.Tests.Layout
{
    /// <summary>Spec 062b AC2: a player's undo history keeps the last 10 actions, newest first.</summary>
    public class UndoHistoryTests
    {
        [Fact]
        public void KeepsTheLastTen_NewestFirst()
        {
            var history = new UndoHistory();
            for (int i = 1; i <= 12; i++) history.Push(LayoutEdit.Move(i, 0, 0, 0));
            Assert.Equal(UndoHistory.Depth, history.Count);
            Assert.True(history.TryPeek(out LayoutEdit top));
            Assert.Equal(12, top.PieceId);
            for (int i = 12; i >= 3; i--)
            {
                Assert.True(history.TryPop(out LayoutEdit edit));
                Assert.Equal(i, edit.PieceId);
            }
            Assert.False(history.TryPop(out _)); // 1 and 2 fell off
            Assert.Equal(0, history.Count);
        }

        [Fact]
        public void Clear_AndNull_LeaveNothing()
        {
            var history = new UndoHistory();
            history.Push(null!);
            Assert.Equal(0, history.Count);
            history.Push(LayoutEdit.Store(3));
            history.Clear();
            Assert.False(history.TryPeek(out _));
        }
    }
}
