using Microsoft.EntityFrameworkCore;
using WashFlow.Domain.Businesses;
using WashFlow.Domain.Common;
using WashFlow.Domain.Customers;
using WashFlow.Domain.Identity;

namespace WashFlow.Infrastructure.Persistence;

public sealed class WashFlowDbContext(DbContextOptions<WashFlowDbContext> options)
    : DbContext(options)
{
    public DbSet<WashFlow.Domain.Catalog.BusinessService> BusinessServices => Set<WashFlow.Domain.Catalog.BusinessService>();
    public DbSet<WashFlow.Domain.Catalog.ServiceOffering> ServiceOfferings => Set<WashFlow.Domain.Catalog.ServiceOffering>();
    public DbSet<WeeklyOpeningDay> WeeklyOpeningDays => Set<WeeklyOpeningDay>();
    public DbSet<LocalCustomer> LocalCustomers => Set<LocalCustomer>();
    public DbSet<LocalVehicle> LocalVehicles => Set<LocalVehicle>();
    public DbSet<OwnedVehicle> OwnedVehicles => Set<OwnedVehicle>();
    public DbSet<WashFlow.Domain.Scheduling.WorkArea> WorkAreas => Set<WashFlow.Domain.Scheduling.WorkArea>();
    public DbSet<WashFlow.Domain.Scheduling.ServiceSchedulingRequirement> ServiceSchedulingRequirements => Set<WashFlow.Domain.Scheduling.ServiceSchedulingRequirement>();
    public DbSet<WashFlow.Domain.Scheduling.EmployeeAvailability> EmployeeAvailabilities => Set<WashFlow.Domain.Scheduling.EmployeeAvailability>();
    public DbSet<WashFlow.Domain.Scheduling.Appointment> Appointments => Set<WashFlow.Domain.Scheduling.Appointment>();
    public DbSet<WashFlow.Domain.Scheduling.AppointmentEvent> AppointmentEvents => Set<WashFlow.Domain.Scheduling.AppointmentEvent>();
    public DbSet<WashFlow.Domain.Scheduling.AppointmentResourceReservation> AppointmentResourceReservations => Set<WashFlow.Domain.Scheduling.AppointmentResourceReservation>();
    public DbSet<WashFlow.Infrastructure.Scheduling.SchedulingCreateReceipt> SchedulingCreateReceipts => Set<WashFlow.Infrastructure.Scheduling.SchedulingCreateReceipt>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessMembership> BusinessMemberships => Set<BusinessMembership>();
    public DbSet<BusinessMembershipRole> BusinessMembershipRoles => Set<BusinessMembershipRole>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmployeeInvitation> EmployeeInvitations => Set<EmployeeInvitation>();
    public DbSet<DeviceSession> DeviceSessions => Set<DeviceSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WashFlowDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampChanges()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IEntity>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.UpdatedAt = entry.Entity.CreatedAt;
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.CreatedAt).IsModified = false;
                // Scheduling aggregates supply an explicit operation timestamp, shared with their
                // events/releases and TimeProvider. Do not replace it with a second wall clock.
                if (entry.Entity.GetType().Namespace != "WashFlow.Domain.Scheduling")
                    entry.Entity.UpdatedAt = now;
            }
        }
    }
}
