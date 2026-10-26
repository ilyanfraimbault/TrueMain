using System.Reflection;
using Data.Logging.Mongo;

namespace TrueMain.UnitTests;

/// <summary>The wording table of the operator-action trail (#2288): every action a writer records has its words.</summary>
public class AuditCatalogTests
{
    private static readonly List<string> Constants = typeof(AuditActions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true, FieldType: var type } && type == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToList();

    [Fact]
    public void EveryActionHasItsWords_AndNoWordsHaveNoAction()
    {
        Assert.NotEmpty(Constants);
        Assert.Equal(Constants.Order(), AuditActions.Labels.Select(pair => pair.Key).Order());
    }

    [Fact]
    public void TheWordsAreDistinctAndSpelledOut()
    {
        var labels = AuditActions.Labels.Select(pair => pair.Value).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
        Assert.All(labels, label => Assert.DoesNotContain("_", label));
    }

    [Theory]
    [InlineData(AuditActions.RenamePersona, "Renamed a known player")]
    [InlineData(AuditActions.SeedAccount, "Asked to seed a main")]
    [InlineData("retired_action", "retired_action")]
    public void LabelWordsAnAction_OrFallsBackToItsName(string action, string expected) =>
        Assert.Equal(expected, AuditActions.Label(action));
}
