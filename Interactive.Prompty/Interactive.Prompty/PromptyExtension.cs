﻿using Microsoft.DotNet.Interactive;

namespace Interactive.Prompty;

public class PromptyExtension
{
    public static Task LoadExtensionAsync(CompositeKernel compositeKernel, CancellationToken cancellationToken = default)
    {
        compositeKernel.AddKernelConnector(new ConnectPromptyKernelDirective());
        compositeKernel.AddKernelConnector(new ConnectPromptyOrchestratorKernelDirective());

        return Task.CompletedTask;
    }
}