## Code readability and maintainability

Write code primarily for human comprehension and review, not for compactness or minimizing tokens.

### Formatting and whitespace

- Use conventional, consistent indentation and whitespace for the language.
- Separate logical steps with blank lines.
- Use blank lines to distinguish setup, validation, main logic, and result handling.
- Avoid dense blocks of uninterrupted code.
- Do not compress statements, branches, or expressions merely to save lines.
- Prefer multiline formatting when it makes control flow or data structures easier to scan.
- Follow the repository's existing formatting conventions and configured formatter.

### Structure and clarity

- Prefer straightforward, explicit code over clever or overly compact solutions.
- Keep functions focused and at a manageable level of abstraction.
- Use descriptive variable and function names.
- Prefer guard clauses over deeply nested conditionals when appropriate.
- Extract complex expressions into meaningfully named intermediate variables.
- Avoid nested ternaries and dense chains of operations.
- Keep related operations together and visually separate distinct responsibilities.
- Do not sacrifice readability for brevity.

### Comments

- Explain non-obvious intent, assumptions, and trade-offs.
- Do not add comments that merely restate what the code already says.
- Prefer making the code self-explanatory before adding comments.

### Changes and reviewability

- Optimize for code that another engineer can understand during a diff review.
- Preserve existing repository conventions unless they are clearly inconsistent.
- Avoid unrelated refactoring or formatting changes.
- Before completing a change, review the modified code for readability, naming, spacing, and unnecessary complexity.
- Run the repository's configured formatter and relevant lint checks when available.

### Priority

When choosing between two equally correct implementations, prefer the one that is easier to read, debug, maintain, and review, even if it requires more lines of code.