/// <summary>
/// Covers Vimm vault list parsing: real rows are extracted, the per-row /vault/999999 placeholder is
/// skipped, HTML entities are decoded, and SectionFor maps names to Vimm's section scheme. The fixture
/// mirrors the real markup (note the space in <c>href= "</c> and the empty decoy anchor).
/// </summary>
[TestClass]
public class VimmVaultParserTests
{
    private const string ListFixture = """
        <table><caption></caption>
        <tr><td style="width:auto"><a href="/vault/999999"></a><a href= "/vault/24612">ABC Wipeout 2</a></td>
            <td><img src="/images/flags/usa.png" class="flag" title="USA"></td></tr>
        <tr><td><a href="/vault/999999"></a><a href= "/vault/24617">Adventure Time: Finn &amp; Jake Investigations</a></td></tr>
        <tr><td><a href="/vault/999999"></a><a href= "/vault/24616">Adventure Time: Don&#039;t Know!</a></td></tr>
        </table>
        """;

    [TestMethod]
    public void ParseList_ExtractsRows_SkipsDecoy_DecodesEntities()
    {
        var entries = VimmVaultParser.ParseList(ListFixture);

        Assert.HasCount(3, entries);
        CollectionAssert.AreEqual(new long[] { 24612, 24617, 24616 }, entries.Select(e => e.VaultId).ToArray());
        Assert.AreEqual("ABC Wipeout 2", entries[0].Title);
        Assert.AreEqual("Adventure Time: Finn & Jake Investigations", entries[1].Title);   // &amp; decoded
        Assert.AreEqual("Adventure Time: Don't Know!", entries[2].Title);                   // &#039; decoded
        Assert.IsFalse(entries.Any(e => e.VaultId == 999999));
    }

    [TestMethod]
    public void ParseList_EmptyOrUnrelatedHtml_ReturnsEmpty()
    {
        Assert.IsEmpty(VimmVaultParser.ParseList(""));
        Assert.IsEmpty(VimmVaultParser.ParseList("<html><body>no games here</body></html>"));
    }

    /// <summary>
    /// The body Vimm actually serves for a section with no games — an HTTP 404 page whose main content
    /// is "No matches found.", with no table wrapper anywhere (verified live: Wii U section Q).
    /// </summary>
    private const string EmptySectionFixture = """
        <main class="mainContent" style="order:1">
        <div style="font-size:28pt; text-align:center">Q</div><p style="text-align:center">No matches found.</p>
        </main>
        """;

    /// <summary>
    /// #297: a 200 that IS a real, result-bearing list page must be distinguishable from a 200 OK
    /// rate-limit/WAF body. The marker is the table+caption wrapper Vimm emits around the game rows.
    /// An empty section is <i>not</i> a row-less 200 — it is a 404, covered by
    /// <see cref="IsEmptySectionPage_TrueOnlyForVimmsNoMatchesPage"/>.
    /// </summary>
    [TestMethod]
    public void IsListPage_TrueForTableCaptionWrapper()
    {
        Assert.IsTrue(VimmVaultParser.IsListPage(ListFixture));                          // real rows
        Assert.IsFalse(VimmVaultParser.IsListPage(""));
        Assert.IsFalse(VimmVaultParser.IsListPage("Too many requests, please slow down."));
        Assert.IsFalse(VimmVaultParser.IsListPage("<html><body>no games here</body></html>"));
        Assert.IsFalse(VimmVaultParser.IsListPage(EmptySectionFixture));   // the real empty-section body
    }

    /// <summary>
    /// #297: Vimm serves a section with no games as an HTTP <b>404</b> carrying a "No matches found."
    /// page — no table wrapper at all. Recognising that body is what lets the seed count such a section
    /// as legitimately empty instead of as a failed fetch; a 404 with any other body stays a failure.
    /// </summary>
    [TestMethod]
    public void IsEmptySectionPage_TrueOnlyForVimmsNoMatchesPage()
    {
        Assert.IsTrue(VimmVaultParser.IsEmptySectionPage(EmptySectionFixture));
        Assert.IsFalse(VimmVaultParser.IsEmptySectionPage(""));                    // bare 404 body
        Assert.IsFalse(VimmVaultParser.IsEmptySectionPage(ListFixture));           // a populated section
        Assert.IsFalse(VimmVaultParser.IsEmptySectionPage("Too many requests, please slow down."));
        Assert.IsFalse(VimmVaultParser.IsEmptySectionPage("<html><body>404 Not Found</body></html>"));
    }

    /// <summary>
    /// #297's sibling check for vault/title pages: the inline "media = [...]" declaration is present
    /// whether the array holds hash-bearing entries, holds nothing, or is entirely empty — so it can
    /// distinguish "legitimately publishes no hashes" from "not a vault page at all".
    /// </summary>
    [TestMethod]
    public void IsVaultPage_TrueForMediaDeclaration_RegardlessOfHashes()
    {
        Assert.IsTrue(VimmVaultParser.IsVaultPage("""<script>let media=[{"ID":1}];</script>"""));
        Assert.IsTrue(VimmVaultParser.IsVaultPage("<script>let media=[];</script>"));   // legitimately no hashes
        Assert.IsFalse(VimmVaultParser.IsVaultPage(""));
        Assert.IsFalse(VimmVaultParser.IsVaultPage("Too many requests, please slow down."));
        Assert.IsFalse(VimmVaultParser.IsVaultPage("<html><body>no media here</body></html>"));
    }

    [TestMethod]
    public void SectionFor_MapsAlpha_Digits_Symbols()
    {
        Assert.AreEqual("A", VimmVaultParser.SectionFor("ActRaiser"));
        Assert.AreEqual("Z", VimmVaultParser.SectionFor("zelda"));          // case-insensitive
        Assert.AreEqual("number", VimmVaultParser.SectionFor("007: Nightfire"));
        Assert.AreEqual("number", VimmVaultParser.SectionFor("3D Lemmings"));
        Assert.AreEqual("number", VimmVaultParser.SectionFor(""));
        Assert.AreEqual("M", VimmVaultParser.SectionFor("  Metroid"));      // leading space trimmed
    }
}
