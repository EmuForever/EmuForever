using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI;

public static class SIProfessionProcurement
{
    // A craftable reagent belongs to its recipe chain. Failure to make it must
    // not silently turn a rare world drop of the finished component into a farm.
    public static bool ResolveMaterial<T>(IReadOnlyList<T> makers, Func<T, bool> make, Func<bool> rawSource) =>
        makers.Count != 0 ? makers.Any(make) : rawSource();

    // Finish checking owned/vendor/AH options across the candidate recipes
    // before any one recipe is allowed to commit the bot to gathering.
    public static bool TryRecipes<T>(IReadOnlyList<T> recipes, Func<T, bool, bool> resolve)
    {
        foreach (bool allowGather in new[] { false, true })
            foreach (T recipe in recipes)
                if (resolve(recipe, allowGather)) return true;
        return false;
    }
}

public sealed class SIProfessionRecipePath
{
    readonly HashSet<int> _spells = new();
    public bool Enter(int spell) => _spells.Count < 8 && _spells.Add(spell);
    public void Leave(int spell) => _spells.Remove(spell);
}
