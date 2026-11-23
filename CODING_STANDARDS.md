# Coding standards

Read by the reviewer (`/code-review`). Each rule here is a judgement call no test can make. A
rule a script could check belongs in a test or the pre-commit hook instead.

Wider sources the reviewer also checks the diff against: the design note
(`docs/design/phase1-look.md`), the token rules in `web/README.md`, the glossary
(`docs/glossary.md`) and the ADRs (`docs/adr/`).

## Comments

- **Comments are rare and say why.** Names, types and tests say what the code does. A comment is
  for what they cannot say: the reason behind a choice that looks wrong, a workaround for
  something outside the code, a gotcha. Every comment is one more thing to keep true, so the
  reviewer flags the comments a diff adds or leaves behind when they restate the code, narrate the
  change or its history ("now uses…", "per the review"), or no longer match the code beside them.
  A diff that changes code under a comment updates or removes that comment. The fix for a
  confusing line is usually a better name, not a comment.

## Web

- **Breakpoints match the design note's words.** The breakpoints are phone (`max-width: 30rem`,
  with `min-width: 30.01rem` for "wider than a phone"), tablet (`min-width: 48rem`, with
  `max-width: 47.99rem` below it) and desktop (`min-width: 80rem`). A media query uses the one
  the design note names for that rule; "from tablet up" is `48rem`, not `30.01rem`. When the
  CSS and the note disagree, one of them changes in the same diff.
- **A finished action confirms with the toast; a failure stays inline.** Save, Update and later
  Delete confirm through `showToast(text)` (`web/src/app/toast.ts`; design note, Feedback). A
  feature adds no banner or success message of its own, and an error never shows a toast: the
  toast goes on its own after 5 seconds, and an error has to stay until the user deals with it.
