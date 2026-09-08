#if VALHEIM_RUNTIME
using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Jotunn.Entities;
using Jotunn.Managers;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

internal sealed class DuelConsoleCommand : ConsoleCommand
{
    private readonly string commandName;
    private readonly Func<ParsedDuelCommand, string> handler;

    public DuelConsoleCommand(string commandName, Func<ParsedDuelCommand, string> handler)
    {
        this.commandName = commandName;
        this.handler = handler;
    }

    public override string Name => commandName;
    public override string Help => commandName == "duel"
        ? "Request or manage a HolmgangDuelMod duel."
        : "HolmgangDuelMod administrator test commands.";
    public override bool IsNetwork => true;

    public override void Run(string[] args, Terminal context)
    {
        var commandLine = "/" + commandName + (args.Length == 0 ? string.Empty : " " + string.Join(" ", args));
        var parsed = DuelCommandParser.Parse(commandLine);
        context?.AddString(handler(parsed));
    }
}

internal sealed class DuelCommandRegistration
{
    private readonly Func<ParsedDuelCommand, string> handler;

    public DuelCommandRegistration(Func<ParsedDuelCommand, string> handler)
    {
        this.handler = handler;
    }

    public void Register()
    {
        CommandManager.Instance.AddConsoleCommand(new DuelConsoleCommand("duel", handler));
        CommandManager.Instance.AddConsoleCommand(new DuelConsoleCommand("dueltest", handler));
    }
}
#endif
