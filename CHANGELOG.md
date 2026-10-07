# Changelog

Each release needs a `## <version>` section matching the version in `package.json`.
`publish.sh` uses that section as the release commit message and tag message, and refuses to
publish if it's missing. Rename `Unreleased` to the new version when you bump it.

## 0.2.3

- Add a "Static Pose" option on gestures to emphasize that hand shape matters more than position and movement.
- Fix model download failing when the project has no `Assets/StreamingAssets` folder.
- Data recording scene: the `REC` label and progress wheel turn red while recording.
- Data recording scene: going back to the gesture list now clears the playing sample and the selection.
- Failed API requests now log the server's error message (for example, an invalid or expired API key).
