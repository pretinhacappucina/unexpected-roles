using System;
using System.Collections.Generic;
using EHR.Modules;
using Hazel;
using UnityEngine;
using static EHR.Translator;

namespace EHR.Roles;

// Peacemaker: crewmate com base de estrutura do Prosecutor (Judge).
// Durante a reunião, ela pode usar o martelo do Judge (igual ao Donator) numa pessoa.
// Em vez de julgar/matar:
//   - Se o alvo for Impostor, ele vira um tripulante comum (perde a role e o time).
//   - Se o alvo for tripulante com uma role especial, ele perde a role e vira tripulante comum.
//   - Se o alvo já for um tripulante comum, nada acontece (o uso não é gasto).
//
// IMPORTANTE: esta role usa um canal de RPC próprio (CustomRPC.Peacemaker) para funcionar
// quando quem usa a habilidade NÃO é o host. Precisa ser registrado no enum CustomRPC
// e no despacho central de RPCs, do mesmo jeito que o Donator e o Crewshifter.
public class Peacemaker : RoleBase
{
    private const int Id = 5820;
    private static List<byte> PlayerIdList = [];

    private static OptionItem UseLimitPerMeeting;

    private static Dictionary<byte, int> MeetingUseLimit = [];

    public override bool IsEnable => PlayerIdList.Count > 0;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.Peacemaker);

        UseLimitPerMeeting = new FloatOptionItem(Id + 10, "Peacemaker.UseLimitPerMeeting", new(1f, 5f, 1f), 1f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Peacemaker])
            .SetValueFormat(OptionFormat.Times);
    }

    public override void Init()
    {
        PlayerIdList = [];
        MeetingUseLimit = [];
    }

    public override void Add(byte playerId)
    {
        PlayerIdList.Add(playerId);
        MeetingUseLimit[playerId] = UseLimitPerMeeting.GetInt();
    }

    public override void Remove(byte playerId)
    {
        PlayerIdList.Remove(playerId);
    }

    // Reseta o limite de usos a cada reunião, igual ao Prosecutor faz com o limite de julgamentos.
    public override void OnReportDeadBody()
    {
        byte[] list = [.. PlayerIdList];
        foreach (byte pid in list) MeetingUseLimit[pid] = UseLimitPerMeeting.GetInt();
    }

    // Ponto de entrada, chamado pelo clique no botão (host) ou pela RPC (não-host).
    public static bool PacifyMsg(PlayerControl pc, byte targetId, bool isUI = false)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame || !pc || !pc.Is(CustomRoles.Peacemaker)) return false;

        if (!pc.IsAlive())
        {
            Utils.SendMessage(GetString("JudgeDead"), pc.PlayerId, importance: MessageImportance.Low);
            return true;
        }

        if (pc.PlayerId == targetId) return true; // não usa em si mesma

        PlayerControl target = Utils.GetPlayerById(targetId);

        if (target == null || !target.IsAlive())
        {
            Utils.SendMessage(GetString("TrialNull"), pc.PlayerId, importance: MessageImportance.Low);
            return true;
        }

        if (target.GetCustomRole() == CustomRoles.Crewmate)
        {
            string alreadyMsg = GetString("Peacemaker.AlreadyNormal");

            if (!isUI) Utils.SendMessage(alreadyMsg, pc.PlayerId);
            else pc.ShowPopUp(alreadyMsg);

            return true; // já é normal, não gasta o uso
        }

        if (!MeetingUseLimit.TryGetValue(pc.PlayerId, out int left) || left < 1)
        {
            string limitMsg = GetString("Peacemaker.LimitReached");

            if (!isUI) Utils.SendMessage(limitMsg, pc.PlayerId);
            else pc.ShowPopUp(limitMsg);

            return true;
        }

        MeetingUseLimit[pc.PlayerId] = left - 1;

        string name = target.GetRealName();

        LateTask.New(() =>
        {
            if (!target) return;

            target.RpcSetCustomRole(CustomRoles.Crewmate);

            Utils.SendMessage(string.Format(GetString("Peacemaker.Pacified"), name), 255, CustomRoles.Peacemaker.ColoredTextByRole(GetString("Peacemaker")), importance: MessageImportance.High);
        }, 0.2f, "Peacemaker Pacify");

        return true;
    }

    // Compatibilidade com o gancho genérico de "julgar" durante a reunião, igual ao Prosecutor faz.
    public override bool OnJudge(PlayerControl pc, PlayerControl target)
    {
        if (Starspawn.IsDayBreak) return false;
        return PacifyMsg(pc, target.PlayerId);
    }

    public override void OnMeetingShapeshift(PlayerControl shapeshifter, PlayerControl target)
    {
        OnJudge(shapeshifter, target);
    }

    // ---------- Botão próprio no mapa de votação, igual ao Prosecutor/Donator ----------

    private static void SendRPC(byte playerId)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.Peacemaker, SendOption.Reliable, AmongUsClient.Instance.HostId);
        writer.Write(playerId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void ReceiveRPC(MessageReader reader, PlayerControl pc)
    {
        byte targetId = reader.ReadByte();
        PacifyMsg(pc, targetId, true);
    }

    private static void PeacemakerOnClick(byte playerId)
    {
        PlayerControl pc = Utils.GetPlayerById(playerId);
        if (pc == null || !pc.IsAlive() || !GameStates.IsVoting || Starspawn.IsDayBreak) return;

        if (AmongUsClient.Instance.AmHost)
            PacifyMsg(PlayerControl.LocalPlayer, playerId, true);
        else
            SendRPC(playerId);
    }

    private static void CreatePeacemakerButton(MeetingHud __instance)
    {
        foreach (PlayerVoteArea pva in __instance.playerStates)
        {
            PlayerControl pc = Utils.GetPlayerById(pva.PlayerId);
            if (!pc || !pc.IsAlive()) continue;

            GameObject template = pva.Buttons.transform.Find("CancelButton").gameObject;
            GameObject targetBox = Object.Instantiate(template, pva.transform);
            targetBox.name = "PeacemakerButton";
            targetBox.transform.localPosition = new(-0.35f, 0.03f, -1.31f);
            var renderer = targetBox.GetComponent<SpriteRenderer>();
            renderer.sprite = CustomButton.Get("PeacemakerIcon");
            var button = targetBox.GetComponent<PassiveButton>();
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener((Action)(() => PeacemakerOnClick(pva.PlayerId)));
        }
    }

    public static class StartMeetingPatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            if (PlayerControl.LocalPlayer.Is(CustomRoles.Peacemaker) && PlayerControl.LocalPlayer.IsAlive())
                CreatePeacemakerButton(__instance);
        }
    }

    public override void ManipulateGameEndCheckCrew(PlayerState playerState, out bool keepGameGoing, out int countsAs)
    {
        if (playerState.IsDead)
        {
            base.ManipulateGameEndCheckCrew(playerState, out keepGameGoing, out countsAs);
            return;
        }

        keepGameGoing = true;
        countsAs = 1;
    }
}