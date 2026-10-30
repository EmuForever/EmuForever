
namespace Game.AI
{
    public enum SIPartyAction { Solo, Errand, Regroup, Stay, Wait }

    public sealed class SIPartyPolicy
    {
        public const float FollowDistance = 8;
        public const float RegroupDistance = 30;
        public const float AssistRadius = 45;
        public bool Returning { get; private set; }
        public void BeginErrand() => Returning = true;

        public SIPartyAction Evaluate(bool hasParty, bool errand, float distance)
        {
            if (!hasParty) { Returning = false; return SIPartyAction.Solo; }
            if (errand) { BeginErrand(); return SIPartyAction.Errand; }
            if (!float.IsFinite(distance)) return SIPartyAction.Wait;
            if (distance > RegroupDistance || Returning && distance > FollowDistance) return SIPartyAction.Regroup;
            Returning = false;
            return SIPartyAction.Stay;
        }
    }
}
