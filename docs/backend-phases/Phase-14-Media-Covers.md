# Phase 14 — Cover Images and Media

**Recommended branch:** `phase-14-media-covers`

---

## Objective

Let a product carry a real photograph. Admin upload, server-side content validation, content-addressed storage outside the database, and static serving with a cache lifetime that matches an immutable filename.

`ProductSummaryDto.coverImageUrl` has existed since Phase 03 and has been null ever since. This fills it.

---

## Current State

### Backend

**None of the pieces exist.** No `wwwroot`, no `UseStaticFiles`, no `IFormFile` anywhere in the repository, no storage abstraction, no image handling of any kind.

Phase 02 added `Product.CoverImagePath` as a nullable `nvarchar(400)`. Phase 03 projects it into `coverImageUrl`. Phase 04 deliberately leaves it null. That column is the whole of the current state.

### Frontend — already wired, which is the good news

`shared/ui/product-art.ts` prefers `coverImageUrl` and falls back to hash-generated SVG artwork when it is absent **or when the image fails to load**. `shared/ui/manga-cover.ts` draws that artwork deterministically from the title's hash so the grid looks like a coherent set with no assets at all. It is placeholder art and is meant to be replaced.

The sample data currently points at freely-licensed Unsplash photography, chosen from two pools by `hash(slug)` in `in-memory/product-images.ts`.

**One detail that matters.** `app.config.ts` registers an `IMAGE_LOADER` that appends `?w={width}&q=70&auto=format&fit=crop` — **but only for `images.unsplash.com` URLs**, passing everything else through untouched. So a URL served by this API arrives at `NgOptimizedImage` unmodified. Serving multiple widths requires the loader to be taught about the new host; without that change, `srcset` does nothing.

Cards render at roughly **200×300 CSS pixels**.

---

## Scope

| Component | Files |
|---|---|
| Application | `Common/Media/IMediaStorage`, `StoredMedia`, `MediaOptions`; `Features/Admin/Media/` — `IProductMediaService` / implementation |
| Infrastructure | `LocalFileMediaStorage`, `ImageContentValidator` |
| API | Two actions on `CatalogController`; static-file middleware in `Program.cs` |

### Out of scope

- **Multiple widths and `srcset`.** See "The multi-width question" — it needs a dependency decision that is not this plan's to make.
- **Cloud object storage.** `IMediaStorage` is the seam; a blob implementation is a class, not a redesign.
- **Image editing.** No cropping, rotating or filtering.
- **Media for anything but products.** No brand logos — the frontend renders brand names as text and draws its own artwork, and nothing in the data should imply a partnership.

---

## Database Changes

**None.** `Product.CoverImagePath` already exists.

It stores a **relative** path such as `covers/9f2a…c1.webp`, never an absolute URL and never a filesystem path. The host is a deployment concern and belongs in configuration; baking `https://mangastore.runasp.net` into a row means every stored value is wrong the day the domain changes.

`ProductSummaryDto.coverImageUrl` is composed at projection time from `MediaOptions.PublicBaseUrl` plus the stored path.

---

## API Contract

Two actions on `CatalogController`, each carrying its own `[Authorize(Roles = Roles.Admin)]`, per the per-action rule Phase 03 established and Phase 13 relies on.

### `POST /catalog/products/{id}/cover`

| | |
|---|---|
| Auth | `[Authorize(Roles = Roles.Admin)]` |
| Content type | `multipart/form-data` |
| Request | `IFormFile file` |
| Success | `200` `ProductCoverDto { string CoverImageUrl }` |
| Errors | `401`, `403`, `404`, `413` too large, `415` unsupported type, `422` |

Replaces any existing cover. The previous file is deleted after the row is updated — see the ordering rule below.

`200` rather than `201`: the cover is a property of the product, not a new resource with its own identity.

**The response is a URL and nothing else.** No filename, no size, no dimensions, no storage path. A storage path in a response body is a map of the filesystem.

### `DELETE /catalog/products/{id}/cover`

| | |
|---|---|
| Success | `204` |
| Errors | `401`, `403`, `404` |

Clears `CoverImagePath` and deletes the file. A product with no cover is a `204`, not a `404` — same reasoning as Phase 10's toggle.

### Static serving

Not a controller. A second `UseStaticFiles` in `Program.cs`, scoped to its own request path and its own physical root:

```csharp
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaOptions.PhysicalRoot),
    RequestPath = "/media",
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        // Filenames are content hashes, so a given URL's bytes never change.
        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
    },
});
```

`ServeUnknownFileTypes = false` means anything without a known MIME mapping is a 404 rather than served as `application/octet-stream`. Combined with the upload validation, only three extensions ever reach the directory.

Placed **after** `UseCors` and **before** `UseAuthentication`. Media is public; running it through the auth pipeline costs latency for no benefit.

---

## Business Rules

### Validate content, never the extension

The guideline says it plainly: *validate content type and size server-side — never trust the extension.* Neither the filename nor the `Content-Type` header is evidence of anything; both are supplied by the caller.

Three checks, in order, cheapest first:

1. **Size.** Reject above `MediaOptions.MaxBytes` (default 2 MB) with **413**. Set `RequestSizeLimit` on the action too, so an oversized body is rejected before it is buffered.
2. **Magic bytes.** Read the first bytes of the stream and match against a fixed table:

   | Format | Signature |
   |---|---|
   | JPEG | `FF D8 FF` |
   | PNG | `89 50 4E 47 0D 0A 1A 0A` |
   | WebP | `52 49 46 46` … `57 45 42 50` at offset 8 |

   Anything else is **415**. Reset the stream position afterwards.
3. **Extension derived from the signature, never from the upload.** A file whose bytes say PNG is stored as `.png`, whatever it was called.

### SVG is rejected, deliberately

SVG is an image format and a script host. An SVG served from the shop's own origin can carry `<script>` and run with the origin's privileges — a stored cross-site scripting vector wearing a picture's clothes.

There is no safe-enough sanitiser worth maintaining for a product photograph. **Reject it**, with a 415 that says so.

### Content-addressed filenames

The stored name is `SHA-256(bytes)` in lowercase hex plus the signature-derived extension:

```text
covers/9f2a4c81…e3c1.webp
```

Three things follow, and all three are the reason:

- **The same image uploaded twice occupies one file.** Detect the collision, reuse it, do not rewrite.
- **A URL's bytes never change**, which is what makes `immutable` and a one-year `max-age` honest rather than a caching bug waiting to happen.
- **The caller never influences the path.** No path traversal, no `../`, no null byte, no case-collision on a case-insensitive filesystem. The filename is derived entirely from content the server has already validated.

Shard into subdirectories by the first two hex characters (`covers/9f/2a4c81….webp`) if the count is expected to grow past a few thousand — some filesystems slow down badly with very large flat directories.

### Ordering: write the file, then the row, then delete the old file

1. Validate.
2. Write the new file. If it already exists by hash, skip.
3. Update `Product.CoverImagePath` and save.
4. **Then** delete the previous file, if it is no longer referenced by any product.

Getting this backwards — deleting first, or updating the row before the file lands — produces a product whose cover URL 404s. A momentarily orphaned file costs disk; a broken image costs a customer.

Step 4 needs the reference check because deduplication means two products can share a file. Deleting a shared file because one product changed its cover would break the other.

A failure in step 4 is logged, not surfaced. The upload succeeded; an orphan is a cleanup concern.

> There is no orphan sweeper in this phase. Note the gap: files whose row was deleted, or whose cleanup failed, accumulate. A `ScopedBackgroundService` — the base class on the foundation branch, still unused — is the natural home for a weekly sweep, and it should be written when there is enough media to justify it.

### Storage is behind an interface

```csharp
namespace MangaStore.Application.Common.Media;

/// <summary>Stores and removes binary media outside the database.</summary>
public interface IMediaStorage
{
    /// <summary>Stores content under a content-addressed name and returns its relative path.</summary>
    Task<StoredMedia> SaveAsync(Stream content, string extension, string folder, CancellationToken ct = default);

    /// <summary>Removes a stored file. Succeeds when the file is already gone.</summary>
    Task DeleteAsync(string relativePath, CancellationToken ct = default);

    /// <summary>Returns whether a relative path currently exists.</summary>
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
}
```

`LocalFileMediaStorage` writes under `MediaOptions.PhysicalRoot`. It **must** resolve the final path and assert it is inside the root before writing — belt and braces, since content-addressed names cannot escape, but a future caller might pass something else.

> **A container filesystem is ephemeral.** The `Dockerfile` builds from `mcr.microsoft.com/dotnet/aspnet:10.0` with no volume, so uploaded covers vanish on redeploy and are invisible to a second instance. Local storage is correct for development and for a single-instance host with a mounted volume; anything else needs a blob implementation of this interface. Record which one the deployment uses, in the deployment notes, before anyone uploads a cover they care about.

### The multi-width question

The guideline asks, reasonably, for a couple of widths so the client can use `srcset`, since cards render at about 200×300.

Doing it needs an imaging library, and that is a decision this phase should surface rather than take:

| Option | Cost |
|---|---|
| **Single stored image, no resizing** *(recommended for now)* | No dependency. The browser downscales. A 2 MB upload is 2 MB on every card — mitigated by an upload guideline and by `max-age` |
| `SixLabors.ImageSharp` | Excellent API. **Licensing needs checking**: the Six Labors Split License is free for open source and small organisations and commercial otherwise. That is a business decision, not an engineering one |
| `SkiaSharp` | Permissively licensed. Heavier native dependency; needs the right runtime package per platform, including inside the container |
| An image CDN | Resizing becomes someone else's problem and the `IMAGE_LOADER` already speaks that dialect. Adds a vendor and a cost |

**Ship the single-image version.** It is complete and correct, and it makes `coverImageUrl` real, which is the point. Record the four options in the PR so the follow-up is a decision rather than a rediscovery.

Whichever is chosen later, the `IMAGE_LOADER` in `app.config.ts` must be taught about the new host — it currently rewrites `images.unsplash.com` only and passes everything else through untouched, so `srcset` on an API-served URL would be inert.

### Recommended upload guidance

Not enforceable without an imaging library, so document it for administrators rather than validating it: 600×900 (2× the card), JPEG or WebP, under 300 KB. The 2 MB ceiling is a limit, not a target.

---

## Security

File upload is the classic remote-code-execution vector. Every rule below exists because of a specific attack.

| Concern | This phase |
|---|---|
| Authentication | Both actions require a bearer token. |
| Authorization | `[Authorize(Roles = Roles.Admin)]` per action. Customers cannot upload. |
| Validation | Size, then magic bytes, then a server-derived extension. |
| Sensitive data | None stored. The response carries a URL only. |
| Concurrency | Content addressing makes concurrent uploads of the same image idempotent. |
| Rate limiting | The global policy. Consider a tighter per-admin policy if uploads ever become customer-facing — they must not. |

### The attacks and the answers

| Attack | Defence |
|---|---|
| Upload `shell.aspx` named `cover.png` | Magic bytes; extension derived from content; the media root is not the application root and is served by `PhysicalFileProvider` with `ServeUnknownFileTypes = false`, so nothing there is ever executed |
| Path traversal via the filename | The filename is a SHA-256 of the content. The upload's name is read for logging and never used in a path |
| Stored XSS via SVG | SVG rejected outright |
| Polyglot file — valid PNG header plus script payload | The bytes are served as `image/png` with `nosniff`, never parsed, never executed. A polyglot is only dangerous if something interprets it, and nothing here does |
| Zip bomb / decompression bomb | Not reachable without an imaging library. **When one is added, this becomes live** — a 4 KB PNG can decompress to gigabytes. Whatever library is chosen must have a dimension limit applied before decode |
| Disk exhaustion | 2 MB cap, `RequestSizeLimit`, admin-only, and deduplication by hash |
| Serving user content from the app origin | A real residual risk. Serving media from a separate origin is the proper fix; `nosniff` plus a strict format whitelist is the mitigation available today. Record it |

### Two things not to do

- **Do not put the media root inside `wwwroot`** or anywhere under the application's content root. A separate directory, configured, ideally on a separate volume.
- **Do not enable directory browsing.** `UseStaticFiles` does not, and `UseDirectoryBrowser` must never be added.

---

## Frontend Contract

**No frontend change is required.** This is the phase's best property.

`shared/ui/product-art.ts` already prefers `coverImageUrl` and falls back to generated artwork when it is absent or fails to load. Products with a cover show a photograph; products without keep the SVG. Both states already render, and both are already tested on the client.

The guideline puts it well: pointing the data at real product images is a change to the data, not to any component.

Two follow-ups, neither blocking:

1. **The `IMAGE_LOADER`** rewrites `images.unsplash.com` only. Nothing breaks — other URLs pass through untouched — but width parameters are not appended to API-served images. Only matters once multiple widths exist.
2. **The sample Unsplash URLs** in `product-images.ts` are freely licensed placeholders. They disappear with the rest of the in-memory layer when `CatalogService` is swapped.

---

## Testing

### Unit tests

| Test | Asserts |
|---|---|
| `ImageContentValidatorTests.AcceptsJpegPngWebpBySignature` | One fixture per format, all named `.txt`, all accepted. |
| `ImageContentValidatorTests.RejectsSvg` | 415, whatever the extension. |
| `ImageContentValidatorTests.RejectsExecutableRenamedAsPng` | An MZ header called `cover.png` → 415. **The core test.** |
| `ImageContentValidatorTests.RejectsEmptyFile` | |
| `ImageContentValidatorTests.RejectsTruncatedSignature` | A two-byte file does not throw. |
| `ImageContentValidatorTests.ExtensionComesFromSignatureNotFilename` | A JPEG called `x.png` is stored `.jpg`. |
| `ImageContentValidatorTests.ResetsStreamPosition` | The caller can still read the content. |
| `MediaStorageTests.SameContentProducesSamePath` | Deduplication. |
| `MediaStorageTests.DifferentContentProducesDifferentPath` | |
| `MediaStorageTests.PathIsAlwaysInsideRoot` | Including if a crafted extension is passed. |
| `MediaStorageTests.DeleteMissingFile_Succeeds` | Idempotent. |
| `ProductMediaServiceTests.Upload_WritesFileBeforeUpdatingRow` | Call ordering. |
| `ProductMediaServiceTests.Upload_DeletesPreviousFileAfterRowUpdate` | And only after. |
| `ProductMediaServiceTests.Upload_SharedFile_IsNotDeletedForOtherProduct` | The deduplication hazard. |
| `ProductMediaServiceTests.Upload_CleanupFailure_StillSucceeds` | An orphan is logged, not surfaced. |
| `ProductMediaServiceTests.Delete_NoCover_Returns204` | |
| `ProductMediaServiceTests.Upload_UnknownProduct_Returns404` | And writes no file. |

### Integration tests

| Test | Asserts |
|---|---|
| `MediaApiTests.Upload_RequiresAdmin` | Anonymous 401, Customer **403**, Admin 200. |
| `MediaApiTests.Delete_RequiresAdmin` | |
| `MediaApiTests.UploadedCoverAppearsInPublicCatalogue` | `coverImageUrl` populated on `GET /catalog/products/{slug}`. |
| `MediaApiTests.UploadedFileIsServedAtItsUrl` | `GET` the returned URL → 200 with the right `Content-Type`. |
| `MediaApiTests.ServedFileCarriesImmutableCacheAndNosniff` | Both headers. |
| `MediaApiTests.MediaPathIsAnonymous` | No token, 200. |
| `MediaApiTests.UnknownMediaPath_Returns404` | Including `/media/../appsettings.json`, which must not resolve. |
| `MediaApiTests.OversizedUpload_Returns413` | |
| `MediaApiTests.SvgUpload_Returns415` | |
| `MediaApiTests.ResponseContainsNoFilesystemPath` | Only a relative URL. |

### Edge cases

- Uploading the same image to two products: one file, two rows, deleting one cover leaves the other's image intact.
- Uploading a cover to a product that is then soft-deleted: the file stays. Deletion is soft and reactivation should restore the cover.
- A `multipart` request with no file part: 422.
- A `multipart` request with two file parts: use the first, or reject — reject, and say which. Ambiguity in an upload is not worth guessing at.
- A filename containing `../` or a null byte: irrelevant by construction, and worth a test that says so.
- The media root not existing at start-up: create it, or fail fast at start-up with a clear message. Do not fail at the first upload.

---

## Acceptance Criteria

- [ ] `IMediaStorage` and `MediaOptions` in Application; `LocalFileMediaStorage` in Infrastructure with a path-containment assertion.
- [ ] `POST` and `DELETE /catalog/products/{id}/cover`, each with its own `[Authorize(Roles = Roles.Admin)]`; `CatalogController` still has no class-level attribute.
- [ ] Validation order: size (413), magic bytes (415), extension derived from the signature.
- [ ] **SVG rejected.**
- [ ] Filenames are `SHA-256(content)` plus a signature-derived extension; identical content deduplicates.
- [ ] `Product.CoverImagePath` stores a **relative** path; the public URL is composed from `MediaOptions.PublicBaseUrl`.
- [ ] Ordering: write file → update row → delete previous file only if unreferenced; cleanup failure is logged, not surfaced.
- [ ] Static serving at `/media` from a `PhysicalFileProvider` outside the content root, with `ServeUnknownFileTypes = false`, `Cache-Control: public, max-age=31536000, immutable` and `X-Content-Type-Options: nosniff`, placed before `UseAuthentication`.
- [ ] No directory browsing; nothing under the media root is executable.
- [ ] The response body carries a URL only — no filesystem path, no original filename.
- [ ] `RequestSizeLimit` on the upload action, matching `MediaOptions.MaxBytes`.
- [ ] The container-ephemerality warning and the multi-width options are recorded in the PR.
- [ ] The orphan-sweeper gap is recorded.
- [ ] No frontend change was required; a product with a cover shows a photograph and one without keeps the generated artwork.
- [ ] `dotnet build -p:TreatWarningsAsErrors=true` succeeds; `dotnet test` green.

---

## Dependencies

```text
Depends on:
  Phase 02 - Product.CoverImagePath.
  Phase 03 - the projection that composes coverImageUrl.
  Phase 13 - the per-action admin authorization pattern on CatalogController.

Blocks:
  Nothing.

Can be implemented independently:
  Mostly. It needs Phases 02 and 03 for the column and the projection.
  It does not need Phase 13, though sharing that phase's authorization
  pattern is why it is sequenced after it. Good candidate for parallel work.
```
