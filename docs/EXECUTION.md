The design and implementation plan (@PLAN.md) has been created and reviewed. Your task is to **execute the plan**, not redesign the solution.

### Instructions

1. **Follow the plan**
   - Treat the approved plan as the primary source of truth.
   - Implement the solution according to the documented architecture, design decisions, and implementation steps.
   - Avoid unnecessary refactoring, scope expansion, or introducing abstractions that aren't required.
   - If you encounter an ambiguity, resolve it using the plan and existing codebase conventions where possible.
   - If a significant deviation from the plan is necessary, explain the issue and request approval before proceeding.
   - IF EXISTING CODEBASE: Examine the relevant existing code, architecture, conventions, and dependencies.
   - Identify any discrepancies between the plan and the current codebase.
   - Do not make assumptions about existing functionality without verifying them.

2. **Implement incrementally**
   - Follow the implementation steps in their documented order, respecting dependencies.
   - Break the work into manageable, logically coherent changes.
   - Complete each step before moving to the next.
   - Maintain a persistent implementation progress record that identifies completed steps, pending steps, important decisions, and verification results. Keep it updated throughout implementation so work can be resumed reliably without repeating or losing context.

3. **Verify your work**
   - Add or update tests where appropriate.
   - Run relevant tests, type checks, linters, and other available validation tools.
   - Verify that each implemented component behaves according to the plan.
   - Address issues introduced by your changes before proceeding.
   - Do not claim that functionality has been verified unless the corresponding checks have actually been performed.

4. **Maintain implementation quality**
   - Prefer simple, maintainable solutions consistent with the existing architecture.
   - Avoid unrelated changes.
   - Do not introduce temporary workarounds or shortcuts without clearly documenting them.

5. **Communicate progress**
   - Provide concise progress updates at meaningful milestones.
   - Report completed steps, validation results, and any blockers.
   - Clearly distinguish between completed, partially completed, and unimplemented work.

### Execution

Begin by reviewing the plan, then proceed with implementation, starting from the first actionable step.

**Do not stop after reviewing the plan or describing what you intend to do. Begin making the actual code changes.**

Option 1: Continue through the implementation plan autonomously unless you encounter a blocker, a significant ambiguity, or a decision requiring approval.
Option 2: After each completed step, provide a summary of the implemented work and present it for review and approval.

When finished, provide a concise summary of:
- What was implemented
- Which files or components were changed
- Which tests and validations were performed and their results
- Any deviations from the original plan
- Any remaining work or known limitations

**Prioritize correctness, adherence to the plan, and verified implementation over speed.**