using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace EHR.Roles;

// BomberMan: impostor com base de Phantom.
// Ao usar a habilidade de desaparecer, ele NÃO some:
// todos os jogadores num raio ao redor dele morrem.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class BomberMan : RoleBase
{
    public static bool On;

    private static OptionItem GasCooldown;
    private static OptionItem GasRadius;
    private static OptionItem KillImpostors;

    private float LastUseTime;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5720 a 5724 estão livres (procure por "5720" no projeto).
        Options.SetupRoleOptions(5720, TabGroup.ImpostorRoles, CustomRoles.BomberMan);

        GasCooldown = new FloatOptionItem(5722, "BomberMan.GasCooldown", new(1f, 120f, 1f), 30f, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.BomberMan]);

        GasRadius = new FloatOptionItem(5723, "BomberMan.GasRadius", new(1f, 15f, 0.5f), 5f, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.BomberMan]);

        KillImpostors = new BooleanOptionItem(5724, "BomberMan.KillImpostors", false, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.BomberMan]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        LastUseTime = -9999f;
    }

    // O botão de desaparecer vira o botão do gás: cooldown configurável e duração mínima.
    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.PhantomCooldown = GasCooldown.GetFloat();
        AURoleOptions.PhantomDuration = 1f;
    }

    // Retornar false cancela o desaparecimento: o BomberMan continua visível.
    public override bool OnVanish(PlayerControl pc)
    {
        if (pc == null || !pc.IsAlive() || !GameStates.IsInTask) return false;

        // Garante o cooldown mesmo com o desaparecimento cancelado
        float now = Time.realtimeSinceStartup;
        if (now - LastUseTime < GasCooldown.GetFloat()) return false;

        LastUseTime = now;

        Vector2 pos = pc.Pos();
        float radius = GasRadius.GetFloat();
        bool killImpostors = KillImpostors.GetBool();

        // Primeiro junta os alvos, depois mata (evita mexer na lista durante o loop)
        var victims = new List<PlayerControl>();

        foreach (PlayerControl target in PlayerControl.AllPlayerControls)
        {
            if (!target || target.PlayerId == pc.PlayerId || !target.IsAlive()) continue;
            if (!killImpostors && target.Is(CustomRoleTypes.Impostor)) continue;
            if (!FastVector2.DistanceWithinRange(pos, target.Pos(), radius)) continue;

            victims.Add(target);
        }

        foreach (PlayerControl victim in victims)
            pc.RpcCheckAndMurder(victim);

        return false;
    }
}