

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    private Dictionary<string, object> _values = new();

    public PromptyKernel(string name) : base(name)
    {
        KernelInfo.LanguageName = "prompty";
        KernelInfo.Description = "Supports prompty configurations and fluent prompt templates";
    }

    Task IKernelCommandHandler<SubmitCode>.HandleAsync(SubmitCode command, KernelInvocationContext context)
    {
        _promptyCode = command.Code;
        _values = new();

        if (!string.IsNullOrWhiteSpace(_promptyCode))
        {
            var prompty = PromptyParser.Parse<PromptyMetadata>(_promptyCode);


            if (prompty.Sample is IDictionary<string, object> samples)
            {
                foreach (var (key, sampleValue) in samples)
                {
                    _values[key] = sampleValue;
                }
            }
        }

        _values["configuration"] = _promptyCode;
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<SendValue>.HandleAsync(SendValue command, KernelInvocationContext context)
    {
        _promptyCode = command.FormattedValue.Value;
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<RequestValue>.HandleAsync(RequestValue command, KernelInvocationContext context)
    {
        if (_values.TryGetValue(command.Name, out var value))
        {
            var valueProduced = new ValueProduced(command: command, value: value, name: command.Name, formattedValue: FormattedValue.CreateSingleFromObject(value, command.MimeType));
            context.Publish(valueProduced);
        }
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<RequestValueInfos>.HandleAsync(RequestValueInfos command, KernelInvocationContext context)
    {
        var valueInfosProduced = new ValueInfosProduced(command: command, valueInfos: [
            .._values.Select(kvp => new KernelValueInfo(kvp.Key, FormattedValue.CreateSingleFromObject(kvp.Value, PlainTextSummaryFormatter.MimeType)))
        ]);

        context.Publish(valueInfosProduced);
        return Task.CompletedTask;
    }
} 