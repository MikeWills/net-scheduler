using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Models.Domain;

namespace NcsScheduler.Data;

public class ApplicationUser : IdentityUser
{
    public int? NetControllerId { get; set; }
    public NetController? NetController { get; set; }
}

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<NetController> NetControllers => Set<NetController>();
    public DbSet<BandCoordinator> BandCoordinators => Set<BandCoordinator>();
    public DbSet<Net> Nets => Set<Net>();
    public DbSet<NetScheduleRule> NetScheduleRules => Set<NetScheduleRule>();
    public DbSet<NetCoordinatorAssignment> NetCoordinatorAssignments => Set<NetCoordinatorAssignment>();
    public DbSet<StandingAssignment> StandingAssignments => Set<StandingAssignment>();
    public DbSet<NetSession> NetSessions => Set<NetSession>();
    public DbSet<SessionAssignment> SessionAssignments => Set<SessionAssignment>();
    public DbSet<Unavailability> Unavailabilities => Set<Unavailability>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<NetControllerNetPreference> NetPreferences => Set<NetControllerNetPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Unique: one callsign per controller
        builder.Entity<NetController>()
            .HasIndex(x => x.Callsign)
            .IsUnique();

        // Unique: iCal tokens must be unique (sparse — only set when first requested)
        builder.Entity<NetController>()
            .HasIndex(x => x.IcalToken)
            .IsUnique()
            .HasFilter("[IcalToken] IS NOT NULL");

        // Unique: one session per net per date
        builder.Entity<NetSession>()
            .HasIndex(x => new { x.NetId, x.SessionDate })
            .IsUnique();

        // Unique: one coordinator record per controller
        builder.Entity<BandCoordinator>()
            .HasIndex(x => x.NetControllerId)
            .IsUnique();

        // Unique: invitation tokens must be unique
        builder.Entity<Invitation>()
            .HasIndex(x => x.Token)
            .IsUnique();

        // Store enums as strings for readability in the DB
        builder.Entity<SessionAssignment>()
            .Property(x => x.AssignmentType)
            .HasConversion<string>();

        builder.Entity<SessionAssignment>()
            .Property(x => x.Status)
            .HasConversion<string>();

        // ApplicationUser -> NetController (optional link)
        builder.Entity<ApplicationUser>()
            .HasOne(u => u.NetController)
            .WithOne()
            .HasForeignKey<ApplicationUser>(u => u.NetControllerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Prevent cascade delete cycles through Unavailability -> Net
        builder.Entity<Unavailability>()
            .HasOne(u => u.Net)
            .WithMany()
            .HasForeignKey(u => u.NetId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique: one preference entry per controller per net
        builder.Entity<NetControllerNetPreference>()
            .HasIndex(x => new { x.NetControllerId, x.NetId })
            .IsUnique();
    }
}
