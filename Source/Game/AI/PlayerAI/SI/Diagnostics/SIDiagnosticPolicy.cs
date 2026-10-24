using System.Runtime.CompilerServices;

namespace Game.AI
{
    public static class SIDiagnosticPolicy
    {
        public static bool Important(string type) => type is "trainer_query_failed" or "idle_recovery" or "slow_update" or
            "corpse_recovery_fallback" or "engaged_task_replaced" or "recovery_blocked" or "preparation_supply_failed" or
            "navigation_overlap_recovery" or "navigation_blocked" or "navigation_failed" or "interaction_failed" or "loot_failed" or
            "combat_failed" or "cast_rejected" or "quest_target_blocked" or "quest_task_failed";
    }

    // The compiler skips interpolation arguments altogether for routine events.
    [InterpolatedStringHandler]
    public ref struct SIDiagnosticMessage
    {
        DefaultInterpolatedStringHandler _text;
        public SIDiagnosticMessage(int literalLength, int formattedCount, string type, out bool enabled)
        {
            enabled = SIDiagnosticPolicy.Important(type);
            _text = enabled ? new(literalLength, formattedCount) : default;
        }
        public void AppendLiteral(string value) => _text.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _text.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string format) => _text.AppendFormatted(value, format);
        public string GetText() => _text.ToStringAndClear();
    }
}
