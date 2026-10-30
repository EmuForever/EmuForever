using System;
namespace Game.AI.SIMovement;

/// <summary>Stable per-character preferences: target choice jitter, route seed and casual jumps.</summary>
public sealed class SIHumanMovementStyle(ulong character)
{
    public ulong Seed { get; } = Mix(character + 0x9E3779B97F4A7C15UL);
    public static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }

    public float Sample(ulong salt) => (Mix(Seed ^ salt) >> 40) / 16777216f;
    public float TargetCost(ulong spawn, uint quest, float distance) =>
        distance * (.8f + Sample(spawn ^ ((ulong)quest << 32)) * .4f) + Sample(spawn + 17) * 4;

    public int JumpInterval(int sequence) => 35000 + (int)(Sample((ulong)sequence + 101) * 85000);

    public const float JumpVelocity = 7.0f;
    public const float Gravity = 19.291103f;
    public const float JumpDuration = 2 * JumpVelocity / Gravity;
    public static float JumpHeight(float elapsedSeconds) =>
        Math.Max(0, JumpVelocity * elapsedSeconds - .5f * Gravity * elapsedSeconds * elapsedSeconds);
}
