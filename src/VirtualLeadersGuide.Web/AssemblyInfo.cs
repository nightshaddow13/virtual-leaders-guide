using System.Runtime.CompilerServices;

// An assembly-level attribute has no member for a /// doc comment to attach to (CS1587) - this bare
// one-liner is the pointer ADR-0030's triage calls for, not a rationale block. See ADR-0041.
[assembly: InternalsVisibleTo("VirtualLeadersGuide.Web.Tests")]
