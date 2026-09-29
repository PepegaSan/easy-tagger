# Easy Tagger

A standalone Windows program for renaming and moving video files. It does not need Python or AutoHotkey.

The window uses a dark layout: black background, a red section bar, chip buttons, and a red highlight for the active selection.

## Start

Double-click `start.bat`. That requires the .NET 8 SDK. Settings are stored in `%LocalAppData%\EasyTagger\config.json`, outside the project folder.

The default hotkey is `Ctrl+Alt+E`. It opens the model or download picker even when the window is in the tray. Closing the window sends it to the tray. Quit from the tray menu.

## Profiles

**Models** assigns a name and a destination folder. Categories added with `+` filter the buttons. The “also in the file name” option writes the category into the name as well. A `[]` tag typed directly into the name is kept when you save.

**Downloads** builds a name from type, target, and content, for example `[Clip] [Hund] [Sitzen]`. The destination folder is the type’s base folder plus the target’s folder name.

## Examples

The screenshots use the built-in sample names Hund, Katze, Familie, and Studio.

### Models

![Models with the Familie and Studio categories](images/modelle.png)

### Downloads

![Downloads composing the name Clip, Hund, and Sitzen](images/downloads.png)
