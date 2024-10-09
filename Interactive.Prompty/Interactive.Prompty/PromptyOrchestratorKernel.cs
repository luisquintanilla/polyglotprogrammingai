using System.Text;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Microsoft.SemanticKernel;
using Kernel = Microsoft.DotNet.Interactive.Kernel;
using SKernel = Microsoft.SemanticKernel.Kernel;
#pragma warning disable SKEXP0040

namespace Interactive.Prompty;

public class PromptyOrchestratorKernel : Kernel,
    IKernelCommandHandler<SendValue>,

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
            apiKey: azureOpenAiApiKey
            ).Build();
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

        }

        _values["input"] = command.Code;

        KernelArguments args = new(_values);

        StringBuilder fullContent = new();
        var displayThing = context.Display(fullContent.ToString(), [PlainTextFormatter.MimeType]);
        await foreach (var content  in _kernelFunction.InvokeStreamingAsync(_kernel, args, context.CancellationToken))
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
}