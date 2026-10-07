#nullable enable annotations

using WeaponImporter.EditorTools.Core.Analysis;

namespace WeaponImporter.EditorTools.Core.Graph;

/// <summary>Parameter names of generated weapon graphs (Facepunch first-person conventions where they exist).</summary>
public static class WeaponParams
{
    public const string Attack = "b_attack";
    public const string AttackDry = "b_attack_dry";
    public const string Empty = "b_empty";
    public const string Reload = "b_reload";
    public const string DeploySkip = "b_deploy_skip";
    public const string Holster = "b_holster";
    public const string Inspect = "b_inspect";
    public const string Deploy = "b_deploy";
    public const string Sprint = "b_sprint";
    public const string MoveBob = "move_bob";
    public const string Ironsights = "ironsights";
    public const string FireMode = "b_firemode";
    public const string Pump = "b_pump";
    public const string Melee = "b_melee";
    public const string Jump = "b_jump";
    public const string Attack2 = "b_attack2";
    public const string Block = "b_block";
    public const string Use = "b_use";
    public const string Throw = "b_throw";
    /// <summary>Which attack plays next when the attack has several clips (1-based, cycled by Weapon Viewmodel).</summary>
    public const string AttackVariant = "attack_variant";
    public const string SpeedReload = "speed_reload";
    public const string SpeedDeploy = "speed_deploy";
    public const string SpeedIronsights = "speed_ironsights";

    /// <summary>Event tag names (graph tags, dispatched to OnAnimTagEvent).</summary>
    public const string TagHolsterFinished = "holster_finished";
    public const string TagReloadIncrement = "reload_increment";
    public const string TagAttackDiscouraged = "attack_discouraged";
}
