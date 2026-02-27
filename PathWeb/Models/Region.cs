using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class Region
{
    public string Region1 { get; set; } = null!;

    public string RegionType { get; set; } = null!;

    public string Ipv4 { get; set; } = null!;

    public string Ipv6 { get; set; } = null!;
}
