using Framework.Constants;
using Framework.IO;
using System;

namespace Game.Chat.Commands
{
    partial class SICommands
    {
        static bool? SelfControlArgument(string argument) => (argument ?? "").Trim().ToLowerInvariant() switch
        {
            "" => null,
            "on" => true,
            "off" => false,
            _ => throw new ArgumentException("Use .si me [on|off]. With no argument, it toggles SI control of your own character.")
        };

        [Command("me", Permission)]
        static bool Me(CommandHandler h, StringArguments arguments) => Run(h, () =>
        {
            var player = h.GetSession()?.GetPlayer()
                ?? throw new InvalidOperationException(".si me requires your logged-in character.");
            bool? enable = SelfControlArgument(arguments.GetString());
            if (enable != false && !player.IsSelfSIControlled && !Enabled(h)) return;
            player.RequestSelfSIControl(enable);
            h.SendSysMessage("SI self-control request queued. .si me toggles control; .si me off returns it to you.");
        });
    }
}
