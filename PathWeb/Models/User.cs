using System;
using System.Collections.Generic;

namespace PathWeb.Models;

public partial class User
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = null!;

    public string Name { get; set; } = null!;

    public byte AuthLevel { get; set; }

    public bool Ninja { get; set; }
}
