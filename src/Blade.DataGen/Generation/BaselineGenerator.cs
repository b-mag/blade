using Blade.DataGen.Model;
using Blade.DataGen.Reference;

namespace Blade.DataGen.Generation;

/// <summary>
/// Generates the "background noise" of a mid-size sheriff's office: places, people, vehicles, deputies,
/// incidents, CAD calls, field interviews, LPR reads, jail bookings and pawn transactions.
/// Planted storylines are layered on top by <see cref="Storylines"/>.
/// </summary>
public sealed class BaselineGenerator(World w, NarrativeBuilder narratives)
{
    private sealed record OffenseProfile(string Type, string Code, string Description, int Weight);

    private static readonly OffenseProfile[] Offenses =
    [
        new("THEFT", "23C", "Larceny/Theft", 20),
        new("BURGLARY", "220", "Burglary/Breaking & Entering", 14),
        new("THEFT_FROM_VEHICLE", "23F", "Theft From Motor Vehicle", 12),
        new("ASSAULT", "13A", "Assault", 10),
        new("VANDALISM", "290", "Destruction/Damage/Vandalism of Property", 10),
        new("DOMESTIC", "13B", "Simple Assault — Domestic", 10),
        new("NARCOTICS", "35A", "Drug/Narcotic Violations", 8),
        new("AUTO_THEFT", "240", "Motor Vehicle Theft", 5),
        new("ROBBERY", "120", "Robbery", 4),
    ];

    //                                  0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19 20 21 22 23
    public static readonly int[] Night = [9, 9, 8, 7, 5, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3, 4, 6, 8, 9];
    public static readonly int[] Day = [1, 1, 1, 1, 1, 1, 2, 4, 7, 9, 9, 9, 9, 9, 9, 8, 7, 5, 3, 2, 2, 1, 1, 1];
    public static readonly int[] Evening = [4, 3, 2, 1, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 4, 5, 6, 7, 8, 9, 9, 8, 7, 6];
    public static readonly int[] Any = Enumerable.Repeat(1, 24).ToArray();

    private List<Person> _frequent = [];
    private List<Location> _pawnShops = [];
    private List<Location> _scrapYards = [];

    public IReadOnlyList<Location> PawnShops => _pawnShops;
    public IReadOnlyList<Location> ScrapYards => _scrapYards;

    public void Generate(int incidentCount)
    {
        BuildLocations();
        BuildDeputies();
        BuildPeopleAndVehicles();
        BuildLprCameras();
        for (var n = 0; n < incidentCount; n++) NewIncident();
        BuildFillerCadCalls(2600);
        BuildFieldInterviews(300);
        BuildLprReads(1600);
        BuildFillerBookings(220);
        BuildPawns(190);
    }

    // ------------------------------------------------------------------ places, people, vehicles
    private void BuildLocations()
    {
        var usedNames = new HashSet<string>();
        foreach (var d in ReferenceData.Districts)
        {
            for (var n = 0; n < 112; n++) w.NewLocation(d.Code, "RESIDENTIAL", unit: w.Chance(0.12) ? $"Apt {w.Between(1, 24)}" : null);
            for (var n = 0; n < 36; n++)
            {
                var type = w.Weighted(ReferenceData.BusinessTypes);
                string name;
                do { name = $"{w.Pick(ReferenceData.BusinessNamePrefixes)} {type}"; } while (!usedNames.Add(name));
                w.NewLocation(d.Code, "COMMERCIAL", businessName: name, businessType: type);
            }
            foreach (var park in new[] { $"{d.Town} City Park", $"{d.Town} Trailhead Lot", $"{d.Town} Public Library" })
                w.NewLocation(d.Code, "PUBLIC", businessName: park, businessType: "Public Facility");
            w.NewLocation(d.Code, "SCHOOL", businessName: $"{d.Town} High School", businessType: "School");
            w.NewLocation(d.Code, "SCHOOL", businessName: $"{d.Town} Middle School", businessType: "School");
        }
        _pawnShops =
        [
            w.NewLocation("NORTH", "COMMERCIAL", "Northgate Blvd", 4410, "Gold Standard Pawn", "Pawn Shop"),
            w.NewLocation("SOUTH", "COMMERCIAL", "Brannock Pike", 1820, "Riverside Pawn & Loan", "Pawn Shop"),
            w.NewLocation("EAST", "COMMERCIAL", "Eastgate Blvd", 2275, "Ashby Pawn & Jewelry", "Pawn Shop"),
            w.NewLocation("WEST", "COMMERCIAL", "Westridge Pkwy", 990, "Frontier Pawn", "Pawn Shop"),
        ];
        _scrapYards =
        [
            w.NewLocation("EAST", "COMMERCIAL", "Railyard Rd", 6120, "Railyard Metal Recycling", "Scrap Metal Recycler"),
            w.NewLocation("SOUTH", "COMMERCIAL", "Levee Rd", 8800, "Delta Scrap & Salvage", "Scrap Metal Recycler"),
        ];
    }

    private void BuildDeputies()
    {
        var badge = 1100;
        foreach (var d in ReferenceData.Districts)
            for (var n = 0; n < 15; n++)
            {
                badge += w.Between(3, 17);
                var sex = w.Chance(0.8) ? "M" : "F";
                w.Deputies.Add(new Deputy
                {
                    Id = w.Deputies.Count + 1,
                    Badge = badge.ToString(),
                    First = sex == "M" ? w.Pick(ReferenceData.FirstNamesMale) : w.Pick(ReferenceData.FirstNamesFemale),
                    Last = w.Pick(ReferenceData.LastNames),
                    Rank = w.Pick(ReferenceData.DeputyRanks),
                    Unit = n < 11 ? "Patrol" : w.Pick(ReferenceData.DeputyUnits),
                    District = d.Code,
                });
            }
    }

    private void BuildPeopleAndVehicles()
    {
        for (var n = 0; n < 820; n++) w.NewPerson();
        // Juveniles (records are sensitivity-flagged when they appear as suspects)
        for (var n = 0; n < 40; n++) w.NewPerson(minAge: 13, maxAge: 17);
        // A pool of repeat offenders gives the graph organic (non-planted) connections.
        _frequent = w.PickSome(w.Persons.Where(p => w.AgeOn(p, w.End) is >= 15 and <= 48).ToList(), 150);

        foreach (var owner in w.PickSome(w.Persons.Where(p => w.AgeOn(p, w.End) >= 18).ToList(), 430))
            w.NewVehicle(owner);
    }

    private void BuildLprCameras()
    {
        foreach (var d in ReferenceData.Districts)
            foreach (var street in w.PickSome(d.CommercialStreets, 3))
            {
                var loc = w.NewLocation(d.Code, "PUBLIC", street, w.Between(100, 9000), $"LPR Camera — {street}", "LPR Camera");
                w.LprCameras.Add(new LprCamera { Id = w.LprCameras.Count + 1, Name = $"{d.Code[0]}-{street.ToUpperInvariant().Replace(' ', '-')}", Location = loc });
            }
    }

    // ------------------------------------------------------------------ incidents
    private void NewIncident()
    {
        var prof = w.Weighted(Offenses.Select(o => (o, o.Weight)).ToList());
        var district = w.Weighted(new List<(string, int)> { ("NORTH", 30), ("SOUTH", 25), ("EAST", 25), ("WEST", 20) });
        var date = w.RandomDate();
        var i = new Incident { OffenseType = prof.Type, OffenseCode = prof.Code, OffenseDescription = prof.Description, District = district };

        Person? victim = null;
        switch (prof.Type)
        {
            case "BURGLARY":
                i.PremiseType = w.Chance(0.55) ? "RESIDENTIAL" : "COMMERCIAL";
                if (i.PremiseType == "COMMERCIAL")
                {
                    i.OccurredFrom = w.AtHour(date, Night);
                    if (w.Chance(0.55)) i.MoTags.Add("REAR_ENTRY");
                    var mo = w.Weighted(new List<(string, int)> { ("PRY_TOOL", 40), ("WINDOW_SMASH", 30), ("FORCED_ENTRY", 15), ("UNLOCKED_ENTRY", 15) });
                    i.MoTags.Add(mo);
                    if (mo is "PRY_TOOL" or "WINDOW_SMASH") i.MoTags.Add("FORCED_ENTRY");
                    if (w.Chance(0.15)) i.MoTags.Add("ALARM_DEFEATED");
                }
                else
                {
                    i.OccurredFrom = w.AtHour(date, Day);
                    if (w.Chance(0.3)) i.MoTags.Add("REAR_ENTRY");
                    var mo = w.Weighted(new List<(string, int)> { ("FORCED_ENTRY", 40), ("PRY_TOOL", 20), ("WINDOW_SMASH", 15), ("UNLOCKED_ENTRY", 25) });
                    i.MoTags.Add(mo);
                    if (mo is "PRY_TOOL" or "WINDOW_SMASH") i.MoTags.Add("FORCED_ENTRY");
                }
                break;
            case "THEFT":
                i.PremiseType = w.Chance(0.6) ? "COMMERCIAL" : "RESIDENTIAL";
                i.OccurredFrom = w.AtHour(date, Day);
                break;
            case "THEFT_FROM_VEHICLE":
                i.PremiseType = "VEHICLE";
                i.OccurredFrom = w.AtHour(date, Night);
                if (w.Chance(0.1)) i.MoTags.Add("CATALYTIC_CONVERTER");
                else if (w.Chance(0.6)) { i.MoTags.Add("WINDOW_SMASH"); i.MoTags.Add("FORCED_ENTRY"); }
                else i.MoTags.Add("UNLOCKED_ENTRY");
                break;
            case "AUTO_THEFT":
                i.PremiseType = "VEHICLE";
                i.OccurredFrom = w.AtHour(date, Night);
                break;
            case "ASSAULT":
                i.PremiseType = w.Weighted(new List<(string, int)> { ("PUBLIC", 35), ("COMMERCIAL", 35), ("RESIDENTIAL", 25), ("SCHOOL", 5) });
                i.OccurredFrom = w.AtHour(date, Evening);
                if (w.Chance(0.1)) i.MoTags.Add("WEAPON_KNIFE");
                else if (w.Chance(0.05)) i.MoTags.Add("WEAPON_FIREARM");
                break;
            case "ROBBERY":
                i.PremiseType = w.Chance(0.6) ? "COMMERCIAL" : "PUBLIC";
                i.OccurredFrom = w.AtHour(date, Evening);
                i.MoTags.Add(w.Weighted(new List<(string, int)> { ("WEAPON_FIREARM", 50), ("WEAPON_KNIFE", 25), ("NONE", 25) }));
                i.MoTags.Remove("NONE");
                break;
            case "VANDALISM":
                i.PremiseType = w.Weighted(new List<(string, int)> { ("RESIDENTIAL", 40), ("COMMERCIAL", 30), ("VEHICLE", 15), ("SCHOOL", 5), ("PUBLIC", 10) });
                i.OccurredFrom = w.AtHour(date, Night);
                break;
            case "NARCOTICS":
                i.PremiseType = w.Chance(0.6) ? "VEHICLE" : "PUBLIC";
                i.OccurredFrom = w.AtHour(date, Evening);
                break;
            case "DOMESTIC":
                i.PremiseType = "RESIDENTIAL";
                i.OccurredFrom = w.AtHour(date, Evening);
                if (w.Chance(0.06)) i.MoTags.Add("WEAPON_KNIFE");
                break;
        }

        // Location + primary persons
        if (i.PremiseType == "RESIDENTIAL")
        {
            victim = w.Pick(w.Persons.Where(p => p.HomeLocationId is not null && w.AgeOn(p, date) >= 18).ToList());
            i.Location = w.HomeOf(victim);
            i.District = i.Location.District;
        }
        else
        {
            var premise = i.PremiseType == "VEHICLE" ? (w.Chance(0.5) ? "RESIDENTIAL" : "COMMERCIAL") : i.PremiseType;
            i.Location = w.Pick(w.LocationsIn(district, premise).Where(l => l.BusinessType is not ("LPR Camera" or "Pawn Shop" or "Scrap Metal Recycler")).ToList());
        }

        // Discovery/reporting time
        var hoursUntilReported = i.OffenseType switch
        {
            "BURGLARY" when i.PremiseType == "COMMERCIAL" => w.Between(2, 9),
            "BURGLARY" or "THEFT_FROM_VEHICLE" or "AUTO_THEFT" or "VANDALISM" => w.Between(1, 10),
            _ => 0,
        };
        if (hoursUntilReported > 0) i.OccurredTo = i.OccurredFrom.AddHours(hoursUntilReported).AddMinutes(-w.Rng.Next(50));
        i.ReportedAt = (i.OccurredTo ?? i.OccurredFrom).AddMinutes(w.Between(4, 45));
        if (i.ReportedAt > w.End) { i.ReportedAt = w.End.AddMinutes(-w.Between(5, 300)); i.OccurredFrom = i.ReportedAt.AddHours(-1); i.OccurredTo = null; }

        i.Deputy = w.DeputyFor(i.District);

        // People
        if (i.PremiseType == "COMMERCIAL" && i.OffenseType != "ASSAULT")
            i.Persons.Add((w.Pick(w.Persons.Where(p => w.AgeOn(p, date) >= 18).ToList()), "REPORTING_PARTY"));
        else
        {
            victim ??= w.Pick(w.Persons.Where(p => w.AgeOn(p, date) >= 18).ToList());
            if (i.OffenseType != "NARCOTICS") i.Persons.Add((victim, "VICTIM"));
        }

        var suspectKnownChance = i.OffenseType switch
        {
            "NARCOTICS" or "DOMESTIC" => 1.0, "ASSAULT" => 0.85, "THEFT" => 0.45, "ROBBERY" => 0.2,
            "BURGLARY" or "AUTO_THEFT" or "VANDALISM" => 0.15, _ => 0.08,
        };
        Person? suspect = null;
        if (w.Chance(suspectKnownChance))
        {
            suspect = i.OffenseType == "DOMESTIC"
                ? w.NewPerson(sex: victim!.Sex == "F" ? "M" : (w.Chance(0.7) ? "F" : "M"), last: w.Chance(0.6) ? victim.Last : null, home: w.HomeOf(victim))
                : w.Chance(0.6) ? w.Pick(_frequent) : w.Pick(w.Persons);
            if (suspect == victim) suspect = w.Pick(_frequent.Where(p => p != victim).ToList());
            i.Persons.Add((suspect, "SUSPECT"));
            if (w.AgeOn(suspect, date) < 18) i.Sensitivity = "JUVENILE";
        }
        if (w.Chance(0.2)) i.Persons.Add((w.Pick(w.Persons.Where(p => w.AgeOn(p, date) >= 18).ToList()), "WITNESS"));

        // Vehicles
        if (i.OffenseType is "THEFT_FROM_VEHICLE" or "AUTO_THEFT" || (i.OffenseType == "VANDALISM" && i.PremiseType == "VEHICLE"))
        {
            var owner = victim ?? i.Persons[0].Person;
            var car = w.Vehicles.FirstOrDefault(v => v.OwnerPersonId == owner.Id) ?? w.NewVehicle(owner);
            i.Vehicles.Add((car, i.OffenseType == "AUTO_THEFT" ? "STOLEN" : "VICTIM_VEHICLE"));
        }
        if (i.OffenseType == "NARCOTICS" && i.PremiseType == "VEHICLE" && suspect is not null)
            i.Vehicles.Add((w.Vehicles.FirstOrDefault(v => v.OwnerPersonId == suspect.Id) ?? w.NewVehicle(suspect), "SUSPECT_VEHICLE"));

        AddProperty(i);

        // Outcome
        var arrestChance = i.OffenseType switch
        {
            "NARCOTICS" => 0.85, "DOMESTIC" => 0.55, "ASSAULT" => 0.4, "THEFT" => 0.35, "ROBBERY" => 0.15, _ => 0.1,
        };
        if (suspect is not null && w.Chance(arrestChance))
        {
            i.Status = "CLEARED_ARREST";
            i.Persons.Add((suspect, "ARRESTEE"));
        }
        else if (suspect is not null && w.Chance(0.25)) i.Status = "CLEARED_EXCEPTIONAL";
        else i.Status = w.Weighted(new List<(string, int)> { ("SUSPENDED", 55), ("OPEN", 40), ("UNFOUNDED", 5) });
        if (i.Sensitivity == "NORMAL" && w.Chance(0.01)) i.Sensitivity = "RESTRICTED";

        i.Narrative = narratives.Build(i);
        w.Incidents.Add(i);

        AddIncidentCadCall(i);
        if (i.Status == "CLEARED_ARREST") AddArrestBooking(i, suspect!);
    }

    private void AddProperty(Incident i)
    {
        (string Item, int Min, int Max, bool Serial)[] bank = i.OffenseType switch
        {
            "BURGLARY" when i.PremiseType == "COMMERCIAL" => i.Location.BusinessType switch
            {
                "Hardware Store" or "Tool Rental" or "Construction Supply" or "Machine Shop" or "Feed & Farm Supply" =>
                [("cordless hammer drill kit", 180, 420, true), ("cordless impact driver", 140, 300, true), ("reciprocating saw", 150, 280, true),
                 ("portable generator", 600, 1400, true), ("spool of copper wire", 200, 900, false), ("rotary laser level", 300, 900, true), ("air compressor", 250, 700, true)],
                "Electronics Store" => [("laptop computer", 600, 1500, true), ("65-inch television", 500, 1200, true), ("gaming console", 400, 550, true), ("tablet", 300, 900, true)],
                "Liquor Store" => [("cases of liquor", 300, 2000, false), ("cash from register", 100, 900, false)],
                "Pharmacy" => [("over-the-counter medications", 200, 900, false), ("cash from register", 100, 700, false)],
                _ => [("cash from office safe", 200, 3000, false), ("cash from register", 80, 700, false), ("laptop computer", 500, 1300, true), ("cartons of cigarettes", 200, 1200, false)],
            },
            "BURGLARY" => [("jewelry", 300, 4000, false), ("handgun", 300, 800, true), ("laptop computer", 500, 1500, true), ("television", 300, 1000, true),
                           ("gaming console", 300, 550, true), ("prescription medication", 20, 100, false), ("cash", 50, 1500, false)],
            "THEFT_FROM_VEHICLE" when i.MoTags.Contains("CATALYTIC_CONVERTER") => [("catalytic converter", 900, 2600, false)],
            "THEFT_FROM_VEHICLE" => [("purse with wallet", 50, 300, false), ("laptop bag", 400, 1400, true), ("handgun", 300, 800, true), ("tool bag", 200, 900, false), ("sunglasses", 80, 250, false)],
            "THEFT" when i.PremiseType == "COMMERCIAL" => [("assorted merchandise", 40, 600, false)],
            "THEFT" => [("delivered package", 30, 300, false), ("bicycle", 150, 1200, true), ("lawn equipment", 200, 800, false)],
            "ROBBERY" => [("cash", 60, 900, false), ("cell phone", 300, 1100, true), ("wallet", 20, 200, false)],
            _ => [],
        };
        if (bank.Length == 0) return;
        var single = i.OffenseType is "THEFT" || i.MoTags.Contains("CATALYTIC_CONVERTER");
        foreach (var b in w.PickSome(bank, single ? 1 : w.Between(1, Math.Min(3, bank.Length))))
            i.Property.Add(new PropertyItem { Description = b.Item, Value = w.Money(b.Min, b.Max), Serial = b.Serial && w.Chance(0.6) ? NewSerial() : null });
    }

    public string NewSerial() => $"{(char)('A' + w.Rng.Next(26))}{(char)('A' + w.Rng.Next(26))}{w.Rng.Next(1_000_000, 9_999_999)}";

    // ------------------------------------------------------------------ CAD, FI, LPR, jail, pawn
    public void AddIncidentCadCall(Incident i)
    {
        var type = i.OffenseType switch
        {
            "BURGLARY" => i.PremiseType == "COMMERCIAL" && w.Chance(0.4) ? "ALARM - COMMERCIAL" : "BURGLARY REPORT",
            "THEFT" => "THEFT REPORT", "THEFT_FROM_VEHICLE" => "VEHICLE BURGLARY", "AUTO_THEFT" => "STOLEN VEHICLE",
            "ASSAULT" => "FIGHT / ASSAULT", "ROBBERY" => "ROBBERY", "VANDALISM" => "CRIMINAL DAMAGE",
            "NARCOTICS" => w.Chance(0.5) ? "SUSPICIOUS VEHICLE" : "NARCOTICS COMPLAINT", "DOMESTIC" => "DOMESTIC DISTURBANCE", _ => "OTHER",
        };
        w.CadCalls.Add(new CadCall
        {
            ReceivedAt = i.ReportedAt.AddMinutes(-w.Between(3, 25)),
            CallType = type,
            Priority = i.OffenseType is "ASSAULT" or "ROBBERY" or "DOMESTIC" ? 1 : i.OffenseType == "NARCOTICS" ? 3 : 2,
            Location = i.Location,
            Disposition = "REPORT TAKEN",
            Incident = i,
            Deputy = i.Deputy,
            Notes = $"RP REPORTS {type}. {(i.OffenseType == "DOMESTIC" ? "RP ADVISES YELLING HEARD. " : "")}UNIT ENRT.",
            Storyline = i.Storyline,
        });
    }

    private static readonly (string Type, string Disposition, int[] Hours, string Note)[] FillerCalls =
    [
        ("TRAFFIC STOP", "CITATION ISSUED", Evening, "TRAFFIC STOP. EQUIPMENT/SPEED."),
        ("TRAFFIC STOP", "WARNING", Day, "TRAFFIC STOP. WARNING ISSUED."),
        ("WELFARE CHECK", "ADVISED", Day, "RP REQ WELFARE CHECK ON ELDERLY NEIGHBOR."),
        ("SUSPICIOUS VEHICLE", "GOA", Night, "RP REPORTS UNK VEH PARKED W/ LIGHTS OFF."),
        ("SUSPICIOUS PERSON", "NO REPORT", Night, "SUBJ WALKING BEHIND BUSINESSES."),
        ("NOISE COMPLAINT", "ADVISED", Night, "LOUD MUSIC FROM RESIDENCE."),
        ("ALARM - RESIDENTIAL", "FALSE ALARM", Day, "ALARM CO REPORTS FRONT DOOR ZONE."),
        ("ALARM - COMMERCIAL", "FALSE ALARM", Night, "ALARM CO REPORTS MOTION ZONE. BLDG SECURE."),
        ("MOTORIST ASSIST", "ASSISTED", Any, "DISABLED VEHICLE ON SHOULDER."),
        ("CIVIL STANDBY", "COMPLETED", Day, "CIVIL STANDBY FOR PROPERTY RETRIEVAL."),
        ("ANIMAL COMPLAINT", "REFERRED", Day, "LOOSE LIVESTOCK ON ROADWAY."),
        ("911 HANGUP", "NO REPORT", Any, "911 HANGUP. CALLBACK NO ANSWER."),
        ("MENTAL HEALTH CRISIS", "TRANSPORTED", Evening, "RP ADVISES FAMILY MEMBER IN CRISIS."),
    ];

    private void BuildFillerCadCalls(int count)
    {
        for (var n = 0; n < count; n++)
        {
            var f = w.Pick(FillerCalls);
            var loc = w.Pick(w.Locations.Where(l => l.BusinessType != "LPR Camera").ToList());
            w.CadCalls.Add(new CadCall
            {
                ReceivedAt = w.AtHour(w.RandomDate(), f.Hours),
                CallType = f.Type,
                Priority = f.Type == "MENTAL HEALTH CRISIS" ? 1 : 3,
                Location = loc,
                Disposition = f.Disposition,
                Deputy = w.DeputyFor(loc.District),
                Notes = f.Note,
                CallerPhone = w.Chance(0.5) ? w.Pick(w.Persons).Phone : null,
            });
        }
    }

    private static readonly string[] FiReasons =
    [
        "Subject walking behind closed businesses after hours.",
        "Occupant of vehicle parked in closed business lot with lights off.",
        "Traffic stop — equipment violation; occupant identified.",
        "Subject matching description from earlier suspicious person call.",
        "Subject loitering near ATM; identified and released.",
        "Consensual contact in park after hours.",
    ];

    private void BuildFieldInterviews(int count)
    {
        for (var n = 0; n < count; n++)
        {
            var person = w.Chance(0.7) ? w.Pick(_frequent) : w.Pick(w.Persons);
            var loc = w.Pick(w.Locations.Where(l => l.PremiseType is "COMMERCIAL" or "PUBLIC" && l.BusinessType != "LPR Camera").ToList());
            var veh = w.Chance(0.4) ? w.Vehicles.FirstOrDefault(v => v.OwnerPersonId == person.Id) : null;
            var reason = w.Pick(FiReasons);
            w.FieldInterviews.Add(new FieldInterview
            {
                ContactAt = w.AtHour(w.RandomDate(), Night),
                Location = loc,
                Deputy = w.DeputyFor(loc.District),
                Person = person,
                Vehicle = veh,
                Reason = reason,
                Notes = $"{reason} {person.FullName} was cooperative. {(veh is null ? "On foot." : $"Associated vehicle: {veh.Description}, plate {veh.Plate}.")} No enforcement action.",
            });
        }
    }

    private void BuildLprReads(int count)
    {
        for (var n = 0; n < count; n++)
        {
            var v = w.Pick(w.Vehicles);
            w.LprReads.Add(new LprRead { ReadAt = w.AtHour(w.RandomDate(), Any), Plate = v.Plate, Vehicle = v, Camera = w.Pick(w.LprCameras) });
        }
    }

    private string HousingFor(Person p, string cls) =>
        p.Sex == "F" ? "D-POD (FEMALE)" : cls == "MAXIMUM" ? "C-POD (MAX)" : w.Pick(new[] { "A-POD", "B-POD", "A-POD", "B-POD", "INTAKE" });

    private void AddArrestBooking(Incident i, Person suspect)
    {
        if (w.AgeOn(suspect, i.ReportedAt) < 18) return; // juveniles go to juvenile detention, not the county jail
        var booked = i.ReportedAt.AddMinutes(w.Between(40, 180));
        var cls = i.OffenseType is "ROBBERY" or "ASSAULT" && i.MoTags.Any(m => m.StartsWith("WEAPON")) ? "MAXIMUM" : w.Chance(0.7) ? "MINIMUM" : "MEDIUM";
        w.Bookings.Add(new Booking
        {
            Person = suspect,
            BookedAt = booked,
            ReleasedAt = Release(booked),
            Charges = i.OffenseDescription.ToUpperInvariant(),
            Incident = i,
            Classification = cls,
            HousingUnit = HousingFor(suspect, cls),
            Notes = w.Chance(0.15) ? "Medical screening: requires daily medication." : "",
        });
    }

    private DateTime? Release(DateTime booked)
    {
        var release = booked.AddHours(w.Chance(0.6) ? w.Between(4, 48) : w.Between(48, 24 * 90));
        return release > w.End ? null : release;
    }

    private void BuildFillerBookings(int count)
    {
        var charges = new[] { "FAILURE TO APPEAR WARRANT", "DUI", "DRIVING WHILE SUSPENDED", "PROBATION VIOLATION", "CONTEMPT OF COURT", "OUTSIDE AGENCY WARRANT" };
        for (var n = 0; n < count; n++)
        {
            var p = w.Chance(0.6) ? w.Pick(_frequent) : w.Pick(w.Persons);
            var booked = w.AtHour(w.RandomDate(), Any);
            if (w.AgeOn(p, booked) < 18) continue;
            var cls = w.Chance(0.75) ? "MINIMUM" : "MEDIUM";
            w.Bookings.Add(new Booking
            {
                Person = p, BookedAt = booked, ReleasedAt = Release(booked), Charges = w.Pick(charges),
                Classification = cls, HousingUnit = HousingFor(p, cls),
            });
        }
    }

    private void BuildPawns(int count)
    {
        var items = new (string Item, int Min, int Max, bool Serial)[]
        {
            ("10K gold chain", 60, 400, false), ("diamond ring", 150, 900, false), ("acoustic guitar", 60, 300, true), ("gaming console", 80, 220, true),
            ("laptop computer", 100, 450, true), ("cordless drill kit", 40, 140, true), ("chainsaw", 60, 200, true), ("compound bow", 80, 250, false),
            ("shotgun", 100, 300, true), ("flat-screen television", 60, 250, true), ("wristwatch", 50, 600, true),
        };
        for (var n = 0; n < count; n++)
        {
            var p = w.Chance(0.5) ? w.Pick(_frequent) : w.Pick(w.Persons.Where(x => w.AgeOn(x, w.End) >= 19).ToList());
            var scrap = w.Chance(0.12);
            var it = w.Pick(items);
            w.Pawns.Add(new PawnTransaction
            {
                TransactionAt = w.AtHour(w.RandomDate(), Day),
                Shop = scrap ? w.Pick(_scrapYards) : w.Pick(_pawnShops),
                Person = p,
                TransactionType = scrap ? "SCRAP_SALE" : "PAWN",
                ItemDescription = scrap ? w.Pick(new[] { "aluminum cans (lbs)", "copper pipe (lbs)", "scrap steel (lbs)", "used catalytic converter (from personal vehicle repair)" }) : it.Item,
                Serial = !scrap && it.Serial ? NewSerial() : null,
                Amount = scrap ? w.Money(20, 300) : w.Money(it.Min, it.Max),
                SellerPhone = p.Phone,
            });
        }
    }
}
