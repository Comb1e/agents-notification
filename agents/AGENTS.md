# Core

- Explore high-risk approaches freely; avoid a defensive working style
- When establishing a solution, prioritize searching for relevant papers and projects to find inspiration, and record the papers and projects actually used in docs/references.md. Note that the use of official libraries such as PyTorch does not need to be recorded
- Prefer explicit state machines for complex transitions
- Keep exactly one official plan; delete superseded solutions by default
- Prefer parallel tool calls
- Handle multiple tasks one at a time; exceptions are parallelizable tasks, e.g., fix another bug while code runs
- No permanent test scripts except for CI tests. Temporary tests only—delete them immediately after running

- User Name: Leafy. Do not write this in other files

# Coding

- Use Git for code management and commits
- Heavily used constants should live in config files
- Frequently reused functionality should be a shared generic interface, not re-implemented per call site
- Batch-process and centrally plan the placement of growing files such as program-generated logs

## Git

### Commits

- Follow Conventional Commits: <type>(<scope>): <subject>
- Common types: feat / fix / docs / refactor / test / chore
- Also include the user's individual parameter adjustments in the commit

### Branches

- Naming: <type>/<short-description>, e.g. feature/user-login, fix/order-total
- Branch off the latest main (pull first), merge promptly, delete when done
- Never push directly to main, always use a PR(Need to resolve conflicts before PR) and do the merge by yourself. Only push to remote when user asks to

# Iteration

- Update README.md after functional change
- Keep a short code-structure section in AGENTS.md
- Clean up cache/tmp/logs at session end
- Don't create or modify files under docs/ unless asked

# README

The README should act as a quick-start guide, not a complete research manual. Keep only:

- Project purpose
- Requirements
- Installation
- First useful command
- Common checks
- Links to detailed documentation

# UI

- Let layout do the explanation instead of words

# Math

- The key logic should be verified through independent controls, boundary cases, and counterexamples
