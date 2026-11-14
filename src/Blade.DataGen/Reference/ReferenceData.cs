namespace Blade.DataGen.Reference;

/// <summary>
/// Hand-built reference lists for a fictional county. Street, town and business names are invented.
/// Person names are common given/family names combined at random — any resemblance to a real person is coincidental.
/// </summary>
public static class ReferenceData
{
    public static readonly string[] FirstNamesMale =
    [
        "James", "Michael", "Robert", "David", "William", "Joseph", "Daniel", "Matthew", "Anthony", "Mark",
        "Steven", "Paul", "Andrew", "Joshua", "Kevin", "Brian", "Jason", "Ryan", "Eric", "Jacob",
        "Tyler", "Aaron", "Adam", "Nathan", "Zachary", "Kyle", "Dustin", "Cody", "Marcus", "Luis",
        "Carlos", "Miguel", "Andre", "Darnell", "Trevor", "Wesley", "Shane", "Derek", "Colton", "Austin"
    ];

    public static readonly string[] FirstNamesFemale =
    [
        "Mary", "Jennifer", "Linda", "Elizabeth", "Susan", "Jessica", "Sarah", "Karen", "Nancy", "Lisa",
        "Ashley", "Emily", "Amanda", "Melissa", "Michelle", "Stephanie", "Rebecca", "Laura", "Amber", "Crystal",
        "Brittany", "Heather", "Tiffany", "Megan", "Rachel", "Kayla", "Courtney", "Danielle", "Maria", "Rosa",
        "Tasha", "Keisha", "Brooke", "Hannah", "Kelsey", "Paige", "Jenna", "Lacey", "Monica", "Vanessa"
    ];

    public static readonly string[] LastNames =
    [
        "Abbott", "Alvarez", "Barker", "Bates", "Benson", "Bishop", "Boone", "Bowers", "Bradley", "Briggs",
        "Burke", "Caldwell", "Carver", "Castillo", "Chandler", "Cobb", "Collier", "Conner", "Crane", "Cross",
        "Dalton", "Davila", "Dawson", "Deleon", "Dillard", "Doyle", "Duran", "Ellison", "Espinoza", "Farley",
        "Fenton", "Fischer", "Fleming", "Fowler", "Garrison", "Gentry", "Gibbs", "Goodwin", "Graves", "Griffin",
        "Hale", "Hammond", "Hardin", "Harmon", "Hayes", "Hendrix", "Hobbs", "Holt", "Huff", "Ingram",
        "Jarvis", "Jennings", "Keller", "Kirby", "Knox", "Lambert", "Landry", "Lowe", "Lucero", "Lyons",
        "Maddox", "Malone", "Marsh", "Maxwell", "McBride", "McCoy", "Mercer", "Molina", "Monroe", "Nash",
        "Navarro", "Nolan", "Norris", "Oakley", "Ochoa", "Owens", "Pace", "Parrish", "Payne", "Pierce",
        "Pruitt", "Quinn", "Ramsey", "Randall", "Reyes", "Rhodes", "Riggs", "Rollins", "Rowe", "Salazar",
        "Sawyer", "Shelton", "Sims", "Slater", "Stokes", "Strickland", "Sutton", "Tate", "Thornton", "Tolbert",
        "Trujillo", "Tucker", "Underwood", "Vance", "Vaughn", "Wade", "Walsh", "Ware", "Whitaker", "Wilder",
        "Winters", "Wolfe", "Yates", "York", "Zamora"
    ];

    public sealed record District(string Code, string Town, double CenterX, double CenterY, string[] ResidentialStreets, string[] CommercialStreets);

    public static readonly District[] Districts =
    [
        new("NORTH", "Northgate", 20, 34,
            ["Ridgeview Rd", "Falcon Way", "Harrow St", "Birchwood Dr", "Summit Dr", "Quail Run Ct", "Aspen Hollow Ln"],
            ["Northgate Blvd", "Industrial Pkwy", "Timber Mill Rd", "Quarry Ln", "Copperline Rd", "Kestrel Ave N"]),
        new("SOUTH", "Brannock", 20, 6,
            ["Magnolia St", "Sawgrass Ct", "Willow Creek Dr", "Pecan St", "Orchard Ln", "Levee Rd", "Delta Ave"],
            ["Brannock Pike", "Market St", "Kestrel Ave S", "Cotton Gin Rd", "River Bend Rd"]),
        new("EAST", "Ashby Mills", 34, 20,
            ["Larkspur Ct", "Elm Terrace", "Primrose Ln", "Bluestem Dr", "Granite Way", "Union St", "Sparrow Ln"],
            ["Ashby Mills Rd", "Commerce Dr", "Eastgate Blvd", "Foundry Rd", "Railyard Rd", "Station St"]),
        new("WEST", "Cedar Fork", 6, 20,
            ["Meadowlark Ln", "Sage St", "Juniper Ave", "Mesa Dr", "Hollow Oak Dr", "Bison Trail", "Prairie View Dr"],
            ["Cedar Fork Rd", "Westridge Pkwy", "Canyon Rd", "Saddle Club Rd", "Ranch Rd 12"]),
    ];

    /// <summary>Business type → weight. Types drive what gets stolen and narrative wording.</summary>
    public static readonly (string Type, int Weight)[] BusinessTypes =
    [
        ("Hardware Store", 4), ("Tool Rental", 2), ("Construction Supply", 2), ("Auto Parts", 3), ("Tire Shop", 2),
        ("Convenience Store", 6), ("Liquor Store", 3), ("Pharmacy", 2), ("Electronics Store", 2), ("Restaurant", 6),
        ("Bar & Grill", 3), ("Dental Office", 2), ("Self Storage", 2), ("Gas Station", 4), ("Salon", 2),
        ("Feed & Farm Supply", 2), ("Thrift Store", 2), ("Credit Union", 1), ("Dollar Store", 3), ("Machine Shop", 2),
    ];

    public static readonly string[] BusinessNamePrefixes =
    [
        "Northgate", "Brannock", "Ashby", "Cedar Fork", "Kestrel", "Pine Hollow", "Red Oak", "Timberline", "Prairie",
        "Riverside", "Summit", "Bluestem", "Iron Creek", "Lone Elm", "Copperline", "Granite", "Meadowlark", "Falcon",
        "Union", "Old Mill", "Railyard", "Westridge", "Magnolia", "Sawgrass", "Harvest", "Keystone", "Frontier", "Heritage"
    ];

    public static readonly string[] VehicleColors = ["White", "Black", "Silver", "Gray", "Red", "Blue", "Maroon", "Green", "Tan", "Gold"];

    public static readonly (string Make, string Model, string Style)[] VehicleModels =
    [
        ("Ford", "F-150", "Pickup"), ("Chevrolet", "Silverado", "Pickup"), ("Ram", "1500", "Pickup"), ("Toyota", "Tacoma", "Pickup"),
        ("Toyota", "Camry", "Sedan"), ("Honda", "Accord", "Sedan"), ("Honda", "Civic", "Sedan"), ("Nissan", "Altima", "Sedan"),
        ("Hyundai", "Elantra", "Sedan"), ("Kia", "Optima", "Sedan"), ("Chevrolet", "Malibu", "Sedan"), ("Ford", "Fusion", "Sedan"),
        ("Toyota", "Prius", "Hatchback"), ("Honda", "CR-V", "SUV"), ("Toyota", "RAV4", "SUV"), ("Ford", "Explorer", "SUV"),
        ("Jeep", "Grand Cherokee", "SUV"), ("Chevrolet", "Tahoe", "SUV"), ("Nissan", "Rogue", "SUV"), ("Subaru", "Outback", "Wagon"),
        ("Ford", "Transit", "Van"), ("Chevrolet", "Express", "Van"), ("Dodge", "Grand Caravan", "Minivan"), ("GMC", "Sierra", "Pickup"),
    ];

    public static readonly string[] DeputyRanks = ["Deputy", "Deputy", "Deputy", "Deputy", "Senior Deputy", "Corporal", "Sergeant"];
    public static readonly string[] DeputyUnits = ["Patrol", "Patrol", "Patrol", "Patrol", "Patrol", "Criminal Investigations", "Narcotics", "Traffic"];

    public static readonly string[] PlateLetters = ["K", "C", "T", "R", "M", "L", "N", "P", "D", "H", "B", "W", "X", "J", "V", "F"];
}
