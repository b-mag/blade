namespace Blade.DataGen.Persistence;

/// <summary>
/// SQLite schema shaped like a simplified RMS / CAD / JMS / pawn-reporting estate.
/// In production these would be read-only replicas of the agency's existing systems.
/// </summary>
public static class Schema
{
    public const string Ddl = """
        PRAGMA foreign_keys = OFF;

        CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);

        CREATE TABLE locations (
            id INTEGER PRIMARY KEY, address TEXT NOT NULL, number INTEGER, street TEXT, unit TEXT, city TEXT,
            district TEXT NOT NULL, premise_type TEXT NOT NULL, business_name TEXT, business_type TEXT, x REAL, y REAL);

        CREATE TABLE persons (
            id INTEGER PRIMARY KEY, first_name TEXT NOT NULL, middle_name TEXT, last_name TEXT NOT NULL, dob TEXT NOT NULL,
            sex TEXT, home_location_id INTEGER, phone TEXT, dl_number TEXT, notes TEXT);

        CREATE TABLE vehicles (
            id INTEGER PRIMARY KEY, plate TEXT NOT NULL UNIQUE, year INTEGER, make TEXT, model TEXT, color TEXT, style TEXT,
            owner_person_id INTEGER, notes TEXT);

        CREATE TABLE deputies (
            id INTEGER PRIMARY KEY, badge TEXT NOT NULL, first_name TEXT, last_name TEXT, rank TEXT, unit TEXT, district TEXT);

        -- RMS
        CREATE TABLE incidents (
            id INTEGER PRIMARY KEY, case_number TEXT NOT NULL UNIQUE, offense_type TEXT NOT NULL, offense_code TEXT,
            offense_description TEXT, district TEXT NOT NULL, location_id INTEGER NOT NULL, premise_type TEXT NOT NULL,
            occurred_from TEXT NOT NULL, occurred_to TEXT, occurred_date TEXT NOT NULL, occurred_hour INTEGER NOT NULL,
            reported_at TEXT NOT NULL, mo_tags TEXT, status TEXT NOT NULL, deputy_id INTEGER, sensitivity TEXT NOT NULL,
            narrative TEXT NOT NULL);
        CREATE TABLE incident_mo (incident_id INTEGER NOT NULL, mo_tag TEXT NOT NULL);
        CREATE TABLE incident_persons (incident_id INTEGER NOT NULL, person_id INTEGER NOT NULL, role TEXT NOT NULL);
        CREATE TABLE incident_vehicles (incident_id INTEGER NOT NULL, vehicle_id INTEGER NOT NULL, role TEXT NOT NULL);
        CREATE TABLE incident_property (
            id INTEGER PRIMARY KEY, incident_id INTEGER NOT NULL, description TEXT, serial TEXT, value REAL, status TEXT);

        -- CAD
        CREATE TABLE cad_calls (
            id INTEGER PRIMARY KEY, call_number TEXT NOT NULL UNIQUE, received_at TEXT NOT NULL, call_type TEXT, priority INTEGER,
            location_id INTEGER, district TEXT, disposition TEXT, incident_id INTEGER, deputy_id INTEGER, caller_phone TEXT, notes TEXT);

        -- Field interviews & LPR
        CREATE TABLE field_interviews (
            id INTEGER PRIMARY KEY, fi_number TEXT NOT NULL UNIQUE, contact_at TEXT NOT NULL, location_id INTEGER, district TEXT,
            deputy_id INTEGER, person_id INTEGER, vehicle_id INTEGER, reason TEXT, notes TEXT);
        CREATE TABLE lpr_cameras (id INTEGER PRIMARY KEY, name TEXT, location_id INTEGER, district TEXT);
        CREATE TABLE lpr_reads (id INTEGER PRIMARY KEY, read_at TEXT NOT NULL, plate TEXT NOT NULL, vehicle_id INTEGER, camera_id INTEGER);

        -- JMS
        CREATE TABLE bookings (
            id INTEGER PRIMARY KEY, booking_number TEXT NOT NULL UNIQUE, person_id INTEGER NOT NULL, booked_at TEXT NOT NULL,
            released_at TEXT, charges TEXT, incident_id INTEGER, housing_unit TEXT, classification TEXT,
            keep_separate_person_id INTEGER, notes TEXT);
        CREATE TABLE booking_coarrestees (booking_id INTEGER NOT NULL, person_id INTEGER NOT NULL);

        -- Pawn / secondhand-dealer reporting
        CREATE TABLE pawn_transactions (
            id INTEGER PRIMARY KEY, ticket_number TEXT NOT NULL UNIQUE, transaction_at TEXT NOT NULL, shop_location_id INTEGER,
            person_id INTEGER, transaction_type TEXT, item_description TEXT, serial TEXT, amount REAL, seller_phone TEXT);

        -- Policy manual
        CREATE TABLE policies (policy_number TEXT PRIMARY KEY, title TEXT NOT NULL, effective TEXT, revised TEXT, file_name TEXT);
        CREATE TABLE policy_sections (
            id INTEGER PRIMARY KEY, policy_number TEXT NOT NULL, section_number TEXT NOT NULL UNIQUE, heading TEXT NOT NULL,
            level INTEGER NOT NULL, parent_section TEXT, ordinal INTEGER NOT NULL, text TEXT NOT NULL);

        -- Vector index (brute-force cosine in the POC; SQL Server 2025 VECTOR / pgvector / Qdrant in production)
        CREATE TABLE embeddings (
            id INTEGER PRIMARY KEY, doc_type TEXT NOT NULL, doc_id TEXT NOT NULL, model TEXT NOT NULL, dim INTEGER NOT NULL,
            vector BLOB NOT NULL, UNIQUE (doc_type, doc_id));

        -- Keyword index (BM25) for hybrid retrieval alongside vectors
        CREATE VIRTUAL TABLE incidents_fts USING fts5(case_number UNINDEXED, narrative, tokenize = 'porter unicode61');
        CREATE VIRTUAL TABLE policy_fts USING fts5(section_number UNINDEXED, heading, text, tokenize = 'porter unicode61');

        -- Evaluation ground truth for planted storylines. The server must never read this table.
        CREATE TABLE eval_storyline_truth (storyline TEXT NOT NULL, entity_type TEXT NOT NULL, entity_key TEXT NOT NULL, note TEXT);

        CREATE INDEX ix_incidents_filter ON incidents (offense_type, district, premise_type, occurred_date);
        CREATE INDEX ix_incidents_hour ON incidents (occurred_hour);
        CREATE INDEX ix_incident_mo ON incident_mo (mo_tag, incident_id);
        CREATE INDEX ix_incident_persons ON incident_persons (person_id);
        CREATE INDEX ix_incident_vehicles ON incident_vehicles (vehicle_id);
        CREATE INDEX ix_property_serial ON incident_property (serial);
        CREATE INDEX ix_pawn_serial ON pawn_transactions (serial);
        CREATE INDEX ix_pawn_phone ON pawn_transactions (seller_phone);
        CREATE INDEX ix_persons_phone ON persons (phone);
        CREATE INDEX ix_lpr_plate ON lpr_reads (plate, read_at);
        CREATE INDEX ix_cad_incident ON cad_calls (incident_id);
        CREATE INDEX ix_bookings_person ON bookings (person_id);
        """;
}
