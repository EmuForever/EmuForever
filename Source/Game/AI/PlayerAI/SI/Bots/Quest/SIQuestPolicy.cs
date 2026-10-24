namespace Game.AI
{
    // Yellow quests can have a recommended level up to two above the player. The actual
    // minimum level still applies; a level-one starter must be able to accept Cutting Teeth (2).
    public static class SIQuestPolicy
    {
        public static bool TooLow(int playerLevel, int questLevel) => questLevel <= playerLevel - 5;
        public static bool TooHigh(int playerLevel, int questLevel, int minimumLevel) =>
            minimumLevel > playerLevel || questLevel > playerLevel + 2;
        public static bool Eligible(int playerLevel, int questLevel, int minimumLevel) =>
            !TooLow(playerLevel, questLevel) && !TooHigh(playerLevel, questLevel, minimumLevel);

        // Only distant quest travel can yield to local work. Interactions and local objectives keep ownership.
        public static bool MayYield(SITaskKind kind, float remaining, float localRadius) =>
            kind is SITaskKind.Objective or SITaskKind.TurnIn && remaining > localRadius * 2;
    }
}
