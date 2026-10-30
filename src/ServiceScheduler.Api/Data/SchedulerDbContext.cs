using Microsoft.EntityFrameworkCore;
using ServiceScheduler.Api.Domain;

namespace ServiceScheduler.Api.Data;

public class SchedulerDbContext(DbContextOptions<SchedulerDbContext> options) : DbContext(options)
{
    public DbSet<Dealership> Dealerships => Set<Dealership>();
    public DbSet<ServiceBay> ServiceBays => Set<ServiceBay>();
    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<TechnicianQualification> TechnicianQualifications => Set<TechnicianQualification>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Dealership>(e =>
        {
            e.HasMany(d => d.Bays).WithOne().HasForeignKey(x => x.DealershipId);
            e.HasMany(d => d.Technicians).WithOne().HasForeignKey(x => x.DealershipId);
        });

        b.Entity<Technician>().HasMany(t => t.Qualifications).WithOne().HasForeignKey(q => q.TechnicianId);
        b.Entity<TechnicianQualification>(e =>
        {
            e.HasKey(q => new { q.TechnicianId, q.ServiceTypeId });
            e.HasOne<ServiceType>().WithMany().HasForeignKey(q => q.ServiceTypeId);
        });

        b.Entity<Customer>().HasMany(c => c.Vehicles).WithOne().HasForeignKey(v => v.CustomerId);
        b.Entity<Vehicle>().HasIndex(v => v.Vin).IsUnique();

        b.Entity<Appointment>(e =>
        {
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

            // SQLite has no timezone-aware type; values are stored as UTC and re-tagged on read.
            foreach (var p in new[] { nameof(Appointment.StartUtc), nameof(Appointment.EndUtc), nameof(Appointment.CreatedUtc) })
                e.Property<DateTime>(p).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
            e.HasOne(a => a.Customer).WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Vehicle).WithMany().HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Dealership).WithMany().HasForeignKey(a => a.DealershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.ServiceType).WithMany().HasForeignKey(a => a.ServiceTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Technician).WithMany().HasForeignKey(a => a.TechnicianId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.ServiceBay).WithMany().HasForeignKey(a => a.ServiceBayId).OnDelete(DeleteBehavior.Restrict);

            // Serves the overlap query: "active bookings at a dealership inside a time window".
            e.HasIndex(a => new { a.DealershipId, a.StartUtc, a.EndUtc });
            e.HasIndex(a => new { a.VehicleId, a.StartUtc });
            e.HasIndex(a => a.IdempotencyKey).IsUnique();
        });
    }
}
