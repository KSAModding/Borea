# Make your mod work in a Borea instance

This page is for authors of code mods.
A mod that finds its files by a hardcoded path to the game profile breaks for every player who uses Borea.

## What an instance is

The game keeps everything per user in one profile folder: `manifest.toml`, the `mods` folder, saves, vehicles, `settings.toml` and the logs.
By default, that folder is `Documents/My Games/Kitten Space Agency`.

A Borea instance is a profile of its own, in a folder that Borea owns.
Each instance has its own mods, saves and vehicles, and the shared profile in `Documents` stays untouched.
When the player starts an instance, Borea starts StarMap with the instance folder as the profile.

## How the profile moves

The game builds every per-user path from one property, `KSA.Constants.DocumentsFolderPath`.
Its getter combines `Environment.SpecialFolder.Personal` with `My Games/Kitten Space Agency`.
For example, `ModLibrary.LocalModsFolderPath` and `GameSaves.SaveFolderPath` start from it.

StarMap reads an instance folder from the `-InstancePath` argument or the `STARMAP_INSTANCE_PATH` environment variable, in `DocumentsPathPatches.TryGetOverride`.
When one is set, `DocumentsPathPatches.Apply` puts a Harmony prefix on the getter of `Constants.DocumentsFolderPath` that returns the instance folder.
`ModLoader.Init` applies it before StarMap loads the mods and before it calls the entry point of the game, so the game, StarMap and every mod that asks the game get the instance folder.

## The failure

A mod that reads `Documents/My Games/Kitten Space Agency` by a literal path finds an empty or wrong folder when it runs in an instance.
A typical sign is a list or a window of the mod that stays empty in an instance, although the same mod works in the shared profile.

## The cause

The mod builds the path itself, so the StarMap prefix on `Constants.DocumentsFolderPath` never runs for it.
The mod is installed in the instance folder, but it reads the shared profile instead.

## The fix

Never write the profile path into your code.
Use one of these instead:

- **The folder of your mod.** For your own files, use `Path.GetDirectoryName(typeof(MyMod).Assembly.Location)`, because StarMap loads your DLL from the folder of your `mod.toml` in `RuntimeMod.TryCreateMod`. Or use `KSA.Mod.DirectoryPath`, which the game sets to that folder in `Mod.MakeUsing`, and StarMap passes that `Mod` to your `[StarMapImmediateLoad]` method from a prefix on `Mod.PrepareSystems` (`ModPatches.OnLoadMod`). At any later time, `ModLibrary.Find("<your mod id>")?.DirectoryPath` gives the same folder.
- **The profile through the game.** For files in the profile, such as saves or another mod's folder, read `Constants.DocumentsFolderPath` or a path that the game builds from it, such as `ModLibrary.LocalModsFolderPath`. Do not save the path in a settings file, because the next start can be a different instance.

Before a release, start your mod once in a Borea instance.
