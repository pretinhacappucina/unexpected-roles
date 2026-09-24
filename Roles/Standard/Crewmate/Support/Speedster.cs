using UnityEngine;

namespace EHR.Roles;

// Role Speedster: só o próprio jogador fica com a velocidade multiplicada (padrão 10x).
// Não usa o nome "Flash" porque o enum CustomRoles já tem um add-on chamado Flash.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Speedster : RoleBase
{
    public static bool On;

    private static OptionItem SpeedMultiplier;

    private int Count;
    private byte SpeedsterId;

    // Multiplicador que está aplicado agora (0 = sem bônus)
    private float AppliedMultiplier;

    // Velocidade que o jogador tinha antes do bônus (para detectar se algo resetou a velocidade)
    private float SpeedBefore;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5670 a 5672 estão livres (procure por "5670" no projeto).
        Options.SetupRoleOptions(5670, TabGroup.CrewmateRoles, CustomRoles.Speedster);

        SpeedMultiplier = new FloatOptionItem(5672, "Speedster.SpeedMultiplier", new(1f, 15f, 0.5f), 10f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Speedster]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        SpeedsterId = playerId;
        Count = 0;
        AppliedMultiplier = 0f;
        SpeedBefore = 0f;
    }

    public override void OnFixedUpdate(PlayerControl pc)
    {
        if (Count++ < 5) return;

        Count = 0;

        // Morreu: tira o bônus
        if (!pc || !pc.IsAlive())
        {
            RemoveBoost();
            return;
        }

        if (!GameStates.IsInTask || ExileController.Instance) return;

        ApplyBoost(pc);
    }

    public override void AfterMeetingTasks()
    {
        PlayerControl pc = SpeedsterId.GetPlayer();

        if (!pc || !pc.IsAlive()) RemoveBoost();
        else ApplyBoost(pc);
    }

    // Aplica o bônus (ou reaplica, se a velocidade foi resetada por algo)
    private void ApplyBoost(PlayerControl pc)
    {
        float multiplier = SpeedMultiplier.GetFloat();
        if (Mathf.Approximately(multiplier, 1f)) return;

        byte id = pc.PlayerId;
        float speed = Main.AllPlayerSpeed[id];

        // Já está com o bônus e ninguém resetou a velocidade
        if (AppliedMultiplier > 0f && !Mathf.Approximately(speed, SpeedBefore)) return;

        SpeedBefore = speed;
        AppliedMultiplier = multiplier;
        Main.AllPlayerSpeed[id] = speed * multiplier;

        pc.MarkDirtySettings();
    }

    private void RemoveBoost()
    {
        if (AppliedMultiplier <= 0f) return;

        Main.AllPlayerSpeed[SpeedsterId] /= AppliedMultiplier;
        AppliedMultiplier = 0f;
        SpeedBefore = 0f;

        PlayerControl pc = SpeedsterId.GetPlayer();
        if (pc) pc.MarkDirtySettings();
    }
}