# Specification Quality Checklist: Approval Limits by Role

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-31
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- "No implementation details" is judged as: no languages, frameworks, libraries, classes, storage or code structure. The spec names none. It does contain four externally observable contract items, kept deliberately and listed here so the checked box is accurate:
  - **Reason codes** (FR-009, FR-015, SC-004): `SelfApprovalNotAllowed`, `DuplicateApprover`, `AboveApprovalLimit`, `SecondApproverMustBeDirector`, `RoleCannotApprove`. AGENTS.md ("How a refusal surfaces") treats these as API surface once the UI switches on them, and constitution principle III requires a refusal to name the concrete fact. They are requirements, not design.
  - **HTTP status codes** (FR-013, FR-014, FR-015, FR-017, Edge Cases): 403, 409, 404, 400. These are the outcome each business case must produce for callers, and each is tied to a business meaning (authority refusal, lifecycle or concurrency conflict, missing invoice, missing reason). They say nothing about how the service produces them.
  - **Configuration keys** (Assumptions only): `Approval:RoleLimits` and `Approval:SecondApproverRole`. FR-010 itself says only "configuration". The keys appear in Assumptions to record the existing source of the limits and the second-approver default (Director); they are not a requirement to use any technology.
  - **Message content** (SC-004): states which facts a refusal message must show (amount and limit, amount and required role, user and invoice, or role). This is user-facing behaviour, not implementation.
- Remaining accepted risk: the codes, status numbers and key names bind the spec to the current public contract. If that contract changes, the spec must change with it.
