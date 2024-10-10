using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Connection;

namespace Interactive.Prompty;

public class ConnectPromptyKernelDirective : ConnectKernelDirective<ConnectPromptyKernel>
{
    public ConnectPromptyKernelDirective() : base("prompty", "add a new prompt kernel")
    {
       
    }

    public override Task<IEnumerable<Kernel>> ConnectKernelsAsync(ConnectPromptyKernel connectCommand, KernelInvocationContext context)
    {
        var kernelName = connectCommand.ConnectedKernelName;
        var kernel = new PromptyKernel(kernelName);
        kernel.UseValueSharing();
      
        return Task.FromResult<IEnumerable<Kernel>>([kernel]);
    }
}