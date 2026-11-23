# Coding standards

Read by the reviewer (`/code-review`). Each rule here is a judgement call no test can make. A
rule a script could check belongs in a test or the pre-commit hook instead.

Wider sources the reviewer also checks the diff against: the design note
(`docs/design/phase1-look.md`), the token rules in `web/README.md`, the glossary
(`docs/glossary.md`) and the ADRs (`docs/adr/`).

## Web

- **Breakpoints match the design note's words.** The breakpoints are phone (`max-width: 30rem`,
  with `min-width: 30.01rem` for "wider than a phone"), tablet (`min-width: 48rem`, with
  `max-width: 47.99rem` below it) and desktop (`min-width: 80rem`). A media query uses the one
  the design note names for that rule; "from tablet up" is `48rem`, not `30.01rem`. When the
  CSS and the note disagree, one of them changes in the same diff.
- **A finished action confirms with the toast; a failure stays inline.** Save, Update and later
  Delete confirm through `showToast(text)` (design note, Feedback). A feature adds no banner or
  success message of its own, and an error never shows a toast.

## Tests

- **Fake clocks start at the current time** (`DateTimeOffset.UtcNow`) and move with `Advance`.
  A fixed start date eventually falls into the past, and anything checked against the real clock
  (a cookie's expiry in the test client, for one) breaks tests that have nothing to do with the
  change.
