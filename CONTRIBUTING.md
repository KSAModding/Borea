# Contributing

Thank you for helping improve Borea.

## Where to start

- Report a reproducible bug through the [issue tracker](https://github.com/KSAModding/Borea/issues).
- Discuss a larger feature or a change to the content-manager specification in [content-manager-design Discussions](https://github.com/KSAModding/content-manager-design/discussions).
- Report a security problem through the private path in [SECURITY.md](SECURITY.md).

## Pull requests

Keep each pull request focused on one problem.
Explain the user-visible change, the tests you ran, and any work that remains outside its scope.

Run the relevant tests and formatting checks before you open a pull request.
The continuous-integration checks run on Windows, Linux, and macOS.

## AI tools

You can use AI tools, such as code assistants and chat models, for code, tests and documentation.

- **You are responsible for your pull request.** You understand the code that you submit, you can explain why it is there, and you work through the review.
- **You can say which tools you used.** It is not required, but one line in the pull request helps the review, for example `AI tools: Claude for the tests, reviewed and tested by me.`
- **Test what you submit.** Run the tests, and for a change to the App or the CLI, try the change itself.
- **Agree on large changes first.** A pull request that changes more than about 1,500 lines, not counting lock files and test fixtures, needs an issue that a maintainer agreed to, or a reason for its size in the pull request.

Maintainers close a pull request without a review when its author does not respond to the review within 14 days, when it shows that nobody ran or read it, such as a build that fails or a description that does not match the change, or when it comes from an account that opens automated pull requests across unrelated projects.
Maintainers can stop reviewing pull requests from an account that does this again.

This section covers contributions to this repository.
Mods in the content index are judged by what they claim and what they do, not by how they were made, and a problem with a listed mod goes through the [takedown and dispute policy](https://github.com/KSAModding/content-index/blob/main/POLICY.md).

## Release tags

A release starts from a tag such as `v0.5.0`, and a tag with a hyphen is published as a GitHub pre-release.
Name a testing build `-beta.N`, for example `v0.5.0-beta.1`, and a dev build `-dev.N`, because the update channel in the App reads that name.
The testing channel reports beta releases, and only the dev channel reports other pre-releases.

## Release scripts

The `release scripts` job tests the scripts in `.github/scripts`. Locally they need Python 3, and bash with jq:

```sh
python3 -m unittest discover --start-directory .github/scripts/tests --pattern "test_*.py"
bash .github/scripts/tests/test-release-announcement.sh
```

## Discord release post

The `announce` job in `release.yml` posts each new release to Discord, through a webhook or a bot.
Add the secrets and variables in the repository settings under Secrets and variables, Actions.
When neither mode is configured, the job posts nothing and passes.

| Mode | Secret | Variable | Discord setup |
|---|---|---|---|
| Webhook | `DISCORD_RELEASE_WEBHOOK_URL` | none | A webhook for the release channel. |
| Bot | `DISCORD_RELEASE_BOT_TOKEN` | `DISCORD_RELEASE_CHANNEL_ID` | The bot is on the server and has `VIEW_CHANNEL` and `SEND_MESSAGES` in the release channel. |

When both modes are configured, the bot posts, and the run shows a warning that the webhook is not used.

Use a small bot only for this post, not a bot with broad rights.
The token gives the workflow everything that the bot can do on the server.

To notify a role, set the variable `DISCORD_RELEASE_ROLE_ID`.
The message then starts with the role mention, and it can notify only that role.
A pre-release does not notify the role.
For the bot, give it the `MENTION_EVERYONE` permission ("Mention @everyone, @here, and All Roles") in the release channel, so the role can stay not mentionable.
For the webhook, make the role mentionable.

Channel and role ids are digits only.
A bot token without a valid channel id, or a role id that is not valid, fails the job.
When the post fails, the log shows the error code and message from Discord.

A post can time out after Discord saved it.
Before you re-run `announce`, make sure that the message is not already in the channel, or it is posted twice.

Changes to the content-manager format or snapshot contract must follow the accepted RFCs in [content-manager-design](https://github.com/KSAModding/content-manager-design).
When an RFC does not answer the question, start a design discussion before implementing a private format extension.

## Licensing your contribution

By opening a pull request, you contribute your code and documentation under the repository's [MIT License](LICENSE).
