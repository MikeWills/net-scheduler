using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Models.Domain;

namespace NcsScheduler.Data;

public static class DbSeeder
{
    public static readonly string[] Roles = ["SuperAdmin", "BandCoordinator", "NetController"];

    public static async Task SeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        // Apply any pending migrations
        await db.Database.MigrateAsync();

        // Seed roles
        foreach (var role in Roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Created role: {Role}", role);
            }
        }

        // Seed SuperAdmin from appsettings.Development.json
        var adminEmail = config["SeedAdmin:Email"];
        var adminPassword = config["SeedAdmin:Password"];

        if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
        {
            var existing = await userManager.FindByEmailAsync(adminEmail);
            if (existing is null)
            {
                // Create a NetController record for the admin
                var adminController = new NetController
                {
                    Callsign = "ADMIN",
                    Name = "System Administrator",
                    Email = adminEmail,
                    IsActive = true
                };
                db.NetControllers.Add(adminController);
                await db.SaveChangesAsync();

                var adminUser = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    NetControllerId = adminController.Id
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "SuperAdmin");
                    adminController.UserId = adminUser.Id;
                    await db.SaveChangesAsync();
                    logger.LogInformation("Seeded SuperAdmin: {Email}", adminEmail);
                }
                else
                {
                    logger.LogError("Failed to create SuperAdmin: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }

        // Seed the 3 core nets if none exist
        if (!await db.Nets.AnyAsync())
        {
            var earlyNet = new Net
            {
                Name = "80m Early Net",
                Band = "80m",
                Description = "Daily early net",
                ScheduledTimeUtc = new TimeOnly(3, 0),
                IsActive = true,
                ScheduleRules = Enum.GetValues<DayOfWeek>().Select(dow => new NetScheduleRule
                {
                    DayOfWeek = dow,
                    IsActive = true
                }).ToList()
            };

            var lateNet = new Net
            {
                Name = "80m Late Net",
                Band = "80m",
                Description = "Friday and Saturday late net",
                ScheduledTimeUtc = new TimeOnly(5, 0),
                IsActive = true,
                ScheduleRules =
                [
                    new NetScheduleRule { DayOfWeek = DayOfWeek.Friday, IsActive = true },
                    new NetScheduleRule { DayOfWeek = DayOfWeek.Saturday, IsActive = true }
                ]
            };

            var holidayNet = new Net
            {
                Name = "80m Holiday Net",
                Band = "80m",
                Description = "Holiday net — runs on US federal holidays",
                ScheduledTimeUtc = new TimeOnly(5, 0),
                IsActive = true
                // No ScheduleRules — sessions are auto-generated from the holiday calendar
            };

            db.Nets.AddRange(earlyNet, lateNet, holidayNet);
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded 3 default nets");
        }
    }
}
