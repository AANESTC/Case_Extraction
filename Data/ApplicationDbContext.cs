using ECourtTracker.API.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECourtTracker.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Case> Cases { get; set; } = null!;
        public DbSet<Hearing> Hearings { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User indexes
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Case indexes
            modelBuilder.Entity<Case>()
                .HasIndex(c => c.CNRNumber)
                .IsUnique();

            modelBuilder.Entity<Case>()
                .HasIndex(c => c.UserId);

            // Case -> User FK
            modelBuilder.Entity<Case>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Case -> Hearing 1:N
            modelBuilder.Entity<Hearing>()
                .HasOne(h => h.Case)
                .WithMany(c => c.Hearings)
                .HasForeignKey(h => h.CaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // Hearing index on CaseId
            modelBuilder.Entity<Hearing>()
                .HasIndex(h => h.CaseId);

            // Hearing index on HearingDate for range queries
            modelBuilder.Entity<Hearing>()
                .HasIndex(h => h.HearingDate);
        }
    }
}
