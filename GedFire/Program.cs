using GedFire.Cli;

return await CliApplication.CreateDefault(new CommandContext(Console.Out, Console.Error)).RunAsync(args);
