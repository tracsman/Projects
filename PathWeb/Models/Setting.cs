using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class Setting
{
    public string? SettingName { get; set; }

    public string? ProdVersion { get; set; }

    public string? BetaVersion { get; set; }
}
