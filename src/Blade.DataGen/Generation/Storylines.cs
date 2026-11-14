using Blade.DataGen.Model;

namespace Blade.DataGen.Generation;

/// <summary>
/// Planted investigative storylines. These guarantee that the demo questions surface something meaningful,
/// and they mirror the kinds of cross-system connections that siloed RMS/CAD/JMS/pawn systems hide:
///  S1  North district commercial burglary series → white work van (LPR + FI) → registered owner → co-resident suspect
///      → prior booking with an associate → associate pawns serial-numbered tools stolen in the series.
///  S2  Catalytic converter thefts → one phone number shared by two identities (scrap sale alias) and mentioned in reports.
///  S3  Repeat domestic violence address with escalating severity (officer-safety / lethality pattern).
///  S4  Use-of-force and crisis incidents involving knife-armed, non-compliant subjects (pairs with Policy 300/330).
/// Every planted person, vehicle, business and address is fictional.
/// </summary>
public sealed class Storylines(World w, NarrativeBuilder narratives, BaselineGenerator baseline)
{
    public const string S1 = "S1_NORTH_COMMERCIAL_BURGLARY_SERIES";
    public const string S2 = "S2_CATALYTIC_CONVERTER_RING";
    public const string S3 = "S3_REPEAT_DV_ADDRESS";
    public const string S4 = "S4_KNIFE_NONCOMPLIANT_SUBJECTS";

    public void PlantAll()
    {
        BurglarySeries();
        CatalyticRing();
        RepeatDomestic();
        KnifeIncidents();
    }

    // ------------------------------------------------------------------ helpers
    private static DateTime At(int y, int m, int d, int h, int min) => new(y, m, d, h, min, 0);

    private Incident NewIncident(string storyline, string type, string code, string desc, Location loc, string premise,
        DateTime from, DateTime? to, int reportDelayMin, string status, params string[] mo)
    {
        var i = new Incident
        {
            Storyline = storyline, OffenseType = type, OffenseCode = code, OffenseDescription = desc,
            Location = loc, District = loc.District, PremiseType = premise,
            OccurredFrom = from, OccurredTo = to, ReportedAt = (to ?? from).AddMinutes(reportDelayMin),
            Status = status, Deputy = w.DeputyFor(loc.District),
        };
        i.MoTags.AddRange(mo);
        return i;
    }

    private void Commit(Incident i, string truthNote)
    {
        w.Incidents.Add(i);
        baseline.AddIncidentCadCall(i);
        w.Truth.Add((i.Storyline!, "INCIDENT", () => i.CaseNumber, truthNote));
    }

    private void Truth(string storyline, string type, Func<string> key, string note) => w.Truth.Add((storyline, type, key, note));

    // ------------------------------------------------------------------ S1
    private void BurglarySeries()
    {
        var home = w.NewLocation("NORTH", "RESIDENTIAL", "Quail Run Ct", 2217);
        var travis = w.NewPerson("Travis", "Kessler", "M", 34, 34, home, middle: "Dean");
        var amber = w.NewPerson("Amber", "Pruitt", "F", 31, 31, home, middle: "Lynn");
        var cody = w.NewPerson("Cody", "Mercer", "M", 29, 29, w.NewLocation("WEST", "RESIDENTIAL", "Bison Trail", 418, unit: "Apt 6"), middle: "Ray");
        travis.Notes = "Prior arrest for commercial burglary (2025).";
        var van = w.NewVehicle(amber, "Ford", "Transit", "Van", "White", 2016, "418KTV");
        van.Notes = "Full-size cargo van, roof ladder rack, no rear windows.";

        Truth(S1, "PERSON", () => travis.FullName, "Primary suspect; lives with registered owner of the van.");
        Truth(S1, "PERSON", () => amber.FullName, "Registered owner of the van; not directly involved.");
        Truth(S1, "PERSON", () => cody.FullName, "Associate; co-arrestee in 2025 burglary; pawned stolen tools.");
        Truth(S1, "VEHICLE", () => van.Plate, "White Ford Transit seen near series scenes (LPR, FI, witnesses).");

        // Prior: Jan 2025 burglary (West) where Travis and Cody were arrested together.
        var priorLoc = w.NewLocation("WEST", "COMMERCIAL", "Westridge Pkwy", 3640, "Westridge Equipment Rental", "Tool Rental");
        var prior = NewIncident(S1, "BURGLARY", "220", "Burglary/Breaking & Entering", priorLoc, "COMMERCIAL",
            At(2025, 1, 17, 23, 50), At(2025, 1, 18, 0, 35), 6, "CLEARED_ARREST", "FORCED_ENTRY", "PRY_TOOL", "REAR_ENTRY");
        prior.Persons.Add((w.NewPerson(minAge: 40, maxAge: 60), "REPORTING_PARTY"));
        prior.Persons.Add((travis, "SUSPECT")); prior.Persons.Add((cody, "SUSPECT"));
        prior.Persons.Add((travis, "ARRESTEE")); prior.Persons.Add((cody, "ARRESTEE"));
        prior.Vehicles.Add((van, "SUSPECT_VEHICLE"));
        prior.Property.Add(new PropertyItem { Description = "cordless impact driver", Value = 240, Serial = baseline.NewSerial(), Status = "RECOVERED" });
        prior.Narrative = narratives.Build(prior,
            $"A responding unit observed a white Ford Transit van, plate {van.Plate}, leaving the rear lot with its headlights off. A traffic stop was conducted on Westridge Pkwy. " +
            $"The driver was identified as {travis.FullName} and the passenger as {cody.FullName}. A 36-inch pry bar, bolt cutters, and a cordless impact driver with the business's inventory tag were located in the cargo area. " +
            "Both subjects were arrested for burglary.");
        Commit(prior, "Prior arrest linking Kessler, Mercer and the van.");
        var priorBooking1 = new Booking { Person = travis, BookedAt = At(2025, 1, 18, 2, 40), ReleasedAt = At(2025, 3, 11, 10, 0), Charges = "BURGLARY/BREAKING & ENTERING; POSSESSION OF BURGLARY TOOLS", Incident = prior, Classification = "MEDIUM", HousingUnit = "B-POD", Notes = "Co-defendant: keep separate.", Storyline = S1 };
        var priorBooking2 = new Booking { Person = cody, BookedAt = At(2025, 1, 18, 2, 55), ReleasedAt = At(2025, 1, 21, 15, 30), Charges = "BURGLARY/BREAKING & ENTERING", Incident = prior, Classification = "MINIMUM", HousingUnit = "A-POD", Notes = "Co-defendant: keep separate.", Storyline = S1 };
        priorBooking1.KeepSeparateFrom = cody; priorBooking2.KeepSeparateFrom = travis;
        priorBooking1.CoArrestees.Add(cody); priorBooking2.CoArrestees.Add(travis);
        w.Bookings.Add(priorBooking1); w.Bookings.Add(priorBooking2);

        // The series: 7 North district commercial burglaries, late night, rear door pried.
        var targets = new (string Name, string Type, string Street, int Num)[]
        {
            ("Timberline Tool Rental", "Tool Rental", "Industrial Pkwy", 6120),
            ("Copperline Construction Supply", "Construction Supply", "Copperline Rd", 3310),
            ("Northgate Hardware & Lumber", "Hardware Store", "Northgate Blvd", 1585),
            ("Quarry Lane Machine Works", "Machine Shop", "Quarry Ln", 880),
            ("Iron Creek Equipment Rental", "Tool Rental", "Industrial Pkwy", 7015),
            ("Summit Electrical Supply", "Construction Supply", "Timber Mill Rd", 4400),
            ("Falcon Auto Parts", "Auto Parts", "Kestrel Ave N", 2740),
        };
        var times = new[]
        {
            At(2025, 8, 23, 23, 10), At(2025, 10, 11, 0, 40), At(2025, 12, 6, 1, 55), At(2026, 2, 21, 2, 30),
            At(2026, 5, 2, 23, 45), At(2026, 7, 18, 1, 20), At(2026, 9, 26, 3, 5),
        };
        var lootBank = new (string Item, int Value)[]
        {
            ("cordless hammer drill kit", 389), ("cordless impact driver", 249), ("reciprocating saw", 229), ("portable generator", 1150),
            ("rotary laser level", 640), ("spool of copper wire", 720), ("cordless circular saw", 279), ("air compressor", 480),
        };
        var lprCamNorth = w.LprCameras.Where(c => c.Location.District == "NORTH").ToList();
        Incident? fifth = null, sixth = null;

        for (var k = 0; k < targets.Length; k++)
        {
            var t = targets[k];
            var loc = w.NewLocation("NORTH", "COMMERCIAL", t.Street, t.Num, t.Name, t.Type);
            var from = times[k];
            var discovered = from.Date.AddDays(from.Hour >= 12 ? 1 : 0).AddHours(6).AddMinutes(w.Between(5, 55));
            var mo = new List<string> { "FORCED_ENTRY", "PRY_TOOL", "REAR_ENTRY" };
            if (k >= 4) mo.Add("ALARM_DEFEATED"); // escalation: later hits defeat the alarm first
            var i = NewIncident(S1, "BURGLARY", "220", "Burglary/Breaking & Entering", loc, "COMMERCIAL", from, discovered, w.Between(5, 20),
                k == 6 ? "OPEN" : "SUSPENDED", mo.ToArray());
            i.Persons.Add((w.NewPerson(minAge: 30, maxAge: 62), "REPORTING_PARTY"));
            foreach (var (item, value) in w.PickSome(lootBank, w.Between(2, 4)))
                i.Property.Add(new PropertyItem { Description = item, Value = value, Serial = item.Contains("copper") ? null : baseline.NewSerial() });

            var extra = k switch
            {
                0 => "The rear steel service door was pried at the latch with a large flat bar. Only high-value cordless tools were taken; cash in the register was not disturbed.",
                1 => $"A delivery driver told me that at approximately 0030 hours he saw a white full-size work van with a ladder rack backed up to the rear door with its lights off.",
                2 => "Security video shows a single male in dark clothing, gloves and a face covering prying the rear door with a long bar at 0158 hours. A white van is partially visible at the edge of the frame. The suspect was inside for under six minutes.",
                3 => "The rear door was pried in the same manner as recent tool-store burglaries in the Northgate area. Tool marks were photographed for comparison.",
                4 => "The alarm panel siren wire was cut from outside through a utility box before entry, then the rear door was pried. Only cordless tool kits and a laser level were taken.",
                5 => "A passing motorist reported a white work van with a roof ladder rack leaving the rear lot at a high rate of speed around 0130 hours and provided a partial plate beginning with \"418\".",
                _ => "The alarm was defeated and the rear door was pried. The method of entry and type of property taken match the series of North district commercial burglaries. CID notified for series review.",
            };
            i.Narrative = narratives.Build(i, extra);
            Commit(i, $"Series burglary #{k + 1}.");
            if (k == 4) fifth = i;
            if (k == 5) sixth = i;

            // LPR reads of the van near three of the scenes (within ~30 minutes)
            if (k is 1 or 3 or 5)
            {
                var read = new LprRead { ReadAt = from.AddMinutes(-w.Between(12, 28)), Plate = van.Plate, Vehicle = van, Camera = w.Pick(lprCamNorth), Storyline = S1 };
                w.LprReads.Add(read);
                w.LprReads.Add(new LprRead { ReadAt = from.AddMinutes(w.Between(20, 40)), Plate = van.Plate, Vehicle = van, Camera = w.Pick(lprCamNorth), Storyline = S1 });
                Truth(S1, "LPR", () => $"{van.Plate} @ {read.ReadAt:yyyy-MM-dd HH:mm}", $"Van read near burglary #{k + 1}.");
            }
        }

        // Field interviews: Travis (and later Cody) in the van behind closed businesses.
        var fiLoc = w.NewLocation("NORTH", "COMMERCIAL", "Industrial Pkwy", 6900, "Industrial Pkwy Business Park (rear lot)", "Business Park");
        var dep = w.DeputyFor("NORTH");
        w.FieldInterviews.Add(new FieldInterview
        {
            ContactAt = At(2026, 3, 14, 1, 40), Location = fiLoc, Deputy = dep, Person = travis, Vehicle = van, Storyline = S1,
            Reason = "Occupant of vehicle parked in closed business lot with lights off.",
            Notes = $"Contacted {travis.FullName} seated in the driver's seat of a white Ford Transit van, plate {van.Plate}, parked behind closed businesses with the engine off. " +
                    "A large pry bar and bolt cutters were visible in the cargo area. Kessler stated he was waiting for a friend and left the area when asked. No enforcement action.",
        });
        foreach (var p in new[] { travis, cody })
            w.FieldInterviews.Add(new FieldInterview
            {
                ContactAt = At(2026, 8, 2, 2, 15), Location = w.Pick(w.LocationsIn("NORTH", "COMMERCIAL")), Deputy = dep, Person = p, Vehicle = van, Storyline = S1,
                Reason = "Traffic stop — equipment violation; occupant identified.",
                Notes = $"Traffic stop on white Ford Transit van plate {van.Plate} for a rear tag light out. Driver {travis.FullName}, passenger {cody.FullName}. " +
                        "Several new cordless tool cases in cargo area; driver stated they do construction work. Warning issued.",
            });
        Truth(S1, "FI", () => "2026-03-14 01:40", "Kessler in van behind closed businesses with pry bar.");

        // Suspicious vehicle calls
        foreach (var when in new[] { At(2026, 6, 20, 0, 52), At(2026, 9, 12, 1, 37) })
            w.CadCalls.Add(new CadCall
            {
                ReceivedAt = when, CallType = "SUSPICIOUS VEHICLE", Priority = 3, Location = fiLoc, Disposition = "GOA", Deputy = dep, Storyline = S1,
                Notes = "RP REPORTS WHITE WORK VAN W/ LADDER RACK CIRCLING BEHIND BUSINESSES, LIGHTS OFF. GOA ON ARRIVAL.",
            });

        // Cody pawns tools whose serial numbers match property stolen in burglaries #5 and #6.
        var frontier = baseline.PawnShops.Single(s => s.BusinessName == "Frontier Pawn");
        foreach (var (inc, days) in new[] { (fifth!, 4), (sixth!, 3) })
        {
            var item = inc.Property.First(p => p.Serial is not null);
            var pawn = new PawnTransaction
            {
                TransactionAt = inc.OccurredFrom.Date.AddDays(days).AddHours(w.Between(10, 16)).AddMinutes(w.Between(0, 59)),
                Shop = frontier, Person = cody, TransactionType = "PAWN", ItemDescription = item.Description, Serial = item.Serial,
                Amount = Math.Round(item.Value * 0.3m), SellerPhone = cody.Phone, Storyline = S1,
            };
            w.Pawns.Add(pawn);
            Truth(S1, "PAWN", () => $"{item.Serial}", $"Serial matches property stolen in {inc.CaseNumber}.");
        }
    }

    // ------------------------------------------------------------------ S2
    private void CatalyticRing()
    {
        var phone = w.NewPhone();
        var jaylen = w.NewPerson("Jaylen", "Ortega", "M", 24, 24, w.NewLocation("EAST", "RESIDENTIAL", "Sparrow Ln", 1907), phone, "Marcus");
        // Same phone, different identity: the alias used at the scrap yard.
        var alias = w.NewPerson("Jay", "Ortiz", "M", 25, 25, w.NewLocation("SOUTH", "RESIDENTIAL", "Pecan St", 77), phone, "");
        alias.Middle = null;
        alias.Notes = "Identity provided at scrap yard; ID not verified.";
        var truck = w.NewVehicle(jaylen, "Chevrolet", "Silverado", "Pickup", "Silver", 2011, w.NewPlate());

        Truth(S2, "PHONE", () => phone, "Shared by Jaylen Ortega and alias 'Jay Ortiz'; appears in two reports.");
        Truth(S2, "PERSON", () => jaylen.FullName, "Primary suspect.");
        Truth(S2, "PERSON", () => alias.FullName, "Alias identity used for scrap sales.");
        Truth(S2, "VEHICLE", () => truck.Plate, "Suspect pickup.");

        var victims = new (string District, string Street, int Num, string Biz, string Make, string Model, int Year, DateTime When)[]
        {
            ("EAST", "Commerce Dr", 5120, "Commerce Drive Apartments (parking lot)", "Toyota", "Prius", 2014, At(2026, 3, 8, 2, 30)),
            ("EAST", "Eastgate Blvd", 3300, "Eastgate Park & Ride", "Honda", "Accord", 2012, At(2026, 4, 19, 3, 10)),
            ("SOUTH", "Market St", 610, "Market Street Church (parking lot)", "Toyota", "Tacoma", 2015, At(2026, 5, 30, 1, 45)),
            ("EAST", "Station St", 140, "Station Street Lofts (parking lot)", "Toyota", "Prius", 2010, At(2026, 7, 11, 2, 5)),
            ("SOUTH", "River Bend Rd", 2900, "River Bend Business Center", "Ford", "F-150", 2013, At(2026, 8, 22, 23, 50)),
            ("EAST", "Foundry Rd", 1222, "Foundry Road Self Storage", "Toyota", "Prius", 2013, At(2026, 10, 4, 3, 30)),
        };
        for (var k = 0; k < victims.Length; k++)
        {
            var v = victims[k];
            var loc = w.NewLocation(v.District, "COMMERCIAL", v.Street, v.Num, v.Biz, "Parking Lot");
            var owner = w.NewPerson(minAge: 22, maxAge: 70);
            var car = w.NewVehicle(owner, v.Make, v.Model, v.Make == "Toyota" && v.Model == "Prius" ? "Hatchback" : "Sedan", w.Pick(new[] { "Silver", "White", "Blue", "Gray" }), v.Year);
            var i = NewIncident(S2, "THEFT_FROM_VEHICLE", "23F", "Theft From Motor Vehicle", loc, "VEHICLE", v.When, v.When.AddHours(5), w.Between(10, 40), "OPEN", "CATALYTIC_CONVERTER");
            i.Persons.Add((owner, "VICTIM"));
            i.Vehicles.Add((car, "VICTIM_VEHICLE"));
            i.Property.Add(new PropertyItem { Description = "catalytic converter", Value = w.Money(1100, 2600) });
            var extra = k switch
            {
                1 => $"A witness walking a dog saw a silver older-model Chevrolet Silverado pickup parked next to the victim vehicle and heard a power saw. Partial plate unknown.",
                3 => $"A handwritten flyer reading \"WE BUY CATS - CASH - CALL {phone}\" was found tucked under the windshield wiper of a neighboring vehicle and collected as evidence.",
                5 => $"The storage facility manager provided gate camera footage showing a silver Chevrolet Silverado pickup, plate {truck.Plate}, entering at 0322 hours and leaving at 0341 hours.",
                _ => "",
            };
            if (k == 5) i.Vehicles.Add((truck, "SUSPECT_VEHICLE"));
            i.Narrative = narratives.Build(i, extra);
            Commit(i, $"Catalytic converter theft #{k + 1}.");
        }

        // Unrelated-looking report that mentions the same phone number.
        var shop = w.NewLocation("SOUTH", "COMMERCIAL", "Kestrel Ave S", 1460, "Brannock Muffler & Exhaust", "Auto Repair");
        var rp = w.NewPerson(minAge: 40, maxAge: 65);
        var tip = NewIncident(S2, "THEFT", "23C", "Larceny/Theft", shop, "COMMERCIAL", At(2026, 6, 9, 14, 10), null, 30, "SUSPENDED");
        tip.Persons.Add((rp, "REPORTING_PARTY"));
        tip.Narrative =
            $"On {tip.ReportedAt:MM/dd/yyyy} at approximately {tip.ReportedAt:HHmm} hours, I responded to Brannock Muffler & Exhaust, {shop.Address}, reference a suspicious sale attempt and theft of a parts invoice book. " +
            $"The owner, {rp.FullName}, stated a young male in a silver pickup offered to sell him four catalytic converters for cash and could not explain where they came from. " +
            $"When the owner refused, the male took a parts invoice book from the counter and left. The male left a phone number, {phone}, \"in case you change your mind.\" " +
            "The owner did not obtain a plate. Information forwarded to CID property crimes. Case suspended pending leads.";
        Commit(tip, "Second report containing the shared phone number.");

        // Traffic stop FI: converters in the truck bed.
        w.FieldInterviews.Add(new FieldInterview
        {
            ContactAt = At(2026, 6, 27, 0, 55), Location = w.Pick(w.LocationsIn("EAST", "COMMERCIAL")), Deputy = w.DeputyFor("EAST"), Person = jaylen, Vehicle = truck, Storyline = S2,
            Reason = "Traffic stop — equipment violation; occupant identified.",
            Notes = $"Stopped silver Chevrolet Silverado plate {truck.Plate} for no headlights. Driver {jaylen.FullName}, phone {phone}. " +
                    "Three catalytic converters, a battery reciprocating saw and a floor jack in the truck bed. Driver stated they came from his cousin's shop. No proof of ownership; no enforcement action.",
        });

        // Scrap sales under the alias using the same phone number.
        foreach (var (when, qty) in new[] { (At(2026, 4, 21, 10, 15), 3), (At(2026, 7, 13, 9, 40), 4), (At(2026, 10, 6, 11, 5), 2) })
            w.Pawns.Add(new PawnTransaction
            {
                TransactionAt = when, Shop = baseline.ScrapYards.Single(s => s.BusinessName == "Railyard Metal Recycling"), Person = alias,
                TransactionType = "SCRAP_SALE", ItemDescription = $"{qty} catalytic converters (cut, no VIN tags)", Amount = qty * w.Money(140, 260),
                SellerPhone = phone, Storyline = S2,
            });
    }

    // ------------------------------------------------------------------ S3
    private void RepeatDomestic()
    {
        var home = w.NewLocation("EAST", "RESIDENTIAL", "Larkspur Ct", 1418);
        var kayla = w.NewPerson("Kayla", "Hobbs", "F", 29, 29, home, middle: "Marie");
        var shane = w.NewPerson("Shane", "Hobbs", "M", 33, 33, home, middle: "Michael");
        shane.Notes = "Owns a 12-gauge shotgun (per victim statement).";
        Truth(S3, "LOCATION", () => home.Address, "Repeat DV address with escalating severity.");

        // 1. Verbal only (CAD, no report)
        w.CadCalls.Add(new CadCall
        {
            ReceivedAt = At(2026, 1, 9, 21, 14), CallType = "DOMESTIC DISTURBANCE", Priority = 1, Location = home, Disposition = "ADVISED", Deputy = w.DeputyFor("EAST"), Storyline = S3,
            CallerPhone = kayla.Phone, Notes = "RP (FEMALE) ADVISES HUSBAND YELLING, THROWING ITEMS. NO WEAPONS. VERBAL ONLY PER UNITS. PARTIES SEPARATED FOR NIGHT.",
        });

        // 2. Pushing — victim declined prosecution
        var a = NewIncident(S3, "DOMESTIC", "13B", "Simple Assault — Domestic", home, "RESIDENTIAL", At(2026, 3, 22, 22, 5), null, 12, "CLEARED_EXCEPTIONAL");
        a.Persons.Add((kayla, "VICTIM")); a.Persons.Add((shane, "SUSPECT"));
        a.Narrative = $"On {a.ReportedAt:MM/dd/yyyy} at approximately {a.ReportedAt:HHmm} hours, I was dispatched to {home.Address} in reference to a domestic disturbance. " +
                      $"I separated the parties. {kayla.FullName} stated her husband, {shane.FullName}, had been drinking, became angry about money, and shoved her into the hallway wall. " +
                      "I observed redness on her left shoulder. Shane Hobbs stated it was only an argument. Kayla Hobbs declined to provide a written statement and stated she did not want him arrested. " +
                      "A lethality assessment was completed; she answered yes to two questions. DV resource card provided. Case cleared by exception (victim declined prosecution).";
        Commit(a, "DV #1 — push, victim declined.");

        // 3. Strangulation indicator — arrest
        var b = NewIncident(S3, "DOMESTIC", "13A", "Aggravated Assault — Domestic (Strangulation)", home, "RESIDENTIAL", At(2026, 6, 14, 23, 40), null, 9, "CLEARED_ARREST");
        b.Persons.Add((kayla, "VICTIM")); b.Persons.Add((shane, "SUSPECT")); b.Persons.Add((shane, "ARRESTEE"));
        b.Narrative = $"On {b.ReportedAt:MM/dd/yyyy} at approximately {b.ReportedAt:HHmm} hours, I responded to {home.Address}, a location with a prior domestic call this year. " +
                      $"{kayla.FullName} was crying and had red marks on both sides of her neck. She stated {shane.FullName} grabbed her by the throat with one hand and pinned her against the refrigerator for several seconds and she could not breathe. " +
                      "She stated he owns a 12-gauge shotgun kept in the bedroom closet. A lethality assessment was completed; she screened in as high danger. " +
                      "Photographs of injuries were taken. Shane Hobbs was arrested for aggravated domestic assault (strangulation). The shotgun was taken for safekeeping. Victim advocate contacted.";
        Commit(b, "DV #2 — strangulation, arrest, firearm in home.");
        w.Bookings.Add(new Booking { Person = shane, BookedAt = At(2026, 6, 15, 1, 5), ReleasedAt = At(2026, 6, 16, 16, 0), Charges = "AGGRAVATED ASSAULT - DOMESTIC (STRANGULATION)", Incident = b, Classification = "MEDIUM", HousingUnit = "B-POD", Notes = "Released on bond. No-contact order with victim as condition of release.", Storyline = S3 });

        // 4. Neighbor reports screaming — GOA
        w.CadCalls.Add(new CadCall
        {
            ReceivedAt = At(2026, 9, 3, 1, 22), CallType = "DOMESTIC DISTURBANCE", Priority = 1, Location = home, Disposition = "GOA", Deputy = w.DeputyFor("EAST"), Storyline = S3,
            Notes = "NEIGHBOR REPORTS FEMALE SCREAMING AT 1418 LARKSPUR CT. NO ANSWER AT DOOR, LIGHTS OFF. NO FURTHER.",
        });

        // 5. Knife threat — arrest, protective order violation
        var c = NewIncident(S3, "DOMESTIC", "13A", "Aggravated Assault — Domestic (Weapon)", home, "RESIDENTIAL", At(2026, 10, 28, 20, 50), null, 6, "CLEARED_ARREST", "WEAPON_KNIFE");
        c.Persons.Add((kayla, "VICTIM")); c.Persons.Add((shane, "SUSPECT")); c.Persons.Add((shane, "ARRESTEE"));
        c.Narrative = $"On {c.ReportedAt:MM/dd/yyyy} at approximately {c.ReportedAt:HHmm} hours, I was dispatched to {home.Address} on a priority call of a male threatening a female with a knife. This is the fourth documented call to this address in 2026. " +
                      $"Prior to arrival, dispatch advised a firearm had previously been associated with the suspect. On arrival I heard yelling inside. {shane.FullName} answered the door holding a kitchen knife at his side. " +
                      "I drew my firearm, created distance, and gave repeated commands to drop the knife. After approximately 40 seconds of commands he placed the knife on the floor and was taken into custody without further force. " +
                      $"{kayla.FullName} stated he held the knife to her face and said he would kill her if she called the police again. A no-contact bond condition from the June arrest was in effect. " +
                      "A lethality assessment screened high danger. Shane Hobbs was arrested for aggravated domestic assault with a deadly weapon and violation of bond conditions. Use of force: firearm display only; reported to supervisor.";
        Commit(c, "DV #3 — knife threat, bond violation, high lethality.");
        w.Bookings.Add(new Booking { Person = shane, BookedAt = At(2026, 10, 28, 22, 30), ReleasedAt = null, Charges = "AGGRAVATED ASSAULT - DOMESTIC W/ DEADLY WEAPON; VIOLATION OF BOND CONDITIONS", Incident = c, Classification = "MAXIMUM", HousingUnit = "C-POD (MAX)", Notes = "Held without bond pending hearing. Victim notification requested prior to any release.", Storyline = S3 });
    }

    // ------------------------------------------------------------------ S4
    private void KnifeIncidents()
    {
        // Use of force: crisis subject with knife, non-compliant, less-lethal used after de-escalation.
        var park = w.Locations.First(l => l.BusinessName == "Cedar Fork City Park");
        var ethan = w.NewPerson("Ethan", "Rollins", "M", 27, 27, middle: "Wade");
        ethan.Notes = "Prior mental health crisis contacts.";
        var uof = NewIncident(S4, "ASSAULT", "13A", "Aggravated Assault on a Law Enforcement Officer", park, "PUBLIC", At(2026, 8, 16, 17, 25), null, 4, "CLEARED_ARREST", "WEAPON_KNIFE");
        uof.Persons.Add((ethan, "SUSPECT")); uof.Persons.Add((ethan, "ARRESTEE"));
        uof.Narrative = $"On {uof.ReportedAt:MM/dd/yyyy} at approximately {uof.ReportedAt:HHmm} hours, I responded to Cedar Fork City Park reference a male waving a knife at people near the playground. " +
                        $"I located {ethan.FullName} holding a fixed-blade knife with an approximately 6-inch blade. He was yelling that people were following him and did not respond to my questions. " +
                        "I positioned my patrol vehicle as cover, maintained approximately 30 feet of distance, and requested a CIT deputy and a less-lethal 40mm launcher. Families were moved out of the area. " +
                        "For approximately six minutes I and the CIT deputy used calm verbal commands and attempted to build rapport. Rollins refused repeated commands to drop the knife and twice advanced toward us, closing to approximately 20 feet. " +
                        "When he advanced a third time with the knife raised, Deputy 2 fired one 40mm impact round striking his thigh, which was ineffective. A conducted energy weapon was then deployed with effective probe contact and Rollins dropped the knife. " +
                        "He was handcuffed and EMS was requested immediately; the probes were removed by EMS and he was transported to Kestrel Regional Medical Center for medical and mental health evaluation. " +
                        "The on-duty sergeant responded to the scene. Body-worn cameras were activated by all deputies. A Use of Force report was completed before end of shift.";
        Commit(uof, "Use of force — knife, non-compliant, de-escalation then 40mm + CEW.");
        w.Bookings.Add(new Booking { Person = ethan, BookedAt = At(2026, 8, 16, 23, 10), ReleasedAt = At(2026, 8, 30, 9, 0), Charges = "AGGRAVATED ASSAULT ON LAW ENFORCEMENT OFFICER (KNIFE)", Incident = uof, Classification = "MEDIUM", HousingUnit = "MEDICAL", Notes = "Mental health observation on intake. Medical clearance obtained prior to booking. Suicide-risk screening completed.", Storyline = S4 });

        // Crisis with knife resolved without force (contrast case).
        var store = w.Pick(w.LocationsIn("EAST", "COMMERCIAL").Where(l => l.BusinessType == "Convenience Store").DefaultIfEmpty(w.Pick(w.LocationsIn("EAST", "COMMERCIAL"))).ToList());
        var subj = w.NewPerson(minAge: 40, maxAge: 55, sex: "F");
        var crisis = NewIncident(S4, "ASSAULT", "13A", "Aggravated Assault (Threat With Knife)", store, "COMMERCIAL", At(2025, 6, 3, 15, 40), null, 5, "CLEARED_EXCEPTIONAL", "WEAPON_KNIFE");
        crisis.Persons.Add((subj, "SUSPECT"));
        crisis.Narrative = $"On {crisis.ReportedAt:MM/dd/yyyy} at approximately {crisis.ReportedAt:HHmm} hours, I responded to {store.BusinessName}, {store.Address}, reference a female threatening the clerk with a knife. " +
                           $"{subj.FullName} was standing near the coolers holding a paring knife and stated she wanted to die. Customers were evacuated. " +
                           "I maintained distance, used the store shelving as a barrier, and spoke with her in a calm voice. She did not comply with initial commands to drop the knife. A CIT-trained deputy arrived and took over communication. " +
                           "After approximately 15 minutes she placed the knife on the floor and walked to us. No force was used. She was placed on an emergency detention for mental health evaluation and transported by EMS. " +
                           "The clerk declined to pursue charges. Body-worn camera activated.";
        Commit(crisis, "Crisis with knife — resolved by de-escalation, no force.");
    }
}
