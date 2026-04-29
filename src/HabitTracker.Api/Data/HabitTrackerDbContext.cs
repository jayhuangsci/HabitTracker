using HabitTracker.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace HabitTracker.Api.Data;


public class HabitTrackerDbContext : DbContext
{

    public HabitTrackerDbContext(DbContextOptions<HabitTrackerDbContext> options)
        :base(options)
    {

    }
    public DbSet<Habit> Habits => Set<Habit>();
    public DbSet<HabitLog> HabitLogs => Set<HabitLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Habit>(entity =>
        {
            entity.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(100);
            entity.Property(x => x.Unit)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(x => x.Description)
                .HasMaxLength(300);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.Name)
                .IsUnique();
        }


        );
        modelBuilder.Entity<HabitLog>(entity =>
        {
            entity.Property(x => x.HabitId)
            .IsRequired();
            entity.Property(x => x.Date)
                .IsRequired();

            entity.Property(x => x.Quantity)
                .IsRequired();

            entity.Property(x => x.Comment)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(x => x.Habit)
                .WithMany(x => x.HabitLogs)
                .HasForeignKey(x => x.HabitId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.HabitId, x.Date })
                .IsUnique();

            entity.HasIndex(x => x.Date);

            entity.ToTable(t =>
                t.HasCheckConstraint(
                    "CK_HabitLogs_Quantity_NonNegative",
                    "\"Quantity\" >= 0"));
        });


    }

}