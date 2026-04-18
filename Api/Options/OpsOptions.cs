using System.ComponentModel.DataAnnotations;

namespace TrueMain.Options;

public sealed class OpsOptions
{
    [Required]
    [MinLength(32)]
    public string ApiKey { get; set; } = string.Empty;
}
