namespace BattleNot;

static class DbValue
{
    // Truncates to a column's VARCHAR length.
    public static string Clip(string value, int max)
    {
        value ??= "";
        return value.Length <= max ? value : value[..max];
    }
}
