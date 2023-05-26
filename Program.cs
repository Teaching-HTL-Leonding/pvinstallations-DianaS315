using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PvContext>(options =>
    options.UseSqlServer(builder.Configuration["ConnectionStrings:DefaultConnection"])
);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapPost("/installations", async (PvContext context, AddPvInstallationDto newPvInstallation) =>
{

    if (-90 > newPvInstallation.Latitude || newPvInstallation.Latitude > 90) { return Results.BadRequest("Latitude has to be between -90 and 90"); }
    if (-180 > newPvInstallation.Longitude || newPvInstallation.Longitude > 180) { return Results.BadRequest("Longitude has to be between -180 and 180"); }
    if (newPvInstallation.Address == null) { return Results.BadRequest("Address cannot be empty"); }
    if (newPvInstallation.Address.Length > 1024) { return Results.BadRequest("Address is to long; enter a maximum of 1024 characters! "); }
    if (newPvInstallation.OwnerName == null) { return Results.BadRequest("Name of the Owner cannot be empty"); }
    if (newPvInstallation.OwnerName.Length > 512) { return Results.BadRequest("Name is to long; enter a maximum of 512 characters!"); }

    var dbPvInstallation = new PvInstallation
    {
        Longitude = newPvInstallation.Longitude,
        Latitude = newPvInstallation.Latitude,
        Address = newPvInstallation.Address,
        OwnerName = newPvInstallation.OwnerName,
        isActive = true,
        Comments = newPvInstallation.Comments
    };

    await context.pvInstallations.AddAsync(dbPvInstallation);
    await context.SaveChangesAsync();

    return Results.Ok(dbPvInstallation.ID);
});

app.MapPost("/installations/{id}/deactivate", async (PvContext context, int id) =>
{

    var pvInstallation = await context.pvInstallations.FirstOrDefaultAsync(p => p.ID == id);
    if (pvInstallation == null) { return Results.NotFound(); }

    pvInstallation.isActive = false;
    context.pvInstallations.Update(pvInstallation);
    await context.SaveChangesAsync();

    return Results.Ok(pvInstallation);
});

app.MapPost("/installations/{id}/reports", async (PvContext context, int id, AddProductionReportDto newProduction) =>
{

    if (newProduction.BatteryWattage < 0) { return Results.BadRequest("Battery Wattage cannot be negative!"); }
    if (newProduction.GridWattage < 0) { return Results.BadRequest("Grid Wattage cannot be negative!"); }
    if (newProduction.HouseholdWattage < 0) { return Results.BadRequest("Household Wattage cannot be negative!"); }
    if (newProduction.ProducedWattage < 0) { return Results.BadRequest("Produced Wattage cannot be negative!"); }

    var installation = await context.pvInstallations.FirstOrDefaultAsync(p => p.ID == id);
    if (installation == null) { return Results.NotFound(); }

    var dbProductionReport = new ProductionReport
    {
        Timestamp = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0),
        ProducedWattage = newProduction.ProducedWattage,
        HouseholdWattage = newProduction.HouseholdWattage,
        BatteryWattage = newProduction.BatteryWattage,
        GridWattage = newProduction.GridWattage,
        PvInstallationId = id
    };

    context.productionReports.Add(dbProductionReport);
    await context.SaveChangesAsync();

    return Results.Ok(dbProductionReport);

});

app.MapGet("/installations/{id}/reports", async (PvContext context, int id, DateTime timestamp, int duration) => {c
     if (duration < 0) { return Results.BadRequest("Duration has to be greater than 0!"); }

    var installation = await context.pvInstallations.FindAsync(id);
    if (installation == null) { return Results.NotFound(); }

    var allProducedWattage = await context.productionReports
    .Where(pr => pr.PvInstallationId == id)
    .Where(pr => pr.Timestamp >= timestamp && pr.Timestamp < timestamp.AddMinutes(duration))
    .SumAsync(pr => pr.ProducedWattage);

    return Results.Ok(allProducedWattage);
});

app.MapGet("/installations/{id}/timeline", async (PvContext context,  int id, DateTime startTimestamp, int duration, int page) =>
{

    if (duration < 0) { return Results.BadRequest("Duration has to be greater than 0!"); }
    if (page < 1) { return Results.BadRequest("Page has to be greater than 1!"); }

    var installation = await context.pvInstallations.FirstOrDefaultAsync(p => p.ID == id);
    if (installation == null) { return Results.NotFound(); }

    const int pageSize = 60;
    var endTimestamp = startTimestamp.AddMinutes(Math.Min(duration, pageSize * page));
    startTimestamp += TimeSpan.FromMinutes(pageSize * (page - 1));

    var timelineData = await context.productionReports
        .Where(r => r.PvInstallationId == id)
        .Where(r => r.Timestamp >= startTimestamp && r.Timestamp < endTimestamp)
        .ToListAsync();

    for (; startTimestamp < endTimestamp; startTimestamp = startTimestamp.AddMinutes(1))
    {
        if (!timelineData.Any(r => r.Timestamp == startTimestamp))
        {
            timelineData.Add(new() { Timestamp = startTimestamp });
        }
    }
    
    
    timelineData.OrderBy(r => r.Timestamp);

    return Results.Ok(timelineData);
});

app.Run();

record AddPvInstallationDto(float Longitude, float Latitude, string Address, string OwnerName, bool isActive, string? Comments);

record AddProductionReportDto(float ProducedWattage, float HouseholdWattage, float BatteryWattage, float GridWattage, int PvInstallationId);

class PvInstallation
{
    public int ID { get; set; }

    public float Longitude { get; set; }

    public float Latitude { get; set; }

    [MaxLength(1024)]
    public string Address { get; set; } = "";

    [MaxLength(512)]
    public string OwnerName { get; set; } = "";

    public Boolean isActive { get; set; }

    [MaxLength(1024)]
    public string? Comments { get; set; } = "";

    public List<ProductionReport> ProductionReports { get; set; } = new();

}

class ProductionReport
{
    public int ID { get; set; }

    public DateTime Timestamp { get; set; }

    public float ProducedWattage { get; set; }

    public float HouseholdWattage { get; set; }

    public float BatteryWattage { get; set; }

    public float GridWattage { get; set; }

    public int PvInstallationId { get; set; }

    public PvInstallation? PvInstallation { get; set; }
}

class PvContext : DbContext
{
    public PvContext(DbContextOptions<PvContext> options) : base(options) { }

    public DbSet<PvInstallation> pvInstallations => Set<PvInstallation>();

    public DbSet<ProductionReport> productionReports => Set<ProductionReport>();

}