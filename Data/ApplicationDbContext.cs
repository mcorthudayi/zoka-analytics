using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ZokaAnalytics.Models;

namespace ZokaAnalytics.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<League> Leagues => Set<League>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<TeamStatistic> TeamStatistics => Set<TeamStatistic>();
    public DbSet<H2HRecord> H2HRecords => Set<H2HRecord>();
    public DbSet<Prediction> Predictions => Set<Prediction>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponItem> CouponItems => Set<CouponItem>();
    public DbSet<SyncLog> SyncLogs => Set<SyncLog>();
    public DbSet<FavoriteMatch> FavoriteMatches => Set<FavoriteMatch>();
    public DbSet<FavoriteTeam> FavoriteTeams => Set<FavoriteTeam>();
    public DbSet<MatchContext> MatchContexts => Set<MatchContext>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<League>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.IsTracked);
        });

        builder.Entity<Team>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Name);
        });

        builder.Entity<Match>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();

            e.HasOne(x => x.HomeTeam)
                .WithMany(t => t.HomeMatches)
                .HasForeignKey(x => x.HomeTeamId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.AwayTeam)
                .WithMany(t => t.AwayMatches)
                .HasForeignKey(x => x.AwayTeamId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.League)
                .WithMany(l => l.Matches)
                .HasForeignKey(x => x.LeagueId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.OddsHome).HasPrecision(6, 2);
            e.Property(x => x.OddsDraw).HasPrecision(6, 2);
            e.Property(x => x.OddsAway).HasPrecision(6, 2);

            e.HasIndex(x => x.KickoffUtc);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.LeagueId, x.Season });
        });

        builder.Entity<TeamStatistic>(e =>
        {
            e.HasIndex(x => new { x.TeamId, x.LeagueId, x.Season }).IsUnique();

            e.HasOne(x => x.Team)
                .WithMany(t => t.Statistics)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.League)
                .WithMany(l => l.TeamStatistics)
                .HasForeignKey(x => x.LeagueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<H2HRecord>(e =>
        {
            e.HasIndex(x => new { x.TeamAId, x.TeamBId }).IsUnique();

            e.HasOne(x => x.TeamA)
                .WithMany()
                .HasForeignKey(x => x.TeamAId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.TeamB)
                .WithMany()
                .HasForeignKey(x => x.TeamBId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Prediction>(e =>
        {
            e.HasOne(x => x.Match)
                .WithOne(m => m.Prediction)
                .HasForeignKey<Prediction>(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.MatchId).IsUnique();
        });

        builder.Entity<Coupon>(e =>
        {
            e.Property(x => x.Stake).HasPrecision(10, 2);
            e.Property(x => x.TotalOdds).HasPrecision(10, 2);
            e.Property(x => x.PotentialReturn).HasPrecision(12, 2);

            e.HasOne(x => x.User)
                .WithMany(u => u.Coupons)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.UserId, x.Status });
        });

        builder.Entity<CouponItem>(e =>
        {
            e.Property(x => x.Odds).HasPrecision(6, 2);

            e.HasOne(x => x.Coupon)
                .WithMany(c => c.Items)
                .HasForeignKey(x => x.CouponId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Match)
                .WithMany(m => m.CouponItems)
                .HasForeignKey(x => x.MatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MatchContext>(e =>
        {
            e.HasIndex(x => x.MatchId).IsUnique();
            e.HasOne(x => x.Match).WithOne()
                .HasForeignKey<MatchContext>(x => x.MatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FavoriteMatch>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.MatchId }).IsUnique();

            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Match).WithMany()
                .HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FavoriteTeam>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.TeamId }).IsUnique();

            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Team).WithMany()
                .HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SyncLog>(e => e.HasIndex(x => x.StartedAtUtc));

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type.IsEnum)
                    property.SetProviderClrType(typeof(int));
            }
        }
    }
}