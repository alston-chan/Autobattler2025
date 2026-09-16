using System.Collections.Generic;
using NUnit.Framework;

/// <summary>Archers to the rear, brawlers to the front (<see cref="EnemyMuster"/>).</summary>
public class EnemyMusterTests
{
    private static List<(int column, int row)> Assign(params EnemyMuster.Unit[] units) => EnemyMuster.Assign(units, 4, 3);

    [Test]
    public void AnArcherAuthoredInFrontMovesToTheRearOfItsLane()
    {
        var cells = Assign(new EnemyMuster.Unit(true, 0, 1));
        Assert.AreEqual((3, 1), cells[0]);
    }

    [Test]
    public void ABrawlerKeepsItsAuthoredCell()
    {
        var cells = Assign(new EnemyMuster.Unit(false, 1, 2));
        Assert.AreEqual((1, 2), cells[0]);
    }

    [Test]
    public void AnArcherDisplacesABrawlerAuthoredAtTheRearWhichThenMovesForward()
    {
        var cells = Assign(new EnemyMuster.Unit(false, 3, 0), new EnemyMuster.Unit(true, 0, 0));
        Assert.AreEqual((3, 0), cells[1], "the archer takes the rear of the lane");
        Assert.AreEqual((0, 0), cells[0], "the brawler goes to the front of it");
    }

    [Test]
    public void TwoArchersInOneLaneTakeTheRearOfNeighbouringLanes()
    {
        var cells = Assign(new EnemyMuster.Unit(true, 0, 1), new EnemyMuster.Unit(true, 1, 1));
        Assert.AreEqual((3, 1), cells[0]);
        Assert.AreEqual(3, cells[1].column, "still the rear column");
        Assert.AreNotEqual(cells[0], cells[1]);
        Assert.AreEqual(1, System.Math.Abs(cells[1].row - 1), "the nearest lane");
    }

    [Test]
    public void NoTwoUnitsShareACell()
    {
        var units = new List<EnemyMuster.Unit>();
        for (int i = 0; i < 6; i++) units.Add(new EnemyMuster.Unit(i % 2 == 0, 0, 0));
        var cells = EnemyMuster.Assign(units, 4, 3);
        Assert.AreEqual(6, new HashSet<(int, int)>(cells).Count);
    }

    [Test]
    public void AFullBoardLeavesTheOverflowWhereItWasAuthored()
    {
        var units = new List<EnemyMuster.Unit>();
        for (int i = 0; i < 5; i++) units.Add(new EnemyMuster.Unit(false, 0, 0));
        var cells = EnemyMuster.Assign(units, 2, 2);
        Assert.AreEqual((0, 0), cells[4], "four cells, five units: the fifth stands where it was put");
    }
}
