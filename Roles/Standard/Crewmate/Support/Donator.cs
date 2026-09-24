using System;
using System.Collections.Generic;
using EHR.Modules;
using static EHR.Translator;

namespace EHR.Roles;

// Versão host-only: sem botão de reunião nem CustomRPC.
// A doação só acontece via comando de chat (/dt, /doar) ou usando o voto
// na reunião (OnJudge), igual ao Judge/Prosecutor, mas sem sincronizar nada
// via rede — tudo roda direto no host.
public class Donator : RoleBase
{
    private const int Id = 9301; // TODO: confirme que este ID não colide com nenhuma outra role do seu Options.cs
    private static List<byte> PlayerIdList = [];

    private static OptionItem TargetsPerMeeting;
    private static OptionItem DonationLimitPerGame;
    private static OptionItem AbilityUseLimit;
    private static OptionItem DonatorAbilityUseGainWithEachTaskCompleted;
    private static OptionItem DonatorAbilityChargesWhenFinishedTasks;

    // Quantos alvos DISTINTOS ela já doou nesta reunião (reseta a cada corpo reportado / reunião)
    private static Dictionary<byte, HashSet<byte>> MeetingDonationTargets = [];

    // Quantas doações (uso total da habilidade) ela ainda tem disponíveis na partida
    private static Dictionary<byte, int> TotalUseLimit = [];

    public override bool IsEnable => PlayerIdList.Count > 0;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.Donator);

        // Mesmos valores padrão do Judge/Prosecutor, pra exigir a MESMA quantidade de tasks pra liberar a habilidade
        AbilityUseLimit = new FloatOptionItem(Id + 10, "AbilityUseLimit", new(0f, 30f, 0.5f), 1f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Donator]).SetValueFormat(OptionFormat.Times);

        DonatorAbilityUseGainWithEachTaskCompleted = new FloatOptionItem(Id + 11, "AbilityUseGainWithEachTaskCompleted", new(0f, 5f, 0.05f), 0.3f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Donator]).SetValueFormat(OptionFormat.Times);

        DonatorAbilityChargesWhenFinishedTasks = new FloatOptionItem(Id + 12, "AbilityChargesWhenFinishedTasks", new(0f, 5f, 0.05f), 0.2f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Donator]).SetValueFormat(OptionFormat.Times);

        // Máximo de pessoas DIFERENTES que ela pode ajudar por reunião (pedido: 2)
        TargetsPerMeeting = new IntegerOptionItem(Id + 13, "DonatorTargetsPerMeeting", new(1, 6, 1), 2, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Donator]).SetValueFormat(OptionFormat.Times);

        // Limite total de doações na partida inteira (opcional, evita ela doar infinitamente se acumular muita carga)
        DonationLimitPerGame = new FloatOptionItem(Id + 14, "DonationLimitPerGame", new(0f, 30f, 1f), 5f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Donator]).SetValueFormat(OptionFormat.Times);
    }

    public override void Init()
    {
        PlayerIdList = [];
        MeetingDonationTargets = [];
        TotalUseLimit = [];
    }

    public override void Add(byte playerId)
    {
        PlayerIdList.Add(playerId);
        MeetingDonationTargets[playerId] = [];
        TotalUseLimit[playerId] = DonationLimitPerGame.GetInt();
        playerId.SetAbilityUseLimit(AbilityUseLimit.GetFloat());
    }

    public override void Remove(byte playerId)
    {
        PlayerIdList.Remove(playerId);
    }

    public override void OnReportDeadBody()
    {
        byte[] list = [.. PlayerIdList];
        foreach (byte pid in list) MeetingDonationTargets[pid] = [];
    }

    // isVote = true quando a doação vem de um voto na reunião (OnJudge/OnMeetingShapeshift).
    // Nesse caso, NÃO consumimos a carga de AbilityUseLimit (ganha por tasks concluídas),
    // só o limite total de doações da partida (TotalUseLimit) e o limite de alvos por reunião.
    public static bool DonateMsg(PlayerControl pc, string msg, bool isUI = false, bool isVote = false)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame || !pc || !pc.Is(CustomRoles.Donator)) return false;

        int operate; // 1:ID 2:Doar
        msg = msg.ToLower().TrimStart().TrimEnd();

        if (GuessManager.CheckCommand(ref msg, "id|donatelist|dl编号|玩家编号|玩家id|id列表|玩家列表|列表|所有id|全部id", true))
            operate = 1;
        else if (GuessManager.CheckCommand(ref msg, "dt|doar|donate|doacao|doação", false))
            operate = 2;
        else
            return false;

        if (!pc.IsAlive())
        {
            Utils.SendMessage(GetString("DonatorDead"), pc.PlayerId, importance: MessageImportance.Low);
            return true;
        }

        switch (operate)
        {
            case 1:
                Utils.SendMessage(GuessManager.GetFormatString(), pc.PlayerId);
                break;
            case 2:
                {
                    if (!TryParseTarget(msg, out byte targetId, out string error))
                    {
                        Utils.SendMessage(error, pc.PlayerId, importance: MessageImportance.Low);
                        return true;
                    }

                    PlayerControl target = Utils.GetPlayerById(targetId);

                    if (target == null)
                    {
                        Utils.SendMessage(GetString("DonateNull"), pc.PlayerId, importance: MessageImportance.Low);
                        return true;
                    }

                    if (target.PlayerId == pc.PlayerId)
                    {
                        if (!isUI)
                            Utils.SendMessage(GetString("DonateSelf"), pc.PlayerId, importance: MessageImportance.Low);
                        else
                            pc.ShowPopUp(GetString("DonateSelf"));

                        return true;
                    }

                    // Via voto (isVote): não exige carga de AbilityUseLimit, só doações restantes na partida.
                    // Via comando de chat: exige carga de habilidade E doações restantes, como antes.
                    bool semCargaDeHabilidade = !isVote && pc.GetAbilityUseLimit() < 1;
                    bool semDoacoesRestantes = TotalUseLimit[pc.PlayerId] < 1;

                    if (semCargaDeHabilidade || semDoacoesRestantes)
                    {
                        if (!isUI)
                            Utils.SendMessage(GetString("DonateMax"), pc.PlayerId);
                        else
                            pc.ShowPopUp(GetString("DonateMax"));

                        return true;
                    }

                    HashSet<byte> targetsThisMeeting = MeetingDonationTargets.TryGetValue(pc.PlayerId, out HashSet<byte> set) ? set : MeetingDonationTargets[pc.PlayerId] = [];

                    // Já bateu no limite de pessoas DIFERENTES nesta reunião e o alvo não é uma delas
                    if (targetsThisMeeting.Count >= TargetsPerMeeting.GetInt() && !targetsThisMeeting.Contains(target.PlayerId))
                    {
                        if (!isUI)
                            Utils.SendMessage(GetString("DonateMeetingMax"), pc.PlayerId);
                        else
                            pc.ShowPopUp(GetString("DonateMeetingMax"));

                        return true;
                    }

                    TaskState ts = target.GetTaskState();

                    if (!ts.HasTasks || ts.IsTaskFinished)
                    {
                        if (!isUI)
                            Utils.SendMessage(GetString("DonateNoTasksLeft"), pc.PlayerId, importance: MessageImportance.Low);
                        else
                            pc.ShowPopUp(GetString("DonateNoTasksLeft"));

                        return true;
                    }

                    Logger.Info($"{pc.GetNameWithRole()} donated tasks to {target.GetNameWithRole()}", "Donator");

                    string name = target.GetRealName();

                    // Só consome a carga de habilidade quando NÃO for voto.
                    if (!isVote) pc.RpcRemoveAbilityUse();

                    TotalUseLimit[pc.PlayerId]--;
                    targetsThisMeeting.Add(target.PlayerId);

                    LateTask.New(() =>
                    {
                        CompleteAllTasks(target);

                        LateTask.New(() => Utils.SendMessage(string.Format(GetString("DonateSuccess"), name), 255, CustomRoles.Donator.ColoredTextByRole(GetString("DonateSuccessTitle")), importance: MessageImportance.High), 0.6f, "Donate Msg");
                    }, 0.2f, "Donate Complete Tasks");

                    break;
                }
        }

        return true;
    }

    /// <summary>
    /// Completa todas as tasks restantes de um jogador, sincronizando via RPC vanilla (CompleteTask) para
    /// que clientes vanilla/não-modados também vejam a barra de progresso atualizada.
    /// TODO: se o seu Utils.cs já tiver um helper equivalente (ex: Utils.CompleteTask(PlayerControl)), prefira usá-lo no lugar disto.
    /// </summary>
    private static void CompleteAllTasks(PlayerControl target)
    {
        if (target == null || target.myTasks == null) return;

        for (var i = 0; i < target.myTasks.Count; i++)
        {
            PlayerTask task = target.myTasks[i];
            if (task == null || task.IsComplete) continue;

            if (AmongUsClient.Instance.AmHost)
            {
                Hazel.MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.CompleteTask, Hazel.SendOption.Reliable, -1);
                writer.WritePacked(task.Id);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }

            target.CompleteTask(task.Id);
        }
    }

    private static bool TryParseTarget(string msg, out byte id, out string error)
    {
        if (msg.StartsWith("/")) msg = msg.Replace("/", string.Empty);

        System.Text.RegularExpressions.Regex r = new("\\d+");
        System.Text.RegularExpressions.MatchCollection mc = r.Matches(msg);
        var result = string.Empty;
        for (var i = 0; i < mc.Count; i++) result += mc[i];

        if (int.TryParse(result, out int num))
            id = Convert.ToByte(num);
        else
        {
            id = byte.MaxValue;
            error = GetString("DonateHelp");
            return false;
        }

        PlayerControl target = Utils.GetPlayerById(id);

        if (target == null || !target.IsAlive())
        {
            error = GetString("DonateNull");
            return false;
        }

        error = string.Empty;
        return true;
    }

    // Gancho chamado quando a Donator vota em alguém na reunião (mesmo sistema do Judge/Prosecutor).
    // Isso permite doar tasks votando no alvo, sem precisar de botão nem RPC, e sem gastar carga de habilidade.
    public override bool OnJudge(PlayerControl pc, PlayerControl target)
    {
        if (Starspawn.IsDayBreak) return false;
        DonateMsg(pc, $"/dt {target.PlayerId}", isVote: true);
        return true;
    }

    public override void OnMeetingShapeshift(PlayerControl shapeshifter, PlayerControl target)
    {
        OnJudge(shapeshifter, target);
    }
}