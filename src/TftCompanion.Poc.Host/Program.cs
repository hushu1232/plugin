using TftCompanion.Poc.Core.Storage;
using TftCompanion.Poc.Host;

if (!PocHostOptions.TryCreateForProduction(args, out PocHostOptions? options, out string failureCode))
{
    Console.WriteLine($"TFTPOC_CONFIG_REJECTED:{failureCode}");
    Environment.ExitCode = 1;
    return;
}

await using PocHostFactory host = new(options!, new WindowsStorageFileSystem());
await host.StartAsync();
Console.WriteLine("TFTPOC_READY");

TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;
await host.StopAsync();
