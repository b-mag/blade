using Blade.DataGen.Model;
using Blade.DataGen.Reference;

namespace Blade.DataGen.Generation;

/// <summary>
/// Holds every generated record plus a single seeded RNG, so the same seed always produces the same data set.
/// </summary>
public sealed class World
{
    public World(int seed, DateOnly asOf, int months = 24)
    {
        Rng = new Random(seed);
        End = asOf.ToDateTime(new TimeOnly(23, 59));
        Start = asOf.AddMonths(-months).ToDateTime(TimeOnly.MinValue);
    }

    public Random Rng { get; }
    public DateTime Start { get; }
    public DateTime End { get; }

    public List<Location> Locations { get; } = [];
    public List<Person> Persons { get; } = [];
    public List<Vehicle> Vehicles { get; } = [];
    public List<Deputy> Deputies { get; } = [];
    public List<Incident> Incidents { get; } = [];
    public List<CadCall> CadCalls { get; } = [];
    public List<FieldInterview> FieldInterviews { get; } = [];
    public List<LprCamera> LprCameras { get; } = [];
    public List<LprRead> LprReads { get; } = [];
    public List<Booking> Bookings { get; } = [];
    public List<PawnTransaction> Pawns { get; } = [];

    /// <summary>Ground-truth facts about planted storylines (evaluation only).</summary>
    public List<(string Storyline, string EntityType, Func<string> EntityKey, string Note)> Truth { get; } = [];

    private static readonly string[] AreaCodes = ["620", "785", "316", "913", "417", "479", "580", "918", "402", "308", "719", "806"];
    private readonly HashSet<string> _plates = [];
    private readonly HashSet<string> _phones = [];

    // ---------------------------------------------------------------- random helpers
    public T Pick<T>(IReadOnlyList<T> items) => items[Rng.Next(items.Count)];
    public bool Chance(double p) => Rng.NextDouble() < p;
    public int Between(int minInclusive, int maxInclusive) => Rng.Next(minInclusive, maxInclusive + 1);

    public T Weighted<T>(IReadOnlyList<(T Item, int Weight)> items)
    {
        var total = items.Sum(i => i.Weight);
        var roll = Rng.Next(total);
        foreach (var (item, weight) in items)
        {
            if (roll < weight) return item;
            roll -= weight;
        }
        return items[^1].Item;
    }

    public List<T> PickSome<T>(IReadOnlyList<T> items, int count) =>
        items.OrderBy(_ => Rng.Next()).Take(Math.Min(count, items.Count)).ToList();

    public DateTime RandomDate() => Start.AddDays(Rng.NextDouble() * (End - Start).TotalDays).Date;

    /// <summary>Picks an hour from a 24-slot weight profile, then a random minute.</summary>
    public DateTime AtHour(DateTime date, int[] hourWeights)
    {
        var hour = Weighted(hourWeights.Select((w, h) => (h, w)).ToList());
        return date.Date.AddHours(hour).AddMinutes(Rng.Next(60));
    }

    public decimal Money(int min, int max, int roundTo = 5) => Math.Round((decimal)Between(min, max) / roundTo) * roundTo;

    // ---------------------------------------------------------------- factories
    public Person NewPerson(string? first = null, string? last = null, string? sex = null, int? minAge = null, int? maxAge = null,
        Location? home = null, string? phone = null, string? middle = null)
    {
        sex ??= Chance(0.55) ? "M" : "F";
        first ??= sex == "M" ? Pick(ReferenceData.FirstNamesMale) : Pick(ReferenceData.FirstNamesFemale);
        last ??= Pick(ReferenceData.LastNames);
        var age = Between(minAge ?? 18, maxAge ?? 72);
        var dob = DateOnly.FromDateTime(End).AddYears(-age).AddDays(-Rng.Next(365));
        home ??= Chance(0.92) ? Pick(Locations.Where(l => l.PremiseType == "RESIDENTIAL").ToList()) : null;

        var p = new Person
        {
            Id = Persons.Count + 1,
            First = first,
            Middle = middle ?? (Chance(0.6) ? ((char)('A' + Rng.Next(26))).ToString() : null),
            Last = last,
            Sex = sex,
            Dob = dob,
            HomeLocationId = home?.Id,
            Phone = phone ?? (Chance(0.8) ? NewPhone() : null),
            DlNumber = $"K{Rng.Next(10_000_000, 99_999_999)}",
        };
        Persons.Add(p);
        return p;
    }

    /// <summary>Phone numbers use the 555-01xx range reserved for fictional use, across several invented area codes.</summary>
    public string NewPhone()
    {
        // 12 area codes x 100 numbers = 1,200 unique values; uniqueness matters because a shared
        // phone number is a real investigative link in the graph and must only occur when planted.
        string phone;
        do
        {
            phone = $"({Pick(AreaCodes)}) 555-01{Rng.Next(100):00}";
        } while (!_phones.Add(phone));
        return phone;
    }

    public Vehicle NewVehicle(Person? owner = null, string? make = null, string? model = null, string? style = null,
        string? color = null, int? year = null, string? plate = null)
    {
        var m = make is null ? Pick(ReferenceData.VehicleModels) : (make, model ?? "", style ?? "");
        var v = new Vehicle
        {
            Id = Vehicles.Count + 1,
            Plate = plate ?? NewPlate(),
            Year = year ?? Between(2004, 2025),
            Make = m.Item1,
            Model = m.Item2,
            Style = m.Item3,
            Color = color ?? Pick(ReferenceData.VehicleColors),
            OwnerPersonId = owner?.Id,
        };
        _plates.Add(v.Plate);
        Vehicles.Add(v);
        return v;
    }

    public string NewPlate()
    {
        string plate;
        do
        {
            plate = $"{Rng.Next(100, 999)}{Pick(ReferenceData.PlateLetters)}{Pick(ReferenceData.PlateLetters)}{Pick(ReferenceData.PlateLetters)}";
        } while (!_plates.Add(plate));
        return plate;
    }

    public Location NewLocation(string district, string premiseType, string? street = null, int? number = null,
        string? businessName = null, string? businessType = null, string? unit = null)
    {
        var d = ReferenceData.Districts.Single(x => x.Code == district);
        street ??= premiseType == "RESIDENTIAL" ? Pick(d.ResidentialStreets) : Pick(d.CommercialStreets);
        var loc = new Location
        {
            Id = Locations.Count + 1,
            Number = number ?? (premiseType == "RESIDENTIAL" ? Between(100, 4999) : Between(100, 9800)),
            Street = street,
            Unit = unit,
            City = d.Town,
            District = district,
            PremiseType = premiseType,
            BusinessName = businessName,
            BusinessType = businessType,
            X = Math.Round(d.CenterX + (Rng.NextDouble() - 0.5) * 12, 3),
            Y = Math.Round(d.CenterY + (Rng.NextDouble() - 0.5) * 12, 3),
        };
        Locations.Add(loc);
        return loc;
    }

    public List<Location> LocationsIn(string district, string premiseType) =>
        Locations.Where(l => l.District == district && l.PremiseType == premiseType).ToList();

    public Deputy DeputyFor(string district) =>
        Pick(Deputies.Where(d => d.District == district && d.Unit == "Patrol").ToList());

    public Location HomeOf(Person p) => Locations[(p.HomeLocationId ?? Pick(Locations.Where(l => l.PremiseType == "RESIDENTIAL").ToList()).Id) - 1];

    public int AgeOn(Person p, DateTime when)
    {
        var d = DateOnly.FromDateTime(when);
        var age = d.Year - p.Dob.Year;
        if (p.Dob > d.AddYears(-age)) age--;
        return age;
    }
}
