# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

ClassicBlog — a small blog demo built with **ASP.NET Core Blazor Server (.NET 10)**, **EF Core + SQLite**, skinned with **BOOTSTRA.386** (the kristopolous/BOOTSTRA.386 theme). No auth, no tests, no lint pipeline.

## Commands

```powershell
dotnet run          # run (creates blog.db + seeds on first launch)
dotnet watch        # hot-reload dev
dotnet build        # build
```

App listens on the URLs in `Properties/launchSettings.json`. The HTTPS dev certificate is **not trusted** by default — run `dotnet dev-certs https --trust` once if a browser warns, or use the HTTP port.

Default seeded admin: **`admin` / `admin`** (change it after first login via `/account/change-password`).

## Architecture

### Render mode
Blazor **interactive Server** render mode (components run on the server, UI updates pushed over SignalR). `Program.cs` registers `AddRazorComponents().AddInteractiveServerComponents()` and maps them with `AddInteractiveServerRenderMode()`. Pages prerender on the server, so an HTTP `GET /` returns fully-rendered HTML (useful when verifying with `curl`).

### Static assets
.NET 10's `MapStaticAssets()` / `@Assets[...]` fingerprinting is used. Assets live under `wwwroot/` and are referenced through `@Assets["..."]` in `Components/App.razor` (not raw paths). When adding files under `wwwroot/lib/...`, keep their relative directory structure intact — the bootstrap.386 CSS resolves fonts via relative `url(...)` (`../fonts/` for glyphicons, `fonts/` for Fixedsys), so moving them breaks the font load.

### Database (EF Core + SQLite)
- Connection string `ConnectionStrings:Default` = `Data Source=blog.db` (file is created in the project/content root at runtime; gitignored).
- **No migrations.** DB initialization uses `Database.EnsureCreatedAsync()` plus `BlogDbContext.EnsureSeedDataAsync()` at startup (see `Program.cs`). `EnsureSeedDataAsync` only seeds when `Posts` is empty. To change schema, either drop `blog.db` (it rebuilds on next run) or switch to `dotnet-ef` migrations (`dotnet tool install -g dotnet-ef` → `migrations add` → replace `EnsureCreated` with `Database.Migrate`).
- The schema includes `Posts`, `Comments`, and `Users` (see `ApplicationUser`). `EnsureCreated` will NOT add new tables to an existing `blog.db` — delete the file after schema changes so it rebuilds.
- `Post.MakeSlug(title)` derives the slug; `Slug` has a unique index. Post content and comments are stored as **Markdown** and rendered via `MarkdownRenderer.ToHtml` (see below).

### bootstrap.386 is Bootstrap **3**, not 5
The theme is the v3.3.2 themed build checked into `wwwroot/lib/bootstrap-386/`. Layout and page markup must use **Bootstrap 3** class names (e.g. `panel panel-default`, `list-group`, `navbar navbar-default`, `navbar-toggle`, `col-md-*`, `label label-success`), not Bootstrap 5. The original template's B5 sidebar layout was replaced — don't reintroduce B5-only classes (e.g. `ps-3`, `d-flex`, `nav flex-column`) without checking B3 support.

Script load order in `App.razor` matters: **jQuery → `bootstrap.min.js` → inline `window._386 = {...}` config → `386.js` → `blazor.web.js`**. B3 components depend on jQuery; `386.js` (the boot/cursor-sweep animation) depends on jQuery and reads the `_386` config set just before it. The dev cert warning and the 386 boot overlay are expected, not bugs.

### Authentication & authorization (cookie auth, no Identity, no self-registration)
A lightweight custom auth layer — **not** ASP.NET Core Identity. No NuGet auth packages; passwords are hashed with the framework's `PasswordHasher<ApplicationUser>` (PBKDF2) via `AccountService`.

- **Cookie auth** is wired in `Program.cs`: `AddAuthentication`/`AddCookie("Cookie")` with `LoginPath=/login`, `AccessDeniedPath=/access-denied`. **All four default schemes** (Default/Challenge/SignIn/SignOut) are set explicitly to `"Cookie"` — do not revert to `AddAuthentication("Cookie")` (single-arg), which leaves `DefaultChallengeScheme` null and the `[Authorize]` HTTP challenge throws.
- Blazor Server bridges the HTTP cookie into the interactive circuit via `AddCascadingAuthenticationState()` + `ServerAuthenticationStateProvider` + `AddHttpContextAccessor()`. `[Authorize]` on a `@page` is enforced **at the HTTP level during prerender** (cookie challenge → 302 to `/login?ReturnUrl=...`); `AuthorizeRouteView` in `Routes.razor` handles in-circuit navigation (`RedirectToLogin` force-loads, `AccessDenied` for wrong role).
- **Login/logout are minimal-API endpoints** (`POST /account/login`, `POST /account/logout`) because cookies can only be issued/cleared on an HTTP response, not from an interactive Blazor circuit. They are plain HTML `<form method="post">`s (in `Login.razor` and the nav bar) containing `<AntiforgeryToken />`. The endpoints call `IAntiforgery.ValidateRequestAsync` manually (minimal APIs don't auto-validate). The antiforgery token is **claims-bound** — a token rendered for an anonymous user won't validate after login, and vice versa; always submit the token from the current page.
- **Roles:** `Roles.Admin` and `Roles.Author` (constants in `Models/Roles.cs`), stored as a string on `ApplicationUser.Role` and issued as `ClaimTypes.Role`. Both roles can write posts (`[Authorize]` on `Manage`/`PostEditor`); only Admin can manage users (`[Authorize(Roles = Roles.Admin)]` on `ManageUsers`, gated by an `"Admin"` policy). `AuthorizeView`/`AuthorizeView Roles` in `NavMenu.razor` toggles the Manage/Users links, the logout form, and the Sign-in link.
- **No self-registration.** Users are created by an admin on `/admin/users` (create with role, change role inline, reset password, delete; self-delete is blocked). Users change their own password on `/account/change-password` (verifies current password).
- **Bootstrap admin is seeded** on first run: username `admin`, password `admin` (idempotent — only if `admin` doesn't exist). **Change it immediately after first login.** Subsequent users are admin-created.
- Pipeline order in `Program.cs` matters: `UseAuthentication` → `UseAuthorization` → `UseAntiforgery` → `MapRazorComponents`.

### Comments, threaded replies & email notifications (MailKit)
- `Comment` has a required `Email` (`[EmailAddress]`) and a nullable `ParentCommentId` for threaded replies (self-referential FK, `DeleteBehavior.Restrict`). `Post` has a nullable `AuthorId` → `ApplicationUser` (`DeleteBehavior.SetNull`), set to the creating user in `PostEditor`. Seeded posts have no author; the sample Welcome post is attributed to the seeded admin.
- Comments are rendered as a tree by the recursive `Components/CommentNode.razor` (renders a comment + its children, indented by `Depth * 24px`). `PostDetail` builds `roots` (ParentCommentId == null) and a `Dictionary<int, List<Comment>> ChildrenByParent` (keyed by parent id — never null) and passes them down. "Reply" sets a `replyTarget`; the single bottom form submits with that `ParentCommentId` (or null for top-level).
- Comment creation + notifications go through `CommentService.AddCommentAsync` (called from `PostDetail`): saves the comment, then **best-effort** email notifications via `IEmailService` — a new comment notifies the post's `Author.Email`; a reply *also* notifies the parent comment's `Email`. Self-notifications (commenter == recipient) and duplicates (parent commenter == post author) are skipped.
- `EmailService` (MailKit, `Services/EmailService.cs`) is **best-effort**: exceptions are logged, never thrown, so a comment post can't fail on SMTP. When `Email:Enabled=false` (the default), it logs the would-be email instead of sending — use that to confirm notifications fire in dev. Configure real SMTP in the `Email` section of `appsettings.json` (`Host`, `Port`, `UseSsl` — true=implicit TLS/465, false=STARTTLS/587 — `Username`, `Password`, `From`).
- `ApplicationUser.Email` is optional and managed by admins on `/admin/users` (editable inline column + create-form field). The seeded admin gets `admin@example.com` for demo notifications.
- Schema changes (Email/AuthorId/ParentCommentId columns) require deleting `blog.db` so `EnsureCreated` rebuilds — it will not alter an existing DB.

### Markdown rendering (best-effort, dependency-free)
`Markdown/MarkdownRenderer.cs` is a self-contained, **safe-by-construction** Markdown→HTML converter used for post bodies, comments, and the editor preview. It is NOT a full CommonMark/GFM implementation — it covers a subset (headings, paragraphs w/ soft-break `<br>`, blockquotes, ordered/unordered lists, fenced code, hr, inline code, images, links, bold, italic, strikethrough). When extending it:
- All text is HTML-escaped (`Escape`); only whitelisted tags are emitted, so user Markdown (including anonymous comments) cannot inject raw HTML/script. Keep it that way — never emit user text without escaping.
- Link/image URLs pass through `SafeLinkUrl`/`SafeImageUrl` (allow http/https/mailto and relative; block `javascript:`, `vbscript:`, `data:`).
- Underscore-italics are deliberately omitted to avoid mangling `snake_case`; italic uses `*` only.
- Inline code spans are extracted to `@@CODE<n>@@` placeholders before escaping/inline-processing, then restored — preserve this ordering if you touch `Inline`.
- Render output with `@((MarkupString)MarkdownRenderer.ToHtml(value))`. The editor (`PostEditor.razor`) keeps a live preview via an `@oninput` handler on the `InputTextArea` updating `previewText` (binding still uses `@bind-Value` for validation).

### Blazor NavLink quirk with B3
`NavLink` adds the `active` class to the rendered `<a>`, but Bootstrap 3 expects `active` on the parent `<li>`. `wwwroot/app.css` mirrors the B3 active style onto `.navbar-nav > li > a.active` — keep that rule when editing the nav.

## Conventions

- Data models in `Models/` (`Post`, `Comment`); EF context in `Data/BlogDbContext.cs`. Razor `_Imports.razor` already imports `ClassicBlog.Data`, `ClassicBlog.Models`, `Microsoft.EntityFrameworkCore`, and `System.ComponentModel.DataAnnotations` — new pages/components don't need to re-add these.
- Post CRUD lives in `Components/Pages/`: `Home` (list, published only), `PostDetail` (detail + comments, route `/post/{id:int}`), `Manage` (table + delete via `@onclick`), `PostEditor` (create at `/manage/new` and edit at `/manage/{id:int}/edit`, sharing one component via two `@page` directives). An edit-form DTO (`PostEditModel`) keeps validation separate from the entity.
- Blazor Server `@onclick` handlers run over SignalR and don't need antiforgery tokens; `EditForm` (form POST) is covered by `app.UseAntiforgery()`.
