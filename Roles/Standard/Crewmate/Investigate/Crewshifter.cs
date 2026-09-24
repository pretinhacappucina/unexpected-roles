using System;
using System.Collections.Generic;
using EHR.Modules;
using Hazel;
using UnityEngine;
using static EHR.Translator;

namespace EHR.Roles;

// Crewshifter: crewmate com base de estrutura do Prosecutor (Judge).
// Durante a reunião, ela pode "julgar" uma pessoa específica (mesmo botão/clique do Judge).
// Em vez de julgar/matar, 10 segundos depois que a reunião ACABA, ela copia a aparência
// da pessoa que foi julgada (cor, chapéu, skin, viseira e pet, igual ao Devourer.SetSkin).
//
// IMPORTANTE: esta role usa um canal de RPC próprio (CustomRPC.Crewshifter) para funcionar
// quando quem usa a habilidade NÃO é o host. Isso exige registrar essa entrada no enum
// CustomRPC do projeto (veja o passo a passo).
public class Crewshifter : RoleBase
{
    private const int Id = 5810;
    private static List<byte> PlayerIdList = [];

    private static OptionItem MarkLimitPerMeeting;
    private static OptionItem CopyDelay;

    // Quem cada Crewshifter marcou na última reunião (limpa depois de copiar)
    private static Dictionary<byte, byte> PendingTarget = [];
    private static Dictionary<byte, int> MeetingUseLimit = [];

    public override bool IsEnable => PlayerIdList.Count > 0;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.CrewmateRoles, CustomRoles.Crewshifter);

        MarkLimitPerMeeting = new FloatOptionItem(Id + 10, "Crewshifter.MarkLimitPerMeeting", new(1f, 5f, 1f), 1f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Crewshifter])
            .SetValueFormat(OptionFormat.Times);

        // Pedido: 10 segundos depois que a reunião acaba
        CopyDelay = new FloatOptionItem(Id + 11, "Crewshifter.CopyDelay", new(1f, 60f, 1f), 10f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Crewshifter])
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void Init()
    {
        PlayerIdList = [];
        PendingTarget = [];
        MeetingUseLimit = [];
    }

    public override void Add(byte playerId)
    {
        PlayerIdList.Add(playerId);
        MeetingUseLimit[playerId] = MarkLimitPerMeeting.GetInt();
    }

    public override void Remove(byte playerId)
    {
        PlayerIdList.Remove(playerId);
        PendingTarget.Remove(playerId);
    }

    // Reseta o limite de marcações a cada reunião, igual ao Prosecutor faz com o limite de julgamentos.
    public override void OnReportDeadBody()
    {
        byte[] list = [.. PlayerIdList];
        foreach (byte pid in list) MeetingUseLimit[pid] = MarkLimitPerMeeting.GetInt();
    }

    // Ponto de entrada, chamado pelo clique no botão (host) ou pela RPC (não-host).
    public static bool MarkMsg(PlayerControl pc, byte targetId, bool isUI = false)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInGame || !pc || !pc.Is(CustomRoles.Crewshifter)) return false;

        if (!pc.IsAlive())
        {
            Utils.SendMessage(GetString("JudgeDead"), pc.PlayerId, importance: MessageImportance.Low);
            return true;
        }

        if (pc.PlayerId == targetId) return true; // não marca a si mesma

        PlayerControl target = Utils.GetPlayerById(targetId);

        if (target == null || !target.IsAlive())
        {
            Utils.SendMessage(GetString("TrialNull"), pc.PlayerId, importance: MessageImportance.Low);
            return true;
        }

        if (!MeetingUseLimit.TryGetValue(pc.PlayerId, out int left) || left < 1)
        {
            string msg = GetString("Crewshifter.LimitReached");

            if (!isUI) Utils.SendMessage(msg, pc.PlayerId);
            else pc.ShowPopUp(msg);

            return true;
        }

        MeetingUseLimit[pc.PlayerId] = left - 1;
        PendingTarget[pc.PlayerId] = targetId;

        string confirm = string.Format(GetString("Crewshifter.Marked"), target.GetRealName());

        if (!isUI) Utils.SendMessage(confirm, pc.PlayerId);
        else pc.ShowPopUp(confirm);

        return true;
    }

    // Chamado quando a reunião termina (mesmo gancho que o Trex e o Sleeper usam pra reaplicar o nome).
    public override void AfterMeetingTasks()
    {
        foreach (byte pid in PlayerIdList)
        {
            if (!PendingTarget.TryGetValue(pid, out byte targetId)) continue;

            PendingTarget.Remove(pid);

            LateTask.New(() => CopyAppearance(pid, targetId), CopyDelay.GetFloat(), "Crewshifter Copy");
        }
    }

    private static void CopyAppearance(byte selfId, byte targetId)
    {
        PlayerControl self = Utils.GetPlayerById(selfId);
        PlayerControl target = Utils.GetPlayerById(targetId);

        if (!self || !self.IsAlive() || !target || !target.IsAlive()) return;
        if (!GameStates.IsInTask) return;

        var outfit = new NetworkedPlayerInfo.PlayerOutfit().Set(
            "",
            target.Data.DefaultOutfit.ColorId,
            target.Data.DefaultOutfit.HatId,
            target.Data.DefaultOutfit.SkinId,
            target.Data.DefaultOutfit.VisorId,
            target.Data.DefaultOutfit.NamePlateId,
            target.Data.DefaultOutfit.PetId);

        SetSkin(self, outfit);

        self.Notify(CustomRoles.Crewshifter.ColoredTextByRole(GetString("CrewshifterCopied")));
    }

    // Mesma lógica do Devourer.SetSkin: troca cor, chapéu, skin, viseira e pet, com RPC para todo mundo ver.
    private static void SetSkin(PlayerControl target, NetworkedPlayerInfo.PlayerOutfit outfit)
    {
        var sender = CustomRpcSender.Create($"Crewshifter.RpcSetSkin({target.Data.PlayerName})", SendOption.Reliable);

        target.SetColor(outfit.ColorId);

        sender.AutoStartRpc(target.NetId, RpcCalls.SetColor)
            .Write(target.Data.NetId)
            .Write((byte)outfit.ColorId)
            .EndRpc();

        target.SetHat(outfit.HatId, outfit.ColorId);
        target.Data.DefaultOutfit.HatSequenceId += 10;

        sender.AutoStartRpc(target.NetId, RpcCalls.SetHatStr)
            .Write(outfit.HatId)
            .Write(target.GetNextRpcSequenceId(RpcCalls.SetHatStr))
            .EndRpc();

        target.SetSkin(outfit.SkinId, outfit.ColorId);
        target.Data.DefaultOutfit.SkinSequenceId += 10;

        sender.AutoStartRpc(target.NetId, RpcCalls.SetSkinStr)
            .Write(outfit.SkinId)
            .Write(target.GetNextRpcSequenceId(RpcCalls.SetSkinStr))
            .EndRpc();

        target.SetVisor(outfit.VisorId, outfit.ColorId);
        target.Data.DefaultOutfit.VisorSequenceId += 10;

        sender.AutoStartRpc(target.NetId, RpcCalls.SetVisorStr)
            .Write(outfit.VisorId)
            .Write(target.GetNextRpcSequenceId(RpcCalls.SetVisorStr))
            .EndRpc();

        target.SetPet(outfit.PetId);
        target.Data.DefaultOutfit.PetSequenceId += 10;

        sender.AutoStartRpc(target.NetId, RpcCalls.SetPetStr)
            .Write(outfit.PetId)
            .Write(target.GetNextRpcSequenceId(RpcCalls.SetPetStr))
            .EndRpc();

        sender.SendMessage();
    }

    // Compatibilidade com o gancho genérico de "julgar" durante a reunião, igual ao Prosecutor faz.
    public override bool OnJudge(PlayerControl pc, PlayerControl target)
    {
        if (Starspawn.IsDayBreak) return false;
        return MarkMsg(pc, target.PlayerId);
    }

    public override void OnMeetingShapeshift(PlayerControl shapeshifter, PlayerControl target)
    {
        OnJudge(shapeshifter, target);
    }

    // ---------- Botão próprio no mapa de votação, igual ao Prosecutor ----------

    private static void SendRPC(byte playerId)
    {
        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.Crewshifter, SendOption.Reliable, AmongUsClient.Instance.HostId);
        writer.Write(playerId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public static void ReceiveRPC(MessageReader reader, PlayerControl pc)
    {
        byte targetId = reader.ReadByte();
        MarkMsg(pc, targetId, true);
    }

    private static void CrewshifterOnClick(byte playerId)
    {
        PlayerControl pc = Utils.GetPlayerById(playerId);
        if (pc == null || !pc.IsAlive() || !GameStates.IsVoting || Starspawn.IsDayBreak) return;

        if (AmongUsClient.Instance.AmHost)
            MarkMsg(PlayerControl.LocalPlayer, playerId, true);
        else
            SendRPC(playerId);
    }

    private static void CreateCrewshifterButton(MeetingHud __instance)
    {
        foreach (PlayerVoteArea pva in __instance.playerStates)
        {
            PlayerControl pc = Utils.GetPlayerById(pva.PlayerId);
            if (!pc || !pc.IsAlive()) continue;

            GameObject template = pva.Buttons.transform.Find("CancelButton").gameObject;
            GameObject targetBox = Object.Instantiate(template, pva.transform);
            targetBox.name = "CrewshifterButton";
            targetBox.transform.localPosition = new(-0.35f, 0.03f, -1.31f);
            var renderer = targetBox.GetComponent<SpriteRenderer>();
            renderer.sprite = CustomButton.Get("CrewshifterIcon");
            var button = targetBox.GetComponent<PassiveButton>();
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener((Action)(() => CrewshifterOnClick(pva.PlayerId)));
        }
    }

    public static class StartMeetingPatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            if (PlayerControl.LocalPlayer.Is(CustomRoles.Crewshifter) && PlayerControl.LocalPlayer.IsAlive())
                CreateCrewshifterButton(__instance);
        }
    }
}