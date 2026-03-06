using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class Device
{
    public Guid DeviceId { get; set; }

    public string Type { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Lab { get; set; } = null!;

    public string? MgmtIpv4 { get; set; }

    public string? MgmtIpv6 { get; set; }

    public string? Os { get; set; }

    public bool InService { get; set; }

    public string? Issues { get; set; }
}
