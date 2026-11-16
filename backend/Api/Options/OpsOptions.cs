using System.ComponentModel.DataAnnotations;
using TrueMain.Configuration;

namespace TrueMain.Options;

public sealed class OpsOptions : IApiOptions
{
    public const string SectionName = "Ops";

    static string IApiOptions.Section => SectionName;

    [Required]
    [MinLength(32)]
    public string ApiKey { get; set; } = string.Empty;
}
