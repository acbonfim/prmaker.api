using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using solvace.executionplans.application.Contracts;
using solvace.prform.Controllers;
using Xunit;

namespace solvace.prform.tests;

/// <summary>0077: etapas que sugerem horas e a rota do controller das ações do DevOps.</summary>
public class AssistantTests
{
    [Theory]
    [InlineData("montar-analise", true)]
    [InlineData("corrigir-edv-solvace", true)]
    [InlineData("validar-qa", true)]
    [InlineData("consultar-base", false)]
    [InlineData("pr-edv-solvace", false)]
    public void Default_hours_steps(string step, bool expected) => Assert.Equal(expected, ExecutionAssistantSettings.Default.SuggestsHoursFor(step));

    [Fact]
    public void Azure_controller_keeps_its_route_and_api_attributes()
    {
        var t = typeof(AzureController);
        Assert.NotNull(t.GetCustomAttribute<ApiControllerAttribute>());
        Assert.Equal("api/v{version:apiVersion}/[controller]", t.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Null(typeof(LogHoursRequest).GetCustomAttribute<RouteAttribute>());
    }
}
