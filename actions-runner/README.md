# Self-Hosted Runner

This directory intentionally tracks only the helper scripts and setup notes for the local GitHub Actions runner.

Do not commit runner machine state. The extracted runner payload, registration files, logs, and generated helper files stay local and are ignored by [actions-runner/.gitignore](./.gitignore).

## Workflow Expectations

The release workflow at [`.github/workflows/release.yml`](../.github/workflows/release.yml) expects:

- a GitHub Actions runner registered to this repository
- a Windows self-hosted runner with the `self-hosted` and `Windows` labels
- repository secrets `PDX_EMAIL` and `PDX_PASSWORD`
- the following environment variables available to the runner process:
  - `CSII_TOOLPATH`
  - `CSII_MANAGEDPATH`
  - `CSII_USERDATAPATH`
  - `CSII_LOCALMODSPATH`
  - `CSII_UNITYMODPROJECTPATH`
  - `CSII_MODPOSTPROCESSORPATH`
  - `CSII_MODPUBLISHERPATH`
  - `CSII_MSCORLIBPATH`
  - `CSII_ENTITIESVERSION`

## Setup

1. Download the Windows x64 GitHub Actions runner from GitHub and extract it into this `actions-runner` directory.
2. Open Command Prompt in this directory.
3. Register the runner against this repository:

```bat
config.cmd --url https://github.com/FennexFox/NoOfficeDemandFix --token <runner-registration-token> --labels self-hosted,Windows
```

4. Start the runner interactively:

```bat
run.cmd
```

5. Set the required `CSII_*` environment variables where the runner process can read them before triggering the workflow.
6. Add `PDX_EMAIL` and `PDX_PASSWORD` as repository secrets in GitHub.

## Cleanup

To remove the runner registration from this machine:

```bat
config.cmd remove --token <runner-registration-token>
```

Generated files such as `.credentials`, `.runner`, `_diag/`, `bin/`, `externals/`, and `run-helper.cmd` should remain local.
