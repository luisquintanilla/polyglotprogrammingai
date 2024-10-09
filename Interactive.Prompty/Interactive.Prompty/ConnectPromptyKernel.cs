using Microsoft.DotNet.Interactive.Commands;

namespace Interactive.Prompty;

public class ConnectPromptyKernel(string connectedKernelName) : ConnectKernelCommand(connectedKernelName)
{

}