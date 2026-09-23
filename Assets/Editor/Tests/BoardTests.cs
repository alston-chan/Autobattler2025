using System;
using NUnit.Framework;

/// <summary>
/// The deployment words, pinned on a board of strings (Docs/PositionalKeywords.md). One idea drives
/// all of them — the first unit in a lane, on either side — so most of these are that idea asked
/// from different angles.
/// </summary>
public class BoardTests
{
    // Company (ally side):            Enemy side:
    //   col 2   col 1   col 0            col 0   col 1   col 2
    //   archer          knight    row 2   warden          sniper
    //           mage              row 1
    //   -       -       rogue     row 0   rat     rat     rat
    private static Board<string> Sample()
    {
        var board = new Board<string>();
        board.Place("knight", true, 0, 2);
        board.Place("archer", true, 2, 2);
        board.Place("mage", true, 1, 1);
        board.Place("rogue", true, 0, 0);
        board.Place("warden", false, 0, 2);
        board.Place("sniper", false, 2, 2);
        board.Place("rat1", false, 0, 0);
        board.Place("rat2", false, 1, 0);
        board.Place("rat3", false, 2, 0);
        return board;
    }

    [Test]
    public void FrontAndRearAreTheOuterColumns()
    {
        var board = Sample();
        Assert.That(board.IsFront("knight"), Is.True);
        Assert.That(board.IsRear("archer"), Is.True);
        Assert.That(board.IsFront("mage"), Is.False);
        Assert.That(board.IsRear("mage"), Is.False);
    }

    [Test]
    public void TheFirstInALaneIsExposedAndTheRestAreCovered()
    {
        var board = Sample();
        Assert.That(board.FirstInLane(true, 2), Is.EqualTo("knight"));
        Assert.That(board.IsExposed("knight"), Is.True);
        Assert.That(board.IsCovered("archer"), Is.True, "the archer stands behind the knight");
        Assert.That(board.IsExposed("mage"), Is.True, "nobody stands in front of the mage in its lane");
    }

    [Test]
    public void AcrossIsTheFirstEnemyInTheLaneNotTheMirror()
    {
        var board = Sample();
        // The archer's mirror cell holds the sniper, but the warden stands in front of it.
        Assert.That(board.Across("archer"), Is.EqualTo("warden"), "cover blocks the mirror");
        Assert.That(board.Across("knight"), Is.EqualTo("warden"));
        Assert.That(board.Across("rogue"), Is.EqualTo("rat1"));
        Assert.That(board.Across("mage"), Is.Null, "nothing stands in the enemy's middle lane");
        Assert.That(board.Across("sniper"), Is.EqualTo("knight"), "the same rule from the enemy's side");
    }

    [Test]
    public void BesideIsOrthogonalOnly()
    {
        var board = Sample();
        // The mage at (1,1) touches (0,1), (2,1), (1,0), (1,2) — all empty here.
        Assert.That(board.Beside("mage"), Is.Empty);
        Assert.That(board.IsAlone("mage"), Is.True);
        // The rats sit shoulder to shoulder along row 0.
        Assert.That(board.Beside("rat2"), Is.EquivalentTo(new[] { "rat1", "rat3" }));
        Assert.That(board.IsAlone("rat2"), Is.False);
        // The knight at (0,2) and the rogue at (0,0) are two rows apart — not beside.
        Assert.That(board.Beside("knight"), Is.Empty);
    }

    [Test]
    public void RankAndLaneExcludeSelfAndTheOtherSide()
    {
        var board = Sample();
        Assert.That(board.Rank("knight"), Is.EquivalentTo(new[] { "rogue" }), "same column");
        Assert.That(board.Lane("knight"), Is.EquivalentTo(new[] { "archer" }), "same row");
        Assert.That(board.Lane("rat1"), Is.EquivalentTo(new[] { "rat2", "rat3" }));
        Assert.That(board.Rank("warden"), Is.EquivalentTo(new[] { "rat1" }));
    }

    [Test]
    public void TheWordsReadAsACardLine()
    {
        var board = Sample();
        Assert.That(board.Keywords("knight"), Is.EqualTo("Front · Exposed · Alone"));
        Assert.That(board.Keywords("archer"), Is.EqualTo("Rear · Covered · Alone"));
        // rat2 stands behind rat1 in its lane: column 0 is nearest the company on both sides.
        Assert.That(board.Keywords("rat2"), Is.EqualTo("Covered · Beside 2"));
        Assert.That(board.Keywords("nobody"), Is.Empty);
    }

    [Test]
    public void TheArchetypesReadInTheSameWords()
    {
        var board = Sample();
        // A lone warden in the front: Front, Alone, Exposed. A sniper behind it: Covered.
        Assert.That(board.IsFront("warden") && board.IsAlone("warden") && board.IsExposed("warden"), Is.True);
        Assert.That(board.IsCovered("sniper"), Is.True);
    }

}
