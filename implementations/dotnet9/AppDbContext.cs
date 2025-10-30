using Microsoft.EntityFrameworkCore;

namespace Dotnet9ReferenceService;

/// <summary>
/// Database context for the Dotnet9 reference service.
/// </summary>
public class AppDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppDbContext"/> class.
    /// </summary>
    /// <param name="options">The database context options.</param>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the instrument status records.
    /// </summary>
    public DbSet<InstrumentStatus> InstrumentStatuses { get; set; }

    /// <summary>
    /// Configures the entity mappings for the database context.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstrumentStatus>(entity =>
        {
            entity.ToTable("dotnet9_instrument_status");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.DeviceId).HasColumnName("device_id").HasColumnType("varchar(255)").IsRequired();
            entity.Property(e => e.PreviousStatus).HasColumnName("previous_status").HasColumnType("varchar(255)").IsRequired();
            entity.Property(e => e.CurrentStatus).HasColumnName("current_status").HasColumnType("varchar(255)").IsRequired();
            entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();

            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_dotnet9_instrument_status_timestamp");
        });
    }
}
