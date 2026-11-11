using Worktown.NameBake;
using Xunit;

namespace Worktown.Gateway.Tests;

/// <summary>Spec 056b AC6: the review sheet and its flags.</summary>
public class ReviewSheetTests
{
    private static string Content(string name, string description = "A warm plate from the town kitchen.") =>
        "{\"name\":\"" + name + "\",\"description\":\"" + description + "\"}";

    [Fact]
    public async Task DecisionsSurviveARebakeAndANoRowIsRegeneratedOnce()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        Assert.Equal(3, f.Stub.CallCount);
        f.Decide(OfflineFixture.FriesName, "OK ");
        f.Decide("boiled potato one", "no");

        f.Stub.EnqueueContent(Content("Soft Potato Bites"));
        var second = await f.BakeAsync();
        Assert.Equal(4, f.Stub.CallCount); // only the "no" row is asked again
        Assert.Equal(1, second.Counts.Regenerated);
        Assert.Equal(1, second.Counts.Misses);
        var rows = f.Sheet();
        var fries = rows.Single(r => r.PlainName == OfflineFixture.FriesName);
        Assert.Equal(ReviewRow.Ok, fries.Decision);
        var redone = rows.Single(r => r.PlainName == "boiled potato one");
        Assert.Equal("Soft Potato Bites", redone.Name);
        Assert.Equal("", redone.Decision);
        Assert.Equal("rejected: Golden Spud Sticks", redone.Note);

        var third = await f.BakeAsync(); // the regenerated row waits for a decision: no more calls
        Assert.Equal(4, f.Stub.CallCount);
        Assert.Equal(0, third.Counts.Regenerated);
        Assert.Equal("rejected: Golden Spud Sticks", f.Sheet().Single(r => r.PlainName == "boiled potato one").Note);
    }

    [Fact]
    public async Task ANoRowCountsInTheLimit()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide("boiled potato one", "no");
        f.Decide("boiled potato two", "no");
        var result = await f.BakeAsync(limit: 1);
        Assert.Equal(4, f.Stub.CallCount);
        Assert.True(result.Counts.LimitReached);
        Assert.Single(f.Sheet(), r => r.Decision == ReviewRow.No); // the other waits for the next bake
    }

    [Fact]
    public async Task AReviewersEditOfANameIsKeptOnARebake()
    {
        using var f = new OfflineFixture();
        await f.BakeAsync();
        f.Decide(OfflineFixture.FriesName, "ok", name: "Crispy Spud Sticks");
        await f.BakeAsync();
        var fries = f.Sheet().Single(r => r.PlainName == OfflineFixture.FriesName);
        Assert.Equal("Crispy Spud Sticks", fries.Name);
        Assert.Equal("", fries.Flags);
    }

    [Fact]
    public async Task RowsAreSortedByFirstRouteThenTargetThenPlainName()
    {
        using var f = new OfflineFixture(items: 4);
        await f.BakeAsync();
        var rows = f.Sheet();
        Assert.Equal(new[] { "fries,veggie_box", "potato_soup", "potato_soup", "potato_soup" }, rows.Select(r => r.Routes).ToArray());
        Assert.True(rows[1].Target);
        Assert.Equal(new[] { "boiled potato three", "boiled potato two" }, rows.Skip(2).Select(r => r.PlainName).ToArray());
        Assert.Equal(ReviewSheet.Header, File.ReadAllLines(f.ReviewPath)[0]);
        Assert.DoesNotContain("\r", File.ReadAllText(f.ReviewPath));
    }

    [Fact]
    public void ParseAndFormatRoundTripAndAcceptASpreadsheetsCrlf()
    {
        var rows = new[]
        {
            new ReviewRow("0123456789ab", "fries,veggie_box", true, "golden fries", "Golden Fries", "Crisp fries.", "dup", "ok", "fine"),
            new ReviewRow("abcdefabcdef", "soda", false, "cup of soda", "Fizzy Cup", "A cold cup.", "", "", ""),
        };
        var text = ReviewSheet.Format(rows);
        Assert.Equal(rows, ReviewSheet.Parse(text));
        Assert.Equal(rows, ReviewSheet.Parse(text.Replace("\n", "\r\n")));
        // A spreadsheet may drop trailing empty cells.
        Assert.Equal(rows[1], ReviewSheet.Parse(ReviewSheet.Header + "\nabcdefabcdef\tsoda\tfalse\tcup of soda\tFizzy Cup\tA cold cup.\n")[0]);
    }

    [Fact]
    public void ParseRejectsABadSheet()
    {
        Assert.Throws<InvalidDataException>(() => ReviewSheet.Parse("key\troutes\n"));
        Assert.Throws<InvalidDataException>(() => ReviewSheet.Parse(ReviewSheet.Header + "\nnot-a-key\ta\tfalse\tb\tc\td\t\t\t\n"));
        var row = "0123456789ab\ta\tfalse\tb\tc\td\t\t\t\n";
        Assert.Throws<InvalidDataException>(() => ReviewSheet.Parse(ReviewSheet.Header + "\n" + row + row));
        Assert.Throws<InvalidDataException>(() => ReviewSheet.Parse(ReviewSheet.Header + "\n0123456789ab\ta\tfalse\tb\tc\td\t\t\t\t\textra\n"));
        Assert.Empty(ReviewSheet.Parse(""));
        Assert.Empty(ReviewSheet.Load(Path.Combine(Path.GetTempPath(), "no-such-review-" + Guid.NewGuid().ToString("N") + ".tsv")));
    }

    [Fact]
    public void FormatRejectsATabInACell()
    {
        var row = new ReviewRow("0123456789ab", "a", false, "b", "Name\tTab", "d", "", "", "");
        Assert.Throws<InvalidDataException>(() => ReviewSheet.Format(new[] { row }));
    }

    // ---- flags: each has a positive and a negative case ----

    [Fact]
    public void DonenessFlagsAMissingLabelWord()
    {
        Assert.True(ReviewFlags.MissesDoneness("Golden Spud Sticks", new[] { "crispy" }));
        Assert.False(ReviewFlags.MissesDoneness("Crispy Spud Sticks", new[] { "crispy" }));
        Assert.False(ReviewFlags.MissesDoneness("Golden Spud Sticks", Array.Empty<string>()));
        Assert.True(ReviewFlags.MissesDoneness("Full Lemonade", new[] { "half-full" }));
        Assert.False(ReviewFlags.MissesDoneness("Half-Full Lemonade", new[] { "half-full" }));
    }

    [Fact]
    public void SameFlagsThePlainName()
    {
        Assert.True(ReviewFlags.IsSame("Golden Fries", "golden fries"));
        Assert.False(ReviewFlags.IsSame("Golden Spud Sticks", "golden fries"));
    }

    [Fact]
    public void LongFlagsNamesOver30Chars()
    {
        Assert.True(ReviewFlags.IsLong(new string('a', 31)));
        Assert.False(ReviewFlags.IsLong(new string('a', 30)));
    }

    [Fact]
    public void DupFlagsTheSameNameOnAnotherRoute()
    {
        var rows = new[]
        {
            new ReviewRow("000000000001", "fries", true, "golden fries", "Golden Sticks", "d.", "", "", ""),
            new ReviewRow("000000000002", "veggie_box", true, "veggie box", "golden sticks", "d.", "", "", ""),
            new ReviewRow("000000000003", "soda", true, "cup of soda", "Fizzy Cup", "d.", "", "", ""),
            new ReviewRow("000000000004", "soda", false, "cup of soda half", "Fizzy Cup", "d.", "", "", ""),
        };
        var labels = new Dictionary<string, IReadOnlyList<string>> { ["000000000001"] = new[] { "crispy" } };
        var flags = ReviewFlags.Compute(rows, labels);
        Assert.Equal("doneness,dup", flags[0]);
        Assert.Equal("dup", flags[1]);
        Assert.Equal("", flags[2]); // the same name on the same route is not a dup
        Assert.Equal("", flags[3]);
    }

    [Fact]
    public void ComputeListsEveryFlagInOrder()
    {
        var name = "Crispy Fried Potato Sticks With Salt";
        var rows = new[]
        {
            new ReviewRow("000000000001", "fries", true, name.ToLowerInvariant(), name, "d.", "", "", ""),
            new ReviewRow("000000000002", "veggie_box", true, "x", name, "d.", "", "", ""),
        };
        var flags = ReviewFlags.Compute(rows, new Dictionary<string, IReadOnlyList<string>> { ["000000000001"] = new[] { "dark" } });
        Assert.Equal("doneness,same,dup,long", flags[0]);
        Assert.Equal("dup,long", flags[1]);
    }
}
