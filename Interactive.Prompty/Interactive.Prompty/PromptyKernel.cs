

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
        // Store the submitted code in the _promptyCode field
        _promptyCode = command.Code;
        
        // Initialize a new dictionary to store values
        _values = new();

        // Check if the submitted code is not empty or whitespace
        if (!string.IsNullOrWhiteSpace(_promptyCode))
        {
            // Parse the submitted code to extract PromptyMetadata
            var promptyMetadata = PromptyParser.Parse<PromptyMetadata>(_promptyCode);

            // If the parsed metadata contains sample values
            if (promptyMetadata?.Sample is not null)
            {
                // Iterate through each key-value pair in the sample
                foreach (var (key, sampleValue) in promptyMetadata.Sample)
                {
                    // Store each sample value in the _values dictionary
                    _values[key] = sampleValue;
                }
            }
        }

        // Store the entire submitted code as a "configuration" value
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
        // Try to retrieve the value associated with the command's name from the _values dictionary
        if (_values.TryGetValue(command.Name, out var value))
        {
            // If the value is found, create a new ValueProduced object
            var valueProduced = new ValueProduced(
                command: command,  // The original command that requested the value
                value: value,      // The retrieved value
                name: command.Name, // The name of the requested value
                formattedValue: FormattedValue.CreateSingleFromObject(
                    value,
                    command.MimeType ?? PlainTextFormatter.MimeType  // Use the specified MIME type or default to plain text
                )
            );

            // Publish the ValueProduced event to the kernel invocation context
            context.Publish(valueProduced);
        }
        // If the value is not found in the dictionary, no action is taken
        return Task.CompletedTask;
    }

    Task IKernelCommandHandler<RequestValueInfos>.HandleAsync(RequestValueInfos command, KernelInvocationContext context)
    {
        var valueInfosProduced = new ValueInfosProduced(command: command, valueInfos: [
            .._values.Select(kvp => new KernelValueInfo(kvp.Key, FormattedValue.CreateSingleFromObject(kvp.Value, command.MimeType ?? PlainTextSummaryFormatter.MimeType)))
        ]);

        context.Publish(valueInfosProduced);
        return Task.CompletedTask;
    }
} 