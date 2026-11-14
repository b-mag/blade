namespace Blade.DataGen.Model;

public sealed class Location
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string Street { get; set; } = "";
    public string? Unit { get; set; }
    public string City { get; set; } = "";
    public string District { get; set; } = "";
    public string PremiseType { get; set; } = "";
    public string? BusinessName { get; set; }
    public string? BusinessType { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string Address => Unit is null ? $"{Number} {Street}" : $"{Number} {Street} {Unit}";
    public string Display => BusinessName is null ? $"{Address}, {City}" : $"{BusinessName}, {Address}, {City}";
}

public sealed class Person
{
    public int Id { get; set; }
    public string First { get; set; } = "";
    public string? Middle { get; set; }
    public string Last { get; set; } = "";
    public DateOnly Dob { get; set; }
    public string Sex { get; set; } = "";
    public int? HomeLocationId { get; set; }
    public string? Phone { get; set; }
    public string DlNumber { get; set; } = "";
    public string? Alias { get; set; }
    public string? Notes { get; set; }
    public string FullName => Middle is null ? $"{First} {Last}" : Middle.Length == 1 ? $"{First} {Middle}. {Last}" : $"{First} {Middle} {Last}";
    public string LastFirst => $"{Last.ToUpperInvariant()}, {First}";
}

public sealed class Vehicle
{
    public int Id { get; set; }
    public string Plate { get; set; } = "";
    public int Year { get; set; }
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string Color { get; set; } = "";
    public string Style { get; set; } = "";
    public int? OwnerPersonId { get; set; }
    public string? Notes { get; set; }
    public string Description => $"{Color} {Year} {Make} {Model}";
}

public sealed class Deputy
{
    public int Id { get; set; }
    public string Badge { get; set; } = "";
    public string First { get; set; } = "";
    public string Last { get; set; } = "";
    public string Rank { get; set; } = "";
    public string Unit { get; set; } = "";
    public string District { get; set; } = "";
    public string Display => $"{Rank} {First[0]}. {Last} #{Badge}";
}

public sealed class PropertyItem
{
    public string Description { get; set; } = "";
    public string? Serial { get; set; }
    public decimal Value { get; set; }
    public string Status { get; set; } = "STOLEN";
}

public sealed class Incident
{
    public int Id { get; set; }
    public string CaseNumber { get; set; } = "";
    public DateTime OccurredFrom { get; set; }
    public DateTime? OccurredTo { get; set; }
    public DateTime ReportedAt { get; set; }
    public string OffenseType { get; set; } = "";
    public string OffenseCode { get; set; } = "";
    public string OffenseDescription { get; set; } = "";
    public string District { get; set; } = "";
    public Location Location { get; set; } = null!;
    public string PremiseType { get; set; } = "";
    public List<string> MoTags { get; } = [];
    public string Status { get; set; } = "OPEN";
    public Deputy Deputy { get; set; } = null!;
    public string Narrative { get; set; } = "";
    public string Sensitivity { get; set; } = "NORMAL";
    public List<(Person Person, string Role)> Persons { get; } = [];
    public List<(Vehicle Vehicle, string Role)> Vehicles { get; } = [];
    public List<PropertyItem> Property { get; } = [];
    /// <summary>Ground truth for tests/evaluation only — written to a separate table the server never reads.</summary>
    public string? Storyline { get; set; }
}

public sealed class CadCall
{
    public int Id { get; set; }
    public string CallNumber { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public string CallType { get; set; } = "";
    public int Priority { get; set; }
    public Location Location { get; set; } = null!;
    public string Disposition { get; set; } = "";
    public Incident? Incident { get; set; }
    public Deputy? Deputy { get; set; }
    public string Notes { get; set; } = "";
    public string? CallerPhone { get; set; }
    public string? Storyline { get; set; }
}

public sealed class FieldInterview
{
    public int Id { get; set; }
    public string FiNumber { get; set; } = "";
    public DateTime ContactAt { get; set; }
    public Location Location { get; set; } = null!;
    public Deputy Deputy { get; set; } = null!;
    public Person Person { get; set; } = null!;
    public Vehicle? Vehicle { get; set; }
    public string Reason { get; set; } = "";
    public string Notes { get; set; } = "";
    public string? Storyline { get; set; }
}

public sealed class LprCamera
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public Location Location { get; set; } = null!;
}

public sealed class LprRead
{
    public int Id { get; set; }
    public DateTime ReadAt { get; set; }
    public string Plate { get; set; } = "";
    public Vehicle? Vehicle { get; set; }
    public LprCamera Camera { get; set; } = null!;
    public string? Storyline { get; set; }
}

public sealed class Booking
{
    public int Id { get; set; }
    public string BookingNumber { get; set; } = "";
    public Person Person { get; set; } = null!;
    public DateTime BookedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string Charges { get; set; } = "";
    public Incident? Incident { get; set; }
    public string HousingUnit { get; set; } = "";
    public string Classification { get; set; } = "";
    public Person? KeepSeparateFrom { get; set; }
    public string Notes { get; set; } = "";
    public List<Person> CoArrestees { get; } = [];
    public string? Storyline { get; set; }
}

public sealed class PawnTransaction
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = "";
    public DateTime TransactionAt { get; set; }
    public Location Shop { get; set; } = null!;
    public Person Person { get; set; } = null!;
    public string TransactionType { get; set; } = "PAWN";
    public string ItemDescription { get; set; } = "";
    public string? Serial { get; set; }
    public decimal Amount { get; set; }
    public string? SellerPhone { get; set; }
    public string? Storyline { get; set; }
}
