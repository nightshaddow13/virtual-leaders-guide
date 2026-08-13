# Passcode is generated as a memorable two-word phrase, not an opaque random token

CONTEXT.md's Passcode entry describes a value a visitor reads off a printed handout or an email and types in
by hand — the entire point is that it's communicated out-of-band by a person, not copy-pasted. An Event now
gets its Passcode auto-generated at creation (CONTEXT.md's Passcode entry, P2-6/#15) rather than left blank for
an Admin to fill in, so that generation needs to produce something a person can realistically transcribe.

We decided to generate Passcode as two Title-cased words concatenated with no separator (e.g. `TigerLantern`),
drawn from the real EFF Large Wordlist (7,776 words) via the `PasswordGenerator` NuGet package
(`Password.ForPassphrase(2, separator: null, capitalize: true, includeNumber: false, includeSymbol: false)`),
rather than a hand-curated word list or an opaque random token (GUID, hex string). ~7,776² (≈60 million)
combinations is well past what this threat model needs — ADR-0009 already accepts that Passcode is "keep
drive-by access out," not a real authentication system, the same reasoning that justified encryption over
hashing there.

## Considered options

- **An opaque random token** (GUID, random hex/base64 string) — much higher entropy per character, but
  defeats the entire point: CONTEXT.md's Passcode is meant to be read aloud or copied from a printed handout by
  a person, and a string like `a3f9c1e0b7d24f6a` is exactly the kind of thing that gets mistyped.
- **A hand-curated word list** (e.g. ~1,000 short, unambiguous words picked for this project) — would give
  tighter control over word difficulty/length than the EFF list provides (which includes some longer or less
  common words), but means authoring and maintaining a word list from scratch for a marginal readability gain
  the EFF list mostly already delivers.

## Consequences

Passcode generation now depends on an external NuGet package and its bundled wordlist rather than anything
hand-rolled in this repo — a new dependency, but one that ships the real EFF list rather than reinventing it
badly. Some generated Passcodes will include longer or less common words than a hand-curated list would allow
(e.g. `ZeppelinLego`) - acceptable for this threat model, but worth revisiting if it turns out to cause real
transcription problems in practice.
