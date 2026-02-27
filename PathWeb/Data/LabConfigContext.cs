using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using PathWeb.Models;

namespace PathWeb.Data;

public partial class LabConfigContext : DbContext
{
    public LabConfigContext(DbContextOptions<LabConfigContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Config> Configs { get; set; }

    public virtual DbSet<PublicIp> PublicIps { get; set; }

    public virtual DbSet<Region> Regions { get; set; }

    public virtual DbSet<Setting> Settings { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Config>(entity =>
        {
            entity.ToTable("Config");

            entity.Property(e => e.ConfigId)
                .HasDefaultValueSql("(newid())", "DF_Config_ConfigID")
                .HasColumnName("ConfigID");
            entity.Property(e => e.Config1).HasColumnName("Config");
            entity.Property(e => e.ConfigType).HasMaxLength(50);
            entity.Property(e => e.CreatedBy).HasMaxLength(50);
            entity.Property(e => e.NinjaOwner).HasMaxLength(50);
            entity.Property(e => e.TenantGuid).HasColumnName("TenantGUID");
            entity.Property(e => e.TenantId).HasColumnName("TenantID");
        });

        modelBuilder.Entity<PublicIp>(entity =>
        {
            entity.HasKey(e => e.RangeId);

            entity.ToTable("PublicIP");

            entity.HasIndex(e => e.Range, "IX_PublicIP").IsUnique();

            entity.Property(e => e.RangeId).HasColumnName("RangeID");
            entity.Property(e => e.AssignedBy).HasMaxLength(50);
            entity.Property(e => e.Device).HasMaxLength(50);
            entity.Property(e => e.Lab)
                .HasMaxLength(10)
                .IsFixedLength();
            entity.Property(e => e.Purpose).HasMaxLength(50);
            entity.Property(e => e.Range).HasMaxLength(18);
            entity.Property(e => e.RangeType).HasMaxLength(15);
            entity.Property(e => e.TenantGuid).HasColumnName("TenantGUID");
            entity.Property(e => e.TenantId).HasColumnName("TenantID");
        });

        modelBuilder.Entity<Region>(entity =>
        {
            entity.HasNoKey();

            entity.Property(e => e.Ipv4)
                .HasMaxLength(3)
                .HasColumnName("IPv4");
            entity.Property(e => e.Ipv6)
                .HasMaxLength(2)
                .HasColumnName("IPv6");
            entity.Property(e => e.Region1)
                .HasMaxLength(25)
                .HasColumnName("Region");
            entity.Property(e => e.RegionType).HasMaxLength(10);
        });

        modelBuilder.Entity<Setting>(entity =>
        {
            entity.HasNoKey();

            entity.Property(e => e.BetaVersion).HasMaxLength(100);
            entity.Property(e => e.ProdVersion).HasMaxLength(100);
            entity.Property(e => e.SettingName).HasMaxLength(50);
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.TenantGuid);

            entity.ToTable("Tenant");

            entity.Property(e => e.TenantGuid)
                .HasDefaultValueSql("(newid())", "DF_Tenant_TenantGUID")
                .HasColumnName("TenantGUID");
            entity.Property(e => e.AddressFamily).HasMaxLength(4);
            entity.Property(e => e.AssignedBy).HasMaxLength(50);
            entity.Property(e => e.AzVm1)
                .HasMaxLength(18)
                .HasColumnName("AzVM1");
            entity.Property(e => e.AzVm2)
                .HasMaxLength(18)
                .HasColumnName("AzVM2");
            entity.Property(e => e.AzVm3)
                .HasMaxLength(18)
                .HasColumnName("AzVM3");
            entity.Property(e => e.AzVm4)
                .HasMaxLength(18)
                .HasColumnName("AzVM4");
            entity.Property(e => e.AzureRegion).HasMaxLength(50);
            entity.Property(e => e.Contacts).HasMaxLength(150);
            entity.Property(e => e.DeletedBy).HasMaxLength(50);
            entity.Property(e => e.ErfastPath).HasColumnName("ERFastPath");
            entity.Property(e => e.ErgatewaySize)
                .HasMaxLength(50)
                .HasColumnName("ERGatewaySize");
            entity.Property(e => e.Ersku)
                .HasMaxLength(10)
                .HasColumnName("ERSKU");
            entity.Property(e => e.Erspeed).HasColumnName("ERSpeed");
            entity.Property(e => e.EruplinkPort)
                .HasMaxLength(50)
                .HasColumnName("ERUplinkPort");
            entity.Property(e => e.Lab)
                .HasMaxLength(3)
                .IsFixedLength();
            entity.Property(e => e.LabVm1)
                .HasMaxLength(18)
                .HasColumnName("LabVM1");
            entity.Property(e => e.LabVm2)
                .HasMaxLength(18)
                .HasColumnName("LabVM2");
            entity.Property(e => e.LabVm3)
                .HasMaxLength(18)
                .HasColumnName("LabVM3");
            entity.Property(e => e.LabVm4)
                .HasMaxLength(18)
                .HasColumnName("LabVM4");
            entity.Property(e => e.LastUpdateBy).HasMaxLength(50);
            entity.Property(e => e.Msftadv)
                .HasMaxLength(37)
                .HasColumnName("MSFTAdv");
            entity.Property(e => e.Msftp2p)
                .HasMaxLength(18)
                .HasColumnName("MSFTP2P");
            entity.Property(e => e.Msftpeering).HasColumnName("MSFTPeering");
            entity.Property(e => e.Msfttags)
                .HasMaxLength(50)
                .HasColumnName("MSFTTags");
            entity.Property(e => e.NinjaOwner).HasMaxLength(50);
            entity.Property(e => e.Skey).HasColumnName("SKey");
            entity.Property(e => e.TenantId).HasColumnName("TenantID");
            entity.Property(e => e.Vpnbgp).HasColumnName("VPNBGP");
            entity.Property(e => e.Vpnconfig)
                .HasMaxLength(15)
                .HasColumnName("VPNConfig");
            entity.Property(e => e.VpnendPoint)
                .HasMaxLength(37)
                .HasColumnName("VPNEndPoint");
            entity.Property(e => e.Vpngateway)
                .HasMaxLength(20)
                .HasColumnName("VPNGateway");
            entity.Property(e => e.WorkItemId).HasColumnName("WorkItemID");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK_User");

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("(newid())", "DF_User_UserID")
                .HasColumnName("UserID");
            entity.Property(e => e.AuthLevel).HasDefaultValue((byte)1, "DF_User_AuthLevel");
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.UserName).HasMaxLength(50);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
