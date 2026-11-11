using System.Net.Sockets;
using System.Reflection;
using FastTowerDefenceRelay;

CommandLine.Command command;
try
{
    command = CommandLine.Parse(args);
}
catch (ArgumentException exception)
{
    await Console.Error.WriteLineAsync($"ftd-relay: {exception.Message}");
    return 1;
}

if (command.Help)
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}
if (command.Version)
{
    var version = typeof(CommandLine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    Console.WriteLine($"ftd-relay {version}");
    return 0;
}

try
{
    using var host = RelayApplication.Build(command.Options!);
    await host.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is SocketException or IOException or ArgumentException)
{
    // Typically a port already in use or an unresolvable listen address.
    await Console.Error.WriteLineAsync($"ftd-relay: {exception.Message}");
    return 1;
}
