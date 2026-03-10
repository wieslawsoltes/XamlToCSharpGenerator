---
title: "Lunet Docs Pipeline"
---

# Lunet Docs Pipeline

This repository uses Lunet for the documentation site.

## Structure

- `site/config.scriban`: site config and API-doc generation
- `site/menu.yml`: top-level navigation
- `site/articles/**`: structured documentation content
- `site/images/logo.svg`: site branding asset
- `site/.lunet/includes/_builtins/bundle.sbn-html`: stable bundle injection for API pages
- `site/.lunet/css/template-main.css`: precompiled template stylesheet
- `site/.lunet/css/site-overrides.css`: project-specific style overrides

## Scripts

- `./build-docs.sh`
- `./build-docs.ps1`
- `./check-docs.sh`
- `./serve-docs.sh`
- `./serve-docs.ps1`

## Output

Generated output is written to `site/.lunet/build/www`.

## Styling pipeline note

Lunet `1.0.10` on macOS 15 has a Dart Sass platform detection issue.
To keep the full template visual quality:

- docs pages are assigned `bundle: "lite"` via `with attributes`
- a local `/_builtins/bundle.sbn-html` override resolves bundle links safely
- `template-main.css` is precompiled and committed, then loaded by the `lite` bundle

To refresh `template-main.css` locally after template updates:

```bash
npx --yes sass --no-source-map --style=expanded \
  --load-path site/.lunet/build/cache/.lunet/resources/npm/bootstrap/5.3.8/scss \
  --load-path site/.lunet/build/cache/.lunet/resources/npm/bootstrap-icons/1.13.1/font \
  site/.lunet/build/cache/.lunet/extends/github/lunet-io/templates/main/dist/.lunet/css/main.scss \
  site/.lunet/css/template-main.css
```

## Workflows

- CI validates docs builds on pull requests and branch pushes.
- Docs workflow publishes the site to GitHub Pages from the built Lunet output.
