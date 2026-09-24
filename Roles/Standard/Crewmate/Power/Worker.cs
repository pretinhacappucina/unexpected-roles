using System.Collections;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace EHR.Roles;

// Worker: crewmate com base de Engineer (para poder entrar em dutos).
// Quando entra em um duto ou acaricia o pet, completa tasks de tripulantes aleatórios.
// A quantidade de tasks completadas é igual ao número de tasks que ELE mesmo já fez.
// Ex.: fez 3 tasks -> cada vez que entrar no duto, 3 tasks de outros tripulantes são completadas.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Worker : RoleBase
{
    public static bool On;

    private static OptionItem ActionCooldown;
    private static OptionItem MaxTasksPerUse;

    private byte WorkerId;
    private int CompletedTasks;
    private float LastUseTime;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5740 a 5743 estão livres (procure por "5740" no projeto).
        Options.SetupRoleOptions(5740, TabGroup.CrewmateRoles, CustomRoles.Worker);

        ActionCooldown = new FloatOptionItem(5742, "Worker.ActionCooldown", new(1f, 120f, 1f), 15f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Worker]);

        // 0 = sem limite (usa todas as tasks que ele já fez)
        MaxTasksPerUse = new IntegerOptionItem(5743, "Worker.MaxTasksPerUse", new(0, 30, 1), 0, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Worker]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        WorkerId = playerId;
        CompletedTasks = 0;
        LastUseTime = -9999f;
    }

    // Base de Engineer: o botão de duto fica disponível, com o mesmo cooldown do pet.
    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.EngineerCooldown = ActionCooldown.GetFloat();
        AURoleOptions.EngineerInVentMaxTime = 1f;
    }

    // Conta as tasks que o próprio Worker completou
    public override void OnTaskComplete(PlayerControl pc, int completedTaskCount, int totalTaskCount)
    {
        if (pc == null || pc.PlayerId != WorkerId) return;

        CompletedTasks = completedTaskCount;
    }

    public override void OnEnterVent(PlayerControl pc, Vent vent)
    {
        Work(pc);
    }

    public override void OnPet(PlayerControl pc)
    {
        Work(pc);
    }

    private void Work(PlayerControl pc)
    {
        if (pc == null || !pc.IsAlive() || !GameStates.IsInTask) return;

        int amount = CompletedTasks;
        int max = MaxTasksPerUse.GetInt();
        if (max > 0) amount = Mathf.Min(amount, max);

        if (amount <= 0) return;

        // Cooldown também vale para o pet (evita completar tudo acariciando várias vezes)
        float now = Time.realtimeSinceStartup;
        if (now - LastUseTime < ActionCooldown.GetFloat()) return;

        // Junta todas as tasks pendentes dos outros tripulantes vivos
        var pool = new List<(PlayerControl Player, uint TaskId)>();

        foreach (PlayerControl target in PlayerControl.AllPlayerControls)
        {
            if (!target || target.PlayerId == pc.PlayerId || !target.IsAlive()) continue;
            if (!target.Is(CustomRoleTypes.Crewmate)) continue;
            if (target.Data == null || target.Data.Tasks == null) continue;

            for (int i = 0; i < target.Data.Tasks.Count; i++)
            {
                var task = target.Data.Tasks[i];
                if (!task.Complete) pool.Add((target, task.Id));
            }
        }

        if (pool.Count == 0) return;

        LastUseTime = now;

        // Sorteia as tasks
        var picks = new List<(PlayerControl Player, uint TaskId)>();

        for (int i = 0; i < amount && pool.Count > 0; i++)
        {
            int index = IRandom.Instance.Next(0, pool.Count);
            picks.Add(pool[index]);
            pool.RemoveAt(index);
        }

        Main.Instance.StartCoroutine(CompleteTasks(picks));
    }

    // Completa uma por vez, com um pequeno intervalo, para não sobrecarregar a rede
    private IEnumerator CompleteTasks(List<(PlayerControl Player, uint TaskId)> picks)
    {
        foreach ((PlayerControl player, uint taskId) in picks)
        {
            if (player && player.IsAlive() && GameStates.IsInTask)
                player.RpcCompleteTask(taskId);

            yield return new WaitForSecondsRealtime(0.15f);
        }
    }
}