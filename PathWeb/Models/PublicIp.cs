using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class PublicIp
{
    public int RangeId { get; set; }

    public string Lab { get; set; } = null!;

    public string Range { get; set; } = null!;

    public string RangeType { get; set; } = null!;

    public string? Device { get; set; }

    public string? Purpose { get; set; }

    public Guid? TenantGuid { get; set; }

    public short? TenantId { get; set; }

    public DateTime? AssignedDate { get; set; }

    public string? AssignedBy { get; set; }
}
