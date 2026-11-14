namespace Blade.Core;

/// <summary>
/// Controlled vocabulary shared by the data generator, the server's query planner
/// (LLM JSON-schema enums) and the desktop UI. Keeping one source of truth means the
/// model can only ever emit values the database actually contains.
/// </summary>
public static class Vocabulary
{
    public static readonly string[] Districts = ["NORTH", "SOUTH", "EAST", "WEST"];

    public static readonly string[] OffenseTypes =
    [
        "BURGLARY", "ROBBERY", "THEFT", "THEFT_FROM_VEHICLE", "AUTO_THEFT",
        "ASSAULT", "VANDALISM", "NARCOTICS", "DOMESTIC"
    ];

    public static readonly string[] PremiseTypes = ["RESIDENTIAL", "COMMERCIAL", "VEHICLE", "PUBLIC", "SCHOOL"];

    public static readonly string[] MoTags =
    [
        "FORCED_ENTRY", "PRY_TOOL", "WINDOW_SMASH", "REAR_ENTRY", "ALARM_DEFEATED",
        "UNLOCKED_ENTRY", "WEAPON_KNIFE", "WEAPON_FIREARM", "CATALYTIC_CONVERTER"
    ];

    public static readonly string[] PersonRoles = ["SUSPECT", "VICTIM", "WITNESS", "REPORTING_PARTY", "ARRESTEE"];

    public static readonly string[] VehicleRoles = ["SUSPECT_VEHICLE", "VICTIM_VEHICLE", "STOLEN", "OBSERVED"];

    public static readonly string[] CaseStatuses = ["OPEN", "SUSPENDED", "CLEARED_ARREST", "CLEARED_EXCEPTIONAL", "UNFOUNDED"];

    /// <summary>Record sensitivity drives role-based filtering before anything reaches a model.</summary>
    public static readonly string[] Sensitivities = ["NORMAL", "JUVENILE", "RESTRICTED"];
}

public static class Agency
{
    public const string Name = "Kestrel County Sheriff's Office";
    public const string ShortName = "KCSO";
    public const string ProductName = "BLADE";
    public const string ProductLongName = "Bureau Linked Analytics & Data Explorer";
    public const string SyntheticDataBanner = "SYNTHETIC DATA — FICTIONAL AGENCY";

    /// <summary>The "as of" date the synthetic data set is anchored to (data covers the 24 months before it).</summary>
    public static readonly DateOnly DataAsOf = new(2026, 11, 14);
}
