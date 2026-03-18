using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class Config
{
    public Guid ConfigId { get; set; }

    public string ConfigType { get; set; } = null!;

    public Guid TenantGuid { get; set; }

    public short TenantId { get; set; }

    public short ConfigVersion { get; set; }

    public string NinjaOwner { get; set; } = null!;

    public string Config1 { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = null!;
}
