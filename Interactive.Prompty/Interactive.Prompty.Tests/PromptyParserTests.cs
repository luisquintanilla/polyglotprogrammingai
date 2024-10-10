using Microsoft.CodeAnalysis.Scripting;
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
        var result = await csharpKernel.SendAsync(submitCode);
        result = await csharpKernel.SendAsync(new SubmitCode(secondSubmission));

        // get assemblys from csharpKernel
        var scripts = new Queue<Script>([csharpKernel.ScriptState.Script]);
        var typeSet = new HashSet<string>();
        var topLevelFunctionSet = new HashSet<string>();
        while (scripts.Count > 0)
        {
            var script = scripts.Dequeue();
            if (script.Previous != null)
            {
                scripts.Enqueue(script.Previous);
            }
            using var memoryStream = new MemoryStream();
            var emitResult = script.GetCompilation().Emit(memoryStream);
            var assembly = Assembly.Load(memoryStream.ToArray());

            // find all methods with [KernelFunction] attribute using reflection
            var methodInfos = assembly.GetTypes()
                .Where(t => typeSet.Add(CreatePluginName(t)))
                .SelectMany(t => t.GetMethods())
                .Where(m => m.GetCustomAttribute<KernelFunctionAttribute>() != null)
                .ToList();

            var kernelFunctions = methodInfos
                .GroupBy(m => CreatePluginName(m.DeclaringType!))
                .Select(
                    g =>
                    {
                        var isTopLevel = g.Key.StartsWith("Submission_");
                        return new
                        {
                            PluginName = g.Key,
                            Methods = g
                            .Where(m =>
                            {
                                if (isTopLevel)
                                {
                                    return topLevelFunctionSet.Add(m.Name);
                                }
                                else
                                {
                                    return true;
                                }
                            })
                            .Select(m => KernelFunctionFactory.CreateFromMethod(m, m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType!, args: [])))
                            .ToArray()
                        };
                    });

            foreach (var kernelFunction in kernelFunctions.Where(g => g.Methods.Length > 0))
            {
                var kernelPlugin = KernelPluginFactory.CreateFromFunctions(pluginName: kernelFunction.PluginName, kernelFunction.Methods);
                kernel.Plugins.Add(kernelPlugin);
            }


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
    }

    /// <summary>Creates a name for a plugin based on its type name.</summary>
    private static string CreatePluginName(Type type)
    {
        string name = type.Name;
        if (type.IsGenericType)
        {
            // Simple representation of generic arguments, without recurring into their generics
            var builder = new StringBuilder();
            AppendWithoutArity(builder, name);

            Type[] genericArgs = type.GetGenericArguments();
            for (int i = 0; i < genericArgs.Length; i++)
            {
                builder.Append('_');
                AppendWithoutArity(builder, genericArgs[i].Name);
            }

            name = builder.ToString();

            static void AppendWithoutArity(StringBuilder builder, string name)
            {
                int tickPos = name.IndexOf('`');
                if (tickPos >= 0)
                {
                    builder.Append(name, 0, tickPos);
                }
                else
                {
                    builder.Append(name);
                }
            }
        }

        // Replace invalid characters

        name = Regex.Replace(name, "[^0-9A-Za-z_]", "_");

        return name;
    }
}
