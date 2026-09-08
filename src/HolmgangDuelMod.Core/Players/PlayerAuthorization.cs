namespace Catosaurluna.HolmgangDuelMod.Core.Players;

public interface IAdminAuthorizer
{
    bool IsAdministrator(string stableId);
}
