# Actor Morpher

Dalamud plugin prototype for locally changing visible actors into selected NPC, demihuman, or monster forms.

## Install

Add this URL to Dalamud's custom plugin repositories:

```text
https://raw.githubusercontent.com/miqote69/ActorMorpher/main/repo.json
```

## Use

- Command: `/actormorpher`
- Alias: `/amorph`

## Current scope

This first project shell lists visible actors and `ModelChara` rows. The actual model replacement path is intentionally left as the next step, after validating the target actor and form data in-game.

## Notes

This is a cosmetic client-side plugin prototype. It does not automate gameplay, send network requests, or collect player data.
