using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Connection;
using Microsoft.DotNet.Interactive.Directives;

namespace Interactive.Prompty;

public class ConnectPromptyOrchestratorKernelDirective : ConnectKernelDirective<ConnectPromptyOrchestratorKernel>
{
    public ConnectPromptyOrchestratorKernelDirective() : base("promptyOrchestrator", "it does things")
    {
        var kernelDirectiveParameter = new KernelDirectiveParameter("--prompty-kernel-name", "the prompty kernel to bind to the orchestrator");

        kernelDirectiveParameter.AddCompletions(context =>
        {
            List<string> availableKernels = [];
            Kernel.Root.VisitSubkernelsAndSelf(k =>
            {
                if( k is PromptyKernel promptyKernel)
                {
                    availableKernels.Add(promptyKernel.Name);
                }
            });
            return availableKernels;
        });
        Parameters.Add(kernelDirectiveParameter);

        Parameters.Add(new KernelDirectiveParameter("--azure-openai-endpoint") { Required = true});
        Parameters.Add(new KernelDirectiveParameter("--azure-openai-apikey") { Required = true });
        Parameters.Add(new KernelDirectiveParameter("--azure-openai-deployment-name") { Required = true });
    }


    public override Task<IEnumerable<Kernel>> ConnectKernelsAsync(ConnectPromptyOrchestratorKernel connectCommand, KernelInvocationContext context)
    {
        var kernel = new PromptyOrchestratorKernel(connectCommand.ConnectedKernelName, connectCommand.PromptyKernelName, connectCommand.AzureOpenAiApiKey, connectCommand.AzureOpenAiEndpoint, connectCommand.AzureOpenAiDeploymentName);
        kernel.UseValueSharing();
        return Task.FromResult<IEnumerable<Kernel>>([kernel]);
    }
}