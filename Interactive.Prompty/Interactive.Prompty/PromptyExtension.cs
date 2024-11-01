using Microsoft.DotNet.Interactive;

namespace Interactive.Prompty;

public class PromptyExtension
{
    public static Task LoadExtensionAsync(CompositeKernel compositeKernel, CancellationToken cancellationToken = default)
    {
        compositeKernel.AddConnectDirective(new ConnectPromptyKernelDirective());
        compositeKernel.AddConnectDirective(new ConnectPromptyOrchestratorKernelDirective());

        return Task.CompletedTask;
    }
}