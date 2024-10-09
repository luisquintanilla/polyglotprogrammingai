using Microsoft.DotNet.Interactive.Commands;

namespace Interactive.Prompty;

public class ConnectPromptyOrchestratorKernel : ConnectKernelCommand
{
    public ConnectPromptyOrchestratorKernel(string connectedKernelName) : base(connectedKernelName)
    {

    }

    public string  PromptyKernelName { get; set; }

    public string AzureOpenAiEndpoint { get; set; }
    public string AzureOpenAiDeploymentName { get; set; }
    public string AzureOpenAiApiKey { get; set; }
}