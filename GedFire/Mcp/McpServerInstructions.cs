using GedFire.Gen;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The instructions the server states once at initialization: that xrefs
// belong only to the bound document, which tool is the write path, and
// whichever of --read-only and --enforce-privacy are in effect. Guidance
// specific to one tool stays on the tool.
// ---------------------------------------------------------------------------

public static class McpServerInstructions
{
    const string Base =
        "Every xref returned by a tool on this server (an individual or family reference such as \"@I123@\" " +
        "or \"@F45@\") belongs only to the single GEDCOM document this server was started against, and is " +
        "meaningless to any other document or provider. date_calc, find_family, find_person, find_source, " +
        "get_document_stats, get_record, get_records, list_people, " +
        "list_unanchored_people, select_targets, " +
        "describe_changeset_ops, validate_changeset, validate_document, and check_plausibility never modify " +
        "that file. apply_changeset is the only tool that writes to it, and only after validation and " +
        "in-memory verification both pass; call validate_changeset first with the same arguments to preview a " +
        "changeset without writing. check_plausibility runs that same preview and returns just the " +
        "chronological/biological plausibility findings it would introduce (a parent's age at a child's " +
        "birth, an event out of order, a possible duplicate, an ancestor cycle), structured for a caller that " +
        "wants to route on severity rather than parse validate_changeset's log lines -- call it alongside " +
        "validate_changeset, not instead of it. validate_document checks the document itself for GEDCOM 7 " +
        "conformance problems (bad tag charset, broken level hierarchy, dangling pointers) independent of any " +
        "changeset -- run it to sanity-check a file before relying on it, not in place of validate_changeset. " +
        "Call describe_changeset_ops before composing a changeset from scratch -- it returns the full op " +
        "dialect, so there is no need to know the changeset format in advance or discover it by trial and " +
        "error.";

    const string ReadOnlyNotice =
        " This server was started with --read-only: every call to apply_changeset is refused. " +
        "validate_changeset remains available to preview changesets.";

    public static string Build(bool readOnly, bool enforcePrivacy)
    {
        string instructions = Base;
        if (readOnly) instructions += ReadOnlyNotice;
        if (enforcePrivacy) instructions += EnforcePrivacyNotice();
        return instructions;
    }

    static string EnforcePrivacyNotice() =>
        " This server was started with --enforce-privacy: individuals with an RESN of CONFIDENTIAL or " +
        "PRIVACY, and individuals plausibly still living (no death-class fact, born within the last " +
        $"{PrivacyFilter.PlausiblyLivingAgeYears} years), are reduced to a \"{PrivacyFilter.LivingGivenName} " +
        "<Surname>\" placeholder with no dates, places, notes, or media -- the same treatment `gedfire " +
        "generate` applies before publishing a site. Do not treat a placeholder as the whole record; it is " +
        "withheld, not absent.";
}
