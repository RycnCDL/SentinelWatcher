using System.Text.Json;

namespace SentinelWatcher.Api.Tests;

/// <summary>
/// Checks the least-privilege role logic in infra/azuredeploy.json (compiled from infra/main.bicep)
/// without deploying it. CI separately checks that azuredeploy.json matches main.bicep.
/// </summary>
public class InfraTemplateTests
{
    private const string SentinelReader = "8d289c81-5878-46d4-8554-54e1e3d8b5cb";
    private const string MonitoringReader = "43d0d8ad-25c7-4714-9337-8ba259a9fe05";
    private const string LogAnalyticsReader = "73c42c96-874c-492b-b04d-ab87d138a893";

    private static readonly Lazy<(JsonElement Root, string Text)> Template = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "infra", "azuredeploy.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir.FullName, "infra", "azuredeploy.json"));
        return (JsonDocument.Parse(text).RootElement.Clone(), text);
    });

    private static JsonElement WorkspaceAccessModule => Template.Value.Root.GetProperty("resources").EnumerateArray()
        .Single(r => r.GetProperty("type").GetString() == "Microsoft.Resources/deployments");

    private static List<JsonElement> ModuleResources(string type) => WorkspaceAccessModule
        .GetProperty("properties").GetProperty("template").GetProperty("resources").EnumerateArray()
        .Where(r => r.GetProperty("type").GetString() == type)
        .ToList();

    private static string? Condition(JsonElement resource) =>
        resource.TryGetProperty("condition", out var c) ? c.GetString() : null;

    [Fact]
    public void DcrAccess_IsNoneWhenTheDcrPageIsDisabled()
    {
        // enableDcrPage=false -> dcrAccess 'None' -> no custom role and no DCR role assignment.
        Assert.Equal("[if(parameters('enableDcrPage'), parameters('dcrRole'), 'None')]",
            Template.Value.Root.GetProperty("variables").GetProperty("dcrAccess").GetString());
        Assert.Equal("[variables('dcrAccess')]",
            WorkspaceAccessModule.GetProperty("properties").GetProperty("parameters").GetProperty("dcrAccess").GetProperty("value").GetString());

        var definition = Assert.Single(ModuleResources("Microsoft.Authorization/roleDefinitions"));
        Assert.Equal("[equals(parameters('dcrAccess'), 'CustomRole')]", Condition(definition));

        var dcrAssignment = Assert.Single(ModuleResources("Microsoft.Authorization/roleAssignments"), r => Condition(r) is not null);
        Assert.Equal("[not(equals(parameters('dcrAccess'), 'None'))]", Condition(dcrAssignment));
    }

    [Fact]
    public void OnlySentinelReaderIsAlwaysAssigned()
    {
        var assignments = ModuleResources("Microsoft.Authorization/roleAssignments");
        Assert.Equal(2, assignments.Count);

        var unconditional = Assert.Single(assignments, r => Condition(r) is null);
        var role = unconditional.GetProperty("properties").GetProperty("roleDefinitionId").GetString();
        Assert.Contains("sentinelReaderRoleId", role);
        Assert.Contains(SentinelReader, Template.Value.Text);
    }

    [Fact]
    public void CustomRole_GrantsOnlyDataCollectionRuleReadInTheWorkspaceResourceGroup()
    {
        var properties = Assert.Single(ModuleResources("Microsoft.Authorization/roleDefinitions")).GetProperty("properties");
        var permissions = Assert.Single(properties.GetProperty("permissions").EnumerateArray());

        Assert.Equal(new[] { "Microsoft.Insights/dataCollectionRules/read" },
            permissions.GetProperty("actions").EnumerateArray().Select(a => a.GetString()));
        Assert.Empty(permissions.GetProperty("dataActions").EnumerateArray());
        Assert.Equal(new[] { "[resourceGroup().id]" },
            properties.GetProperty("assignableScopes").EnumerateArray().Select(a => a.GetString()));
    }

    [Fact]
    public void MonitoringReaderIsOnlyAFallback_AndLogAnalyticsReaderIsNeverUsed()
    {
        var variables = WorkspaceAccessModule.GetProperty("properties").GetProperty("template").GetProperty("variables");
        Assert.Equal(MonitoringReader, variables.GetProperty("monitoringReaderRoleId").GetString());
        Assert.Contains("if(equals(parameters('dcrAccess'), 'CustomRole')", variables.GetProperty("dcrRoleDefinitionId").GetString());
        Assert.DoesNotContain(LogAnalyticsReader, Template.Value.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAppSettingFollowsTheParameter()
    {
        Assert.Contains("createObject('name', 'Sentinel__EnableDcrPage', 'value', string(parameters('enableDcrPage')))",
            Template.Value.Text);
    }
}
