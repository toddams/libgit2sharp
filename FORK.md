# Runa's LibGit2Sharp fork

This fork of [libgit2/libgit2sharp](https://github.com/libgit2/libgit2sharp) lives at
`toddams/libgit2sharp` on the branch `runa/aot-gitbuf`. It branches from upstream `eaa698d0` (Merge
pull request #2186). Runa uses it as the submodule `libs/libgit2sharp`, through a project reference.

Every change is managed code. The native library still comes unchanged from
`LibGit2Sharp.NativeBinaries` 2.0.324, so a bug inside libgit2 itself can't be fixed here. Runa runs
git for those cases instead; see `docs/git-cli-commands.md` in Runa. Tests for the APIs the fork adds
live in Runa, under `tests/Runa.Repositories.LibGit2.Tests`.

The entries run oldest first. Anything marked *uncommitted* hasn't been committed to the fork yet.

## At a glance

| Date | Commit | Change |
|---|---|---|
| 2026-09-09 | `f8542790` | `git_buf` results survive Native AOT |
| 2026-09-09 | `c8ef9847` | Builds as a project reference: `net8.0` only, no package on build |
| 2026-09-09 | `1197c84d` | Diff callbacks survive Native AOT |
| 2026-09-09 | `cedfae82` | `Diff.Compare` for two in-memory buffers |
| 2026-09-27 | `04b7ec13` | Clean filtering, blob hashing, string attributes and index reload |
| 2026-09-27 | `bdb2eae2` | The clean filter warns instead of throwing on unsafe CRLF |
| 2026-09-28 | `d1eb2c28` | Status can leave rewrites unsplit, as `git status` does |
| 2026-09-29 | `06253fb1` | Stash messages passed through as `git stash` does |
| 2026-09-29 | `f6de1c0d` | `RepositoryInformation.CommonPath` |

---

## `git_buf` results survive Native AOT

- **Commit:** `f8542790` (2026-09-09), "Pass git_buf by ref so native writes survive Native AOT marshalling"
- **Used by:** every API that returns a `git_buf`
- **Tests:** the Native AOT build of `Runa.Repositories.LibGit2.Tests`

**Why.** Runa ships as a Native AOT build. There, every API that returns a `git_buf` came back empty,
including config discovery, a branch's upstream and remote name, refspec transforms, repository
discovery, filtered blob content, describe and short ids. `GitBuf` was a sequential-layout class
passed by value. CoreCLR pins blittable classes, so libgit2's writes were visible, but Native AOT
copies the object in and never copies it back.

**What.** A blittable `GitBufNative` struct is now passed by `ref` on all 22 declarations. `GitBuf`
keeps ownership and disposal, and exposes `ptr`, `asize` and `size` over the struct.

---

## Builds as a project reference

- **Commit:** `c8ef9847` (2026-09-09), "Build net8.0 only and skip pack-on-build when consumed as a project reference"
- **Used by:** Runa's project references to `LibGit2Sharp.csproj`

**Why.** Inside Runa's solution, the `net472` target was dead weight, and `GeneratePackageOnBuild`
produced a NuGet package on every build.

**What.** The project targets `net8.0` only, and `GeneratePackageOnBuild` is `false`.

---

## Diff callbacks survive Native AOT

- **Commit:** `1197c84d` (2026-09-09), "Pass diff callback payloads by pointer so they survive Native AOT reverse P/Invoke"
- **Used by:** `Patch` and `ContentChanges`, which Runa's diff engine builds on
- **Tests:** the Native AOT build of `Runa.Repositories.LibGit2.Tests`

**Why.** Under Native AOT, building a `Patch` or `ContentChanges` aborted the process. The diff
hunk, line and binary callbacks took `GitDiffHunk`, `GitDiffLine` and `GitDiffBinary` as classes.
Reverse P/Invoke marshalling hands class parameters in as null, so the callback dereferenced null
(libgit2sharp#2082).

**What.** The three payloads are blittable structs, and the callbacks take them by pointer.

---

## `Diff.Compare` for two in-memory buffers

- **Commit:** `cedfae82` (2026-09-09), "Expose git_diff_buffers as Diff.Compare(byte[], string, byte[], string)"
- **Used by:** `NativeFileDiffEngine.Compare`

**Why.** Runa diffs a working-tree file against its index or HEAD version with libgit2's diff engine.
Upstream compares only two `Blob`s, which would mean writing the working-tree content into the object
database just to diff it.

**What.** `Diff.Compare(byte[] oldContent, string oldPath, byte[] newContent, string newPath, CompareOptions)`
diffs two in-memory buffers through `git_diff_buffers`. Like the blob overload, it returns
`ContentChanges`.

---

## Clean filtering, blob hashing, string attributes and index reload

- **Commit:** `04b7ec13` (2026-09-27), "Expose clean filtering, blob hashing, string attributes and index reload"
- **Used by:** `FileDiffReader` for the first three, and `LibGit2Repository.WithFreshIndex` for the reload
- **Tests:** `FilterAndHashTests`

**Why.** Runa's working-tree diffs and hunk staging need the file the way git would store it. That
means line endings after `core.autocrlf`, `eol`/`text` and `ident`, and a blob id to compare with the
index, so a stale diff is refused rather than applied. Runa also has to recognise what libgit2 can't
reproduce: filter drivers (LFS, git-crypt) and `working-tree-encoding`. Separately, libgit2 caches
the index and re-reads it only for status and checkout. After another git process wrote the index,
including Runa's own `git apply --cached`, reads saw a stale copy, and upstream's reload is
internal.

**What.** Four additions:

| API | Does |
|---|---|
| `ObjectDatabase.ReadFilteredFile(path)` | Runs a working-tree file through its clean filters |
| `ObjectDatabase.HashBlob(content)` | Computes a blob id without writing the blob |
| `Repository.GetStringAttribute(path, name)` | Returns an attribute's string value, or null |
| `Index.Reload()` | Calls `git_index_read` without force, so it re-reads only when the file changed |

---

## The clean filter warns instead of throwing on unsafe CRLF

- **Commit:** `bdb2eae2` (2026-09-27), "Allow unsafe CRLF conversion when reading a file through the clean filter"
- **Used by:** `FileDiffReader`, through `ReadFilteredFile`
- **Tests:** `FilterAndHashTests.ReadFilteredFile_UnderAutoCrlfAndSafeCrlf_DoesNotThrowOnMixedLineEndings`

**Why.** With `core.safecrlf` set, the clean filter threw on a file with mixed line endings, so the
diff panel failed on it. `git diff` and `git add -p` only warn in that case.

**What.** `ReadFilteredFile` loads the filter list with `GIT_FILTER_ALLOW_UNSAFE`.

---

## Status can leave rewrites unsplit

- **Commit:** `d1eb2c28` (2026-09-28), "Make splitting rewrites for status rename detection optional"
- **Used by:** `LibGit2Repository.CollectStatus`, which turns it off
- **Tests:** `RepositoryContextStagingTests.ARewrittenFileStaysModifiedWhenItsOldContentMovesToANewFile`

**Why.** `DetectRenamesInIndex` and `DetectRenamesInWorkDir` always added
`GIT_STATUS_OPT_RENAMES_FROM_REWRITES`. A heavily rewritten file whose old content had moved to a new
file came back as added, with the new file renamed from it. `git status` never splits rewrites: it
reports a modification plus an add.

**What.** `StatusOptions.DetectRenamesFromRewrites` defaults to `true`, which keeps upstream's
behaviour. Runa sets it to `false`.

---

## Stash messages passed through as `git stash` does

- **Commit:** `06253fb1` (2026-09-29), "Pass stash messages through as git stash does"
- **Used by:** `LibGit2Repository`'s stash, and the autostash around checkout and pull
- **Tests:** `StashTests`

**Why.** `StashCollection.Add` prettified the message before handing it to libgit2. An empty message
became an empty string rather than null. libgit2 writes git's default
`WIP on <branch>: <sha> <subject>` only for a null message, so a stash without a message came out as
`On <branch>: `. A given message also gained a newline of its own on top of the one libgit2 adds when
it formats `On <branch>: <message>`.

**What.** An empty message is passed as null, and any other message is passed unchanged, as
`git stash push -m` does.

---

## `RepositoryInformation.CommonPath`

- **Commit:** `f6de1c0d` (2026-09-29), "Expose the common git directory as RepositoryInformation.CommonPath"
- **Used by:** `LibGit2Repository`'s constructor, for `CommonDirectory`
- **Tests:** `GitDirectoriesTests`, `RepositoryContextWorktreeTests.ASubmoduleTab_ListsItsCheckoutAsTheMainWorktree`

**Why.** Runa needs a linked worktree's shared git directory. It watches the shared refs there, and it
uses the directory to tell the main worktree from linked ones. Runa used to run
`git rev-parse --path-format=absolute --git-common-dir` in its repository constructor. That blocked
the thread opening the tab and failed silently on git older than 2.31. libgit2 already resolves this
directory when it opens a repository.

**What.** `RepositoryInformation.CommonPath` is read from `git_repository_commondir`, next to `Path`.
It equals `Path` except in a linked worktree.
