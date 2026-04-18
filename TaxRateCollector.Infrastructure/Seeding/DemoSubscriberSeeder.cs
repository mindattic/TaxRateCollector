using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Seeding;

public static class DemoSubscriberSeeder
{
    private const string Password = "Demo1234";

    private static readonly (string Email, string FullName, (string Code, string Name)[] States)[] DemoUsers =
    [
        (
            "demo.west@taxratecollector.com", "West Coast Demo",
            [("WA","Washington"),("OR","Oregon"),("CA","California"),("AK","Alaska"),("HI","Hawaii")]
        ),
        (
            "demo.east@taxratecollector.com", "East Coast Demo",
            [("ME","Maine"),("NH","New Hampshire"),("VT","Vermont"),("MA","Massachusetts"),
             ("RI","Rhode Island"),("CT","Connecticut"),("NY","New York"),("NJ","New Jersey"),
             ("DE","Delaware"),("MD","Maryland"),("VA","Virginia"),("NC","North Carolina"),
             ("SC","South Carolina"),("GA","Georgia"),("FL","Florida")]
        ),
        (
            "demo.coastal@taxratecollector.com", "Coastal Demo",
            [("WA","Washington"),("OR","Oregon"),("CA","California"),("AK","Alaska"),("HI","Hawaii"),
             ("ME","Maine"),("NH","New Hampshire"),("VT","Vermont"),("MA","Massachusetts"),
             ("RI","Rhode Island"),("CT","Connecticut"),("NY","New York"),("NJ","New Jersey"),
             ("DE","Delaware"),("MD","Maryland"),("VA","Virginia"),("NC","North Carolina"),
             ("SC","South Carolina"),("GA","Georgia"),("FL","Florida")]
        ),
        (
            "demo.midwest@taxratecollector.com", "Midwest Demo",
            [("OH","Ohio"),("MI","Michigan"),("IN","Indiana"),("IL","Illinois"),("WI","Wisconsin"),
             ("MN","Minnesota"),("IA","Iowa"),("MO","Missouri"),("ND","North Dakota"),
             ("SD","South Dakota"),("NE","Nebraska"),("KS","Kansas")]
        ),
        (
            "demo.south@taxratecollector.com", "Southern Demo",
            [("TX","Texas"),("OK","Oklahoma"),("AR","Arkansas"),("LA","Louisiana"),
             ("MS","Mississippi"),("AL","Alabama"),("TN","Tennessee"),("KY","Kentucky"),
             ("WV","West Virginia")]
        )
    ];

    public static async Task SeedAsync(AppDbContext db, UserManager<IdentityUser> userManager)
    {
        foreach (var (email, fullName, states) in DemoUsers)
        {
            var existing = await userManager.FindByEmailAsync(email);
            IdentityUser user;

            if (existing == null)
            {
                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var result = await userManager.CreateAsync(user, Password);
                if (!result.Succeeded)
                {
                    Console.WriteLine($"[seed] Failed to create {email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    continue;
                }
                Console.WriteLine($"[seed] Created demo subscriber: {email}");
            }
            else
            {
                user = existing;
            }

            var subscriber = await db.Subscribers.FirstOrDefaultAsync(s => s.UserId == user.Id);
            if (subscriber == null)
            {
                subscriber = new Subscriber
                {
                    UserId = user.Id,
                    FullName = fullName,
                    AddressLine1 = "123 Demo Street",
                    City = "Demo City",
                    StateCode = states[0].Code,
                    ZipCode = "00000",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.ToString("o")
                };
                db.Subscribers.Add(subscriber);
                await db.SaveChangesAsync();
            }

            var existingCodes = await db.SubscribedStates
                .Where(ss => ss.SubscriberId == subscriber.Id)
                .Select(ss => ss.StateCode)
                .ToHashSetAsync(StringComparer.OrdinalIgnoreCase);

            foreach (var (code, name) in states)
            {
                if (!existingCodes.Contains(code))
                {
                    db.SubscribedStates.Add(new SubscribedState
                    {
                        SubscriberId = subscriber.Id,
                        StateCode = code,
                        StateName = name,
                        IsActive = true,
                        StartDate = DateTime.UtcNow.ToString("o")
                    });
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
