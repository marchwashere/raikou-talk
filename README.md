# Raikou Talk

Static HGSS Raikou manipulation reader. Choose Q2, Q3, Q4, or Full HP, select the observed follower response, and follow the planned actions. The reader only stops for response choices that change the remaining route.

Seed **100A0D2A** · possible B1F arrival advances **3146–3359** · target **3484**. HP bands for 65 maximum HP: Q2 **17–32**, Q3 **33–48**, Q4 **49–64**, Full **65**.

Static enlarged emotes, compact turn sequences, Back/Restart, response filtering, and all matching original starting advances once the finishing route is known. Arrival movement affects the model but is omitted from the instructions. Natural final Left turns and BONK finishes retain the original strategy behavior.

## Deploy on Debian with Caddy

The checked-in `public/` directory is ready to serve. No build, Node.js, .NET, or application process is needed on the server. Opening the HTML directly with `file://` does not work; serve it over HTTP(S).

This example assumes Caddy is already installed as a system service. For this private repository, first add a read-only [GitHub deploy key](https://docs.github.com/en/authentication/connecting-to-github-with-ssh/managing-deploy-keys) for your server, or authenticate Git using an account with repository access.

Run as the server user that will pull updates:

```bash
sudo apt install -y git
sudo install -d -m 755 -o "$(id -un)" -g "$(id -gn)" /srv/raikou-talk
git clone https://github.com/marchwashere/raikou-talk.git /srv/raikou-talk
chmod -R a+rX /srv/raikou-talk/public
```

The HTTPS clone above requires HTTPS credentials for a private repository. If you configured an SSH deploy key, use `git@github.com:marchwashere/raikou-talk.git` instead (or your configured SSH hostname alias).

Add this domain block to `/etc/caddy/Caddyfile`, replacing `your-domain.com`:

```caddyfile
your-domain.com {
    root * /srv/raikou-talk/public
    encode zstd gzip
    header Cache-Control "no-cache"
    file_server
}
```

Serve only `public/`, not the repository root. Point the domain's DNS at the server and allow inbound TCP ports 80 and 443. The site opens at `https://your-domain.com/`, with no extra URL path. Caddy handles HTTPS automatically.

```bash
sudo caddy validate --config /etc/caddy/Caddyfile &&
sudo systemctl reload caddy
```

For future updates:

```bash
git -C /srv/raikou-talk pull --ff-only
```

File updates do not require a Caddy reload. Keep older content-hashed `public/data/*.json` files when updating so existing browser sessions can finish. Caddy references: [file server](https://caddyserver.com/docs/caddyfile/directives/file_server), [Linux service](https://caddyserver.com/docs/running#using-the-service), [HTTPS](https://caddyserver.com/docs/automatic-https).

## Source layout

- `src/`: browser UI and state navigation source.
- `public/`: generated, ready-to-host website, including strategy trees and static emotes.
- `ExportWeb.cs`: generates browser screens through the shared native reader and checks parity.
- `engine/`: C# simulation, response catalog, strategy reader, and embedded game data required by the exporter.
- `data/`: four original strategy exports and their pinned SHA-256 hashes.
- `build.ps1`: complete standalone Windows build and deployable ZIP packaging.
- `test-state.mjs`, `test-browser.cjs`: exhaustive tree checks and browser interaction tests.

No dependency on the original development checkout, game ROM, or save file is required. The browser follows precomputed response trees rather than running a new search. Each visitor has an independent in-memory run; refreshing resets to band selection. All site assets are local, with no third-party scripts or network API.

## Build and test

The full exporter build uses the Windows .NET Framework 4.x compiler and System.Drawing. From this repository's root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File build.ps1
node test-state.mjs
node test-browser.cjs
```

State tests use only Node.js built-ins. Browser tests additionally require Playwright and Microsoft Edge; `RAIKOU_PLAYWRIGHT` can point to an installed Playwright package directory. Browser testing dependencies are for development only.

The build creates `dist/Raikou Manip Web.zip`. Generated verification reports go into `verification/`. All **856 band/start combinations** and **821 screens** are checked against the shared reader. Browser tests cover all bands, finishing origins, BONK, Back/Restart, filtering, keyboard controls, mobile layout, root/subpath serving, failed requests, retry, and stale-load protection.

After changing the source, rebuild and commit the updated `public/` files as well so a server pull deploys the changes.
