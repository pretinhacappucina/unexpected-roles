using AmongUs.GameOptions;
using UnityEngine;

namespace EHR.Roles;

// Recruiter: impostor com base de Shapeshifter.
// Ao usar a metamorfose em alguém, o Recruiter NÃO se transforma:
// o jogador escolhido é recrutado e vira Impostor.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Recruiter : RoleBase
{
    public static bool On;

    private static OptionItem RecruitCooldown;

    private float LastRecruitTime;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5690 a 5692 estão livres (procure por "5690" no projeto).
        Options.SetupRoleOptions(5690, TabGroup.ImpostorRoles, CustomRoles.Recruiter);

        RecruitCooldown = new FloatOptionItem(5692, "Recruiter.RecruitCooldown", new(1f, 120f, 1f), 30f, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Recruiter]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        LastRecruitTime = -9999f;
    }

    // O botão de metamorfose vira o botão de recrutar: cooldown de 30s (padrão) e duração mínima.
    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.ShapeshifterCooldown = RecruitCooldown.GetFloat();
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    // Retornar false cancela a metamorfose visual do Recruiter.
    public override bool OnShapeshift(PlayerControl shapeshifter, PlayerControl target, bool shapeshifting)
    {
        // Se por algum motivo a metamorfose aconteceu, deixa ele voltar ao normal.
        if (!shapeshifting) return true;

        if (shapeshifter == null || target == null) return false;
        if (target.PlayerId == shapeshifter.PlayerId) return false;
        if (!target.IsAlive()) return false;
        if (target.Is(CustomRoleTypes.Impostor)) return false; // já é impostor

        // Garante o cooldown mesmo com a metamorfose cancelada
        float now = Time.realtimeSinceStartup;
        if (now - LastRecruitTime < RecruitCooldown.GetFloat()) return false;

        LastRecruitTime = now;

        // O jogador escolhido vira Impostor
        target.RpcSetCustomRole(CustomRoles.Impostor);

        target.MarkDirtySettings();
        shapeshifter.MarkDirtySettings();

        return false;
    }
}