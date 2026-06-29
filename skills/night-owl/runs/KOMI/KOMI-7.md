# KOMI-7: Crypto Keeper Audit - Update 1

- Outcome: Added and installed an authenticated Jira attachment downloader using the standard library and Jira REST API v3.
- Verification: Downloader self-test, full Night Owl self-test, Python compilation, shell syntax, deployment hash comparison, and secret scan passed.
- Resume: Add `ATLASSIAN_SITE_URL`, `ATLASSIAN_EMAIL`, and `ATLASSIAN_API_TOKEN` to the mode-`0600` Night Owl config, return the issue to To Do, then download and audit attachment `10000`.
- Safety: Credentials stay in private config and out of arguments/logs; downloads refuse plaintext HTTP, unsafe filenames, and overwrites.
- Jira: https://komichris.atlassian.net/browse/KOMI-7
