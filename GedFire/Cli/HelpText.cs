namespace GedFire.Cli;

public static class HelpText
{
    public const string Text = """
        GedFire -- GEDCOM processor and site generator

        Commands:
          create    --output <ged70> --name <gedcom-name> [--xref @I00001@] [--sex M|F|X|U]
                        Create a new GEDCOM 7 document seeded with one named person.

          upgrade   --input <ged55> --output <ged70>
                        Upgrade a GEDCOM 5.5 file to GEDCOM 7.0.

          downgrade --input <ged70> --output <ged55>
                        Write a GEDCOM 7.0 file in GEDCOM 5.5 format.

          generate  --input <ged> --output-dir <dir> [--format html]
                        Generate the family pages and index from a GEDCOM file.

          export-index  --input <ged> --output <json>
                        Export the research person index (one JSON entry per
                        individual: xref, normalized name, birth/death, parents,
                        marriages with spouse and children xrefs).

          select-targets --input <ged> --output <wanted.json> --count <N> --surnames <list>
                        Detect every research gap for the given surnames
                        (New parent/spouse/child, Enrich person), score each
                        by nominal points and GED-only difficulty, and draw
                        <N> of them uniformly at random (one Legendary-band
                        cap per pack) into a self-contained wanted.json.

          apply     --input <ged> --changes <json> --items all|1,3 [--dry-run]
                        Apply approved research-proposal changeset items to the
                        GEDCOM. Validates every op first; writes the file only
                        after in-memory verification (byte-stable round-trip,
                        pointer resolution, record-count deltas) passes.

          validate  <file> [--warnings-as-errors]
                        Run GEDCOM 7 conformance checks (GED001-GED014) and
                        print one diagnostic per line, sorted by severity then
                        code. Exits 1 if any Error is present (or any Warning
                        too, with --warnings-as-errors); 0 otherwise.

          pack      --input <ged> --media-dir <dir> --output <gdz>
                        Bundle a GEDCOM file and its referenced media into a
                        GEDZIP (.gdz) archive.

          unpack    --input <gdz> --output-dir <dir>
                        Extract a GEDZIP archive's gedcom.ged and media files
                        into a directory.

          mcp       --input <ged> [--read-only] [--enforce-privacy]
                        Start a stdio Model Context Protocol server exposing
                        this GEDCOM to MCP-compatible agent clients. Resident
                        process: stays running until stdin closes. Every tool
                        except apply_changeset is read-only; apply_changeset
                        is the only one that writes, after validation and
                        in-memory verification pass.
                        Watches the input file and reloads automatically if
                        it changes on disk.
                          --read-only        Disable apply_changeset: every
                                              call to it is refused.
                                              validate_changeset and every
                                              read-only tool stay available.
                          --enforce-privacy   Apply the same privacy filter
                                              `generate` uses before
                                              publishing a site: RESN
                                              CONFIDENTIAL/PRIVACY and
                                              plausibly-living individuals
                                              are reduced to a placeholder
                                              in every tool's output.

          date-calc --op normalize|add|sub|diff
                        Genealogical date arithmetic using GedCore's GEDCOM
                        date grammar -- no GEDCOM file read or required.
                          normalize --date <d>            resolve a dual-dated year
                          add|sub   --date <d> --age <y/m/d>   date +/- age
                          diff      --from <d> --to <d>    elapsed y/m/d between two dates
                        Dates are exact Gregorian "D MON YYYY"; --age is
                        e.g. "63y 4m 2d". See README/AGENTS.md for details.

          find-person --input <ged> --query <name>
                        One-shot mirror of the mcp server's find_person tool:
                        same matcher, same JSON result, no protocol needed.
                          [--max-results N]                 1-20, default 8
                          [--sex M|F]                       ranks a differing recorded sex lower
                          [--birth-year Y] [--birth-place P]
                          [--death-year Y] [--death-place P]
                          [--father NAME] [--mother NAME]
                          [--spouse-name NAME] [--marriage-year Y] [--marriage-place P]

          get-record --input <ged> --xref <@I1@>
                        One-shot mirror of the mcp server's get_record tool.

          get-document-stats --input <ged>
                        One-shot mirror of the mcp server's get_document_stats
                        tool: person/family counts, GEDCOM version, and the
                        running gedfire version.

        Options:
          --help, -h       Show this help message.
          --version, -v    Show the GedFire version.
        """;
}
