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

        // Request the configuration from the prompty kernel
        var result =
            await Root.SendAsync(new RequestValue("configuration", mimeType: PlainTextFormatter.MimeType,
                _promptyKernel), context.CancellationToken);

        // Extract the configuration value
        var value = result.Events.OfType<ValueProduced>().Single();
        var promptyCode = value.FormattedValue.Value;

        // Check if the prompty code has changed
        if (promptyCode != _promptyCode)
        {
            // Update the stored prompty code
            _promptyCode = promptyCode;

            // Create a new kernel function from the updated prompty code
            _kernelFunction = _kernel.CreateFunctionFromPrompty(_promptyCode);

            // Create a new kernel plugin with the updated function
            var promptyKernelPlugin = KernelPluginFactory.CreateFromFunctions(
                pluginName: "prompties",
                [_kernelFunction]);

            // Add the new plugin to the kernel
            _kernel.Plugins.Add(promptyKernelPlugin);

            // Request value infos from the prompty kernel
            result =
                await Root.SendAsync(new RequestValueInfos(_promptyKernel), context.CancellationToken);

            // Extract and store values from the prompty kernel
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

        // Find the C# kernel
        var csharpKernel = Root.FindKernelByName("csharp") as CSharpKernel;

        if (csharpKernel is not null)
        {
            // Clear all existing plugins from the kernel
            // This ensures we start with a clean slate
            _kernel.Plugins.Clear();

            // Generate new plugins from the C# kernel
            // This creates plugin representations of functions defined in the C# kernel
            var plugins = GeneratePluginFromKernel(csharpKernel);

            // Iterate through each generated plugin
            foreach (var plugin in plugins)
            {
                // Add each new plugin to the kernel
                // This populates the kernel with fresh plugins derived from the C# kernel
                _kernel.Plugins.Add(plugin);
            }
            // The result is that the kernel now has an updated set of plugins
            // reflecting the current state of the C# kernel
        }

        // Store the input code
        _values["input"] = command.Code;

        // Get the chat completion service
        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        // Set up kernel arguments with Azure OpenAI settings
        KernelArguments args = new(new AzureOpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
        });

        // Add stored values to the kernel arguments
        foreach (var argValue in _values)
        {
            args.Add(argValue.Key, argValue.Value);
        }

        // Prepare for streaming output
        StringBuilder fullContent = new();
        var displayThing = context.Display(fullContent.ToString(), [PlainTextFormatter.MimeType]);

        // Stream the kernel function output
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

        var pluginValues = _kernel.Plugins.SelectMany(p => p)
            .Select(p => new KernelValueInfo(p.Name, new FormattedValue(PlainTextFormatter.MimeType, p.Description)));

        var valueInfosProduced = new ValueInfosProduced(
    command: command,
    valueInfos: values.Concat(pluginValues).ToArray());

        context.Publish(valueInfosProduced);

        return Task.CompletedTask;
    }


    /// <summary>
    /// Generates a collection of KernelPlugins from a CSharpKernel.
    /// </summary>
    /// <param name="csharpKernel">The CSharpKernel to generate plugins from.</param>
    /// <returns>An IEnumerable of KernelPlugin objects.</returns>
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

            // Compile the script to an in-memory assembly
            using var memoryStream = new MemoryStream();
            var result = script.GetCompilation().Emit(memoryStream);
            if (!result.Success)
            {
                continue;
            }

            var assembly = Assembly.Load(memoryStream.ToArray());

            // Find all methods with [KernelFunction] attribute using reflection
            var methodInfos = assembly.GetTypes()
                .Where(t => typeSet.Add(CreatePluginName(t)))
                .SelectMany(t => t.GetMethods())
                .Where(m => m.GetCustomAttribute<KernelFunctionAttribute>() != null)
                .ToList();

            // Group methods by plugin name and create KernelFunctions
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

            // Create KernelPlugins from the grouped functions
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

                /// <summary>
                /// Appends the name without arity information to the StringBuilder.
                /// </summary>
                /// <param name="builder">The StringBuilder to append to.</param>
                /// <param name="name">The name to append.</param>
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

            // Replace invalid characters with underscores
            name = Regex.Replace(name, "[^0-9A-Za-z_]", "_");

            return name;
        }
    }
}