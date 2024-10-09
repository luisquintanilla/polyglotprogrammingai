using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Microsoft.DotNet.Interactive.ValueSharing;

namespace Interactive.Prompty;

public class PromptyKernel : Kernel,
    IKernelCommandHandler<SubmitCode>,
    IKernelCommandHandler<SendValue>,
    IKernelCommandHandler<RequestValue>,
    IKernelCommandHandler<RequestValueInfos>
{
    private string _promptyCode;

    public PromptyKernel(string name) : base(name)
    {
        KernelInfo.LanguageName = "prompty";
        KernelInfo.Description = "Supports prompty configurations and fluent prompt templates";
    }

    Task IKernelCommandHandler<SubmitCode>.HandleAsync(SubmitCode command, KernelInvocationContext context)
    {
        _promptyCode = command.Code;
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<SendValue>.HandleAsync(SendValue command, KernelInvocationContext context)
    {
        _promptyCode = command.FormattedValue.Value;
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<RequestValue>.HandleAsync(RequestValue command, KernelInvocationContext context)
    {
        if (command.Name == "configuration")
        {
            var valueProduced = new ValueProduced(command: command, value: _promptyCode, name: "configuration", formattedValue: new FormattedValue(PlainTextFormatter.MimeType, _promptyCode));
            context.Publish(valueProduced);
        }
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<RequestValueInfos>.HandleAsync(RequestValueInfos command, KernelInvocationContext context)
    {
        var valueInfosProduced = new ValueInfosProduced(command: command, valueInfos: [
            new KernelValueInfo("configuration", new FormattedValue(PlainTextFormatter.MimeType, _promptyCode))
            ]);

        context.Publish(valueInfosProduced);
        return Task.CompletedTask;
    }
}