namespace Blade.Server.Data;

public sealed record IncidentRecord(
    int Id, string CaseNumber, string OffenseType, string OffenseDescription, string District, int LocationId, string PremiseType,
    DateTime OccurredFrom, DateTime? OccurredTo, DateTime ReportedAt, string[] MoTags, string Status, string Sensitivity,
    string Narrative, int DeputyId);

public sealed record LocationRecord(int Id, string Address, string City, string District, string PremiseType, string? BusinessName, string? BusinessType, double X, double Y)
{
    public string Display => BusinessName is null ? $"{Address}, {City}" : $"{BusinessName}, {Address}, {City}";
}

public sealed record PersonRecord(int Id, string First, string? Middle, string Last, DateOnly Dob, string Sex, int? HomeLocationId, string? Phone, string? Notes)
{
    public string FullName => Middle is null ? $"{First} {Last}" : Middle.Length == 1 ? $"{First} {Middle}. {Last}" : $"{First} {Middle} {Last}";
}

public sealed record VehicleRecord(int Id, string Plate, int Year, string Make, string Model, string Color, string Style, int? OwnerPersonId, string? Notes)
{
    public string Description => $"{Color} {Year} {Make} {Model}";
}

public sealed record DeputyRecord(int Id, string Badge, string First, string Last, string Rank, string Unit, string District)
{
    public string Display => $"{Rank} {First} {Last} #{Badge}";
}

public sealed record IncidentPersonLink(int IncidentId, int PersonId, string Role);
public sealed record IncidentVehicleLink(int IncidentId, int VehicleId, string Role);
public sealed record PropertyRecord(int IncidentId, string Description, string? Serial, double Value, string Status);

public sealed record FieldInterviewRecord(int Id, string FiNumber, DateTime ContactAt, int LocationId, string District, int PersonId, int? VehicleId, string Reason, string Notes);

public sealed record BookingRecord(int Id, string BookingNumber, int PersonId, DateTime BookedAt, DateTime? ReleasedAt, string Charges, int? IncidentId,
    string HousingUnit, string Classification, int? KeepSeparatePersonId, string Notes, int[] CoArrestees);

public sealed record PawnRecord(int Id, string TicketNumber, DateTime TransactionAt, int ShopLocationId, int PersonId, string TransactionType,
    string ItemDescription, string? Serial, double Amount, string? SellerPhone);

public sealed record LprCameraRecord(int Id, string Name, int LocationId, string District);
public sealed record LprReadRecord(int Id, DateTime ReadAt, string Plate, int? VehicleId, int CameraId);

public sealed record CadCallRecord(int Id, string CallNumber, DateTime ReceivedAt, string CallType, int LocationId, string Disposition, int? IncidentId, string Notes);

public sealed record PolicySectionRecord(string PolicyNumber, string PolicyTitle, string SectionNumber, string Heading, int Level, string Text);
