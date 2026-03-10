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
- `site/.lunet/css/site-overrides.css`: project-specific style overrides

## Scripts

- `./build-docs.sh`
- `./build-docs.ps1`
- `./check-docs.sh`
- `./serve-docs.sh`
- `./serve-docs.ps1`

## Output

Generated output is written to `site/.lunet/build/www`.

## Workflows

- CI validates docs builds on pull requests and branch pushes.
- Docs workflow publishes the site to GitHub Pages from the built Lunet output.
