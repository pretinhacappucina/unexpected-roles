using UnityEngine;

namespace EHR.Roles;

// Sleeper: crewmate que dorme a partida toda.
//  - Todos sabem que ele é o Sleeper (texto "Sleeper zZz" ao lado do nome).
//  - Ele não consegue se mover (velocidade mínima).
//  - Ninguém consegue matá-lo.
//  - Completa todas as tasks de uma vez assim que a partida começa.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Sleeper : RoleBase
{
    public static bool On;

    private int Count;
    private byte SleeperId;
    private bool TasksDone;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // ID: confira se 5680 está livre (procure por "5680" no projeto).
        Options.SetupRoleOptions(5680, TabGroup.CrewmateRoles, CustomRoles.Sleeper);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        SleeperId = playerId;
        Count = 0;
        TasksDone = false;
    }

    public override void OnFixedUpdate(PlayerControl pc)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Count++ < 5) return;

        Count = 0;

        if (!pc.IsAlive() || !GameStates.IsInTask || ExileController.Instance) return;

        // Completa todas as tasks uma única vez, quando a partida já está rolando.
        if (!TasksDone)
        {
            CompleteAllTasks(pc);
            TasksDone = true;
        }

        // Mantém o Sleeper parado: se a velocidade dele mudar, volta para o mínimo.
        if (Mathf.Approximately(Main.AllPlayerSpeed[pc.PlayerId], Main.MinSpeed)) return;

        Main.AllPlayerSpeed[pc.PlayerId] = Main.MinSpeed;
        pc.MarkDirtySettings();
    }

    private static void CompleteAllTasks(PlayerControl pc)
    {
        var tasks = pc.Data?.Tasks;
        if (tasks == null) return;

        // Copia os ids antes, para não mexer na lista enquanto itera.
        var ids = new System.Collections.Generic.List<uint>();
        foreach (var task in tasks)
            if (!task.Complete)
                ids.Add(task.Id);

        foreach (uint id in ids)
            pc.RpcCompleteTask(id);
    }

    // Ninguém consegue matar o Sleeper.
    public override bool OnCheckMurderAsTarget(PlayerControl killer, PlayerControl target)
    {
        return false;
    }

    // Todos veem que ele é o Sleeper.
    public override string GetSuffix(PlayerControl seer, PlayerControl target, bool hud = false, bool meeting = false)
    {
        if (target == null || target.PlayerId != SleeperId) return string.Empty;

        return $"<size=80%><color=#9fa8da>{Translator.GetString("Sleeper")} zZz</color></size>";
    }
}