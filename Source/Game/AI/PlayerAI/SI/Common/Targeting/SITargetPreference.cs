using System;
namespace Game.AI
{
    public sealed class SITargetPreference
    {
        public ulong Seed { get; }
        public SITargetPreference(ulong character) => Seed = Mix(character + 0x9E3779B97F4A7C15UL);

        public static ulong Mix(ulong value)
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        public float Sample(ulong salt) => (Mix(Seed ^ salt) >> 40) / 16777216f;
        public float TargetCost(ulong spawn, int quest, float distance) =>
            distance * (.8f + Sample(spawn ^ ((ulong)(uint)quest << 32)) * .4f) + Sample(spawn + 17) * 4;
    }
}
