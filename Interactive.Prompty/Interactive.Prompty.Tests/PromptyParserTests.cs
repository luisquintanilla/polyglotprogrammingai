using Microsoft.CodeAnalysis.Scripting;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.CSharp;
using Microsoft.DotNet.Interactive.PackageManagement;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kernel = Microsoft.SemanticKernel.Kernel;

namespace Interactive.Prompty.Tests;

public class PromptyParserTests
{
    [Fact]
    public void Parse_WhenGivenValidPrompty_ReturnsExpectedObject()
    {
        // Arrange
        var prompty = File.ReadAllText("basic.prompty");

        // Act
        var result = PromptyParser.Parse<PromptyMetadata>(prompty);
        var sample = result.Sample as IDictionary<string, object>;

        // Assert
        Assert.Equal("Seth", sample!["firstName"]);
    }

    [Fact]
    public async Task CreatesKernelFunctionsFromCSharpKernel()
    {

        var csharpKernel = new CSharpKernel();
        csharpKernel.UseNugetDirective((k, resolvedPackageReference) =>
        {
            k.AddAssemblyReferences(resolvedPackageReference
                .SelectMany(r => r.AssemblyPaths));
            return Task.CompletedTask;
        });

        var initializeCode = """
            #r "nuget: Microsoft.SemanticKernel, 1.22.0"
            using Microsoft.SemanticKernel;
            using Microsoft.SemanticKernel.ChatCompletion;
            using System.ComponentModel;
                    
            [KernelFunction]
            [Description("Get the weather for a city and state")]
            public static Task<string> GetWeather(
                [Description("The city to get the weather for")] string city,
                [Description("The state to get the weather for")] string state)
            {
                return Task.FromResult($"The weather in {city}, {state} is 72 degrees");
            }
            """;

        var secondSubmission = """
            [KernelFunction]
            [Description("Get the weather for a city and state")]
            public static Task<string> GetWeather(
                [Description("The city to get the weather for")] string city,
                [Description("The state to get the weather for")] string state)
            {
                return Task.FromResult($"The weather in {city}, {state} is 95 degrees");
            }
            """;

        var submitCode = new SubmitCode(initializeCode);
        await csharpKernel.SendAsync(submitCode);
        await csharpKernel.SendAsync(new SubmitCode(secondSubmission));

        var extractedPlugins = PromptyOrchestratorKernel.GeneratePluginFromKernel(csharpKernel);

        extractedPlugins.SelectMany(p => p.Select(f => f.Name)).Should().BeEquivalentTo(["GetWeather"]);
    }

    [Fact]
    public async Task WhateverAsync()
    {
        var azureOpenAiDeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOY_NAME")!;
        var azureOpenAiEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!;
        var azureOpenAiApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!;
        var kernel = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: azureOpenAiDeploymentName,
            endpoint: azureOpenAiEndpoint,
            apiKey: azureOpenAiApiKey
            ).Build();

        var csharpKernel = new CSharpKernel();
        csharpKernel.UseNugetDirective((k, resolvedPackageReference) =>
        {
            k.AddAssemblyReferences(resolvedPackageReference
                .SelectMany(r => r.AssemblyPaths));
            return Task.CompletedTask;
        });

        var initializeCode = """
            #r "nuget: Microsoft.SemanticKernel, 1.22.0"
            using Microsoft.SemanticKernel;
            using Microsoft.SemanticKernel.ChatCompletion;
            using System.ComponentModel;
                    
            [KernelFunction]
            [Description("Get the weather for a city and state")]
            public static Task<string> GetWeather(
                [Description("The city to get the weather for")] string city,
                [Description("The state to get the weather for")] string state)
            {
                return Task.FromResult($"The weather in {city}, {state} is 72 degrees");
            }
            """;

        var secondSubmission = """
            [KernelFunction]
            [Description("Get the weather for a city and state")]
            public static Task<string> GetWeather(
                [Description("The city to get the weather for")] string city,
                [Description("The state to get the weather for")] string state)
            {
                return Task.FromResult($"The weather in {city}, {state} is 95 degrees");
            }
            """;

        var submitCode = new SubmitCode(initializeCode);
        await csharpKernel.SendAsync(submitCode);
        await csharpKernel.SendAsync(new SubmitCode(secondSubmission));

        var extractedPlugins = PromptyOrchestratorKernel.GeneratePluginFromKernel(csharpKernel);

        foreach (var extractedPlugin in extractedPlugins)
        {
            kernel.Plugins.Add(extractedPlugin);
        }
      
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory("You are a helpful AI assistant");
        chatHistory.AddUserMessage("Hi, what's the weather in new york");

        var chatMessage = await chatService.GetChatMessageContentAsync(chatHistory,
            kernel: kernel,
            executionSettings: new AzureOpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            });

        chatMessage.Content.Should().Contain("95 degrees");
    }

}
