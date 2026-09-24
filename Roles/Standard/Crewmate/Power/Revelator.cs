using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace EHR.Roles;

// Revelator: crewmate com base de Engineer.
// Ao entrar em um duto, se houver alguém na mesma sala, uma dessas pessoas é sorteada e
// tem a role revelada para TODOS (texto ao lado do nome). Precisa fazer 2 tasks entre cada uso.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Revelator : RoleBase
{
    public static bool On;

    private static OptionItem TasksRequired;
    private static OptionItem VentCooldown;
    private static OptionItem RevealDuration;

    private byte RevelatorId;
    private int TasksSinceLastUse;
    private bool Ready;

    // Jogador revelado -> tempo (Time.realtimeSinceStartup) até o qual o texto deve aparecer
    private static readonly Dictionary<byte, float> RevealedUntil = [];

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5760 a 5764 estão livres (procure por "5760" no projeto).
        Options.SetupRoleOptions(5760, TabGroup.CrewmateRoles, CustomRoles.Revelator);

        TasksRequired = new IntegerOptionItem(5762, "Revelator.TasksRequired", new(1, 10, 1), 2, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Revelator]);

        VentCooldown = new FloatOptionItem(5763, "Revelator.VentCooldown", new(0f, 60f, 1f), 10f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Revelator]);

        RevealDuration = new FloatOptionItem(5764, "Revelator.RevealDuration", new(3f, 60f, 1f), 8f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Revelator]);
    }

    public override void Init()
    {
        On = false;
        RevealedUntil.Clear();
    }

    public override void Add(byte playerId)
    {
        On = true;
        RevelatorId = playerId;
        TasksSinceLastUse = 0;
        Ready = false; // precisa fazer as tasks antes do primeiro uso também
    }

    // Base de Engineer: o botão de duto fica disponível.
    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.EngineerCooldown = VentCooldown.GetFloat();
        AURoleOptions.EngineerInVentMaxTime = 1f;
    }

    // Conta as tasks do próprio Revelator para liberar o próximo uso
    public override void OnTaskComplete(PlayerControl pc, int completedTaskCount, int totalTaskCount)
    {
        if (pc == null || pc.PlayerId != RevelatorId || Ready) return;

        TasksSinceLastUse++;

        if (TasksSinceLastUse >= TasksRequired.GetInt()) Ready = true;
    }

    public override void OnEnterVent(PlayerControl pc, Vent vent)
    {
        if (pc == null || !pc.IsAlive() || !GameStates.IsInTask) return;
        if (!Ready) return; // ainda não fez as tasks necessárias

        // Junta quem está na mesma sala que o Revelator (fora ele mesmo)
        PlainShipRoom room = pc.GetPlainShipRoom();
        if (room == null) return;

        var candidates = new List<PlayerControl>();

        foreach (PlayerControl target in PlayerControl.AllPlayerControls)
        {
            if (!target || target.PlayerId == pc.PlayerId || !target.IsAlive()) continue;
            if (target.GetPlainShipRoom() == room) candidates.Add(target);
        }

        if (candidates.Count == 0) return; // ninguém na sala: não gasta o uso

        PlayerControl chosen = candidates[IRandom.Instance.Next(0, candidates.Count)];

        RevealedUntil[chosen.PlayerId] = Time.realtimeSinceStartup + RevealDuration.GetFloat();

        // Reseta o progresso: precisa fazer as tasks de novo para revelar outra vez
        Ready = false;
        TasksSinceLastUse = 0;

        Utils.NotifyRoles();
    }

    // Texto com a role, visível para todos enquanto o tempo não acabar.
    public override string GetSuffix(PlayerControl seer, PlayerControl target, bool hud = false, bool meeting = false)
    {
        if (target == null) return string.Empty;
        if (!RevealedUntil.TryGetValue(target.PlayerId, out float until)) return string.Empty;
        if (Time.realtimeSinceStartup >= until) return string.Empty;

        CustomRoles role = target.GetCustomRole();
        string roleName = Translator.GetString(role.ToString());

        return $"<color=#ffca28>({roleName})</color>";
    }
}