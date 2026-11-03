using Companion.Api;
using Companion.Api.Data;

// The server as it runs in Docker, on Unraid and in development. The Windows program (src/PlatinVault.Desktop) builds the
// same app through CompanionHost inside its own process.
// "--backup-now" and "--restore <file>" run a database command instead of the server (see DatabaseCommands).
var (command, file, rest) = DatabaseCommands.Parse(args);
var builder = CompanionHost.CreateBuilder(new CompanionHostOptions { Args = rest });
if (command is not null)
{
    await using var tool = builder.Build();
    return await DatabaseCommands.RunAsync(command, file, tool.Services, Console.Out);
}

WebApplication app;
try
{
    app = await CompanionHost.BuildAsync(builder);
}
catch (DatabaseRefusedException)
{
    // The database belongs to a newer PlatinVault or is damaged: the server has logged the explanation in German and English and
    // changed nothing. Exit with an error code instead of a stack trace.
    return 3;
}
app.Run();
return 0;
