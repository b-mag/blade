using Blade.DataGen;
using Blade.DataGen.Generation;
using static Blade.Tests.DataGenFixture;

namespace Blade.Tests;

public sealed class DataGenTests(DataGenFixture fx) : IClassFixture<DataGenFixture>
{
    // ---------------------------------------------------------------- volumes & hygiene
    [Fact]
    public void Volumes_are_in_expected_ranges()
    {
        var c = fx.Result.TableCounts;
        Assert.InRange(c["incidents"], 1400, 1600);
        Assert.InRange(c["persons"], 900, 1200);
        Assert.InRange(c["cad_calls"], 3500, 4500);
        Assert.InRange(c["bookings"], 400, 600);
        Assert.InRange(c["field_interviews"], 290, 330);
        Assert.Equal(18, c["policies"]);
        Assert.True(c["policy_sections"] > 150);
    }

    [Fact]
    public void Only_the_planted_phone_is_shared_between_people()
    {
        using var db = fx.Open();
        var shared = Rows(db, "SELECT phone, COUNT(*) FROM persons WHERE phone IS NOT NULL GROUP BY phone HAVING COUNT(*) > 1");
        Assert.Single(shared);
        var truthPhone = Scalar<string>(db, $"SELECT entity_key FROM eval_storyline_truth WHERE storyline='{Storylines.S2}' AND entity_type='PHONE'");
        Assert.Equal(truthPhone, shared[0][0]);
    }

    [Fact]
    public void Phone_numbers_use_the_fictional_555_01xx_range()
    {
        using var db = fx.Open();
        Assert.Equal(0, Scalar<long>(db, "SELECT COUNT(*) FROM persons WHERE phone IS NOT NULL AND phone NOT LIKE '(___) 555-01__'"));
    }

    [Fact]
    public void Juveniles_are_never_booked_into_the_county_jail()
    {
        using var db = fx.Open();
        var underage = Scalar<long>(db, """
            SELECT COUNT(*) FROM bookings b JOIN persons p ON p.id = b.person_id
            WHERE date(p.dob, '+18 years') > date(b.booked_at)
            """);
        Assert.Equal(0, underage);
        Assert.True(Scalar<long>(db, "SELECT COUNT(*) FROM incidents WHERE sensitivity='JUVENILE'") > 0);
    }

    [Fact]
    public void Every_incident_has_a_cad_call_and_a_narrative()
    {
        using var db = fx.Open();
        Assert.Equal(0, Scalar<long>(db, "SELECT COUNT(*) FROM incidents i WHERE NOT EXISTS (SELECT 1 FROM cad_calls c WHERE c.incident_id = i.id)"));
        Assert.Equal(0, Scalar<long>(db, "SELECT COUNT(*) FROM incidents WHERE length(narrative) < 150"));
    }

    [Fact]
    public async Task Same_seed_produces_identical_data()
    {
        var other = Path.Combine(Path.GetTempPath(), $"blade-test-{Guid.NewGuid():N}.db");
        try
        {
            await DataGenerator.RunAsync(fx.Options(other));
            using var a = fx.Open();
            using var b = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={other};Mode=ReadOnly;Pooling=False");
            b.Open();
            const string sql = "SELECT group_concat(case_number || narrative, '') FROM (SELECT case_number, narrative FROM incidents ORDER BY id)";
            Assert.Equal(Scalar<string>(a, sql), Scalar<string>(b, sql));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(other);
        }
    }

    // ---------------------------------------------------------------- S1 burglary series
    private const string DemoFilterSql = """
        SELECT i.case_number FROM incidents i
        WHERE i.offense_type = 'BURGLARY' AND i.district = 'NORTH' AND i.premise_type = 'COMMERCIAL'
          AND i.occurred_date >= '2025-05-14' AND (i.occurred_hour >= 22 OR i.occurred_hour < 6)
          AND EXISTS (SELECT 1 FROM incident_mo m WHERE m.incident_id = i.id AND m.mo_tag = 'FORCED_ENTRY')
        """;

    [Fact]
    public void Demo_question_filter_returns_all_seven_series_burglaries_among_lookalikes()
    {
        using var db = fx.Open();
        var hits = Rows(db, DemoFilterSql).Select(r => (string)r[0]!).ToHashSet();
        var series = Rows(db, $"SELECT entity_key FROM eval_storyline_truth WHERE storyline='{Storylines.S1}' AND note LIKE 'Series burglary%'")
            .Select(r => (string)r[0]!).ToList();

        Assert.Equal(7, series.Count);
        Assert.All(series, s => Assert.Contains(s, hits));
        Assert.InRange(hits.Count, 10, 40); // the series must be found among realistic noise, not alone
    }

    [Fact]
    public void Series_van_is_read_by_lpr_near_three_burglaries_and_seen_on_field_interviews()
    {
        using var db = fx.Open();
        Assert.Equal(6, Scalar<long>(db, "SELECT COUNT(*) FROM lpr_reads WHERE plate = '418KTV'"));
        Assert.Equal(3, Scalar<long>(db, "SELECT COUNT(*) FROM field_interviews f JOIN vehicles v ON v.id = f.vehicle_id WHERE v.plate = '418KTV'"));
        // Registered owner is not the suspect — the link only appears through the shared home address.
        var owner = Rows(db, "SELECT p.last_name, p.home_location_id FROM vehicles v JOIN persons p ON p.id = v.owner_person_id WHERE v.plate = '418KTV'").Single();
        Assert.Equal("Pruitt", owner[0]);
        Assert.Equal(owner[1], Scalar<long>(db, "SELECT home_location_id FROM persons WHERE first_name='Travis' AND last_name='Kessler'"));
    }

    [Fact]
    public void Associate_pawned_tools_whose_serials_match_series_property()
    {
        using var db = fx.Open();
        var matches = Rows(db, """
            SELECT p.first_name || ' ' || p.last_name, i.case_number
            FROM pawn_transactions pt
            JOIN incident_property ip ON ip.serial = pt.serial
            JOIN incidents i ON i.id = ip.incident_id
            JOIN persons p ON p.id = pt.person_id
            """);
        Assert.Equal(2, matches.Count);
        Assert.All(matches, m => Assert.Equal("Cody Mercer", m[0]));
    }

    [Fact]
    public void Suspect_and_associate_were_co_arrested_with_keep_separate_orders()
    {
        using var db = fx.Open();
        Assert.Equal(2, Scalar<long>(db, "SELECT COUNT(*) FROM booking_coarrestees"));
        Assert.Equal(2, Scalar<long>(db, "SELECT COUNT(*) FROM bookings WHERE keep_separate_person_id IS NOT NULL"));
    }

    [Fact]
    public void Keyword_index_finds_van_witness_reports_in_the_series()
    {
        using var db = fx.Open();
        var hits = Rows(db, "SELECT case_number FROM incidents_fts WHERE incidents_fts MATCH 'van AND (ladder OR pried OR pry)'").Select(r => (string)r[0]!).ToList();
        var s1 = Rows(db, $"SELECT entity_key FROM eval_storyline_truth WHERE storyline='{Storylines.S1}' AND entity_type='INCIDENT'").Select(r => (string)r[0]!).ToHashSet();
        Assert.True(hits.Count >= 3);
        Assert.All(hits, h => Assert.Contains(h, s1));
    }

    [Fact]
    public void Embedding_text_strips_dispatch_and_administrative_boilerplate()
    {
        const string n = "On 01/02/2026 at approximately 0700 hours, I was dispatched to 1 Main St in reference to a burglary. " +
                         "The rear door was pried. Case suspended pending new information. My body-worn camera was activated during this contact.";
        Assert.Equal("The rear door was pried.", NarrativeBuilder.ContentForEmbedding(n));
    }

    // ---------------------------------------------------------------- S2 / S3 / S4
    [Fact]
    public void Shared_phone_appears_in_two_reports_one_fi_and_three_scrap_sales()
    {
        using var db = fx.Open();
        var phone = Scalar<string>(db, $"SELECT entity_key FROM eval_storyline_truth WHERE storyline='{Storylines.S2}' AND entity_type='PHONE'");
        Assert.Equal(2, Scalar<long>(db, $"SELECT COUNT(*) FROM incidents WHERE narrative LIKE '%{phone}%'"));
        Assert.Equal(1, Scalar<long>(db, $"SELECT COUNT(*) FROM field_interviews WHERE notes LIKE '%{phone}%'"));
        Assert.Equal(3, Scalar<long>(db, $"SELECT COUNT(*) FROM pawn_transactions WHERE seller_phone = '{phone}' AND transaction_type = 'SCRAP_SALE'"));
    }

    [Fact]
    public void Repeat_dv_address_has_five_calls_and_escalates_to_a_knife()
    {
        using var db = fx.Open();
        Assert.Equal(5, Scalar<long>(db, "SELECT COUNT(*) FROM cad_calls c JOIN locations l ON l.id = c.location_id WHERE l.address = '1418 Larkspur Ct'"));
        var last = Rows(db, "SELECT i.mo_tags, i.status FROM incidents i JOIN locations l ON l.id = i.location_id WHERE l.address = '1418 Larkspur Ct' ORDER BY i.occurred_from DESC LIMIT 1").Single();
        Assert.Contains("WEAPON_KNIFE", (string)last[0]!);
        Assert.Equal("CLEARED_ARREST", last[1]);
    }

    [Fact]
    public void Knife_use_of_force_case_exists_for_policy_pairing()
    {
        using var db = fx.Open();
        Assert.Equal(1, Scalar<long>(db, "SELECT COUNT(*) FROM incidents WHERE narrative LIKE '%40mm%' AND mo_tags LIKE '%WEAPON_KNIFE%'"));
    }

    // ---------------------------------------------------------------- policies
    [Theory]
    [InlineData("300.3.6", "Edged")]
    [InlineData("300.3.5", "Non-Compliant")]
    [InlineData("300.4.1", "Deadly Force")]
    [InlineData("830.4", null)]
    public void Policy_sections_needed_by_golden_questions_exist(string section, string? headingContains)
    {
        using var db = fx.Open();
        var rows = Rows(db, $"SELECT heading, length(text) FROM policy_sections WHERE section_number = '{section}'");
        var row = Assert.Single(rows);
        if (headingContains is not null) Assert.Contains(headingContains, (string)row[0]!, StringComparison.OrdinalIgnoreCase);
        Assert.True((long)row[1]! > 100);
    }
}
