using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PromptCrew.Api.Data;

public sealed class CrewDbContext(DbContextOptions<CrewDbContext> options) : DbContext(options)
{
    public DbSet<CrewProject> Projects => Set<CrewProject>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<TaskChain> Chains => Set<TaskChain>();
    public DbSet<PromptRound> Rounds => Set<PromptRound>();
    public DbSet<RoundResult> Results => Set<RoundResult>();
    public DbSet<AccessKey> AccessKeys => Set<AccessKey>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        foreach (var type in new[] { typeof(CrewProject), typeof(Employee), typeof(TaskChain), typeof(PromptRound) })
        {
            model.Entity(type).HasBaseType((Type?)null);
            model.Entity(type).HasKey(nameof(Entity.Id));
            model.Entity(type).Property(nameof(Entity.Version)).IsConcurrencyToken();
        }
        model.Entity<CrewProject>().Property(x => x.Name).HasMaxLength(120);
        model.Entity<Employee>(e =>
        {
            e.HasAlternateKey(x => new { x.ProjectId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.NormalizedName }).IsUnique();
            e.HasOne<CrewProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<TaskChain>(e =>
        {
            e.HasAlternateKey(x => new { x.ProjectId, x.Id });
            e.HasOne<CrewProject>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PromptRound>(e =>
        {
            e.HasAlternateKey(x => new { x.ProjectId, x.Id });
            e.HasAlternateKey(x => new { x.ProjectId, x.ChainId, x.Id });
            e.HasIndex(x => new { x.ProjectId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.ChainId, x.Number }).IsUnique();
            e.HasOne<TaskChain>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ChainId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.ProjectId, x.EmployeeId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PromptRound>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ChainId, x.ParentRoundId })
                .HasPrincipalKey(x => new { x.ProjectId, x.ChainId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PromptRound>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ChainId, x.RevisesRoundId })
                .HasPrincipalKey(x => new { x.ProjectId, x.ChainId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Results).WithOne().HasForeignKey(x => new { x.ProjectId, x.RoundId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.References).WithOne().HasForeignKey(x => new { x.ProjectId, x.RoundId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Tags).WithOne().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RoundResult>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProjectId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.RoundId, x.Version }).IsUnique();
        });
        model.Entity<RoundReference>(e =>
        {
            e.HasKey(x => new { x.RoundId, x.ReferencedRoundId });
            e.HasOne<PromptRound>().WithMany().HasForeignKey(x => new { x.ProjectId, x.ReferencedRoundId })
                .HasPrincipalKey(x => new { x.ProjectId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RoundTag>(e =>
        {
            e.HasKey(x => new { x.RoundId, x.Value });
            e.HasIndex(x => x.Value);
        });
        model.Entity<AccessKey>().HasIndex(x => x.TokenHash).IsUnique();
    }
}
public sealed class CrewDesignFactory : IDesignTimeDbContextFactory<CrewDbContext>
{
    public CrewDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CrewDbContext>()
        .UseSqlite("Data Source=promptcrew-design.db").Options);
}
