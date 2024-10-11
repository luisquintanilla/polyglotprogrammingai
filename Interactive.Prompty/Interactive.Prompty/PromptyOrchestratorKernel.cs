using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Microsoft.DotNet.Interactive.ValueSharing;
using Microsoft.SemanticKernel;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive.CSharp;
using Kernel = Microsoft.DotNet.Interactive.Kernel;
using SKernel = Microsoft.SemanticKernel.Kernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.CodeAnalysis.Scripting;
using System.Reflection;
using System.Text.RegularExpressions;
#pragma warning disable SKEXP0040

namespace Interactive.Prompty;

public class PromptyOrchestratorKernel : Kernel,
    IKernelCommandHandler<SendValue>,
    IKernelCommandHandler<RequestValueInfos>,
    IKernelCommandHandler<SubmitCode>
{
    private readonly string _promptyKernel;
    private string _promptyCode = string.Empty;
    private KernelFunction _kernelFunction;
    private readonly SKernel _kernel;
    private readonly Dictionary<string, object?> _values = new();

    public PromptyOrchestratorKernel(string name, string promptyKernel, string azureOpenAiApiKey, string azureOpenAiEndpoint, string azureOpenAiDeploymentName) : base(name)
    {
        _promptyKernel = promptyKernel;
        KernelInfo.LanguageName = "prompty";
        KernelInfo.Description = "Supports prompty configurations and fluent prompt templates";
        _kernel = SKernel.CreateBuilder().AddAzureOpenAIChatCompletion(
            deploymentName: azureOpenAiDeploymentName,
            endpoint: azureOpenAiEndpoint,
            apiKey: azureOpenAiApiKey)
            .Build();
    }

    async Task IKernelCommandHandler<SubmitCode>.HandleAsync(SubmitCode command, KernelInvocationContext context)
    {

        var result =
            await Root.SendAsync(new RequestValue("configuration", mimeType: PlainTextFormatter.MimeType,
                _promptyKernel), context.CancellationToken);

        var value = result.Events.OfType<ValueProduced>().Single();

        var promptyCode = value.FormattedValue.Value;


        if (promptyCode != _promptyCode)
        {
            _promptyCode = promptyCode;

            _kernelFunction = _kernel.CreateFunctionFromPrompty(_promptyCode);

            var promptyKernelPlugin = KernelPluginFactory.CreateFromFunctions(
                pluginName: "prompties",
                [_kernelFunction]);

            _kernel.Plugins.Add(promptyKernelPlugin);

            result =
                await Root.SendAsync(new RequestValueInfos(_promptyKernel), context.CancellationToken);


            var valuesToGet = result.Events.OfType<ValueInfosProduced>().SingleOrDefault();
            if (valuesToGet is not null)
            {
                foreach (var valueInfo in valuesToGet.ValueInfos)
                {
                    result =
                        await Root.SendAsync(new RequestValue(valueInfo.Name, mimeType: PlainTextFormatter.MimeType,
                            _promptyKernel), context.CancellationToken);

                    value = result.Events.OfType<ValueProduced>().Single();

                    _values[valueInfo.Name] = value.FormattedValue.Value;
                }
            }

        }

        var csharpKernel = Root.FindKernelByName("csharp") as CSharpKernel;

        if (csharpKernel is not null)
        {
            _kernel.Plugins.Clear();
            var plugins = GeneratePluginFromKernel(csharpKernel);
            foreach (var plugin in plugins)
            {
                _kernel.Plugins.Add(plugin);
            }
        }

        _values["input"] = command.Code;
        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        KernelArguments args = new(new AzureOpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
        });

        foreach (var argValue in _values)
        {
            args.Add(argValue.Key, argValue.Value);
        }

        StringBuilder fullContent = new();
        var displayThing = context.Display(fullContent.ToString(), [PlainTextFormatter.MimeType]);
        await foreach (var content in _kernelFunction.InvokeStreamingAsync(_kernel, args, context.CancellationToken))
        {
            fullContent.Append(content);
            displayThing.Update(fullContent.ToString());
        }

    }

    Task IKernelCommandHandler<SendValue>.HandleAsync(SendValue command, KernelInvocationContext context)
    {
        _values[command.Name] = command.FormattedValue.Value;
        return Task.CompletedTask;
    }

    public Task HandleAsync(RequestValueInfos command, KernelInvocationContext context)
    {
        var values = _values.Select(x => new KernelValueInfo(x.Key, new FormattedValue(PlainTextFormatter.MimeType, x.Value?.ToString() ?? string.Empty)));
        var valueInfosProduced = new ValueInfosProduced(
            command: command,
            valueInfos: values.ToArray());

        context.Publish(valueInfosProduced);

        var pluginValues = _kernel.Plugins.SelectMany(p => p)
            .Select(p => new KernelValueInfo(p.Name, new FormattedValue(PlainTextFormatter.MimeType, p.Description)));

        var pluginValueInfosProduced = new ValueInfosProduced(
            command: command,
            valueInfos: pluginValues.ToArray());

        context.Publish(pluginValueInfosProduced);

        return Task.CompletedTask;
    }

    public static IEnumerable<KernelPlugin> GeneratePluginFromKernel(CSharpKernel csharpKernel)
    {
        List<KernelPlugin> plugins = [];
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
            var result = script.GetCompilation().Emit(memoryStream);
            if (!result.Success)
            {
                continue;
            }

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
                                .Where(m => !isTopLevel || topLevelFunctionSet.Add(m.Name))
                                .Select(m => KernelFunctionFactory.CreateFromMethod(m,
                                    m.IsStatic ? null : Activator.CreateInstance(m.DeclaringType!, args: [])))
                                .ToArray()
                        };
                    });

            foreach (var kernelFunction in kernelFunctions.Where(g => g.Methods.Length > 0))
            {
                var kernelPlugin =
                    KernelPluginFactory.CreateFromFunctions(pluginName: kernelFunction.PluginName,
                        kernelFunction.Methods);
                plugins.Add(kernelPlugin);
            }
        }

        return plugins;

        static string CreatePluginName(Type type)
        {
            var name = type.Name;
            if (type.IsGenericType)
            {
                // Simple representation of generic arguments, without recurring into their generics
                var builder = new StringBuilder();
                AppendWithoutArity(builder, name);

                var genericArgs = type.GetGenericArguments();
                foreach (var t in genericArgs)
                {
                    builder.Append('_');
                    AppendWithoutArity(builder, t.Name);
                }

                name = builder.ToString();

                static void AppendWithoutArity(StringBuilder builder, string name)
                {
                    var tickPos = name.IndexOf('`');
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
}