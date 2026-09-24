using AmongUs.GameOptions;
using UnityEngine;

namespace EHR.Roles;

// Teleporter: impostor com base de Shapeshifter.
// Ao usar a metamorfose em alguém, o Teleporter NÃO se transforma:
// ele é teleportado até a posição do jogador escolhido.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Teleporter : RoleBase
{
    public static bool On;

    private static OptionItem TeleportCooldown;

    private float LastTeleportTime;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5700 a 5702 estão livres (procure por "5700" no projeto).
        Options.SetupRoleOptions(5700, TabGroup.ImpostorRoles, CustomRoles.Teleporter);

        TeleportCooldown = new FloatOptionItem(5702, "Teleporter.TeleportCooldown", new(1f, 120f, 1f), 30f, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Teleporter]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        LastTeleportTime = -9999f;
    }

    // O botão de metamorfose vira o botão de teleporte: cooldown configurável e duração mínima.
    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.ShapeshifterCooldown = TeleportCooldown.GetFloat();
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    // Retornar false cancela a metamorfose visual do Teleporter.
    public override bool OnShapeshift(PlayerControl shapeshifter, PlayerControl target, bool shapeshifting)
    {
        // Se por algum motivo a metamorfose aconteceu, deixa ele voltar ao normal.
        if (!shapeshifting) return true;

        if (shapeshifter == null || target == null) return false;
        if (target.PlayerId == shapeshifter.PlayerId) return false;
        if (!shapeshifter.IsAlive() || !target.IsAlive()) return false;

        // Garante o cooldown mesmo com a metamorfose cancelada
        float now = Time.realtimeSinceStartup;
        if (now - LastTeleportTime < TeleportCooldown.GetFloat()) return false;

        LastTeleportTime = now;

        // Teleporta o Teleporter até a posição do jogador escolhido
        shapeshifter.TP(target.Pos(), log: false);

        return false;
    }
}