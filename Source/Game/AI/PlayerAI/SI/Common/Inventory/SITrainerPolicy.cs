namespace Game.AI
{
    public static class SITrainerPolicy
    {
        // Availability is data driven: every level, including level one and odd levels, is checked.
        public static bool NeedsQuery(int playerLevel, int queriedLevel) => playerLevel != queriedLevel;
    }
}
