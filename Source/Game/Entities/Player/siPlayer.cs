using Game.AI;
using System;
using System.Linq;

namespace Game.Entities
{
    // A normal persistent Player whose decisions are owned by worldserver.
    public sealed partial class siPlayer : Player
    {
        internal static string NormalizeStoredName(string name, string surname)
        {
            if (string.IsNullOrWhiteSpace(surname) || string.IsNullOrEmpty(name)) return name;
            string first = name.Trim();
            string suffix = " " + surname.Trim();
            while (first.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) first = first[..^suffix.Length];
            return first.Length is >= 2 and <= 12 && first.All(char.IsAsciiLetter) ? first : name;
        }

        public SIPlayer Controller { get; private set; }
        public string NavigationMode { get; internal set; } = "Not requested";
        public bool NavigationBlocked { get; internal set; }

        public siPlayer(WorldSession session) : base(session)
        {
            if (!session.IsServerControlled)
                throw new ArgumentException("siPlayer requires a server-controlled session.", nameof(session));
        }

        internal void InitializeController()
        {
            LoadQuestHistory();
            GetSession().CompleteSIActiveMoverInitialization();
            Controller = new SIPlayer(this);
            SetAI(Controller);
            Controller.Start();
        }

        public override void Dispose()
        {
            Controller?.Dispose();
            base.Dispose();
        }
    }
}

