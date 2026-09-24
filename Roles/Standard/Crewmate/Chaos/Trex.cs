using System.Collections;
using System.Collections.Generic;
using EHR.Modules;
using UnityEngine;

namespace EHR.Roles;

// O nome da classe TEM que ser igual ao nome no enum CustomRoles (Trex).
public class Trex : RoleBase
{
    public static bool On;

    private static OptionItem StompRange;
    private static OptionItem StompDuration;
    private static OptionItem StompCooldown;

    private int Count;
    private HashSet<byte> StompedPlayers;
    private Vector2 LastPosition;
    private byte TrexId;

    // Pixel art de T-rex (8 colunas x 6 linhas)
    // G = #4caf50 (verde)   D = #2e7d32 (verde escuro)   L = #c5e1a5 (barriga)
    // K = #000000 (olho)    T = #ffffff (dentes)         . = vazio
    //
    // . . . . G G G G
    // . . . . G K G G
    // . . . . G G T T
    // D . . G G G D .
    // D D G G G L L .
    // . . G G . G G G
    public static string Name => "<voffset=7em><alpha=#00>.</alpha></voffset><size=150%><line-height=97%><cspace=0.16em><#0000>WWWW</color><mark=#4caf50>WWWW</mark>\n<#0000>WWWW</color><mark=#4caf50>W</mark><mark=#000000>W</mark><mark=#4caf50>WW</mark>\n<#0000>WWWW</color><mark=#4caf50>WW</mark><mark=#ffffff>WW</mark>\n<mark=#2e7d32>W</mark><#0000>WW</color><mark=#4caf50>WWW</mark><mark=#2e7d32>W</mark><#0000>W</color>\n<mark=#2e7d32>WW</mark><mark=#4caf50>WWW</mark><mark=#c5e1a5>WW</mark><#0000>W</color>\n<#0000>WW</color><mark=#4caf50>WW</mark><#0000>W</color><mark=#4caf50>WWW</mark>";

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5660 a 5664 estão livres (procure por "5660" no projeto).
        Options.SetupRoleOptions(5660, TabGroup.CrewmateRoles, CustomRoles.Trex);

        StompRange = new FloatOptionItem(5662, "Trex.StompRange", new(0.5f, 5f, 0.1f), 1.5f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Trex]);

        StompDuration = new FloatOptionItem(5663, "Trex.StompDuration", new(0.5f, 10f, 0.5f), 3f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Trex]);

        StompCooldown = new FloatOptionItem(5664, "Trex.StompCooldown", new(0f, 30f, 0.5f), 5f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Trex]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        TrexId = playerId;
        LastPosition = Utils.GetPlayerById(playerId).Pos();
        StompedPlayers = [];
        Count = 0;

        // IMPORTANTE: não tentamos mais aplicar o nome customizado aqui (nem com LateTask
        // fixo, nem esperando a intro terminar). Foi exatamente essa tentativa de aplicar
        // o nome no início da partida que causava o bug do nome virar "1 monte de W" —
        // algo no processo de sincronização logo após a intro acaba derrubando as tags de
        // rich text (<mark=...>, <#0000>, etc.), sobrando só os caracteres crus.
        // O Car real NÃO faz isso: ele só define o nome em AfterMeetingTasks(), depois da
        // primeira reunião, quando o jogo já está totalmente sincronizado e estável. O Trex
        // agora segue exatamente o mesmo padrão.
    }

    // Enquanto o T-rex anda, quem estiver perto dele é "pisoteado" e fica lento.
    public override void OnFixedUpdate(PlayerControl pc)
    {
        if (!pc.IsAlive() || !GameStates.IsInTask || ExileController.Instance) return;

        if (Count++ < 5) return;

        Count = 0;

        Vector2 pos = pc.Pos();
        bool moved = !FastVector2.DistanceWithinRange(pos, LastPosition, 0.1f);
        LastPosition = pos;

        if (!moved) return;

        if (FastVector2.TryGetClosestPlayerInRange(pos, StompRange.GetFloat(), out PlayerControl target, x => x.PlayerId != pc.PlayerId) && StompedPlayers.Add(target.PlayerId))
            Main.Instance.StartCoroutine(Stomp(target));
    }

    private IEnumerator Stomp(PlayerControl target)
    {
        float oldSpeed = Main.AllPlayerSpeed[target.PlayerId];
        Main.AllPlayerSpeed[target.PlayerId] = Main.MinSpeed;
        target.MarkDirtySettings();

        yield return new WaitForSecondsRealtime(StompDuration.GetFloat());

        Main.AllPlayerSpeed[target.PlayerId] = oldSpeed;
        target.MarkDirtySettings();

        // Tempo em que o alvo fica imune a outro pisão
        yield return new WaitForSecondsRealtime(StompCooldown.GetFloat());

        StompedPlayers.Remove(target.PlayerId);
    }

    // Igual ao Car: só aplica o nome customizado depois da reunião terminar (a tela de
    // votação usa o nome real dos jogadores; quando ela fecha, reaplicamos o nome pixel-art).
    public override void AfterMeetingTasks()
    {
        var pc = TrexId.GetPlayer();
        if (!pc || !pc.IsAlive()) return;
        pc.RpcSetName(Name);
    }
}