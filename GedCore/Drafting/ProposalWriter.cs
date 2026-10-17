using System.Text;

namespace GedCore.Drafting;

/// <summary>A finished changeset as the proposal presents it: where it came from, the hash of its bytes, and its JSON.</summary>
public sealed record ProposalChangeset(string Name, string Sha256, string Json);

/// <summary>The proposal document, or why it could not be written; a changeset the writer cannot present is an error, never skipped.</summary>
public sealed record ProposalDocument(string? Markdown, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Presents finished changesets for review: one numbered section per changeset, in the order given, each showing
/// its operations and the findings behind them. Adds nothing to a changeset and compiles nothing.
/// </summary>
public static class ProposalWriter
{
    public static ProposalDocument Write(string proposal, IReadOnlyList<ProposalChangeset> changesets)
    {
        var markdown = new StringBuilder();
        var errors = new List<string>();
        markdown.AppendLine($"# Proposal: {proposal}").AppendLine();
        markdown.AppendLine($"This proposal presents {changesets.Count} changeset(s). Rule on each changeset on its own: approve or deny the exact file named in its section.").AppendLine();

        foreach (var (changeset, index) in changesets.Select((c, i) => (c, i + 1)))
            ChangesetSection.Write(index, changeset, markdown, errors);

        return new ProposalDocument(errors.Count == 0 ? markdown.ToString() : null, errors);
    }
}
