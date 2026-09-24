using System.Text;
using UnityEngine;

namespace EHR.Roles;

// Noisebodie (neutra): precisa reportar corpos para vencer.
// No começo da partida é sorteado se ele precisa reportar 1 ou 2 corpos (50% / 50% por padrão).
// Quando ele chega no número sorteado, ele vence a partida.
// O nome da classe TEM que ser igual ao nome no enum CustomRoles.
public class Noisebodie : RoleBase
{
    public static bool On;

    private static OptionItem ChanceOfOneBody;

    private byte NoisebodieId;
    private int RequiredReports;
    private int Reports;

    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        // IDs: confira se 5750 e 5752 estão livres (procure por "5750" no projeto).
        Options.SetupRoleOptions(5750, TabGroup.NeutralRoles, CustomRoles.Noisebodie);

        // Chance (%) de precisar reportar só 1 corpo. O resto é a chance de precisar de 2.
        ChanceOfOneBody = new IntegerOptionItem(5752, "Noisebodie.ChanceOfOneBody", new(0, 100, 5), 50, TabGroup.NeutralRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Noisebodie]);
    }

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
        NoisebodieId = playerId;
        Reports = 0;

        // Sorteia a meta: 1 corpo (com a chance configurada) ou 2 corpos
        RequiredReports = IRandom.Instance.Next(0, 100) < ChanceOfOneBody.GetInt() ? 1 : 2;
    }

    // Conta os reports de corpos feitos pelo Noisebodie
    public override bool CheckReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, PlayerControl killer)
    {
        // target == null é reunião de emergência (botão), que não conta
        if (reporter == null || target == null) return true;
        if (reporter.PlayerId != NoisebodieId || !reporter.IsAlive()) return true;

        Reports++;

        if (Reports >= RequiredReports) Win();

        return true;
    }

    private void Win()
    {
        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Noisebodie);
        CustomWinnerHolder.WinnerIds.Add(NoisebodieId);
    }

    // Mostra o progresso ao lado do nome, por exemplo (0/2)
    public override void GetProgressText(byte playerId, bool comms, StringBuilder resultText)
    {
        resultText.Append($" <color=#ffd54f>({Reports}/{RequiredReports})</color>");
    }
}