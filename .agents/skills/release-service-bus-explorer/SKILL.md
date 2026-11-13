---
name: release-service-bus-explorer
description: Release ServiceBusEmulatorExplorer through its verified tag, GitHub Actions, draft, and README screenshot gates.
disable-model-invocation: true
---

# Release Service Bus Explorer

Publish one immutable `vMAJOR.MINOR.PATCH` release from the current repository. Treat the release as a sequence of gates: **branch first, tag second, publish last**. Invocation authorizes the normal release writes, including the release commit, branch push, tag push, and publication; it does not authorize rewriting history or moving an existing tag.

## 1. Establish the contract

1. Resolve the repository root and read `AGENTS.md` plus any linked Git preferences that exist.
2. Read these files before executing them:
   - `.github/workflows/release.yml`
   - `.github/workflows/update-readme-screenshot.yml`
   - `scripts/Push-ReleaseTag.ps1`
   - every publish, artifact-validation, or smoke-test script invoked by `release.yml`
3. Confirm that the inspected files still implement this contract:
   - a `v*` tag triggers the release workflow;
   - the workflow tests, builds, validates, signs (after the user's approval and phone code), and launch-smokes both Windows assets;
   - successful validation creates a draft release;
   - publishing triggers the README screenshot workflow.

Adapt to harmless filename changes found in the repository. Pause before release if the contract has materially changed or a gate has disappeared. This gate is complete only when every command that will mutate GitHub has been identified and its ordering is known.

## 2. Resolve tools and remote state

Use the existing GitHub CLI session. Resolve `gh` with `Get-Command gh`; on Windows, fall back to `C:\Program Files\GitHub CLI\gh.exe`. Run `gh auth status` and verify the expected account and `github.com` host.

If a sandboxed call reports a closed proxy such as `127.0.0.1:9`, rerun the same read with the required network escalation. Treat authentication as invalid only when an unrestricted `gh auth status` fails. Do not start a new login while the existing unrestricted session is valid.

Fetch tags and remote refs, then verify all of the following:

- the working branch tracks `origin/<branch>`;
- the repository remote resolves to the expected GitHub repository;
- local and remote branch ancestry is understood;
- `.github/workflows/release.yml` is active;
- the proposed tag and release do not already exist.

Give every command an explicit timeout: normally 30-60 seconds, up to 300 seconds for release builds or workflow watches. This gate is complete only when remote reads succeed and the release target is unambiguous.

## 3. Prepare the release commit

Inspect `git status`, tracked and untracked files, and the complete diff. Account for every changed file. Keep unrelated user work out of the release.

If release changes are uncommitted:

1. Run the repository's relevant fast tests and UI smoke proof.
2. Run `git diff --check`.
3. Stage only the intended paths.
4. Inspect the staged diff and run `git diff --cached --check`.
5. Create one intentional, terse commit describing the release payload.

If the working tree is clean and `HEAD` already contains the intended payload, use that commit; do not create an empty version-bump commit. This repository injects the assembly/file version from the release tag during `Publish-Release.ps1`, so no project-file version edit is required unless the inspected repository contract explicitly introduces one.

Leave the release commit on the checked-out branch with its `origin/<branch>` upstream configured, and verify that the working tree is clean. The tag helper owns the ordered branch push and tag push in step 6. This gate is complete only when the intended release SHA is known and ready to push.

## 4. Choose the version

Use an explicitly requested `vMAJOR.MINOR.PATCH` version when provided. Otherwise fetch tags, select the highest stable semantic tag, and increment its patch component. State the proposed old and new versions before mutation.

Verify the new tag is absent from:

- local refs;
- remote tag refs;
- GitHub Releases, including drafts.

Keep existing releases unchanged. Publishing the new release as latest must not draft, delete, hide, or modify older releases.

## 5. Bring the README screenshot up to date

Run this on the release branch after the release commit is pushed and before tagging (DEC-060), so the tag already contains the current UI:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Sync-ReleaseScreenshot.ps1 -Branch master
```

Give it a 45-minute timeout. It runs the screenshot workflow for the branch's commit. When the image changed, it opens the pull request, waits for its checks, merges it and fast-forwards the local branch. Invoking this skill authorizes that merge. When it reports that the screenshot is already current, nothing changed. Either way, tag the commit the branch points to afterwards. If it fails, stop before tagging and report the run or pull request it names.

## 6. Push branch, then tag

Use `scripts/Push-ReleaseTag.ps1`; it is the single source of truth for ordered branch and tag publication. Run its dry run first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Push-ReleaseTag.ps1 -Tag v0.1.4 -ExternalCommandTimeoutSeconds 60 -WhatIf
```

Replace the example tag with the resolved version. Confirm that the plan pushes the branch without followed tags, verifies the remote SHA and active workflow, creates an annotated tag at `HEAD`, and pushes only that tag.

Then run the real invocation. Use a child PowerShell command so `-Confirm:$false` remains a Boolean switch even when the parent execution policy blocks scripts:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command '& { & ".\scripts\Push-ReleaseTag.ps1" -Tag "v0.1.4" -ExternalCommandTimeoutSeconds 60 -Confirm:$false }'
```

Verify the local and remote tag both resolve to the release commit. This gate is complete only when the tag is pushed once at the intended SHA.

## 7. Watch the release gate

The `sign` job waits for the user: they approve the `code-signing` environment and type the SimplySign phone code (DEC-059). An agent must do neither. Right after the tag push, open the signing helper in a visible Windows PowerShell window on the user's desktop. Launch it outside the sandbox, otherwise the window stays invisible:

```powershell
$signing = Start-Process powershell.exe -WorkingDirectory (Resolve-Path .) -PassThru -ArgumentList @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-NoExit',
    '-File', '.\scripts\Approve-ReleaseSigning.ps1', '-Tag', 'v0.1.4')
$signing.Id
```

Tell the user that a "signing" PowerShell window is open: it shows each step, asks to approve once the build is done, then asks for the phone code. Report the window's process ID. `-NoExit` keeps the result visible; the window belongs to the user, so never close it yourself. Keep watching the run below while the user answers.

Find the newest `release.yml` push run whose `headBranch` equals the tag:

```powershell
gh run list --workflow release.yml --event push --branch v0.1.4 --limit 5 --json databaseId,status,conclusion,headBranch,url
gh run watch <run-id> --exit-status --interval 10
```

Use a bounded watch of about five minutes and send the user a concise progress update at least every 60 seconds. A `403` while `gh` requests check annotations is informational when the watch exits successfully and the run conclusion is `success`. The run waits as long as the user takes to approve and send the code; that wait is not a failure.

Require successful completion of every applicable job step, including:

- fast tests;
- both asset builds and artifact validation;
- signing both executables and the signature check;
- runtime-required executable launch smoke;
- portable executable launch smoke;
- draft release creation and upload.

Inspect the draft with `gh release view <tag> --json ...`. Require `isDraft: true`, `isPrerelease: false`, exactly the expected two uploaded executable assets, non-zero sizes, and names containing the tag. This gate is complete only when the hosted run and the draft asset contract both pass.

## 8. Publish last

Publish only the validated draft as latest:

```powershell
gh release edit v0.1.4 --draft=false --latest
```

Verify the canonical `/releases/tag/<tag>` URL reports `isDraft: false`, `isPrerelease: false`, a publication timestamp, and the same two assets. This gate is complete only when the public release is readable as latest.

## 9. Check the README safety net

Publishing runs the screenshot workflow again for the published tag. Find that run and watch it to completion:

```powershell
gh run list --workflow update-readme-screenshot.yml --event release --branch v0.1.4 --limit 5 --json databaseId,status,conclusion,headBranch,url
gh run watch <run-id> --exit-status --interval 10
```

Require both jobs to succeed:

- capture and validate the UI from the published tag;
- compare and commit the documentation image when visuals changed.

Because step 5 already updated the screenshot, the expected result is no change. If the run still pushed a branch, open and merge that pull request and report it as a gap in step 5. Fetch `origin` afterward. If the screenshot workflow created a commit and the local working tree is clean, fast-forward with `git pull --ff-only`. Verify `origin/<branch>...<branch>` reports `0 0`.

This gate is complete only when the released tag reproduced a valid screenshot and the local checkout is synchronized.

## 10. Report evidence

Return:

- release version, tag SHA, and public release link;
- both asset names and sizes;
- release workflow link and conclusion;
- README screenshot workflow link and conclusion;
- whether the README image changed;
- final working-tree and remote synchronization state;
- any warning that could not be independently verified.

The release is complete only when every item above is evidenced. Leave older releases unchanged.

## Failure recovery

- Before tag creation, fix the failing gate and rerun it with a changed diagnosis.
- After tag creation, treat the tag as immutable. Inspect `gh run view <run-id> --log-failed`; fix forward on a new commit and use the next version unless the user explicitly authorizes deleting the failed tag.
- Keep a failed draft unpublished. Preserve its logs and report the exact failed step.
- If README automation fails after publication, keep the successful release state visible, diagnose the screenshot workflow separately, and report that documentation automation is incomplete.
- After three materially different failed attempts at one gate, stop and report the blocker.
