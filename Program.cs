using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;


var builder = WebApplication.CreateBuilder(args);

// add swagger para testes
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// swagger para desenvolvimento
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// https
app.UseHttpsRedirection();

// =============================
// código armazenado em listas, mémoria
// =============================
var motorcycles = new List<Motorcycle>();
var deliveryDrivers = new List<DeliveryDriver>();
var rentals = new List<Rental>();
var notifications = new List<Notification>();

// =============================
// crud da moto
// =============================
app.MapPost("/motorcycles", (MotorcycleRequest request) =>
{
    // Prevent duplicate license plates
    if (motorcycles.Any(m => m.LicensePlate.Equals(request.LicensePlate, StringComparison.OrdinalIgnoreCase)))
        return Results.Conflict("License plate already exists");

    var motorcycle = new Motorcycle
    {
        Id = request.Id,
        Year = request.Year,
        Model = request.Model,
        LicensePlate = request.LicensePlate
    };

    motorcycles.Add(motorcycle);

    // Generate notification if motorcycle is 2024
    if (request.Year == 2024)
    {
        notifications.Add(new Notification
        {
            MotorcycleId = motorcycle.Id,
            Year = motorcycle.Year
        });
    }

    return Results.Created($"/motorcycles/{motorcycle.Id}", motorcycle);
});

app.MapGet("/motorcycles", (string? licensePlate) =>
{
    var query = motorcycles.AsQueryable();
    if (!string.IsNullOrEmpty(licensePlate))
        query = query.Where(m => m.LicensePlate.Contains(licensePlate, StringComparison.OrdinalIgnoreCase));

    return Results.Ok(query.ToList());
});

app.MapGet("/motorcycles/{id}", (string id) =>
{
    var motorcycle = motorcycles.FirstOrDefault(m => m.Id == id);
    return motorcycle is not null ? Results.Ok(motorcycle) : Results.NotFound();
});

app.MapPut("/motorcycles/{id}", (string id, MotorcycleRequest request) =>
{
    var motorcycle = motorcycles.FirstOrDefault(m => m.Id == id);
    if (motorcycle == null)
        return Results.NotFound();

    // previni 2 placas iguais
    if (motorcycles.Any(m => m.LicensePlate.Equals(request.LicensePlate, StringComparison.OrdinalIgnoreCase) && m.Id != id))
        return Results.Conflict("License plate already exists");

    motorcycle.LicensePlate = request.LicensePlate;
    motorcycle.Model = request.Model;
    motorcycle.Year = request.Year;

    return Results.Ok(motorcycle);
});

app.MapDelete("/motorcycles/{id}", (string id) =>
{
    var motorcycle = motorcycles.FirstOrDefault(m => m.Id == id);
    if (motorcycle == null)
        return Results.NotFound();

    // Prevent deletion if motorcycle has active rentals
    if (rentals.Any(r => r.MotorcycleId == id && r.Active))
        return Results.BadRequest("Motorcycle has active rentals");

    motorcycles.Remove(motorcycle);
    return Results.NoContent();
});

// =============================
// crud entregador
// =============================
app.MapPost("/drivers", (DriverRequest request) =>
{
    if (deliveryDrivers.Any(d => d.Cnpj == request.Cnpj))
        return Results.Conflict("CNPJ already exists");

    if (deliveryDrivers.Any(d => d.DriverLicenseNumber == request.DriverLicenseNumber))
        return Results.Conflict("Driver license number already exists");

    if (!new[] { "A", "B", "AB" }.Contains(request.DriverLicenseType))
        return Results.BadRequest("Invalid driver license type. Must be A, B, or AB");

    var driver = new DeliveryDriver
    {
        Id = request.Id,
        Name = request.Name,
        Cnpj = request.Cnpj,
        BirthDate = request.BirthDate,
        DriverLicenseNumber = request.DriverLicenseNumber,
        DriverLicenseType = request.DriverLicenseType
    };

    deliveryDrivers.Add(driver);

    return Results.Created($"/drivers/{driver.Id}", driver);
});

app.MapGet("/drivers", () => Results.Ok(deliveryDrivers));

app.MapGet("/drivers/{id}", (string id) =>
{
    var driver = deliveryDrivers.FirstOrDefault(d => d.Id == id);
    return driver is not null ? Results.Ok(driver) : Results.NotFound();
});

app.MapPut("/drivers/{id}", (string id, DriverRequest request) =>
{
    var driver = deliveryDrivers.FirstOrDefault(d => d.Id == id);
    if (driver == null)
        return Results.NotFound();

    if (deliveryDrivers.Any(d => d.Cnpj == request.Cnpj && d.Id != id))
        return Results.Conflict("CNPJ already exists");

    if (deliveryDrivers.Any(d => d.DriverLicenseNumber == request.DriverLicenseNumber && d.Id != id))
        return Results.Conflict("Driver license number already exists");

    driver.Name = request.Name;
    driver.Cnpj = request.Cnpj;
    driver.BirthDate = request.BirthDate;
    driver.DriverLicenseNumber = request.DriverLicenseNumber;
    driver.DriverLicenseType = request.DriverLicenseType;

    return Results.Ok(driver);
});

app.MapDelete("/drivers/{id}", (string id) =>
{
    var driver = deliveryDrivers.FirstOrDefault(d => d.Id == id);
    if (driver == null)
        return Results.NotFound();

    if (rentals.Any(r => r.DriverId == id && r.Active))
        return Results.BadRequest("Driver has active rentals");

    deliveryDrivers.Remove(driver);
    return Results.NoContent();
});

// =============================
// crud do aluguel
// =============================
app.MapPost("/rentals", (RentalRequest request) =>
{
    var driver = deliveryDrivers.FirstOrDefault(d => d.Id == request.DriverId);
    if (driver == null)
        return Results.NotFound("Driver not found");

    if (!driver.DriverLicenseType.Contains("A"))
        return Results.BadRequest("Driver not qualified for category A motorcycles");

    var motorcycle = motorcycles.FirstOrDefault(m => m.Id == request.MotorcycleId);
    if (motorcycle == null)
        return Results.NotFound("Motorcycle not found");

    if (rentals.Any(r => r.MotorcycleId == request.MotorcycleId && r.Active))
        return Results.BadRequest("Motorcycle is already rented");

    if (rentals.Any(r => r.DriverId == request.DriverId && r.Active))
        return Results.BadRequest("Driver already has an active rental");

    decimal dailyRate = request.PlanDays switch
    {
        7 => 30.00m,
        15 => 28.00m,
        30 => 22.00m,
        45 => 20.00m,
        50 => 18.00m,
        _ => 0
    };

    if (dailyRate == 0)
        return Results.BadRequest("Invalid rental plan");

    var startDate = DateTime.UtcNow.Date.AddDays(1); // rental starts the next day
    var expectedEndDate = startDate.AddDays(request.PlanDays);
    var totalValue = dailyRate * request.PlanDays;

    var rental = new Rental
    {
        DriverId = request.DriverId,
        MotorcycleId = request.MotorcycleId,
        StartDate = startDate,
        ExpectedEndDate = expectedEndDate,
        EndDate = expectedEndDate,
        PlanDays = request.PlanDays,
        DailyRate = dailyRate,
        TotalValue = totalValue
    };

    rentals.Add(rental);

    return Results.Created($"/rentals/{rental.Id}", rental);
});

app.MapGet("/rentals", () => Results.Ok(rentals));

app.MapPost("/rentals/{id}/return", (string id, ReturnRequest request) =>
{
    var rental = rentals.FirstOrDefault(r => r.Id == id && r.Active);
    if (rental == null)
        return Results.NotFound("Rental not found or already closed");

    rental.Active = false;
    rental.EndDate = request.ReturnDate;

    decimal penalty = 0;
    decimal extraCharge = 0;
    decimal totalValue = rental.TotalValue;

    if (request.ReturnDate < rental.ExpectedEndDate)
    {
        var earlyDays = (rental.ExpectedEndDate - request.ReturnDate).Days;
        var unusedDailyValue = earlyDays * rental.DailyRate;

        penalty = rental.PlanDays switch
        {
            7 => unusedDailyValue * 0.2m,
            15 => unusedDailyValue * 0.4m,
            _ => 0
        };

        totalValue = rental.TotalValue - unusedDailyValue + penalty;
    }
    else if (request.ReturnDate > rental.ExpectedEndDate)
    {
        var extraDays = (request.ReturnDate - rental.ExpectedEndDate).Days;
        extraCharge = extraDays * 50.00m;
        totalValue = rental.TotalValue + extraCharge;
    }

    return Results.Ok(new ReturnResponse(totalValue, penalty, extraCharge));
});

// =============================
// notificacao
// =============================
app.MapGet("/notifications", () => Results.Ok(notifications));

// =============================
// HEALTH CHECK
// =============================
app.MapGet("/health", () => Results.Ok("API is running!"));

app.Run();

// =============================
// classes 
// =============================
public record MotorcycleRequest(string Id, int Year, string Model, string LicensePlate);
public record DriverRequest(string Id, string Name, string Cnpj, DateTime BirthDate, string DriverLicenseNumber, string DriverLicenseType);
public record RentalRequest(string DriverId, string MotorcycleId, int PlanDays);
public record ReturnRequest(DateTime ReturnDate);
public record ReturnResponse(decimal TotalValue, decimal Penalty, decimal ExtraCharge);

public class Motorcycle
{
    public string Id { get; set; } = default!;
    public int Year { get; set; }
    public string Model { get; set; } = default!;
    public string LicensePlate { get; set; } = default!;
}

public class DeliveryDriver
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Cnpj { get; set; } = default!;
    public DateTime BirthDate { get; set; }
    public string DriverLicenseNumber { get; set; } = default!;
    public string DriverLicenseType { get; set; } = default!;
}

public class Rental
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DriverId { get; set; } = default!;
    public string MotorcycleId { get; set; } = default!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime ExpectedEndDate { get; set; }
    public int PlanDays { get; set; }
    public decimal DailyRate { get; set; }
    public decimal TotalValue { get; set; }
    public bool Active { get; set; } = true;
}

public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MotorcycleId { get; set; } = default!;
    public int Year { get; set; }
    public DateTime NotificationDate { get; set; } = DateTime.UtcNow;
}
