using System.Text;
using Blade.DataGen.Model;

namespace Blade.DataGen.Generation;

/// <summary>
/// Builds first-person deputy report narratives from structured incident facts using varied phrase banks.
/// The goal is realistic variety (so similarity search is meaningful) while keeping the facts in the
/// narrative consistent with the structured fields (so the quality checker has something to verify).
/// </summary>
public sealed class NarrativeBuilder(World w)
{
    private static string D(DateTime t) => t.ToString("MM/dd/yyyy");
    private static string T(DateTime t) => t.ToString("HHmm");

    private string P(params string[] options) => w.Pick(options);

    public string Build(Incident i, string? extra = null)
    {
        var sb = new StringBuilder();
        sb.Append(Opening(i)).Append(' ');
        sb.Append(i.OffenseType switch
        {
            "BURGLARY" => i.PremiseType == "COMMERCIAL" ? CommercialBurglary(i) : ResidentialBurglary(i),
            "THEFT" => Theft(i),
            "THEFT_FROM_VEHICLE" => TheftFromVehicle(i),
            "AUTO_THEFT" => AutoTheft(i),
            "ASSAULT" => Assault(i),
            "ROBBERY" => Robbery(i),
            "VANDALISM" => Vandalism(i),
            "NARCOTICS" => Narcotics(i),
            "DOMESTIC" => Domestic(i),
            _ => "",
        });
        if (!string.IsNullOrWhiteSpace(extra)) sb.Append(' ').Append(extra.Trim());
        sb.Append(' ').Append(Closing(i));
        return Normalize(sb.ToString());
    }

    private static string Normalize(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Embedding text — shared implementation in <see cref="Blade.Core.NarrativeText.Content"/>.</summary>
    public static string ContentForEmbedding(string narrative) => Blade.Core.NarrativeText.Content(narrative);

    // ------------------------------------------------------------------ shared pieces
    private string Opening(Incident i)
    {
        var where = i.Location.BusinessName is null ? i.Location.Address : $"{i.Location.BusinessName}, {i.Location.Address}";
        var callType = i.OffenseType switch
        {
            "BURGLARY" => P("a burglary", "a burglary report", "a break-in"),
            "THEFT" => P("a theft", "a theft report", "a larceny"),
            "THEFT_FROM_VEHICLE" => P("a vehicle burglary", "a theft from a vehicle", "a car break-in"),
            "AUTO_THEFT" => P("a stolen vehicle", "a stolen vehicle report"),
            "ASSAULT" => P("a disturbance with injuries", "an assault", "a fight"),
            "ROBBERY" => P("a robbery", "a robbery just occurred"),
            "VANDALISM" => P("criminal damage", "a vandalism report", "damage to property"),
            "NARCOTICS" => P("a suspicious person", "a narcotics complaint", "a suspicious vehicle"),
            "DOMESTIC" => P("a domestic disturbance", "a family disturbance", "a domestic violence call"),
            _ => "a call for service",
        };
        return P(
            $"On {D(i.ReportedAt)} at approximately {T(i.ReportedAt)} hours, I was dispatched to {where} in reference to {callType}.",
            $"On {D(i.ReportedAt)} at {T(i.ReportedAt)} hours I responded to {where} reference {callType}.",
            $"On {D(i.ReportedAt)}, at approximately {T(i.ReportedAt)} hours, I was assigned a call at {where} for {callType}.");
    }

    private string Closing(Incident i)
    {
        var arrestee = Role(i, "ARRESTEE");
        var arrest = arrestee is null ? "" :
            P($"{arrestee.FullName} was identified as the suspect and placed under arrest for {i.OffenseDescription.ToLowerInvariant()}. ",
              $"I placed {arrestee.FullName} under arrest for {i.OffenseDescription.ToLowerInvariant()}. ");
        var s = arrest + i.Status switch
        {
            "CLEARED_ARREST" => P("The suspect was transported to the Kestrel County Detention Center and booked without incident.",
                                  "The arrestee was transported and booked at the Kestrel County Detention Center."),
            "SUSPENDED" => P("No further leads at this time. Case suspended pending new information.",
                             "There are no workable leads at this time; case inactive pending further information."),
            "UNFOUNDED" => "After investigation it was determined no crime occurred. Case unfounded.",
            "CLEARED_EXCEPTIONAL" => "The victim declined prosecution. Case cleared by exception.",
            _ => P("Case forwarded to the Criminal Investigations Division for follow-up.",
                   "This report is forwarded to CID for review.",
                   "Case remains open pending follow-up investigation."),
        };
        var bwc = w.Chance(0.6) ? " My body-worn camera was activated during this contact." : "";
        return s + bwc;
    }

    private Person? Role(Incident i, string role) => i.Persons.FirstOrDefault(p => p.Role == role).Person;

    private string LossSentence(Incident i)
    {
        if (i.Property.Count == 0) return "";
        var items = string.Join("; ", i.Property.Select(p => p.Serial is null ? p.Description : $"{p.Description} (S/N {p.Serial})"));
        var total = i.Property.Sum(p => p.Value);
        return P($"Reported stolen: {items}. Estimated loss ${total:N0}.",
                 $"Items taken included {items}, with a total estimated value of ${total:N0}.",
                 $"The victim provided the following list of stolen property: {items}. Total loss approximately ${total:N0}.");
    }

    private string EvidenceSentence(Incident i)
    {
        var parts = new List<string>();
        var entry = i.OffenseType is "BURGLARY" || i.MoTags.Contains("WINDOW_SMASH");
        var commercial = i.PremiseType == "COMMERCIAL" || i.Location.BusinessName is not null;
        if (w.Chance(0.8)) parts.Add(entry ? P("I photographed the scene and point of entry.", "Scene photographs were taken.") : P("Scene photographs were taken.", "I photographed the area."));
        if (entry && w.Chance(0.35)) parts.Add(P("Latent prints were lifted and submitted to the Evidence Unit.", "I processed the point of entry for latent prints with negative results.", "Two latent lifts were collected and placed into evidence."));
        if (w.Chance(0.3)) parts.Add(commercial
            ? P("Surveillance video was requested from the business.", "The manager advised the surveillance system was not recording.")
            : P("A neighbor's doorbell camera may have captured the area; follow-up requested.", "The victim does not have security cameras."));
        if (w.Chance(0.4)) parts.Add(commercial
            ? P("A canvass of neighboring businesses was negative for witnesses.", "No witnesses were located in the area.")
            : P("I canvassed nearby residences with negative results.", "No witnesses were located during a neighborhood canvass."));
        return string.Join(' ', parts);
    }

    private string EntrySentence(Incident i, string building)
    {
        var mo = i.MoTags;
        var door = mo.Contains("REAR_ENTRY") ? P("rear door", "rear service door", "back door") : P("front door", "side door", "front entry door");
        if (mo.Contains("PRY_TOOL"))
            return P($"I observed pry marks on the {door} frame consistent with a flat-bladed tool. The strike plate was bent and the door was forced open.",
                     $"The {door} had been forced open; tool marks approximately one inch wide were visible near the latch.",
                     $"Entry was gained by prying the {door}. Fresh tool marks and splintered wood were observed at the jamb.");
        if (mo.Contains("WINDOW_SMASH"))
            return P($"Entry was made by breaking a {P("front", "side", "rear")} window of the {building}. Glass was found on the interior floor.",
                     $"A window on the {building} had been shattered and used as the point of entry.");
        if (mo.Contains("FORCED_ENTRY"))
            return P($"The {door} had been kicked in; the deadbolt was torn from the frame.",
                     $"Forced entry was evident at the {door}, which had been struck until the frame split.");
        if (mo.Contains("UNLOCKED_ENTRY"))
            return P($"There were no signs of forced entry. The victim believes the {door} may have been left unlocked.",
                     $"No forced entry was found; entry appears to have been made through an unlocked {door}.");
        return "The point of entry could not be determined.";
    }

    // ------------------------------------------------------------------ offense bodies
    private string CommercialBurglary(Incident i)
    {
        var rp = Role(i, "REPORTING_PARTY");
        // Closing time is the evening before the estimated burglary time (OccurredFrom is when entry was made).
        var closed = (i.OccurredFrom.Hour < 12 ? i.OccurredFrom.Date.AddDays(-1) : i.OccurredFrom.Date).AddHours(w.Between(19, 21)).AddMinutes(w.Pick(new[] { 0, 15, 30, 45 }));
        if (closed >= i.OccurredFrom) closed = i.OccurredFrom.AddHours(-2);
        var sb = new StringBuilder();
        sb.Append(P(
            $"I met with {rp?.FullName ?? "the manager"}, who stated the business was secured at approximately {T(closed)} hours on {D(closed)} and the burglary was discovered upon opening.",
            $"The reporting party, {rp?.FullName ?? "an employee"}, advised the business closed and was locked at {T(closed)} hours on {D(closed)}. Staff discovered the break-in when arriving to open."));
        sb.Append(' ').Append(EntrySentence(i, "business"));
        if (i.MoTags.Contains("ALARM_DEFEATED"))
            sb.Append(' ').Append(P("The alarm control panel wiring had been cut inside the back office.", "The alarm keypad was pulled from the wall and the siren wire disconnected."));
        sb.Append(' ').Append(LossSentence(i));
        sb.Append(' ').Append(EvidenceSentence(i));
        return sb.ToString();
    }

    private string ResidentialBurglary(Incident i)
    {
        var v = Role(i, "VICTIM");
        var sb = new StringBuilder();
        sb.Append(P(
            $"The homeowner, {v?.FullName ?? "the victim"}, stated they left the residence at approximately {T(i.OccurredFrom)} hours and returned to find it had been entered.",
            $"{v?.FullName ?? "The victim"} advised they were at work during the day and came home to find the house ransacked.",
            $"The victim, {v?.FullName ?? "unknown"}, reported the residence was entered while they were away."));
        sb.Append(' ').Append(EntrySentence(i, "residence"));
        sb.Append(' ').Append(P("Bedrooms had been rummaged through and drawers were dumped on the floor.", "The master bedroom closet had been searched.", "Several rooms appeared to have been searched."));
        sb.Append(' ').Append(LossSentence(i)).Append(' ').Append(EvidenceSentence(i));
        return sb.ToString();
    }

    private string Theft(Incident i)
    {
        var s = Role(i, "SUSPECT");
        if (i.PremiseType == "COMMERCIAL")
            return P(
                $"Loss prevention staff advised a {(s is null ? "subject" : "subject later identified as " + s.FullName)} concealed merchandise and exited past all points of sale without paying.",
                $"The store clerk reported a subject took merchandise from the shelf and left the store without paying. {(s is null ? "The subject left the area before my arrival." : "The subject was detained and identified as " + s.FullName + ".")}") +
                " " + LossSentence(i);
        var item = i.Property.FirstOrDefault()?.Description ?? "";
        var story = item.Contains("package") ? "The victim reported a package was taken from the front porch shortly after delivery."
                  : item.Contains("bicycle") ? "The victim reported their bicycle was taken from the front yard."
                  : "The victim stated lawn equipment was taken from an unlocked shed in the back yard.";
        return story + " " + LossSentence(i) + " " + EvidenceSentence(i);
    }

    private string TheftFromVehicle(Incident i)
    {
        var veh = i.Vehicles.FirstOrDefault().Vehicle;
        var vdesc = veh is null ? "their vehicle" : $"their {veh.Description} (plate {veh.Plate})";
        if (i.MoTags.Contains("CATALYTIC_CONVERTER"))
            return P(
                $"The victim stated that when they started {vdesc} it made a loud roaring noise. I looked under the vehicle and observed the catalytic converter had been cut out; saw marks were visible on the exhaust pipe on both sides.",
                $"The victim reported the catalytic converter was cut from {vdesc} overnight. Fresh cuts from a reciprocating saw were visible on the exhaust.") +
                " " + LossSentence(i) + " " + EvidenceSentence(i);
        var entry = i.MoTags.Contains("WINDOW_SMASH")
            ? P("The front passenger window had been smashed.", "The rear driver-side window was broken out.", "A window had been shattered and glass was on the seat.")
            : P("The victim believes the vehicle was left unlocked.", "There was no damage to the vehicle; it may have been unlocked.");
        return $"The victim reported {vdesc} was entered while parked. {entry} " + LossSentence(i) + " " + EvidenceSentence(i);
    }

    private string AutoTheft(Incident i)
    {
        var veh = i.Vehicles.FirstOrDefault().Vehicle;
        return P(
            $"The victim reported their {veh?.Description} bearing plate {veh?.Plate} was parked at the location and was gone when they returned.",
            $"The owner advised their {veh?.Description} (plate {veh?.Plate}) was stolen from the parking area overnight.") +
            " " + P("The victim had both keys in their possession.", "The victim stated a spare key may have been left in the vehicle.", "Broken glass was found where the vehicle had been parked.") +
            " The vehicle was entered into NCIC as stolen.";
    }

    private string Assault(Incident i)
    {
        var v = Role(i, "VICTIM");
        var s = Role(i, "SUSPECT");
        var weapon = i.MoTags.Contains("WEAPON_KNIFE")
            ? P(" The suspect displayed a folding knife during the altercation.", " The victim stated the suspect swung a knife at them.", " A kitchen knife was recovered at the scene.")
            : i.MoTags.Contains("WEAPON_FIREARM") ? P(" The victim stated the suspect brandished a handgun.", " Witnesses reported the suspect displayed a black handgun.") : "";
        var place = i.PremiseType == "COMMERCIAL" ? "inside the business" : i.PremiseType == "RESIDENTIAL" ? "at the residence" : "in the parking lot";
        return P(
            $"{v?.FullName ?? "The victim"} stated they got into a verbal argument with {(s?.FullName ?? "an unknown male")} {place} which became physical.",
            $"I contacted {v?.FullName ?? "the victim"} who had visible injuries to the face. They reported being struck by {(s?.FullName ?? "an unknown subject")} {place}.") +
            weapon + " " +
            P("EMS evaluated the victim on scene.", "The victim refused medical treatment.", "The victim was transported to Kestrel Regional Medical Center for treatment.") +
            " " + P("Witness statements were obtained.", "Two witnesses provided written statements.", "No independent witnesses were located.");
    }

    private string Robbery(Incident i)
    {
        var weapon = i.MoTags.Contains("WEAPON_FIREARM") ? "a black handgun" : i.MoTags.Contains("WEAPON_KNIFE") ? "a knife" : "what appeared to be a weapon concealed in a pocket";
        return P(
            $"The clerk stated a subject wearing a dark hoodie and face covering approached the counter, displayed {weapon}, and demanded money from the register.",
            $"The victim stated they were approached by a subject who displayed {weapon} and demanded their wallet and phone.") +
            " " + P("The suspect fled on foot toward the rear of the building.", "The suspect fled in a dark sedan, no plate obtained.", "The suspect fled northbound on foot.") +
            " " + LossSentence(i) + " " + EvidenceSentence(i);
    }

    private string Vandalism(Incident i) =>
        P("The victim reported graffiti spray-painted on the exterior wall.", "The victim reported all four tires on their vehicle were slashed.",
          "The victim stated rocks were thrown through two front windows.", "The victim reported their mailbox and fence were damaged overnight.") +
        $" Estimated damage is ${w.Money(150, 3500):N0}. " + EvidenceSentence(i);

    private string Narcotics(Incident i)
    {
        var s = Role(i, "SUSPECT");
        return P(
            $"I made contact with {s?.FullName ?? "a subject"} who was seated in a parked vehicle with the engine running. I observed a glass pipe with burnt residue in plain view.",
            $"While on patrol I observed {s?.FullName ?? "a subject"} conduct what appeared to be a hand-to-hand transaction. A consent search located a baggie of a white crystalline substance.",
            $"During a traffic stop I detected the odor of marijuana. A probable cause search located a quantity of suspected methamphetamine.") +
            " " + P("The substance field-tested positive for methamphetamine.", "The substance was placed into evidence for lab analysis.", "Approximately 3.2 grams of suspected methamphetamine was seized.") +
            " " + P("Miranda warnings were read and the subject declined to answer questions.", "Miranda was read; the subject admitted the item was theirs.", "");
    }

    private string Domestic(Incident i)
    {
        var v = Role(i, "VICTIM");
        var s = Role(i, "SUSPECT");
        return P(
            $"{v?.FullName ?? "The victim"} stated their partner, {s?.FullName ?? "the suspect"}, became angry during an argument and pushed them into a wall.",
            $"I separated the parties. {v?.FullName ?? "The victim"} stated {s?.FullName ?? "the suspect"} grabbed their arm and would not let them leave the residence.") +
            " " + P("I observed redness on the victim's upper arm.", "No visible injuries were observed.", "The victim had a small abrasion on the forearm.") +
            " " + P("A lethality assessment was completed.", "Victim resource information was provided.", "The victim was provided a DV resource card and advised of protective order options.");
    }
}
