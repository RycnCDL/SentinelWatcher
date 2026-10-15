using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace SentinelWatcher.Api.Tests;

/// <summary>
/// Guards the security model: the API accepts no query text, ARM path or Resource Graph query
/// from the browser. The only inputs are the workspace resource ID (resolved against the
/// allow-list) and the Sentinel Health trend window in days (1 to 30). A new parameter makes
/// this test fail, so it has to be reviewed and added here on purpose.
/// </summary>
public class EndpointInputSurfaceTests
{
    private static readonly string[] AllowedParameters = { "workspace", "days" };

    private static IEnumerable<MethodInfo> Actions() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    [Fact]
    public void ThereAreActionsToCheck()
    {
        Assert.True(Actions().Count() >= 7);
    }

    [Fact]
    public void Actions_AcceptOnlyWorkspaceAndDays()
    {
        foreach (var action in Actions())
        {
            foreach (var parameter in action.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken)) continue;

                Assert.True(AllowedParameters.Contains(parameter.Name),
                    $"{action.DeclaringType!.Name}.{action.Name} accepts '{parameter.Name}'.");
                Assert.Null(parameter.GetCustomAttribute<FromBodyAttribute>());
                Assert.Null(parameter.GetCustomAttribute<FromRouteAttribute>());
            }
        }
    }

    [Fact]
    public void Parameters_HaveSimpleTypes()
    {
        foreach (var parameter in Actions().SelectMany(a => a.GetParameters()))
        {
            if (parameter.Name == "workspace") Assert.Equal(typeof(string), parameter.ParameterType);
            if (parameter.Name == "days") Assert.Equal(typeof(int), parameter.ParameterType);
        }
    }

    [Fact]
    public void Routes_HaveNoTemplateParameters()
    {
        foreach (var action in Actions())
        {
            foreach (var attribute in action.GetCustomAttributes<HttpMethodAttribute>())
                Assert.DoesNotContain("{", attribute.Template ?? string.Empty);

            var controllerRoute = action.DeclaringType!.GetCustomAttribute<RouteAttribute>();
            Assert.DoesNotContain("{", (controllerRoute?.Template ?? string.Empty).Replace("[controller]", string.Empty));
        }
    }
}
