using EMIC_DevAgent.Core.Agents.Base;
using Xunit;

namespace EMIC_DevAgent.Tests;

public class AgentContextTests
{
    [Fact]
    public void AgentContext_DefaultState_HasEmptyCollections()
    {
        var context = new AgentContext();

        Assert.Empty(context.GeneratedFiles);
        Assert.Empty(context.PendingQuestions);
        Assert.Empty(context.ValidationResults);
        Assert.Equal(string.Empty, context.OriginalPrompt);
        Assert.Null(context.Analysis);
        Assert.Null(context.SdkState);
        Assert.Null(context.Plan);
        Assert.Null(context.LastCompilation);
    }

    [Fact]
    public void AgentResult_Success_ReturnsCorrectStatus()
    {
        var result = AgentResult.Success("TestAgent", "Operacion completada");

        Assert.Equal("TestAgent", result.AgentName);
        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal("Operacion completada", result.Message);
    }

    [Fact]
    public void AgentResult_Failure_ReturnsCorrectStatus()
    {
        var result = AgentResult.Failure("TestAgent", "Error en la operacion");

        Assert.Equal(ResultStatus.Failure, result.Status);
        Assert.Equal("Error en la operacion", result.Message);
    }

    [Fact]
    public void AgentResult_NeedsInput_ReturnsCorrectStatus()
    {
        var result = AgentResult.NeedsInput("TestAgent", "Se requiere input del usuario");

        Assert.Equal(ResultStatus.NeedsInput, result.Status);
    }

    [Fact]
    public void PromptAnalysis_CanSetIntent()
    {
        var analysis = new PromptAnalysis
        {
            Intent = IntentType.CreateApi,
            ComponentName = "Temperature",
            Category = "Sensors"
        };

        Assert.Equal(IntentType.CreateApi, analysis.Intent);
        Assert.Equal("Temperature", analysis.ComponentName);
        Assert.Equal("Sensors", analysis.Category);
    }

    [Fact]
    public void AgentContext_Properties_CanStoreArbitraryData()
    {
        var context = new AgentContext();
        context.Properties["key1"] = "value1";
        context.Properties["key2"] = 42;

        Assert.Equal("value1", context.Properties["key1"]);
        Assert.Equal(42, context.Properties["key2"]);
    }
}
