# Gptnt Plays: Keep Talking and Nobody Explodes
This mod adds control into Keep Talking and Nobody Explodes through HTTP requests, based on the work from the TwitchPlays mod.

## Latest Build
If you want to install the mod, you can always get the latest build from [here](https://github.com/GPTNT/gptntPlays/tree/main/build).

## Endpoints
### /bombinfo
returns information about the current bomb such as time left, number of strikes, solvable modules...
### /startmission
eg: `http://localhost:8085/startMission?seed=1&timeLimit=300&numStrikes=3&needyTime=90&isFront=true&optWidgets=5&components=Venn&timeScale=1.0&timeStepSize=250` \
starts a mission with the specified details including
- seed
- timeLimit
- numStrikes
- needyTime
- isFront
- optWidgets
- components
- timeScale
- timeStepSize
### /action
eg: `http://localhost:8085/action?action=click&x_pos=0.5&y_pos=0.6` \
The action endpoint specifies what type of action to send to the game alongside any necessary arguments \
actions:
(rotation)
- left
- right
- up
- down
- flip
(mouse actions)
- click - needs a x_pos and y_pos
- hold - needs a x_pos and y_pos
- release
- out - to zoom out of a module
### /observation
eg: `http://localhost:8085/action?action=observation`
Returns a JSON object containing a screenshot and segmentation mask captured from the game. \
JSON structure 
`{
    screenshot: [<base64-encoded PNG bytes>],
    segmentation: [<base64-encoded PNG bytes>]
}`
### /causestrike
Causes a strike to the bomb with a reason
### /screenshot
Returns a base64 list of PNG bytes of the screenshot
### /settimescale
Sets the time scale of the game to run slower or faster
### /setstepunit
Sets the length of one time step
### /timestep
Takes one time step


## Building the Mod
If you want to build the mod yourself you can follow the instructions found [here](https://github.com/samfun123/KtaneTwitchPlays/wiki/How-to-build).

## Original Repository
This mod is built on top of the [TwitchPlays](https://github.com/samfun123/KtaneTwitchPlays) mod.
