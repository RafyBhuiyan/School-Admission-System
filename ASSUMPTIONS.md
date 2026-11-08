# Assumptions and decisions

## Step 1

- Retain the existing `Backend` and `Frontend` directory names; the plan permits different folder names.
- Retain .NET 10 and React 19 from the existing starters. Both meet the plan's minimum versions. `global.json` selects a stable .NET 10 SDK with feature-band roll-forward.
- Use one API project and one xUnit test project in the root solution. Services will use `AppDbContext` directly, without boilerplate repositories.
- `.env.example` is a configuration contract for later steps. It contains placeholders, and is not automatically loaded by the scaffold.
- The user initialized the GitHub remote at `RafyBhuiyan/School-Admission-System`. Connectivity is verified; public visibility has not been verified because the separate API check was declined.

## Business rules

The detailed assumptions required by step 4 will be recorded when the database model and application foundations are in place. The revised plan remains the baseline; no admission behavior has been implemented or verified at step 1.
