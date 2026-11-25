namespace EscapeFromBorderlands.Client;

// Instance-seeded once by RarityResolver. Zero means the bonus was not rolled.
// Reductions multiply the remaining cost/duration; Aim and Search are speed bonuses.
internal struct EyewearRoll
{
    internal float Concussion, Flash, Sway, Aim, Arm, Recoil, Hip, MovingSway, Search;
}
