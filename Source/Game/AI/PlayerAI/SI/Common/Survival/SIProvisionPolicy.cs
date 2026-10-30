using System;
using Framework.Constants;

namespace Game.AI;

public static class SIProvisionPolicy
{
    public static int Target(Class playerClass, bool drink, int ordinary, int mageFood, int mageDrink) =>
        ordinary <= 0 ? 0 : playerClass == Class.Mage ? Math.Max(ordinary, drink ? mageDrink : mageFood) : ordinary;
    public static bool Low(int have, int target) => target > 0 && have < Math.Max(1, target / 2);
    public static float HealthThreshold(Class playerClass) => 90;
    public static float ManaThreshold(Class playerClass) => playerClass == Class.Mage ? 75 : 60;
    public static float RecoveredHealth(Class playerClass) => playerClass == Class.Mage ? 95 : 90;
    public static float RecoveredMana(Class playerClass) => playerClass == Class.Mage ? 95 : 60;
    public static bool FoodOrDrink(ItemClass itemClass, ItemSubClassConsumable subclass) =>
        itemClass == ItemClass.Consumable && subclass == ItemSubClassConsumable.FoodDrink;
}

public static class SIRecoveryConsumables
{
    public static (bool Health, bool Mana) Needs(float health, float mana, bool usesMana, bool healthAura, bool manaAura, float threshold=50) =>
        Needs(health, mana, usesMana, healthAura, manaAura, threshold, threshold);
    public static (bool Health, bool Mana) Needs(float health, float mana, bool usesMana, bool healthAura, bool manaAura, float healthThreshold, float manaThreshold) =>
        (health < healthThreshold && !healthAura, usesMana && mana < manaThreshold && !manaAura);
}
