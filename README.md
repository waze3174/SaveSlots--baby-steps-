# SaveSlots (baby steps)

a melonloader mod for baby steps that gives you more save slots. the game has 3 (alpha, beta, sigma), this lets you have up to 12, and you can give each save a name so you can tell them apart.

## what it does

- more save slots on the save screen, 6 by default, up to 12 (gamma, delta, epsilon...)
- the list moves up and shrinks a bit so everything still fits on screen, back button included
- works with a gamepad like the normal slots
- name a save: highlight it and press **Y** (gamepad) or **F2** (keyboard). with a mouse, click a save or just point at it and press F2. the name shows instead of the slot letter, like `MANBREAKER (POISON SWAMP)`. empty name puts the letter back. deleting a save clears its name
- with [BabyStepsMenuLib](https://github.com/caleborchard/BabyStepsMenuLib) installed (optional, comes with the multiplayer mod) naming opens its on-screen keyboard, and you can type on a real keyboard into it too. without it, Y opens steam's keyboard, or just type on a keyboard
- while you type a name, the minimap mod's keys and buttons are off

## install

1. install [melonloader](https://melonwiki.xyz/) 0.7.x for baby steps
2. drop `SaveSlots.dll` in `Baby Steps/Mods/`

optional: [BabyStepsMenuLib](https://github.com/caleborchard/BabyStepsMenuLib) for the on-screen keyboard

## settings

`Baby Steps/UserData/SaveSlots/SaveSlots.cfg` (made on first launch):

- `SlotCount` how many slots, 3 to 12 (default 6)
- `[SaveSlotNames]` the save names, you can edit them here too

## good to know

- the mod only reads the game's save files (to show each slot's day and place), it never writes them. saves past sigma are normal game saves (`save3.sav` and up) and they go to steam cloud like the others
- names live only in the mod's cfg
- if you remove the mod while playing on slot 4 or higher, the game keeps loading that slot but the menu only shows the first 3. your other saves stay on disk and come back when you put the mod back

## build

needs the .net 6 sdk and the game with melonloader installed (for the references).

```
dotnet build src/SaveSlots.csproj -c Release -p:GameDir="C:\path\to\Baby Steps"
```

`BabyStepsMenuLib.dll` is expected in the game's `Mods` folder to build.

## credits

- baby steps by gabe cuzzillo, maxi boch and bennett foddy
- on-screen keyboard from [BabyStepsMenuLib](https://github.com/caleborchard/BabyStepsMenuLib) by caleb orchard (not included, used if installed)

tested on baby steps (steam) with melonloader 0.7.3, on steam deck.

not affiliated with or endorsed by gabe cuzzillo, maxi boch, bennett foddy or devolver digital.

## licence

cc0 1.0, do whatever you want with it. see `LICENSE`.
