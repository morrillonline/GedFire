using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using GedCore;
using GedCore.Apply;

namespace GedFire.Gen;

// ---------------------------------------------------------------------------
// SiteGenerator — generates the modern card-based HTML for a genealogy site.
// URL generation and source citation logic mirrors original Gedfire VB.
// HTML structure is modernized: table-based layout → semantic dl.facts cards,
// footnote spans → popover-ready sup>a.fn, citation list hidden in DOM
// (no visible sources section — all source detail surfaces via hover popups).
// ---------------------------------------------------------------------------

public sealed class SiteGenerator
{
    readonly GedModel _model;
    readonly string   _templateText;
    readonly FamilyUrlResolver _urls = new();
    readonly MediaOptions _media;
    readonly List<string> _warnings = new();
    // Relative (unescaped) paths confirmed to exist under _media.MediaDir and
    // actually referenced by rendered HTML — staged into <output>/media/ once
    // generation finishes. Absolute-URL payloads never appear here.
    readonly HashSet<string> _stagedRelativePaths = new(StringComparer.Ordinal);
    // URLs of the family pages actually written — the resolver mints URLs for
    // families the generator skips (no children, or no husband), and index
    // links must not point at those.  Populated by GenerateFamilyPages, which
    // runs before the index pages are generated.
    readonly HashSet<string> _writtenUrls = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Non-fatal notices collected while generating — currently just missing
    /// or out-of-bounds media files (a dangling FILE payload skips its
    /// &lt;figure&gt; rather than failing the whole run). Populated by
    /// <see cref="Generate"/>; read after it returns.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public SiteGenerator(GedModel model, string templateText, MediaOptions? media = null)
    {
        _model        = model;
        _templateText = templateText;
        _media        = media ?? MediaOptions.None;
    }

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    public void Generate(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        GenerateFamilyPages(outputDir);
        GenerateIndexPages(outputDir);
        if (_media.MediaDir != null)
            MediaStager.Stage(_media.MediaDir, outputDir, _stagedRelativePaths);
    }

    // -----------------------------------------------------------------------
    // Family pages
    // -----------------------------------------------------------------------

    void GenerateFamilyPages(string dir)
    {
        foreach (var fam in _model.GedcomFamilies)
        {
            if (fam.Children.Count == 0) continue;
            // A family recorded with only a mother is anchored on her.
            if ((fam.Husband ?? fam.Wife) is not { } anchor) continue;

            var page = new WebPage { Url = GetFamilyUrl(fam) };

            ExportFamily(anchor, page, fam);

            string html = FinalizePageHtml(page, fam.Description());
            WriteFile(Path.Combine(dir, GetFamilyUrl(fam)), html);
            _writtenUrls.Add(GetFamilyUrl(fam));
        }
    }

    // A link target is safe only if a page with that URL was written.
    bool TargetExists(string url) => url.Length > 0 && _writtenUrls.Contains(url);

    void ExportFamily(GedIndividual anchor, WebPage page, GedFamily fam)
    {
        // Breadcrumb
        page.Write("<nav class=\"crumb\"><a href=\"index.html\">Index of Names</a>" +
                   " &nbsp;›&nbsp; " + H(anchor.LastName) +
                   " &nbsp;›&nbsp; " + H(anchor.Husbandname()) + "</nav>\r\n");

        // Title; the family's own citations support its links.
        page.Write("<h1 class=\"fam-title\">" + H(fam.Description()));
        ExportSources(fam.Sources, page);
        page.Write("</h1>\r\n");

        // Couple cards
        page.Write("<div class=\"couple\">\r\n");
        if (fam.Husband != null)
            ExportParentCard(fam.Husband, "Husband", page, fam);
        if (fam.Wife != null)
            ExportParentCard(fam.Wife, "Wife", page, fam);
        page.Write("</div>\r\n");

        // Children section
        ExportChildren(fam, page);

        // Family/spouse photos not already shown as a parent's portrait
        RenderGallery(fam, page);
    }

    // -----------------------------------------------------------------------
    // Parent cards (.person)
    // -----------------------------------------------------------------------

    void ExportParentCard(GedIndividual person, string role, WebPage page, GedFamily fam)
    {
        bool isHusband = person == fam.Husband;
        // The couple's own facts and notes are told once, on the first card.
        bool tellsTheCouple = person == (fam.Husband ?? fam.Wife);
        string displayName = isHusband
            ? person.Husbandname()
            : person.Wifename(fam.SpouseOf(person));

        page.Write("<div class=\"person\">\r\n");
        page.Write("<div class=\"role\">" + role + "</div>\r\n");
        page.Write("<h2>" + H(displayName) + "</h2>\r\n");
        RenderPortrait(person, page);
        page.Write("<dl class=\"facts\">\r\n");

        WritePersonFacts(person, page, compact: false);

        if (tellsTheCouple)
            foreach (var fact in FamilyFacts(fam))
                WriteFactRow(fact, page, compact: false);

        // Father / Mother
        if (person.FamChild != null)
        {
            string father = person.FamChild.Husband != null
                ? FullnameHtml(person.FamChild.Husband, page.Url, null) : "";
            string mother = person.FamChild.Wife != null
                ? FullnameHtml(person.FamChild.Wife, page.Url, person.FamChild.Husband) : "";
            if (father.Length > 0) page.Write("<dt>Father</dt><dd>" + father + "</dd>\r\n");
            if (mother.Length > 0) page.Write("<dt>Mother</dt><dd>" + mother + "</dd>\r\n");
        }

        // "Also m." — other marriages (excluding current spouse)
        WriteAlsoMarried(person, fam, page);

        page.Write("</dl>\r\n");
        // The couple's notes follow their facts on the first card.
        if (tellsTheCouple) ExportNarrativeNotes(fam.NarrativeNotes, page);
        ExportPersonNotes(person, page);
        page.Write("</div>\r\n");
    }

    // -----------------------------------------------------------------------
    // Fact rows: every fact a card shows is one dt/dd row in the same form —
    // value and date, its footnotes, then place and detail clauses in a muted
    // span. A parent card reads "at Place"; a compact child card "· Place".
    // -----------------------------------------------------------------------

    // Facts that follow the death in a card, after the Died and Buried rows.
    static readonly HashSet<string> AfterDeathTags = new(StringComparer.Ordinal) { "BURI", "CREM" };

    // Born; the life's facts in date order; Died and Buried; what followed; the will.
    void WritePersonFacts(GedIndividual person, WebPage page, bool compact)
    {
        WriteFactRow(person.Birth, page, compact);
        foreach (var fact in Chronological(LifeFacts(person)))
            WriteFactRow(fact, page, compact);
        WriteDeathRows(person, page, compact);
        foreach (var fact in Chronological(AfterDeathFacts(person)))
            WriteFactRow(fact, page, compact);
        WriteWillRow(person, page, compact);
    }

    static IEnumerable<GedEvent> LifeFacts(GedIndividual person) =>
        person.Census
            .Concat(person.OtherEvents.Where(e => !AfterDeathTags.Contains(e.Tag)))
            .Concat(person.Attributes);

    static IEnumerable<GedEvent> AfterDeathFacts(GedIndividual person) =>
        person.OtherEvents.Where(e => AfterDeathTags.Contains(e.Tag) && e != person.Burial);

    // The couple's facts: marriage, divorce and the rest, in date order.
    static IEnumerable<GedEvent> FamilyFacts(GedFamily fam) =>
        Chronological(new[] { fam.Marriage, fam.Divorce }.OfType<GedEvent>().Concat(fam.OtherEvents));

    // Dated facts by date; undated ones after them in document order.
    static IEnumerable<GedEvent> Chronological(IEnumerable<GedEvent> facts) =>
        facts.OrderBy(f => GedDate.Parse(f.Date)
                           ?? (GedDate.ParseYear(f.Date) is > 0 and < 10000 and var year ? new DateTime(year, 1, 1) : DateTime.MaxValue));

    // A burial stands in for a missing death; beside a death it is shown as "Buried".
    void WriteDeathRows(GedIndividual person, WebPage page, bool compact)
    {
        WriteFactRow(person.DeathOrBurial, page, compact, heading: "Died");
        if (person.Death != null)
            WriteFactRow(person.Burial, page, compact);
    }

    // A will and its probate share one row: the will, then where and when it was proved.
    void WriteWillRow(GedIndividual person, WebPage page, bool compact)
    {
        if (person.Will is not { } will)
        {
            WriteFactRow(person.Probate, page, compact);
            return;
        }
        page.Write("<dt>" + H(FactText.Heading(will)) + "</dt><dd>");
        WriteFactBody(will, page, compact);
        if (person.Probate is { } probate)
        {
            page.Write(", proved " + H(EventString(probate)));
            ExportSources(probate.Sources, page);
            WriteDetails(FactText.Details(probate), page, compact);
        }
        page.Write("</dd>\r\n");
    }

    void WriteFactRow(GedEvent? fact, WebPage page, bool compact, string? heading = null)
    {
        if (fact == null) return;
        page.Write("<dt>" + H(heading ?? FactText.Heading(fact)) + "</dt><dd>");
        WriteFactBody(fact, page, compact);
        page.Write("</dd>\r\n");
    }

    void WriteFactBody(GedEvent fact, WebPage page, bool compact)
    {
        page.Write(H(string.Join(", ", new[] { fact.Value, fact.Date }.Where(s => s.Length > 0))));
        ExportSources(fact.Sources, page);
        var secondary = new List<string>();
        if (fact.Place.Length > 0)
            secondary.Add((compact ? "· " : "at ") + PlaceAbbr(fact.Place));
        secondary.AddRange(FactText.Details(fact));
        WriteDetails(secondary, page, compact);
    }

    static void WriteDetails(IReadOnlyCollection<string> clauses, WebPage page, bool compact)
    {
        if (clauses.Count == 0) return;
        page.Write(" <span class=\"" + (compact ? "sub" : "place") + "\">" + H(string.Join("; ", clauses)) + "</span>");
    }

    // A fact in running prose: "occupation Miller in Salem, Mass., 1850 (details)", footnoted.
    // A word such as "b." or "m." names a vital in place of its label.
    void WriteFactPhrase(GedEvent fact, WebPage page, string? word = null)
    {
        var words = new List<string> { word ?? FactText.Word(fact) };
        if (fact.Value.Length > 0) words.Add(fact.Value);
        if (EventString(fact) is { Length: > 0 } when) words.Add(when);
        page.Write(H(string.Join(" ", words)));
        var details = FactText.Details(fact);
        if (details.Count > 0) page.Write(" (" + H(string.Join("; ", details)) + ")");
        ExportSources(fact.Sources, page);
    }

    // "Also m." list for parent cards — other marriages excluding the current spouse.
    void WriteAlsoMarried(GedIndividual person, GedFamily currentFam, WebPage page)
    {
        var currentSpouse = currentFam.SpouseOf(person);
        var others = person.FamSpouse.Where(f => f.SpouseOf(person) != currentSpouse).ToList();
        if (others.Count == 0) return;

        page.Write("<dt>Also m.</dt><dd>\r\n");
        int ord = 2;
        foreach (var f in others)
        {
            var spouse = f.SpouseOf(person);
            if (spouse == null) { ord++; continue; }
            // A marriage with children has a page of its own; a childless one is told in full here,
            // with the spouse's own facts when they appear nowhere else.
            if (f.Children.Count > 0)
                WriteMarriageShort(person, spouse, f, ord, showOrd: true, page);
            else
                WriteMarriageFull(person, spouse, f, ord, showOrd: true, page,
                    ToldOnlyInline(spouse) ? SpouseDetail.All : SpouseDetail.None);
            ord++;
        }
        page.Write("</dd>\r\n");
    }

    // -----------------------------------------------------------------------
    // Name-source annotations rendered as body text inside a person's own card
    // (near them). A "note" — the personal-note prose (@S00257@) or a NOTE-derived
    // source — becomes a paragraph; a real source reference becomes a footnoted
    // line. Used for parents and for childless children (who have no own page),
    // so the prose always sits beside the person it describes, never in a shared
    // blob whose subject is ambiguous on a page full of people.
    // -----------------------------------------------------------------------

    void ExportPersonNotes(GedIndividual indi, WebPage page)
    {
        ExportNarrativeNotes(indi.NarrativeNotes, page);

        foreach (var sref in indi.NameSources)
            if (sref.IsNote && sref.DataText.Trim().Length > 0)
            {
                page.Write("<p class=\"bio-inline\">");
                SourcePhrase(sref, page, allowInline: true);
                page.Write("</p>\r\n");
            }

        var refs = indi.NameSources.Where(s => !s.IsNote).ToList();
        if (refs.Count > 0)
        {
            page.Write("<p class=\"bio-inline note-src\">Name source:");
            foreach (var sref in refs) SourcePhrase(sref, page, allowInline: false);
            page.Write("</p>\r\n");
        }
    }

    // A person's or a family's notes, each a paragraph with its citations footnoted.
    // A person's or a family's notes. Every paragraph is a p.bio-inline, plain or HTML alike: an uncited
    // note opens with "NOTE: ", and a cited note's footnotes close its last paragraph.
    void ExportNarrativeNotes(IEnumerable<GedNarrativeNote> notes, WebPage page)
    {
        foreach (var note in notes)
        {
            bool isHtml = string.Equals(note.Mime, "text/html", StringComparison.OrdinalIgnoreCase);
            var paragraphs = isHtml ? NarrativeHtmlParagraphs(note.Text) : [HtmlLineBreaks(H(note.Text))];
            if (paragraphs.Count == 0) paragraphs = [""];
            for (int i = 0; i < paragraphs.Count; i++)
            {
                page.Write("<p class=\"bio-inline\">");
                if (i == 0 && note.Sources.Count == 0) page.Write("NOTE: ");
                page.Write(paragraphs[i]);
                if (i == paragraphs.Count - 1)
                    foreach (var source in note.Sources)
                        SourcePhrase(source, page, allowInline: false);
                page.Write("</p>\r\n");
            }
        }
    }

    // A marriage told in prose after the spouse's name: " — m. in Place, date; divorce …", the couple's facts in date order.
    void WriteMarriagePhrases(GedFamily fam, WebPage page)
    {
        string separator = " — ";
        foreach (var fact in FamilyFacts(fam))
        {
            page.Write(separator);
            WriteFactPhrase(fact, page, fact == fam.Marriage ? "m." : null);
            separator = "; ";
        }
    }

    // -----------------------------------------------------------------------
    // Media (Subproject J): parent-card portraits + a family/spouse gallery.
    // The generator never touches CROP visually in v1 — a resolved image
    // carries a data-crop attribute so the theme can adopt cropping later.
    // -----------------------------------------------------------------------

    // "Preferred first" rule: a person's first OBJE link is their portrait.
    // It is consumed by position whether or not it actually renders (a
    // missing file still occupies the portrait slot rather than falling
    // through to the gallery as a second attempt).
    void RenderPortrait(GedIndividual person, WebPage page)
    {
        var link = person.Media.FirstOrDefault();
        if (link == null) return;
        string html = RenderMediaLink(link, asPortrait: true);
        if (html.Length > 0) page.Write(html);
    }

    // Everything else: the couple's remaining photos plus any family-level
    // (marriage) media. Rendered once per family page, right before the
    // hidden source list that WebPage.FinalizeBody appends. Deduplicated by
    // target media object — a couple photo is typically linked from BOTH
    // spouses (and sometimes the FAM as well), and must render once, not
    // once per link; the portraits' objects are excluded too, since they
    // already rendered in the parent cards.
    void RenderGallery(GedFamily fam, WebPage page)
    {
        var seen = new HashSet<GedMediaObject>();
        if (fam.Husband?.Media.FirstOrDefault() is { } hPortrait) seen.Add(hPortrait.Target);
        if (fam.Wife?.Media.FirstOrDefault() is { } wPortrait) seen.Add(wPortrait.Target);

        var links = new List<GedMediaLink>();
        void Collect(IEnumerable<GedMediaLink> candidates)
        {
            foreach (var link in candidates)
                if (seen.Add(link.Target))
                    links.Add(link);
        }
        if (fam.Husband != null) Collect(fam.Husband.Media.Skip(1));
        if (fam.Wife    != null) Collect(fam.Wife.Media.Skip(1));
        Collect(fam.Media);
        if (links.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var link in links)
            sb.Append(RenderMediaLink(link, asPortrait: false));
        if (sb.Length == 0) return;

        page.Write("<section class=\"gallery\">\r\n");
        page.Write(sb.ToString());
        page.Write("</section>\r\n");
    }

    // Renders one media link as <figure><img>/<a></figure>, or "" when the
    // underlying file can't be resolved (ResolveMediaSrc already recorded a
    // warning). Only a file whose media type starts "image/" renders as an
    // <img>; anything else (a scanned will as a PDF, say) renders as a plain
    // link with the display title as its text.
    string RenderMediaLink(GedMediaLink link, bool asPortrait)
    {
        var file = link.Target.Files.FirstOrDefault(
                       f => f.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                   ?? link.Target.Files.FirstOrDefault();
        if (file == null) return "";

        string? src = ResolveMediaSrc(file.Path, link.Target.Xref);
        if (src == null) return "";

        bool isImage = file.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        string? displayTitle = link.DisplayTitle;
        string title = displayTitle ?? "Photograph";

        var sb = new StringBuilder();
        sb.Append("<figure class=\"").Append(asPortrait ? "portrait" : "media").Append("\">");
        if (isImage)
        {
            sb.Append("<img src=\"").Append(Attr(src)).Append("\" alt=\"").Append(Attr(title)).Append('"');
            if (link.Crop != null)
                sb.Append(" data-crop=\"").Append(CropAttr(link.Crop)).Append('"');
            sb.Append('>');
        }
        else
        {
            sb.Append("<a href=\"").Append(Attr(src)).Append("\">").Append(H(title)).Append("</a>");
        }
        if (displayTitle != null)
            sb.Append("<figcaption>").Append(H(title)).Append("</figcaption>");
        sb.Append("</figure>\r\n");
        return sb.ToString();
    }

    // Resolves a FILE payload to the URL used in HTML, checking the file's
    // real existence against --media-dir along the way (a relative path with
    // no --media-dir configured is treated as unresolvable, not assumed
    // present). Absolute URLs pass straight through — never staged, never
    // checked. A path that would resolve outside --media-dir (path
    // traversal) is rejected the same as a missing one.
    string? ResolveMediaSrc(string filePath, string objeXref)
    {
        if (MediaPaths.IsAbsoluteUrl(filePath)) return filePath;

        if (_media.MediaDir == null)
        {
            _warnings.Add($"no --media-dir given; skipping media for {objeXref}: {filePath}");
            return null;
        }

        string relative = MediaPaths.UnescapeFilePath(filePath);
        string mediaRoot = Path.GetFullPath(_media.MediaDir);
        string full = Path.GetFullPath(Path.Combine(mediaRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (full != mediaRoot && !full.StartsWith(mediaRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            _warnings.Add($"media path escapes --media-dir, skipped for {objeXref}: {filePath}");
            return null;
        }
        if (!File.Exists(full))
        {
            _warnings.Add($"media file missing for {objeXref}: {relative}");
            return null;
        }

        _stagedRelativePaths.Add(relative);
        return _media.MediaBaseUrl + filePath;
    }

    static string CropAttr(GedCrop crop) =>
        string.Join(",", new[] { crop.Top, crop.Left, crop.Height, crop.Width }
            .Select(v => v?.ToString() ?? ""));

    // -----------------------------------------------------------------------
    // Children section
    // -----------------------------------------------------------------------

    void ExportChildren(GedFamily fam, WebPage page)
    {
        if (fam.Children.Count == 0) return;
        page.Write("<section class=\"fam-children\">\r\n");
        page.Write("<h3 class=\"sec\">Children</h3>\r\n");
        page.Write("<div class=\"children\">\r\n");
        foreach (var child in fam.Children)
            ExportChildCard(child, page, fam);
        page.Write("</div>\r\n");
        page.Write("</section>\r\n");
    }

    void ExportChildCard(GedIndividual child, WebPage page, GedFamily parentFam)
    {
        string url = GetIndividualUrl(child);
        bool hasOwnPage = url.Length > 0 && url != page.Url;

        // Year span
        int byr = GedDate.ParseYear(child.Birth?.Date);
        int dyr = GedDate.ParseYear(child.DeathOrBurial?.Date);
        string yr = "";
        if (byr > 0) yr = byr.ToString();
        if (dyr > 0) yr += (yr.Length > 0 ? " – " : "– ") + dyr.ToString();

        page.Write("<div class=\"child\">\r\n");
        page.Write("<div class=\"hd\">");
        page.Write("<span class=\"nm\">");
        // Use maiden name for female children (Wifename() with a null husband just returns
        // the birth-surname form without any married-name chain)
        string childName = child.IsMale
            ? child.Husbandname()
            : (child.Title.Length > 0
                ? child.Title + " " + child.FirstMiddle() + " " + child.LastName
                : child.FirstMiddle() + " " + child.LastName);
        if (hasOwnPage)
            page.Write("<a href=\"" + Attr(url) + "\">" + H(childName) + "</a>");
        else
            page.Write(H(childName));
        page.Write("</span>");
        if (yr.Length > 0)
            page.Write("<span class=\"yr\">" + yr + "</span>");
        page.Write("</div>\r\n");

        page.Write("<dl class=\"facts\">\r\n");

        // A child with a page of their own is summarized here; a childless
        // child's card is their only record, so it carries every fact.
        if (hasOwnPage)
        {
            WriteFactRow(child.Birth, page, compact: true);
            WriteDeathRows(child, page, compact: true);
        }
        else
            WritePersonFacts(child, page, compact: true);

        // Married
        WriteChildMarriages(child, page, hasOwnPage);
        WriteNeverMarried(child, page);

        page.Write("</dl>\r\n");

        // Name-source notes for childless children (they have no own page)
        if (!hasOwnPage)
            ExportPersonNotes(child, page);

        page.Write("</div>\r\n");
    }

    // A recorded never-married answer fills the Married row of a child no marriage is recorded for.
    void WriteNeverMarried(GedIndividual child, WebPage page)
    {
        if (child.FamSpouse.Count > 0 || child.NeverMarried is not { } answer) return;
        page.Write("<dt>Married</dt><dd>Never married");
        ExportSources(answer.Sources, page);
        page.Write("</dd>\r\n");
    }

    // Married section for a child card.
    // Children who have their own family page get a "family page →" link per
    // child-producing marriage.  Children without their own page show full
    // spouse detail inline (per the completeness rule).
    void WriteChildMarriages(GedIndividual child, WebPage page, bool hasOwnPage)
    {
        if (child.FamSpouse.Count == 0) return;

        page.Write("<dt>Married</dt><dd>\r\n");

        bool multipleMarriages = child.FamSpouse.Count > 1;
        int ord = 1;

        foreach (var f in child.FamSpouse)
        {
            var spouse = f.SpouseOf(child);
            if (spouse == null) { continue; }

            bool spouseIsChildless = spouse.IsChildless() && spouse.FamChild == null;

            // Full inline detail when:
            //   (a) the child has no own page — this card is their only record, so
            //       every marriage must be shown in full (completeness rule), or
            //   (b) the spouse is completely childless with no parents — they appear
            //       nowhere else on the site, so their detail must live here.
            bool useFull = !hasOwnPage || spouseIsChildless;
            if (!useFull)
                WriteMarriageShort(child, spouse, f, ord, multipleMarriages, page);
            else
                WriteMarriageFull(child, spouse, f, ord, multipleMarriages, page,
                    spouseIsChildless ? SpouseDetail.All : SpouseDetail.Vitals);

            ord++;
        }

        page.Write("</dd>\r\n");
    }

    void WriteMarriageShort(GedIndividual child, GedIndividual spouse,
                                  GedFamily f, int ord, bool showOrd, WebPage page)
    {
        page.Write("<p class=\"marr\">");
        if (showOrd) page.Write("<span class=\"ord\">(" + ord + ")</span> ");

        string spouseHtml = SpouseNameHtml(child, spouse, page);

        // If this marriage produced children, show a "family page →" link
        bool hasFamPage = f.Children.Count > 0;
        string famUrl   = hasFamPage ? GetFamilyUrl(f) : "";
        bool   selfLink = famUrl == page.Url;

        page.Write(spouseHtml);

        if (hasFamPage && !selfLink)
            page.Write(" <a class=\"tofam\" href=\"" + Attr(famUrl) + "\">family page →</a>");

        page.Write("</p>\r\n");
    }

    // How a spouse's facts are told after their name in a marriage line.
    enum SpouseDetail { None, Vitals, All }

    // A spouse is told only in their partner's marriage line when they have no parents and no children on the site.
    static bool ToldOnlyInline(GedIndividual spouse) => spouse.IsChildless() && spouse.FamChild == null;

    // A wife is named with her earlier married names; a husband by his own.
    string SpouseNameHtml(GedIndividual person, GedIndividual spouse, WebPage page) =>
        spouse.IsMale ? FullnameHtml(spouse, page.Url, null) : FullnameHtml(spouse, page.Url, person);

    void WriteMarriageFull(GedIndividual child, GedIndividual spouse,
                                 GedFamily f, int ord, bool showOrd, WebPage page, SpouseDetail detail)
    {
        page.Write("<p class=\"marr\">");
        if (showOrd) page.Write("<span class=\"ord\">(" + ord + ")</span> ");

        page.Write(SpouseNameHtml(child, spouse, page));

        // A childless couple has no page whose title carries the family's own citations.
        if (f.Children.Count == 0) ExportSources(f.Sources, page);
        WriteMarriagePhrases(f, page);

        if (detail != SpouseDetail.None)
            WriteSpouseDetails(child, spouse, detail, page);
        page.Write("</p>\r\n");

        // The couple's notes, the notes of a spouse told only here, and of that spouse's other childless marriages.
        ExportNarrativeNotes(f.NarrativeNotes, page);
        if (detail != SpouseDetail.None && ToldOnlyInline(spouse))
        {
            ExportPersonNotes(spouse, page);
            foreach (var other in spouse.FamSpouse.Where(o => o != f && o.Children.Count == 0))
                ExportNarrativeNotes(other.NarrativeNotes, page);
        }
    }

    // The spouse's facts in a muted line: "Silas b. 1830; occupation Peddler; d. 1890; m. (2) …". The line is
    // opened by its first clause, so a spouse with nothing to tell gets none.
    void WriteSpouseDetails(GedIndividual partner, GedIndividual spouse, SpouseDetail detail, WebPage page)
    {
        string first = H(spouse.FirstName.Length > 0 ? spouse.FirstName : spouse.Fullname);

        // Each clause after the first is set off by a semicolon; the first names the spouse.
        bool wrote = false;
        void Clause()
        {
            page.Write(wrote ? "; " : "<br><span class=\"sub\">" + first + " ");
            wrote = true;
        }
        void FactClause(GedEvent? fact, string? word = null)
        {
            if (fact == null) return;
            Clause();
            WriteFactPhrase(fact, page, word);
        }

        FactClause(spouse.Birth, "b.");
        if (detail == SpouseDetail.All)
            foreach (var fact in Chronological(LifeFacts(spouse))) FactClause(fact);
        FactClause(spouse.DeathOrBurial, "d.");
        if (spouse.Death != null) FactClause(spouse.Burial, "bur.");
        if (detail == SpouseDetail.All)
            foreach (var fact in Chronological(AfterDeathFacts(spouse))) FactClause(fact);
        if (spouse.Will != null)
        {
            FactClause(spouse.Will, "leaving a will dated");
            if (spouse.Probate != null)
            {
                page.Write(", ");
                WriteFactPhrase(spouse.Probate, page, "proved");
            }
        }
        else FactClause(spouse.Probate, "leaving a will proved");

        // The spouse's other marriages.
        for (int x = 0; x < spouse.FamSpouse.Count; x++)
        {
            var other = spouse.FamSpouse[x];
            var otherSpouse = other.SpouseOf(spouse);
            if (otherSpouse == null || otherSpouse == partner) continue;
            Clause();
            page.Write("m. (" + (x + 1) + ") " + H(otherSpouse.IsMale ? otherSpouse.Husbandname() : otherSpouse.Wifename(spouse)));
            if (otherSpouse.FamChild != null)
                page.Write((otherSpouse.IsMale ? ", the son of " : ", the daughter of ") + H(otherSpouse.FamChild.FamilyPhrase()));
            if (other.Children.Count == 0) ExportSources(other.Sources, page);
            WriteMarriagePhrases(other, page);
        }
        if (wrote) page.Write(".</span>");
    }

    // -----------------------------------------------------------------------
    // Source footnote generation.
    // Every citation always shows the full reference — no ibid., no short form.
    // The footnote list is hidden in the DOM; popup JS reads it from there.
    // -----------------------------------------------------------------------

    void SourcePhrase(GedSourceRef sref, WebPage page, bool allowInline)
    {
        // DataText (3 DATA/4 TEXT) is inline annotation — stays as body.
        // Note holds the global source's NOTE field; when non-empty it is a
        // pre-formatted bibliographic citation from FTM that is cleaner than
        // rebuilding from PUBL (which has "Name: " prefix / trailing ";" FTM
        // artifacts). Promote it to citation instead of double-emitting.
        string body = sref.DataText.Trim();

        string citation = "";
        if (!sref.NoCitation)
        {
            if (sref.Note.Length > 0)
            {
                // Use the pre-formatted NOTE as the citation base.
                string note = sref.Note;
                // Strip FTM-appended ", Source Medium: X" (not a citation field).
                int medIdx = note.IndexOf(", Source Medium:", StringComparison.OrdinalIgnoreCase);
                if (medIdx > 0) note = note[..medIdx];
                // Clean up trailing "." and the CONT "." artifact ("\n.") from GEDCOM.
                note = note.Trim().TrimEnd('.').Trim();
                // A note a changeset composes from the title alone does not name the author.
                if (sref.Author.Length > 0 && sref.Title.Length > 0
                    && note.StartsWith(sref.Title, StringComparison.Ordinal)
                    && !note.Contains(sref.Author, StringComparison.OrdinalIgnoreCase))
                    note = sref.Author + ", " + note;

                string pg = sref.Page;
                if (pg.StartsWith("page ", StringComparison.OrdinalIgnoreCase)) pg = pg[5..];
                if (pg.StartsWith("p. ",   StringComparison.OrdinalIgnoreCase)) pg = pg[3..];
                citation = pg.Length > 0 ? note + ", " + pg + "." : note + ".";
            }
            else
            {
                // No pre-formatted note — build from structured GEDCOM fields.
                if (sref.Author.Length > 0)      citation += sref.Author + ", ";
                if (sref.Title.Length > 0)       citation += sref.Title + " ";
                if (sref.Publication.Length > 0) citation += "(" + sref.Publication + ")";

                string pg = sref.Page;
                if (pg.StartsWith("page ", StringComparison.OrdinalIgnoreCase)) pg = pg[5..];
                if (pg.StartsWith("p. ",   StringComparison.OrdinalIgnoreCase)) pg = pg[3..];
                if (pg.Length > 0) citation += ", " + pg;

                citation = citation.Trim();
                if (citation.Length > 0 && !citation.EndsWith('.'))
                    citation += ".";
            }
        }
        // NoCitation suppresses the source's bibliographic text entirely —
        // the footnote carries only the citation's own DATA/TEXT annotation.
        // (Sole user in the data: the "Personal note" pseudo-source @S00257@,
        // whose NOTE is FTM boilerplate, not display content.)

        // Everything a citation says comes from the file, so all of it is escaped: none of it is markup.
        if (allowInline && sref.IsNote)
        {
            if (body.Length > 0) page.Write(HtmlLineBreaks(H(body)) + " ");
        }
        else if (body.Length > 0)
        {
            if (citation.Length > 0) citation += " ";
            citation += body;
        }

        // The author's commentary follows the source and its extract.
        string commentary = sref.Commentary.Trim();
        if (commentary.Length > 0)
        {
            // The verbatim extract is left as written, so a sentence break is supplied before the commentary.
            if (citation.Length > 0) citation += citation[^1] is '.' or '!' or '?' or '"' or '”' or ')' ? " " : ". ";
            citation += commentary;
        }

        if (citation.Length > 0)
            page.AddFootnote(H(citation));
    }

    void ExportSources(List<GedSourceRef> sources, WebPage page)
    {
        foreach (var sref in sources)
            SourcePhrase(sref, page, allowInline: false);
    }

            static string HtmlLineBreaks(string text) => text.Replace("\r\n", "\n").Replace("\n", "<br>\r\n");

    // -----------------------------------------------------------------------
    // Index pages
    // -----------------------------------------------------------------------

    void GenerateIndexPages(string dir)
    {
        var inds = _model.SortedIndividuals;
        var files = new List<string>();
        var titles = new Dictionary<string, string>();
        int pageNum = 0;
        const int pageSize = 200;

        for (int start = 0; start < inds.Count; start += pageSize)
        {
            int end   = Math.Min(start + pageSize - 1, inds.Count - 1);
            string fname = "index" + pageNum + ".html";
            string title = IndexPageTitle(inds, start, end);
            string body  = IndexHtml(inds, start, end, pageNum,
                                     (int)Math.Ceiling((double)inds.Count / pageSize));

            var page = new WebPage();
            page.Write(body);
            string html = FinalizePageHtml(page, "Index: " + title);
            WriteFile(Path.Combine(dir, fname), html);

            files.Add(fname);
            titles[fname] = title;
            pageNum++;
        }

        GenerateIndexOfIndexes(dir, files, titles);
    }

    static string IndexPageTitle(List<GedIndividual> sorted, int from, int to)
    {
        var first = sorted[from];
        var last  = sorted[to];
        string r = first.LastName + ",";
        if (first.FirstMiddle().Length > 0) r += first.FirstMiddle()[0];
        r += " - " + last.LastName + ",";
        if (last.FirstMiddle().Length > 0) r += last.FirstMiddle()[0];
        return r;
    }

    string IndexHtml(List<GedIndividual> sorted, int from, int to,
                     int pageNum, int totalPages)
    {
        var sb = new StringBuilder();

        sb.Append("<h1 class=\"idx-title\">Index of Names</h1>\r\n");
        sb.Append("<p class=\"count\"><b>" + (to - from + 1) + "</b> names on this page &nbsp;·&nbsp; ");
        sb.Append("<b>" + _model.Individuals.Count + "</b> individuals total</p>\r\n");

        AppendPager(sb, pageNum, totalPages);

        sb.Append("<div class=\"tbl\">\r\n");
        sb.Append("<table class=\"index\">\r\n");
        sb.Append("<thead><tr>");
        sb.Append("<th>Name</th><th>Born</th><th>Died</th><th>Spouse(s)</th><th>Place(s)</th>");
        sb.Append("</tr></thead>\r\n");
        sb.Append("<tbody>\r\n");

        for (int i = from; i <= to; i++)
        {
            string row = HtmlIndexRow(sorted[i]);
            if (row.Length > 0) sb.Append(row);
        }

        sb.Append("</tbody></table></div>\r\n");

        AppendPager(sb, pageNum, totalPages);

        return sb.ToString();
    }

    static void AppendPager(StringBuilder sb, int pageNum, int totalPages)
    {
        if (totalPages <= 1) return;
        sb.Append("<div class=\"pager\">");
        if (pageNum > 0)
            sb.Append("<a href=\"index" + (pageNum - 1) + ".html\">← Previous</a>");
        else
            sb.Append("<span></span>");
        sb.Append("<span class=\"where\">Page " + (pageNum + 1) + " of " + totalPages + "</span>");
        if (pageNum < totalPages - 1)
            sb.Append("<a href=\"index" + (pageNum + 1) + ".html\">Next →</a>");
        else
            sb.Append("<span></span>");
        sb.Append("</div>\r\n");
    }

    string HtmlIndexRow(GedIndividual ind)
    {
        string u = GetIndividualUrl(ind);
        if (u.Length == 0) return "";

        string birth  = ind.Birth?.Date  != null ? GedDate.ParseYear(ind.Birth.Date).ToString()  : "";
        string death  = GedDate.ParseYear(ind.DeathOrBurial?.Date).ToString();
        if (birth == "0") birth = "";
        if (death == "0") death = "";
        string spouses = SpouseShortString(ind);
        string places  = GetShortPlaces(ind);

        string nameCell;
        // Check for multiple child-producing marriages (multiple family pages)
        var childFams = ind.FamSpouse.Where(f => f.Children.Count > 0).ToList();
        if (childFams.Count > 1)
        {
            // Name is not a single link; each spouse links to that marriage's page
            nameCell = "<span class=\"nm\">" + H(ind.FirstMiddle() + " " + ind.LastName) +
                       "</span>";
        }
        else if (TargetExists(u))
        {
            nameCell = "<a class=\"nm\" href=\"" + Attr(u) + "\">" +
                       H(ind.FirstMiddle() + " " + ind.LastName) + "</a>";
        }
        else
        {
            // The resolver minted a URL, but no page was written for it
            // (a childless couple's minted name) — show the person unlinked rather than link a 404.
            nameCell = "<span class=\"nm\">" + H(ind.FirstMiddle() + " " + ind.LastName) +
                       "</span>";
        }

        string spouseCell = childFams.Count > 1
            ? LinkedSpouseCellHtml(ind)
            : H(spouses);

        var sb = new StringBuilder();
        sb.Append("<tr>");
        sb.Append("<td data-label=\"Name\">" + nameCell + "</td>");
        sb.Append("<td class=\"num\" data-label=\"Born\">" + H(birth) + "</td>");
        sb.Append("<td class=\"num\" data-label=\"Died\">" + H(death) + "</td>");
        sb.Append("<td data-label=\"Spouse(s)\" class=\"sub\">" + spouseCell + "</td>");
        sb.Append("<td data-label=\"Place(s)\" class=\"sub\">" + H(places) + "</td>");
        sb.Append("</tr>\r\n");
        return sb.ToString();
    }

    // Spouse cell for a person with several child-producing marriages: their
    // name has no single page to link to, so each spouse links to that
    // marriage's family page instead.
    string LinkedSpouseCellHtml(GedIndividual indi)
    {
        var parts = new List<string>();
        foreach (var f in indi.FamSpouse)
        {
            var spouse = f.SpouseOf(indi);
            if (spouse == null || spouse.FirstName.Length == 0) continue;
            string famUrl = f.Children.Count > 0 ? GetFamilyUrl(f) : "";
            parts.Add(TargetExists(famUrl)
                ? "<a href=\"" + Attr(famUrl) + "\">" + H(spouse.FirstName) + "</a>"
                : H(spouse.FirstName));
        }
        return string.Join(", ", parts);
    }

    string SpouseShortString(GedIndividual indi)
    {
        var parts = new List<string>();
        foreach (var f in indi.FamSpouse)
        {
            var spouse = f.SpouseOf(indi);
            if (spouse?.FirstName.Length > 0)
                parts.Add(spouse.FirstName);
        }
        if (parts.Count > 0) return string.Join(", ", parts);
        int years = YearsOld(indi);
        if (years >= 0 && years <= 16) return "d.y.";
        return "";
    }

    static int YearsOld(GedIndividual indi)
    {
        var d1 = GedDate.Parse(indi.Birth?.Date);
        var d2 = GedDate.Parse(indi.DeathOrBurial?.Date);
        if (d1 == null || d2 == null) return -1;
        return (int)((d2.Value - d1.Value).TotalDays / 365.0);
    }

    static string GetShortPlaces(GedIndividual indi)
    {
        var seen = new List<string>();
        void add(string p)
        {
            if (p.Length == 0) return;
            int c = p.LastIndexOf(',');
            string s = c >= 0 ? p[(c + 1)..].Trim() : p;
            if (!seen.Contains(s)) seen.Add(s);
        }
        add(indi.Birth?.Place ?? "");
        foreach (var f in indi.FamSpouse) add(f.Marriage?.Place ?? "");
        add(indi.DeathOrBurial?.Place ?? "");
        return string.Join(", ", seen);
    }

    void GenerateIndexOfIndexes(string dir, List<string> files,
                                  Dictionary<string, string> titles)
    {
        var sb = new StringBuilder();
        sb.Append("<h1 class=\"idx-title\">Index of Names</h1>\r\n");
        sb.Append("<p class=\"count\"><b>" + _model.Individuals.Count +
                  "</b> individuals &nbsp;·&nbsp; <b>" + _model.Families.Count +
                  "</b> families</p>\r\n");
        sb.Append("<ul class=\"idx-list\">\r\n");
        foreach (var f in files)
            sb.Append("  <li><a href=\"" + f + "\">" + H(titles[f]) + "</a></li>\r\n");
        sb.Append("</ul>\r\n");

        var page = new WebPage();
        page.Write(sb.ToString());
        WriteFile(Path.Combine(dir, "index.html"),
            FinalizePageHtml(page, "Index of Names"));
    }

    // -----------------------------------------------------------------------
    // URL / page-name generation (mirrors VB Family.GetURL + MakePageName)
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // URL resolution — policy lives in FamilyUrlResolver; these delegates keep
    // the public API stable for callers and tests.
    // -----------------------------------------------------------------------

    public string GetIndividualUrl(GedIndividual indi) => _urls.IndividualUrl(indi);

    public string GetFamilyUrl(GedFamily fam) => _urls.FamilyUrl(fam);

    // -----------------------------------------------------------------------
    // FullnameHtml — mirrors VB ExportEngineHTML.FullnameHTML
    // -----------------------------------------------------------------------

    string FullnameHtml(GedIndividual person, string currentUrl, GedIndividual? wifeOf)
    {
        string fullname = wifeOf != null
            ? person.Wifename(wifeOf)
            : (person.Title.Length > 0
                ? person.Title + " " + person.FirstMiddle() + " " + person.LastName
                : person.FirstMiddle() + " " + person.LastName);

        string pUrl = GetIndividualUrl(person);
        if (pUrl.Length == 0 || pUrl == currentUrl) return H(fullname);
        return "<a href=\"" + Attr(pUrl) + "\">" + H(fullname) + "</a>";
    }

    // -----------------------------------------------------------------------
    // Event prose helpers
    // -----------------------------------------------------------------------

    static string EventString(GedEvent? e)
    {
        if (e == null) return "";
        string txt = e.Date;
        txt = txt.Replace("ABT.", "ca.").Replace("ABT", "ca.");
        foreach (var (from, to) in MonthMap)
            txt = txt.Replace(from, to);
        if (e.Place.Length > 0) txt = "in " + PlaceAbbr(e.Place) + (txt.Length > 0 ? ", " + txt : "");
        return txt;
    }

    static string PlaceAbbr(string place)
    {
        foreach (var (full, abbr) in StateAbbr)
            place = place.Replace(full, abbr);
        return place;
    }

    // Minimal HTML encoding for text content (not attributes)
    static string H(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Inline formatting a note may keep; a nested block element is unwrapped to its content.
    static readonly HashSet<string> PortableNoteTags = new(StringComparer.Ordinal)
    {
        "br", "b", "i", "u", "s", "sup", "sub",
    };

    // The safe inner HTML of each of a note's paragraphs: a top-level p or div is a paragraph of its own,
    // and the inline content between them forms another.
    static List<string> NarrativeHtmlParagraphs(string html)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.ToString().Trim().Length > 0) paragraphs.Add(current.ToString().Trim());
            current.Clear();
        }

        var body = new HtmlParser().ParseDocument(html).Body;
        foreach (var node in body?.ChildNodes ?? Enumerable.Empty<INode>())
        {
            if (node is IElement { LocalName: "p" or "div" } block)
            {
                Flush();
                foreach (var child in block.ChildNodes)
                    AppendSafeHtml(child, current);
                Flush();
            }
            else
                AppendSafeHtml(node, current);
        }
        Flush();
        return paragraphs;
    }

    static void AppendSafeHtml(INode node, StringBuilder output)
    {
        if (node is IText text)
        {
            output.Append(H(text.Data));
            return;
        }

        if (node is not IElement element) return;
        string tag = element.LocalName.ToLowerInvariant();
        if (tag is "script" or "style") return;

        if (PortableNoteTags.Contains(tag))
        {
            output.Append('<').Append(tag).Append('>');
            if (tag != "br")
            {
                foreach (var child in element.ChildNodes)
                    AppendSafeHtml(child, output);
                output.Append("</").Append(tag).Append('>');
            }
            return;
        }

        foreach (var child in element.ChildNodes)
            AppendSafeHtml(child, output);
    }

    // HTML encoding for attribute values (href) — also escapes the quote that
    // would otherwise terminate the attribute.
    static string Attr(string s) => H(s).Replace("\"", "&quot;");

    static readonly (string from, string to)[] MonthMap =
    [
        ("JAN","January"),("FEB","February"),("MAR","March"),("APR","April"),
        ("MAY","May"),("JUN","June"),("JUL","July"),("AUG","August"),
        ("SEP","September"),("OCT","October"),("NOV","November"),("DEC","December"),
    ];

    static readonly (string full, string abbr)[] StateAbbr =
    [
        ("Alabama","Ala."),("Arizona","Ariz."),("Arkansas","Ark."),
        ("California","Calif."),("Colorado","Colo."),("Connecticut","Conn."),
        ("Delaware","Del."),("Florida","Fla."),("Georgia","Ga."),
        ("Illinois","Ill."),("Indiana","Ind."),("Kansas","Kans."),
        ("Kentucky","Ky."),("Louisiana","La."),("Maryland","Md."),
        ("Massachusetts","Mass."),("Michigan","Mich."),("Minnesota","Minn."),
        ("Mississippi","Miss."),("Missouri","Mo."),("Montana","Mont."),
        ("Nebraska","Nebr."),("Nevada","Nev."),("New Hampshire","N.H."),
        ("New Jersey","N.J."),("New Mexico","N.Mex."),("New York","N.Y."),
        ("North Carolina","N.C."),("North Dakota","N.Dak."),("Oklahoma","Okla."),
        ("Oregon","Oreg."),("Pennsylvania","Pa."),("Rhode Island","R.I."),
        ("South Carolina","S.C."),("South Dakota","S.Dak."),("Tennessee","Tenn."),
        ("Texas","Tex."),("Vermont","Vt."),("Virginia","Va."),
        ("Washington","Wash."),("West Virginia","W.Va."),("Wisconsin","Wisc."),
        ("Wyoming","Wyo."),
    ];

    // -----------------------------------------------------------------------
    // Template / file helpers
    // -----------------------------------------------------------------------

    string FinalizePageHtml(WebPage page, string title)
    {
        string body = page.FinalizeBody();
        string html = _templateText;
        html = html.Replace("<insert title>", H(title));
        html = html.Replace("<insert stylesheet>", "");
        html = html.Replace("<insert body>", body);
        return html;
    }

    static void WriteFile(string path, string content)
    {
        using var sw = new StreamWriter(path, append: false,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        sw.Write(content);
    }
}

// ---------------------------------------------------------------------------
// WebPage — modern footnote format (sup>a.fn + ol.src)
// ---------------------------------------------------------------------------

public sealed class WebPage
{
    sealed class FootnoteEntry
    {
        public int            Position;
        public int            Number;   // footnote number used in the body sup href
        public string         Text = ""; // citation text only — no sup tag
        public FootnoteEntry? Next;
        public string AllText() =>
            Next != null ? Text + " See also " + Next.AllText() : Text;
    }

    readonly StringBuilder _html = new();
    readonly List<FootnoteEntry> _footnotes = new();
    int _position;

    public string Url            { get; init; } = "";
    public int    FootnoteNumber { get; private set; }

    public void Write(string txt)
    {
        _html.Append(txt);
        _position = _html.Length;
    }

    public void AddFootnote(string txt)
    {
        FootnoteNumber++;
        int n   = FootnoteNumber;
        string sup = "<sup><a class=\"fn\" href=\"#s" + n + "\">[" + n + "]</a></sup>";

        var existing = _footnotes.FirstOrDefault(f => f.Position == _position);
        if (existing != null)
        {
            // Chain: no sup in body, just append to the chain text.
            // The entry's Number (and body sup href) still use n so the
            // numbering skips exactly as the original VB does.
            var chained = new FootnoteEntry { Position = _position, Number = n, Text = txt };
            chained.Next  = existing.Next;
            existing.Next = chained;
            return;
        }

        // First entry at this position: write sup to body, store citation text separately.
        var entry = new FootnoteEntry { Position = _position, Number = n, Text = txt };
        _html.Append(sup);
        _footnotes.Add(entry);
    }

    public string FinalizeBody()
    {
        if (_footnotes.Count > 0)
        {
            // Hidden list — not displayed, but popup JS reads <li id="sN"> from it.
            Write("<ol class=\"src\" hidden aria-hidden=\"true\">\r\n");
            foreach (var f in _footnotes)
                Write("<li id=\"s" + f.Number + "\" value=\"" + f.Number + "\">" +
                      f.AllText() + "</li>\r\n");
            Write("</ol>\r\n");
        }
        return _html.ToString();
    }
}

// ---------------------------------------------------------------------------
// GedFamily extension helpers
// ---------------------------------------------------------------------------

public static class GedFamilyExtensions
{
    public static string Description(this GedFamily fam)
    {
        string h = fam.Husband?.Fullname ?? "";
        string w = fam.Wife != null ? fam.Wife.Wifename(fam.Husband) : "";
        if (h.Length == 0) return w;
        if (w.Length == 0) return h;
        return h + " and " + w;
    }

    public static string FamilyPhrase(this GedFamily fam)
    {
        string s = "";
        if (fam.Husband != null)
        {
            s += fam.Husband.FirstMiddle();
            if (fam.Wife != null) s += " and ";
        }
        if (fam.Wife != null)
        {
            s += fam.Wife.FirstMiddle() + " ";
            if (!string.Equals(fam.Wife.LastName, "unknown", StringComparison.OrdinalIgnoreCase))
                s += "(" + fam.Wife.LastName + ") ";
        }
        if (fam.Husband != null)
            s += fam.Husband.LastName;
        return s;
    }
}
