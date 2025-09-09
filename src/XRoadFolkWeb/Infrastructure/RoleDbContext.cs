using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace XRoadFolkWeb.Infrastructure;

public class RoleDbContext : DbContext
{
    private readonly ICurrentUserAccessor? _currentUser;

    public RoleDbContext(DbContextOptions<RoleDbContext> options, ICurrentUserAccessor? currentUser = null) : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<AppUserRole> UserRoles => Set<AppUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AppUserRole>(e =>
        {
            e.ToTable("AppUserRoles");
            e.HasKey(x => new { x.User, x.Role });
            e.Property(x => x.User).HasMaxLength(128).IsRequired();
            e.Property(x => x.Role).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedUtc).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.ModifiedUtc);
            e.Property(x => x.ModifiedBy).HasMaxLength(256);
            e.Property(x => x.DeletedUtc);
            e.Property(x => x.DeletedBy).HasMaxLength(256);
            e.Property(x => x.IsDeleted).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Role).HasDatabaseName("IX_AppUserRoles_Role");
            e.HasIndex(x => x.CreatedUtc).HasDatabaseName("IX_AppUserRoles_CreatedUtc");
            e.HasIndex(x => x.IsDeleted).HasDatabaseName("IX_AppUserRoles_IsDeleted");
        });
    }

    public override int SaveChanges()
    {
        ApplyTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyTimestamps()
    {
        var utcNow = DateTime.UtcNow;
        string? actor = _currentUser?.Name;
        foreach (var entry in ChangeTracker.Entries<AppUserRole>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedUtc == default)
                {
                    entry.Entity.CreatedUtc = utcNow;
                }
                if (string.IsNullOrWhiteSpace(entry.Entity.CreatedBy) && !string.IsNullOrWhiteSpace(actor))
                {
                    entry.Entity.CreatedBy = actor;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedUtc = utcNow;
                if (!string.IsNullOrWhiteSpace(actor))
                {
                    entry.Entity.ModifiedBy = actor;
                }
            }
        }
    }
}

public class AppUserRole
{
    public string User { get; set; } = string.Empty; // sam or UPN
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public string? ModifiedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public string? DeletedBy { get; set; }
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
