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
using Kernel = Microsoft.DotNet.Interactive.Kernel;
using SKernel = Microsoft.SemanticKernel.Kernel;
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

            result =
                await Root.SendAsync(new RequestKernelInfo(_promptyKernel), context.CancellationToken);


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

        _values["input"] = command.Code;

        KernelArguments args = new(_values);

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

        return Task.CompletedTask;
    }
}