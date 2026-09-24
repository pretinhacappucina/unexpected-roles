using System.Collections;
using AmongUs.GameOptions;
using UnityEngine;

namespace EHR.Roles;

// Whistleblower: crewmate com base de Engineer (para poder entrar em dutos).
// Quando entra em um duto ou acaricia o pet, aparece um texto vermelho ao lado do nome
// de cada impostor vivo, só para ele, por um tempo aleatório entre o mínimo e o máximo.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Whistleblower : RoleBase
{
    public static bool On;

    // Texto que aparece ao lado dos impostores. Mude aqui se quiser outro (ex.: "Impostor").
    private const string ImpostorText = "Imposter";

    private static OptionItem MinDuration;
    private static OptionItem MaxDuration;
    private static OptionItem VentCooldown;

    private byte WhistleblowerId;

    // Momento (Time.realtimeSinceStartup) até o qual o texto deve ficar visível
    private float ShowUntil;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5710 a 5714 estão livres (procure por "5710" no projeto).
        Options.SetupRoleOptions(5710, TabGroup.CrewmateRoles, CustomRoles.Whistleblower);

        MinDuration = new FloatOptionItem(5712, "Whistleblower.MinDuration", new(0.1f, 3f, 0.1f), 0.1f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Whistleblower]);

        MaxDuration = new FloatOptionItem(5713, "Whistleblower.MaxDuration", new(0.1f, 3f, 0.1f), 0.5f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Whistleblower]);

        VentCooldown = new FloatOptionItem(5714, "Whistleblower.VentCooldown", new(0f, 60f, 1f), 10f, TabGroup.CrewmateRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Whistleblower]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        WhistleblowerId = playerId;
        ShowUntil = 0f;
    }

    // Base de Engineer: o botão de duto fica disponível, com cooldown configurável.
    public override void ApplyGameOptions(IGameOptions opt, byte playerId)
    {
        AURoleOptions.EngineerCooldown = VentCooldown.GetFloat();
        AURoleOptions.EngineerInVentMaxTime = 1f;
    }

    public override void OnEnterVent(PlayerControl pc, Vent vent)
    {
        Reveal(pc);
    }

    public override void OnPet(PlayerControl pc)
    {
        Reveal(pc);
    }

    private void Reveal(PlayerControl pc)
    {
        if (pc == null || !pc.IsAlive() || !GameStates.IsInTask) return;

        float min = Mathf.Min(MinDuration.GetFloat(), MaxDuration.GetFloat());
        float max = Mathf.Max(MinDuration.GetFloat(), MaxDuration.GetFloat());
        float duration = UnityEngine.Random.Range(min, max);

        ShowUntil = Time.realtimeSinceStartup + duration;

        // Atualiza os nomes agora para o texto aparecer, e de novo quando o tempo acabar.
        Utils.NotifyRoles(SpecifySeer: pc);
        Main.Instance.StartCoroutine(Hide(pc, duration));
    }

    private IEnumerator Hide(PlayerControl pc, float duration)
    {
        yield return new WaitForSecondsRealtime(duration);

        if (pc) Utils.NotifyRoles(SpecifySeer: pc);
    }

    // Texto vermelho ao lado do nome dos impostores, visível só para o Whistleblower.
    public override string GetSuffix(PlayerControl seer, PlayerControl target, bool hud = false, bool meeting = false)
    {
        if (meeting) return string.Empty;
        if (seer == null || target == null) return string.Empty;
        if (seer.PlayerId != WhistleblowerId || target.PlayerId == seer.PlayerId) return string.Empty;
        if (Time.realtimeSinceStartup >= ShowUntil) return string.Empty;
        if (!target.IsAlive() || !target.Is(CustomRoleTypes.Impostor)) return string.Empty;

        return $"<color=#ff1919>{ImpostorText}</color>";
    }
}