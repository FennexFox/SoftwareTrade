# Local Runner Notes

This repository no longer requires a self-hosted GitHub Actions runner for releases.

Releases are prepared locally with [../scripts/release.ps1](../scripts/release.ps1), and [../.github/workflows/release.yml](../.github/workflows/release.yml) now runs on GitHub-hosted infrastructure only to create a GitHub Release from a pushed `v*` tag.

This directory is kept only so any local `actions-runner` checkout on this machine stays ignored by [actions-runner/.gitignore](./.gitignore). If you still experiment with a local runner here, do not commit runner machine state such as `.credentials`, `.runner`, `_diag/`, `bin/`, `externals/`, or generated helper files.
