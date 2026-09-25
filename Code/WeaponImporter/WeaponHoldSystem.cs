namespace WeaponImporter;

/// <summary>
/// Reads every <see cref="WeaponHold"/>'s action triggers after components update and before
/// the animgraph evaluates (Stage.UpdateBones, order -1), so auto-reset graph triggers set by
/// game code in OnUpdate are never missed.
/// </summary>
public sealed class WeaponHoldSystem : GameObjectSystem
{
    public WeaponHoldSystem( Scene scene ) : base( scene )
    {
        Listen( Stage.UpdateBones, -1, ReadTriggers, "WeaponHold.ReadTriggers" );
    }

    private void ReadTriggers()
    {
        foreach ( var hold in Scene.GetAllComponents<WeaponHold>() )
        {
            if ( hold.Active )
                hold.ReadTriggers();
        }
    }
}
