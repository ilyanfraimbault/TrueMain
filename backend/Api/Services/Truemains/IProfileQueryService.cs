using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface IProfileQueryService
{
    /// <summary>
    /// Returns the truemain profile payload for <paramref name="nameTag"/>
    /// (<c>gameName-tagLine</c>). Returns <c>null</c> when the name tag is
    /// malformed or no Riot account matches — the controller maps that to
    /// 404.
    /// </summary>
    Task<ProfileReadModel?> GetAsync(string nameTag, CancellationToken ct);
}
