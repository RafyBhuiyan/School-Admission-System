# Continuous integration

Add CI after the initial scaffold commit, before step 2. The workflow in
`.github/workflows/ci.yml` runs on every branch push and pull request. It can
also be started manually from GitHub's Actions tab once it is on the default branch.

## Checks

| Required check name | Commands |
| --- | --- |
| Backend checks | .NET restore, Release build, and test discovery/execution |
| Frontend checks | `npm ci`, `npm run lint`, and `npm run build` |

The backend uses the SDK specified in `global.json`. The frontend uses Node.js 22
and the committed npm lockfile. The jobs run independently on Linux runners and
need no repository secrets or database at this stage.

The backend test project currently contains no tests. A green backend check
therefore confirms restore/build and successful test discovery, not admission
behavior. There is no frontend test script yet. Add meaningful tests as the
application grows, and add a PostgreSQL service when database tests need it.

## Enable the workflow

1. In GitHub Desktop, review `.github/workflows/ci.yml`, this guide, and the README change.
2. Commit with summary `ci: add backend and frontend checks`.
3. Click **Push origin** (or **Publish repository** if the repository has not been published).
4. Open the repository on GitHub, select **Actions**, and open the **CI** run for this commit.
5. Confirm that **Backend checks** and **Frontend checks** both pass. If a job fails,
   open its failed step, fix the reported error locally, and commit and push the fix.

## Require checks before merging

After the first successful run, configure branch protection in GitHub:

1. Open **Settings > Branches > Add classic branch protection rule**.
2. Set the branch name pattern to `master`, the current primary branch. If you
   rename it, use the new name instead.
3. Enable **Require a pull request before merging**. For solo development, leave
   required approving reviews disabled so another reviewer is not needed.
4. Enable **Require status checks to pass before merging** and select
   **Backend checks** and **Frontend checks**.
5. Enable **Require branches to be up to date before merging**.
6. Enable **Do not allow bypassing the above settings** if you want the rule to
   apply to administrators too, then save the rule.

Branch protection availability depends on repository visibility and GitHub plan.
See GitHub's [branch protection guide](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule).

For each subsequent implementation step, create a branch in GitHub Desktop,
commit and publish it, and open a pull request into `master`. Wait for both
checks to pass before merging. CI detects failures; branch protection enforces
the merge requirement.

## Add deployment later

Add continuous deployment after Docker packaging (planned steps 22–24), a hosting
target, and deployment configuration are ready. Extend CI as features arrive:
add backend tests, database integration and concurrency tests, frontend tests,
and container build checks at their corresponding implementation stages.

The current workflow runs validation only. Its first hosted run is verified in
GitHub after pushing; local checks do not execute GitHub Actions itself.
