using UnityEngine;

[CreateAssetMenu(fileName = "Card", menuName = "Cards/Card")]
public class Card : ScriptableObject
{
    public string cardName;
    public CardStat[] stats;
    public CardAbility[] abilities;
}

[System.Serializable]
public struct CardStat
{
    [TextArea]
    public string description;
    public string keyword;
    public CardStatPolarity polarity;
    public CardStatTarget target;
    public CardStatModifierMode modifierMode;
    public float amount;
}

[System.Serializable]
public struct CardAbility
{
    [TextArea]
    public string description;
}

public enum CardStatPolarity
{
    Good,
    Bad
}

public enum CardStatTarget
{
    MaxHealth,
    WalkSpeed,
    SprintSpeed,
    SprintDuration,
    JumpPower,
    BulletDamage,
    BulletSpeed,
    BulletGravity,
    BulletBounces,
    FireRate,
    BulletsPerShot,
    MagazineSize,
    StartingBullets,
    ReloadTime,
    CrosshairPointDistance
}

public enum CardStatModifierMode
{
    Flat,
    Percent
}