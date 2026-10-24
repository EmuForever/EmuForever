using System;
using System.Numerics;
namespace Game.AI.SIMovement;

/// <summary>Stable character preferences with fresh variation for each journey.</summary>
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
    public int PauseMilliseconds(int sequence) => Sample((ulong)sequence + 501) < .22f
        ? 140 + (int)(Sample((ulong)sequence + 701) * 350) : 0;

    public static Vector3 Detour(ulong seed, int sequence, Vector3 from, Vector3 to)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 15) return to;
        ulong random = Mix(seed ^ (ulong)sequence);
        float fraction = .35f + ((random >> 40) / 16777216f) * .3f;
        float offset = ((random & 0xffff) / 65535f - .5f) * Math.Min(9, distance * .2f);
        return new(from.X + dx * fraction - dy / distance * offset,
            from.Y + dy * fraction + dx / distance * offset, from.Z + (to.Z - from.Z) * fraction);
    }

    public const float JumpVelocity = 7.0f;
    public const float Gravity = 19.291103f;
    public const float JumpDuration = 2 * JumpVelocity / Gravity;
    public static float JumpHeight(float elapsedSeconds) =>
        Math.Max(0, JumpVelocity * elapsedSeconds - .5f * Gravity * elapsedSeconds * elapsedSeconds);
}
